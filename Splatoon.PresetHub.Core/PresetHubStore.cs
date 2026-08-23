using System.Text.Json;

namespace Splatoon.PresetHub.Core;

public sealed class PresetHubStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string rootDirectory;

    public PresetHubStore(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
        Directory.CreateDirectory(rootDirectory);
    }

    public IReadOnlyList<RepositoryDefinition> LoadRepositories()
    {
        var repositories = Load<List<RepositoryDefinition>>("repositories.json") ?? [];
        if(repositories.Count == 0)
        {
            repositories.Add(RepositoryDefinition.OfficialSplatoon());
            SaveRepositories(repositories);
        }
        return repositories;
    }

    public void SaveRepositories(IEnumerable<RepositoryDefinition> repositories) =>
        Save("repositories.json", repositories.ToArray());

    public RepositorySnapshot? LoadSnapshot(string repositoryId) =>
        Load<RepositorySnapshot>($"cache/{SafeName(repositoryId)}.json");

    public void SaveSnapshot(RepositorySnapshot snapshot) =>
        Save($"cache/{SafeName(snapshot.Repository.Id)}.json", snapshot);

    public IReadOnlyList<InstallationRecord> LoadInstallations() =>
        Load<List<InstallationRecord>>("installations.json") ?? [];

    public void SaveInstallations(IEnumerable<InstallationRecord> installations) =>
        Save("installations.json", installations.ToArray());

    private T? Load<T>(string relativePath)
    {
        var path = GetPath(relativePath);
        if(!File.Exists(path)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    private void Save<T>(string relativePath, T value)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private string GetPath(string relativePath) => Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string SafeName(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
