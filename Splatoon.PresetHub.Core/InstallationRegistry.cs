namespace Splatoon.PresetHub.Core;

public sealed class InstallationRegistry
{
    private readonly PresetHubStore store;
    private readonly Dictionary<string, InstallationRecord> records;

    public InstallationRegistry(PresetHubStore store)
    {
        this.store = store;
        records = store.LoadInstallations().ToDictionary(x => x.PresetId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<InstallationRecord> Records => records.Values;

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
        return null;
    }

    public void MarkInstalled(PresetEntry preset, string runtimeIdentity)
    {
        records[preset.Id] = new()
        {
            PresetId = preset.Id,
            Kind = preset.Kind,
            ContentHash = preset.ContentHash,
            Fingerprint = preset.Fingerprint,
            RuntimeIdentity = runtimeIdentity,
            RepositoryName = preset.RepositoryName,
            SourceUri = preset.SourceUri,
            InstalledAt = DateTimeOffset.UtcNow,
        };
        Save();
    }

    public void Remove(string presetId)
    {
        if(records.Remove(presetId)) Save();
    }

    public void Remove(PresetEntry preset)
    {
        var removed = records.Remove(preset.Id);
        foreach(var alias in preset.AlternatePresetIds) removed |= records.Remove(alias);
        if(removed) Save();
    }

    private void Save() => store.SaveInstallations(records.Values.OrderBy(x => x.InstalledAt));
}
