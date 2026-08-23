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
        if(!records.TryGetValue(preset.Id, out var record)) return InstallationStatus.NotInstalled;
        return record.ContentHash == preset.ContentHash
            ? InstallationStatus.Installed
            : InstallationStatus.UpdateAvailable;
    }

    public InstallationRecord? Find(string presetId) => records.GetValueOrDefault(presetId);

    public void MarkInstalled(PresetEntry preset, string runtimeIdentity)
    {
        records[preset.Id] = new()
        {
            PresetId = preset.Id,
            Kind = preset.Kind,
            ContentHash = preset.ContentHash,
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

    private void Save() => store.SaveInstallations(records.Values.OrderBy(x => x.InstalledAt));
}
