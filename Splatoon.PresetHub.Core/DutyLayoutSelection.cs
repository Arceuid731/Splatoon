namespace Splatoon.PresetHub.Core;

/// <summary>Choose a non-overlapping default batch; alternatives remain selectable.</summary>
public static class DutyLayoutSelection
{
    public static IReadOnlyDictionary<string, string> Recommend(
        IEnumerable<PresetEntry> families,
        IEnumerable<PresetEntry> installed)
    {
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        var occupied = installed.SelectMany(Mechanics).ToHashSet(StringComparer.Ordinal);
        var candidates = families.Where(x => x.Kind == PresetKind.Layout)
            .Select(family => (Family: family, Choices: Choices(family)
                .Where(x => x.Compatibility != PresetCompatibility.Incompatible)
                .OrderBy(x => x.LanguageDependent)
                .ThenByDescending(IsEnglishEdition)
                .ThenByDescending(x => x.ConfidenceScore).ToArray()))
            .Where(x => x.Choices.Length > 0)
            .OrderBy(x => x.Choices[0].LanguageDependent)
            .ThenByDescending(x => IsEnglishEdition(x.Choices[0]))
            .ThenByDescending(x => Mechanics(x.Choices[0]).Count)
            .ThenByDescending(x => x.Choices[0].ConfidenceScore)
            .ThenBy(x => x.Family.Id, StringComparer.Ordinal);

        foreach(var candidate in candidates)
        {
            var choice = candidate.Choices.FirstOrDefault(x => !Mechanics(x).Any(occupied.Contains));
            if(choice == null) continue;
            selected[candidate.Family.Id] = choice.Id;
            occupied.UnionWith(Mechanics(choice));
        }
        return selected;
    }

    private static bool IsEnglishEdition(PresetEntry preset) =>
        preset.RelativePath.StartsWith("[EN Set]/", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<PresetEntry> Choices(PresetEntry family) => family.Variants.Count > 0 ? family.Variants : [family];

    private static IReadOnlyList<string> Mechanics(PresetEntry preset) => preset.Summary.MechanicIdentifiers
        // NPC identity alone is not a mechanic: complementary overlays can target the same boss.
        .Where(x => x.StartsWith("Cast ", StringComparison.Ordinal) || x.StartsWith("Status ", StringComparison.Ordinal) ||
                    x.StartsWith("VFX ", StringComparison.Ordinal))
        .ToArray();
}
