using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers.LegacyPlayer;
using ECommons.SimpleGui;
using Splatoon.Gui.PresetHub;
using Splatoon.PresetHub.Core;

namespace Splatoon.Modules.PresetHub;

internal sealed class PresetHubModule : IDisposable
{
    private readonly object gate = new();
    private readonly PresetHubStore store;
    private readonly RepositorySyncService syncService;
    private readonly List<RepositoryDefinition> repositories;
    private readonly Dictionary<string, RepositorySnapshot> snapshots = [];
    private readonly PresetHubDutyPromptWindow dutyPromptWindow;
    private DutyPromptPreferences dutyPromptPreferences;
    private uint pendingTerritory;
    private long pendingTerritorySince;

    internal PresetHubInstaller Installer { get; }
    internal ScriptSecurityAnalyzer SecurityAnalyzer { get; } = new();
    internal bool IsSyncing { get; private set; }
    internal string LastMessage { get; private set; } = "Using local cache.";

    internal PresetHubModule()
    {
        var dataDirectory = Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "PresetHub");
        store = new(dataDirectory);
        repositories = store.LoadRepositories().ToList();
        foreach(var repository in repositories)
        {
            var snapshot = store.LoadSnapshot(repository.Id);
            if(snapshot != null) snapshots[repository.Id] = snapshot;
        }

        syncService = new(new(P.HttpClient), new(), store);
        Installer = new(new(store));
        dutyPromptPreferences = store.LoadDutyPromptPreferences();
        dutyPromptWindow = new(this);
        EzConfigGui.WindowSystem.AddWindow(dutyPromptWindow);
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
        Svc.Framework.Update += OnFrameworkUpdate;
        Installer.CleanupStaleLayoutUi();
        SchedulePrompt(Svc.ClientState.TerritoryType);
        _ = SyncAllAsync();
    }

    internal DutyPromptPreferences DutyPromptPreferences => dutyPromptPreferences;

    internal IReadOnlyList<RepositoryDefinition> Repositories
    {
        get
        {
            lock(gate) return repositories.ToArray();
        }
    }

    internal IReadOnlyList<PresetEntry> Presets
    {
        get
        {
            lock(gate)
            {
                return snapshots.Values
                    .SelectMany(x => x.Presets)
                    .OrderBy(x => x.Expansion)
                    .ThenBy(x => x.Category)
                    .ThenBy(x => x.Duty)
                    .ThenBy(x => x.Title)
                    .ToArray();
            }
        }
    }

    internal IReadOnlyList<PresetEntry> GetDutySuggestions(uint territoryId) =>
        DutyPresetMatcher.FindSuggestions(Presets, territoryId, Installer.GetStatus);

    internal void SetDutyPromptsEnabled(bool enabled)
    {
        dutyPromptPreferences = dutyPromptPreferences with { Enabled = enabled };
        store.SaveDutyPromptPreferences(dutyPromptPreferences);
        if(!enabled) dutyPromptWindow.IsOpen = false;
    }

    internal void SetDutySuppressed(uint territoryId, bool suppressed)
    {
        var territories = dutyPromptPreferences.SuppressedTerritoryIds.ToHashSet();
        if(suppressed) territories.Add(territoryId);
        else territories.Remove(territoryId);
        dutyPromptPreferences = dutyPromptPreferences with { SuppressedTerritoryIds = territories };
        store.SaveDutyPromptPreferences(dutyPromptPreferences);
        if(suppressed && dutyPromptWindow.TerritoryId == territoryId) dutyPromptWindow.IsOpen = false;
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        dutyPromptWindow.IsOpen = false;
        SchedulePrompt(territoryId);
    }

    private void SchedulePrompt(uint territoryId)
    {
        pendingTerritory = territoryId;
        pendingTerritorySince = Environment.TickCount64;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if(pendingTerritory == 0 || IsSyncing || !dutyPromptPreferences.Enabled) return;
        if(Environment.TickCount64 - pendingTerritorySince > 30_000 || Svc.ClientState.TerritoryType != pendingTerritory)
        {
            pendingTerritory = 0;
            return;
        }
        if(!Svc.ClientState.IsLoggedIn || !Player.Available || !Svc.Condition[ConditionFlag.BoundByDuty]) return;

        var territoryId = pendingTerritory;
        pendingTerritory = 0;
        if(dutyPromptPreferences.SuppressedTerritoryIds.Contains(territoryId)) return;
        var suggestions = GetDutySuggestions(territoryId);
        if(suggestions.Count > 0) dutyPromptWindow.Show(territoryId, suggestions);
    }

    internal async Task SyncAllAsync(bool force = false)
    {
        lock(gate)
        {
            if(IsSyncing) return;
            IsSyncing = true;
            LastMessage = "Refreshing repositories...";
        }

        var errors = new List<string>();
        try
        {
            foreach(var repository in Repositories.Where(x => x.Enabled))
            {
                try
                {
                    var snapshot = await syncService.SyncAsync(repository, force).ConfigureAwait(false);
                    lock(gate) snapshots[repository.Id] = snapshot;
                }
                catch(Exception exception)
                {
                    errors.Add($"{repository.FullName}: {exception.Message}");
                    exception.Log();
                }
            }
        }
        finally
        {
            lock(gate)
            {
                IsSyncing = false;
                LastMessage = errors.Count == 0
                    ? $"Index ready: {snapshots.Values.Sum(x => x.Presets.Count)} presets."
                    : $"Cache kept; refresh failed for {errors.Count} repository(s): {string.Join(" | ", errors)}";
            }
        }
    }

    internal bool AddRepository(string repositoryText, string reference, string pathPrefixes, out string error)
    {
        error = "";
        var normalized = repositoryText.Trim().TrimEnd('/');
        if(Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            if(!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                error = "Only github.com repositories are supported in V1.";
                return false;
            }
            normalized = uri.AbsolutePath.Trim('/');
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if(parts.Length != 2)
        {
            error = "Use owner/repository or a GitHub repository URL.";
            return false;
        }

        var id = $"{parts[0]}-{parts[1]}".ToLowerInvariant();
        lock(gate)
        {
            if(repositories.Any(x => x.Id == id))
            {
                error = "This repository is already configured.";
                return false;
            }

            repositories.Add(new()
            {
                Id = id,
                Owner = parts[0],
                Name = parts[1],
                Ref = string.IsNullOrWhiteSpace(reference) ? "main" : reference.Trim(),
                DisplayName = normalized,
                Trust = RepositoryTrust.Untrusted,
                PathPrefixes = pathPrefixes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            });
            store.SaveRepositories(repositories);
        }
        _ = SyncAllAsync(force: true);
        return true;
    }

    internal void SetEnabled(string repositoryId, bool enabled)
    {
        lock(gate)
        {
            var index = repositories.FindIndex(x => x.Id == repositoryId);
            if(index < 0) return;
            repositories[index] = repositories[index] with { Enabled = enabled };
            store.SaveRepositories(repositories);
        }
    }

    internal void RemoveRepository(string repositoryId)
    {
        lock(gate)
        {
            repositories.RemoveAll(x => x.Id == repositoryId);
            snapshots.Remove(repositoryId);
            store.SaveRepositories(repositories);
        }
    }

    public void Dispose()
    {
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.Framework.Update -= OnFrameworkUpdate;
        EzConfigGui.WindowSystem.RemoveWindow(dutyPromptWindow);
        dutyPromptWindow.IsOpen = false;
    }
}
