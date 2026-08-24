namespace Splatoon.PresetHub.Core;

public sealed class RepositorySyncService(
    GitHubRepositoryClient client,
    PresetIndexer indexer,
    PresetHubStore store)
{
    public const int CurrentIndexVersion = 3;

    public async Task<RepositorySnapshot> SyncAsync(
        RepositoryDefinition repository,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var revision = await client.GetRevisionAsync(repository, cancellationToken).ConfigureAwait(false);
        var cached = store.LoadSnapshot(repository.Id);
        if(!force && cached?.Revision == revision && cached.IndexVersion == CurrentIndexVersion) return cached;

        var files = await client.DownloadIndexableFilesAsync(repository, revision, cancellationToken).ConfigureAwait(false);
        var snapshot = new RepositorySnapshot
        {
            IndexVersion = CurrentIndexVersion,
            Repository = repository,
            Revision = revision,
            SyncedAt = DateTimeOffset.UtcNow,
            Presets = indexer.Index(repository, files),
        };
        store.SaveSnapshot(snapshot);
        return snapshot;
    }
}
