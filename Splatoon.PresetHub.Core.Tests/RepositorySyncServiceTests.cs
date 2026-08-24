using System.Net;
using System.Text;
using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class RepositorySyncServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PresetHubTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadsArchiveIndexesFilesAndReusesRevisionCache()
    {
        var handler = new GitHubHandler();
        var store = new PresetHubStore(directory);
        var service = new RepositorySyncService(
            new(new HttpClient(handler)),
            new(),
            store);
        var repository = RepositoryDefinition.OfficialSplatoon();

        var first = await service.SyncAsync(repository, cancellationToken: TestContext.Current.CancellationToken);
        var second = await service.SyncAsync(repository, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Presets.Count);
        Assert.Equal(RepositorySyncService.CurrentIndexVersion, first.IndexVersion);
        Assert.Equal(first.Revision, second.Revision);
        Assert.Equal(2, handler.CommitRequests);
        Assert.Equal(1, handler.TreeRequests);
        Assert.Equal(2, handler.RawRequests);
    }

    public void Dispose()
    {
        if(Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class GitHubHandler : HttpMessageHandler
    {
        internal int CommitRequests { get; private set; }
        internal int TreeRequests { get; private set; }
        internal int RawRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if(request.RequestUri!.AbsolutePath.Contains("/commits/", StringComparison.Ordinal))
            {
                CommitRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sha\":\"abc123\"}", Encoding.UTF8, "application/json"),
                });
            }

            if(request.RequestUri.Host == "api.github.com")
            {
                TreeRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"truncated\":false,\"tree\":[{\"path\":\"Presets/Dawntrail/Dungeons/Duty.md\",\"type\":\"blob\",\"size\":60},{\"path\":\"Presets/Dawntrail/Dungeons/Legacy.txt\",\"type\":\"blob\",\"size\":60}]}",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            RawRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("Legacy.txt", StringComparison.Ordinal)
                    ? "Legacy~{\"ZoneLockH\":[1199],\"Elements\":{}}"
                    : "~Lv2~{\"Name\":\"Mechanic\",\"Group\":\"Duty\"}"),
            });
        }
    }
}
