namespace Splatoon.PresetHub.Core;

public sealed class InstallationRegistry
{
    private readonly PresetHubStore store;
    private readonly Dictionary<string, InstallationRecord> records;

    public InstallationRegistry(PresetHubStore store)
    {
        this.store = store;
        records = store.LoadInstallations().GroupBy(x => x.PresetId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.MaxBy(record => record.InstalledAt)!, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<InstallationRecord> Records => records.Values;

    public void RememberPresets(IEnumerable<PresetEntry> presets)
    {
        var indexed = presets.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        var updated = new Dictionary<string, InstallationRecord>(records, StringComparer.Ordinal);
        var changed = false;
        foreach(var record in records.Values)
        {
            if(record.Preset == null && indexed.TryGetValue(record.PresetId, out var preset))
            {
                updated[record.PresetId] = record with { Preset = preset with { Variants = [] } };
                changed = true;
            }
        }
        if(changed) Persist(updated);
    }

    public IReadOnlyList<PresetEntry> IncludeUnavailableInstallations(IReadOnlyList<PresetEntry> families)
    {
        var found = families.SelectMany(x => x.Variants.Count > 0 ? x.Variants : [x])
            .Select(x => Find(x)?.PresetId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return families.Concat(records.Values.Where(x => !found.Contains(x.PresetId) && x.Preset != null)
            .Select(x => x.Preset! with { Id = x.PresetId, Variants = [], VariantCount = 1 })).ToArray();
    }

    public InstallationStatus GetStatus(PresetEntry preset)
    {
        var record = Find(preset);
        if(record == null) return InstallationStatus.NotInstalled;
        return record.ContentHash == preset.ContentHash ||
               (preset.Fingerprint.Length > 0 && record.Fingerprint == preset.Fingerprint)
            ? InstallationStatus.Installed
            : InstallationStatus.UpdateAvailable;
    }

    public InstallationRecord? Find(string presetId) => records.GetValueOrDefault(presetId);

    public InstallationRecord? Find(PresetEntry preset)
    {
        if(records.TryGetValue(preset.Id, out var direct)) return direct;
        foreach(var alias in preset.AlternatePresetIds)
        {
            if(records.TryGetValue(alias, out var alternate)) return alternate;
        }
        // Older hubs used the export's ordinal in its file. Never reuse that ordinal
        // after a refresh: an inserted export could otherwise replace a different layout.
        var exactMatches = records.Values.Where(x => x.Kind == preset.Kind &&
            preset.Fingerprint.Length > 0 && x.Fingerprint == preset.Fingerprint).ToArray();
        if(exactMatches.Length == 1) return exactMatches[0];
        var matches = records.Values.Where(x => x.Kind == preset.Kind &&
             preset.RuntimeIdentity.Length > 0 && (x.Preset?.RuntimeIdentity ?? x.RuntimeIdentity) == preset.RuntimeIdentity &&
             x.RepositoryName == preset.RepositoryName && x.SourceUri == preset.SourceUri).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public void MarkInstalled(PresetEntry preset, string runtimeIdentity, PresetEntry? replaced = null)
    {
        var updated = new Dictionary<string, InstallationRecord>(records, StringComparer.Ordinal);
        var previous = Find(replaced ?? preset);
        if(previous != null) updated.Remove(previous.PresetId);
        foreach(var alias in preset.AlternatePresetIds) updated.Remove(alias);
        updated[preset.Id] = new()
        {
            Preset = preset with { Variants = [] },
            PresetId = preset.Id,
            Kind = preset.Kind,
            ContentHash = preset.ContentHash,
            Fingerprint = preset.Fingerprint,
            RuntimeIdentity = runtimeIdentity,
            RepositoryName = preset.RepositoryName,
            SourceUri = preset.SourceUri,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        Persist(updated);
    }

    public void Remove(string presetId)
    {
        var updated = new Dictionary<string, InstallationRecord>(records, StringComparer.Ordinal);
        if(updated.Remove(presetId)) Persist(updated);
    }

    public void Remove(PresetEntry preset)
    {
        var record = Find(preset);
        var updated = new Dictionary<string, InstallationRecord>(records, StringComparer.Ordinal);
        var removed = record != null && updated.Remove(record.PresetId);
        foreach(var alias in preset.AlternatePresetIds) removed |= updated.Remove(alias);
        if(removed) Persist(updated);
    }

    private void Persist(Dictionary<string, InstallationRecord> updated)
    {
        store.SaveInstallations(updated.Values.OrderBy(x => x.InstalledAt));
        records.Clear();
        foreach(var pair in updated) records.Add(pair.Key, pair.Value);
    }
}
