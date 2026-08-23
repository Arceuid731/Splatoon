using System.Net.Http.Headers;
using System.Text.Json;

namespace Splatoon.PresetHub.Core;

public sealed class GitHubRepositoryClient(HttpClient httpClient)
{
    private const int MaxFileBytes = 2 * 1024 * 1024;
    private readonly HttpClient httpClient = Configure(httpClient);

    public async Task<string> GetRevisionAsync(
        RepositoryDefinition repository,
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/commits/{Uri.EscapeDataString(repository.Ref)}";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidDataException("GitHub returned a commit without a SHA.");
    }

    public async Task<IReadOnlyList<(string RelativePath, string Content)>> DownloadIndexableFilesAsync(
        RepositoryDefinition repository,
        string revision,
        CancellationToken cancellationToken = default)
    {
        var treeUri = $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/git/trees/{revision}?recursive=1";
        using var response = await httpClient.GetAsync(treeUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var treeStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(treeStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if(document.RootElement.TryGetProperty("truncated", out var truncated) && truncated.GetBoolean())
        {
            throw new InvalidDataException("GitHub truncated the repository tree; narrow the configured path prefixes.");
        }

        var paths = document.RootElement.GetProperty("tree")
            .EnumerateArray()
            .Where(item => item.GetProperty("type").GetString() == "blob")
            .Where(item => !item.TryGetProperty("size", out var size) || size.GetInt64() <= MaxFileBytes)
            .Select(item => item.GetProperty("path").GetString())
            .OfType<string>()
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => repository.PathPrefixes.Count == 0 || repository.PathPrefixes.Any(prefix =>
                path.StartsWith(prefix.TrimStart('/').Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            .Where(path => !repository.ExcludedPathPrefixes.Any(prefix =>
                path.StartsWith(prefix.TrimStart('/').Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        using var concurrency = new SemaphoreSlim(8);
        var downloads = paths.Select(async path =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var rawUri = $"https://raw.githubusercontent.com/{repository.Owner}/{repository.Name}/{revision}/{Uri.EscapeDataString(path).Replace("%2F", "/")}";
                var content = await httpClient.GetStringAsync(rawUri, cancellationToken).ConfigureAwait(false);
                return (RelativePath: path, Content: content);
            }
            finally
            {
                concurrency.Release();
            }
        });
        return await Task.WhenAll(downloads).ConfigureAwait(false);
    }

    private static HttpClient Configure(HttpClient client)
    {
        if(!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Splatoon-PresetHub", "1.0"));
        }
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
