namespace Splatoon.PresetHub.Core;

public sealed record CoveragePlan(
    IReadOnlyList<CoverageContribution> Selected,
    IReadOnlyList<CoverageContribution> Alternatives,
    int AvailableAids,
    int CoveredAids,
    bool SearchComplete);

/// <summary>
/// Maximizes the number of distinct aids without activating two providers for
/// one aid. A linked contribution is selected in full or not at all.
/// </summary>
public static class CoveragePlanner
{
    public static CoveragePlan Compute(IEnumerable<CoverageContribution> source, uint territory,
        CoveragePreferences? preferences = null)
    {
        preferences ??= new();
        var all = source.Where(x => x.TerritoryId == territory).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var eligible = all.Where(x => x.Automatic && preferences.Allows(x) && x.Claims.Count > 0).ToArray();
        // Equivalent coverage sets have identical conflicts. Keep the best variant
        // in the optimizer, but retain every source in the library/alternatives UI.
        var candidates = eligible.GroupBy(CoverageKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => IsPreferred(x, preferences))
                .ThenByDescending(x => x.Local)
                .ThenBy(x => x.LanguageDependent)
                .ThenByDescending(x => x.LanguageRank)
                .ThenByDescending(x => x.Confidence)
                .ThenByDescending(x => x.ElementCount)
                .ThenBy(x => x.Id, StringComparer.Ordinal).First()).ToArray();
        var selected = new List<CoverageContribution>();
        var forcedIds = preferences.SelectedAlternatives.Values.Reverse().ToArray();
        var forcedAids = new HashSet<string>();
        foreach(var id in forcedIds)
        {
            var candidate = candidates.FirstOrDefault(x => x.Id == id);
            if(candidate == null || Aids(candidate).Any(forcedAids.Contains)) continue;
            selected.Add(candidate);
            forcedAids.UnionWith(Aids(candidate));
        }
        foreach(var local in candidates.Where(x => x.Local).OrderByDescending(x => Aids(x).Count()).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            if(Aids(local).Any(forcedAids.Contains)) continue;
            selected.Add(local);
            forcedAids.UnionWith(Aids(local));
        }
        var complete = true;
        foreach(var component in Components(candidates.Where(x => !Aids(x).Any(forcedAids.Contains)).ToArray()))
        {
            var result = Solve(component);
            selected.AddRange(result.Items);
            complete &= result.Complete;
        }
        var selectedIds = selected.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return new(selected.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray(),
            all.Where(x => !selectedIds.Contains(x.Id)).ToArray(),
            eligible.SelectMany(Aids).Distinct(StringComparer.Ordinal).Count(),
            selected.SelectMany(Aids).Distinct(StringComparer.Ordinal).Count(), complete);
    }

    public static string CoverageKey(CoverageContribution contribution) =>
        string.Join('|', Aids(contribution).Order(StringComparer.Ordinal));

    private static bool IsPreferred(CoverageContribution candidate, CoveragePreferences preferences) =>
        preferences.SelectedAlternatives.GetValueOrDefault(CoverageKey(candidate)) == candidate.Id;

    private static IEnumerable<string> Aids(CoverageContribution contribution) => contribution.Claims.Select(x => x.AidId).Distinct();

    private static IEnumerable<CoverageContribution[]> Components(CoverageContribution[] source)
    {
        var byAid = source.SelectMany((item, index) => Aids(item).Select(aid => (aid, index)))
            .GroupBy(x => x.aid).ToDictionary(x => x.Key, x => x.Select(y => y.index).ToArray());
        var seen = new HashSet<int>();
        for(var index = 0; index < source.Length; index++)
        {
            if(!seen.Add(index)) continue;
            var pending = new Queue<int>();
            var component = new List<CoverageContribution>();
            pending.Enqueue(index);
            while(pending.TryDequeue(out var current))
            {
                component.Add(source[current]);
                foreach(var neighbor in Aids(source[current]).SelectMany(aid => byAid[aid]))
                    if(seen.Add(neighbor)) pending.Enqueue(neighbor);
            }
            yield return component.ToArray();
        }
    }

    private static (CoverageContribution[] Items, bool Complete) Solve(CoverageContribution[] component)
    {
        var ordered = component.OrderByDescending(x => Aids(x).Count()).ThenByDescending(Quality)
            .ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var keys = ordered.Select(x => Aids(x).ToHashSet(StringComparer.Ordinal)).ToArray();
        var best = new List<int>();
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        for(var index = 0; index < ordered.Length; index++)
            if(!keys[index].Overlaps(occupied)) { best.Add(index); occupied.UnionWith(keys[index]); }
        var bestCount = occupied.Count;
        var bestQuality = best.Sum(i => Quality(ordered[i]) * keys[i].Count);
        var budget = 200_000;
        var current = new List<int>();
        occupied.Clear();
        void Search(int index, long quality)
        {
            if(--budget < 0) return;
            if(occupied.Count > bestCount || occupied.Count == bestCount && quality > bestQuality)
            { best = [.. current]; bestCount = occupied.Count; bestQuality = quality; }
            if(index == ordered.Length) return;
            var possible = new HashSet<string>(occupied, StringComparer.Ordinal);
            for(var i = index; i < ordered.Length; i++)
                if(!keys[i].Overlaps(occupied)) possible.UnionWith(keys[i]);
            if(possible.Count < bestCount) return;
            if(!keys[index].Overlaps(occupied))
            {
                current.Add(index);
                occupied.UnionWith(keys[index]);
                Search(index + 1, quality + Quality(ordered[index]) * keys[index].Count);
                occupied.ExceptWith(keys[index]);
                current.RemoveAt(current.Count - 1);
            }
            Search(index + 1, quality);
        }
        // Large source lists normally collapse to isolated alternatives. Keep a
        // deterministic bounded search for unusually entangled dependency packs.
        if(ordered.Length <= 128) Search(0, 0);
        else budget = -1;
        return (best.Select(i => ordered[i]).ToArray(), budget >= 0);
    }

    private static long Quality(CoverageContribution item) =>
        (item.Local ? 1_000_000_000 : 0) + (item.LanguageDependent ? 0 : 1_000_000) + item.LanguageRank * 100_000L + item.Confidence * 100L + Math.Min(item.ElementCount, 99);
}
