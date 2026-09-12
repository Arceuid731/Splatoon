using Dalamud.Game.ClientState.Conditions;
using System.Net.Http;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers.LegacyPlayer;
using ECommons.ExcelServices;
using ECommons.SimpleGui;
using Lumina.Excel.Sheets;
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
    private IReadOnlyList<PresetEntry> catalog = [];
    private bool catalogDirty = true;
    private readonly PresetHubDutyPromptWindow dutyPromptWindow;
    private DutyPromptPreferences dutyPromptPreferences;
    private readonly DutyPromptScheduler promptScheduler = new();
    private readonly System.Threading.CancellationTokenSource lifetime = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private Task syncTask = Task.CompletedTask;
    private bool syncRequested;
    private bool forceRequested;
    private bool disposed;
    private volatile bool syncing;

    internal PresetHubInstaller Installer { get; }
    internal ScriptSecurityAnalyzer SecurityAnalyzer { get; } = new();
    internal bool IsSyncing => syncing;
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

        syncService = new(new(httpClient), new(), store);
        var registry = new InstallationRegistry(store);
        registry.RememberPresets(snapshots.Values.SelectMany(x => x.Presets));
        Installer = new(registry);
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
                if(!catalogDirty) return catalog;
                var enabledIds = repositories.Where(x => x.Enabled).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                var indexed = snapshots.Values
                    .Where(x => enabledIds.Contains(x.Repository.Id))
                    .SelectMany(x => x.Presets)
                    .ToArray();
                catalog = PresetCatalog.Create(indexed).Families.Select(ResolveFamilyTerritoryMetadata).ToArray();
                catalogDirty = false;
                return catalog;
            }
        }
    }

    internal IReadOnlyList<PresetEntry> GetDutySuggestions(uint territoryId) =>
        DutyPresetMatcher.FindSuggestions(Presets, territoryId, Installer.GetFamilyStatus);

    internal IReadOnlyList<PresetEntry> InstalledPresets =>
        Installer.IncludeUnavailableInstallations(Presets).Select(ResolveFamilyTerritoryMetadata).ToArray();

    internal IReadOnlyDictionary<string, string> RecommendDutyLayouts(uint territoryId, IReadOnlyList<PresetEntry> suggestions) =>
        DutyLayoutSelection.Recommend(suggestions,
            InstalledPresets.SelectMany(x => x.Variants.Count > 0 ? x.Variants : [x])
                .Where(x => x.Kind == PresetKind.Layout && x.TerritoryIds.Contains(territoryId) &&
                            Installer.GetStatus(x) == InstallationStatus.Installed));

    internal void SetDutyPromptsEnabled(bool enabled)
    {
        dutyPromptPreferences = dutyPromptPreferences with { Enabled = enabled };
        store.SaveDutyPromptPreferences(dutyPromptPreferences);
        if(!enabled) dutyPromptWindow.IsOpen = false;
        else SchedulePrompt(Svc.ClientState.TerritoryType);
    }

    internal void SetDutySuppressed(uint territoryId, bool suppressed)
    {
        var territories = dutyPromptPreferences.SuppressedTerritoryIds.ToHashSet();
        if(suppressed) territories.Add(territoryId);
        else territories.Remove(territoryId);
        dutyPromptPreferences = dutyPromptPreferences with { SuppressedTerritoryIds = territories };
        store.SaveDutyPromptPreferences(dutyPromptPreferences);
        if(suppressed && dutyPromptWindow.TerritoryId == territoryId) dutyPromptWindow.IsOpen = false;
        if(!suppressed) SchedulePrompt(Svc.ClientState.TerritoryType);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        dutyPromptWindow.IsOpen = false;
        SchedulePrompt(territoryId);
    }

    private void SchedulePrompt(uint territoryId)
    {
        promptScheduler.Schedule(territoryId);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var territoryId = Svc.ClientState.TerritoryType;
        if(!promptScheduler.ShouldCheck(territoryId,
               Svc.ClientState.IsLoggedIn && Player.Available && Svc.Condition[ConditionFlag.BoundByDuty],
               dutyPromptPreferences.Enabled)) return;
        if(dutyPromptPreferences.SuppressedTerritoryIds.Contains(territoryId))
        {
            promptScheduler.CompleteCheck(false, false);
            return;
        }
        var suggestions = GetDutySuggestions(territoryId);
        promptScheduler.CompleteCheck(suggestions.Count > 0, IsSyncing);
        if(suggestions.Count > 0) dutyPromptWindow.Show(territoryId, suggestions);
    }

    internal Task SyncAllAsync(bool force = false)
    {
        lock(gate)
        {
            if(disposed) return Task.CompletedTask;
            syncRequested = true;
            forceRequested |= force;
            if(IsSyncing) return syncTask;
            syncing = true;
            LastMessage = "Refreshing repositories...";
            return syncTask = Task.Run(SyncLoopAsync);
        }
    }

    private async Task SyncLoopAsync()
    {
        var errors = new List<string>();
        try
        {
            while(true)
            {
                RepositoryDefinition[] requested;
                bool force;
                lock(gate)
                {
                    if(!syncRequested || disposed) break;
                    syncRequested = false;
                    force = forceRequested;
                    forceRequested = false;
                    requested = repositories.Where(x => x.Enabled).ToArray();
                }
                errors.Clear();
                foreach(var repository in requested)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var snapshot = await syncService.SyncAsync(repository, force, lifetime.Token).ConfigureAwait(false);
                        lock(gate)
                        {
                            if(disposed || !repositories.Contains(repository)) continue;
                            snapshots[repository.Id] = snapshot;
                            catalogDirty = true;
                        }
                    }
                    catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { throw; }
                    catch(Exception exception)
                    {
                        errors.Add(repository.FullName);
                        exception.Log();
                    }
                }
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
        finally
        {
            lock(gate)
            {
                syncing = false;
                var enabledIds = repositories.Where(x => x.Enabled).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                var rawPresets = snapshots.Values.Where(x => enabledIds.Contains(x.Repository.Id)).SelectMany(x => x.Presets).ToArray();
                var result = PresetCatalog.Create(rawPresets);
                LastMessage = errors.Count == 0
                    ? $"{result.Families.Count} presets available · {result.DistinctChoices} versions."
                    : $"Refresh failed: {string.Join(", ", errors)}. Showing saved presets.";
                // A request can arrive between the loop's last check and this lock.
                if(syncRequested && !disposed) _ = SyncAllAsync();
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
                error = "Use a github.com repository.";
                return false;
            }
            normalized = uri.AbsolutePath.Trim('/');
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if(parts.Length != 2 || parts.Any(part => part is "." or ".." ||
            !System.Text.RegularExpressions.Regex.IsMatch(part, @"^[A-Za-z0-9_.-]+$")))
        {
            error = "Use owner/repository or a GitHub repository URL.";
            return false;
        }

        var id = $"{parts[0]}-{parts[1]}".ToLowerInvariant();
        lock(gate)
        {
            if(repositories.Any(x => x.Id == id || x.FullName.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
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
        _ = SyncAllAsync();
        return true;
    }

    internal void SetEnabled(string repositoryId, bool enabled)
    {
        lock(gate)
        {
            var index = repositories.FindIndex(x => x.Id == repositoryId);
            if(index < 0) return;
            repositories[index] = repositories[index] with { Enabled = enabled };
            catalogDirty = true;
            store.SaveRepositories(repositories);
        }
        if(enabled) _ = SyncAllAsync();
    }

    internal void RemoveRepository(string repositoryId)
    {
        lock(gate)
        {
            repositories.RemoveAll(x => x.Id == repositoryId);
            snapshots.Remove(repositoryId);
            catalogDirty = true;
            store.SaveRepositories(repositories);
        }
    }

    public void Dispose()
    {
        lock(gate) disposed = true;
        lifetime.Cancel();
        httpClient.Dispose();
        _ = syncTask.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        Installer.Dispose();
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        Svc.Framework.Update -= OnFrameworkUpdate;
        EzConfigGui.WindowSystem.RemoveWindow(dutyPromptWindow);
        dutyPromptWindow.IsOpen = false;
    }

    private static PresetEntry ResolveFamilyTerritoryMetadata(PresetEntry preset)
    {
        var variants = preset.Variants.Select(ResolveTerritoryMetadata).ToArray();
        var resolved = ResolveTerritoryMetadata(preset);
        return resolved with { Variants = variants };
    }

    private static PresetEntry ResolveTerritoryMetadata(PresetEntry preset)
    {
        if(preset.TerritoryIds.Count != 1) return preset;
        try
        {
            var territory = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(preset.TerritoryIds[0]);
            var content = territory?.ContentFinderCondition.ValueNullable;
            var duty = content?.Name.ToString();
            var expansion = territory?.ExVersion.ValueNullable?.Name.ToString();
            var category = content?.ContentType.ValueNullable?.Name.ToString();
            duty = string.IsNullOrWhiteSpace(duty) ? ExcelTerritoryHelper.GetName(preset.TerritoryIds[0], true) : duty;
            return preset with
            {
                Duty = string.IsNullOrWhiteSpace(duty) ? preset.Duty : duty,
                Expansion = string.IsNullOrWhiteSpace(expansion) ? preset.Expansion : expansion,
                Category = string.IsNullOrWhiteSpace(category) ? preset.Category : category,
            };
        }
        catch
        {
            return preset;
        }
    }
}
