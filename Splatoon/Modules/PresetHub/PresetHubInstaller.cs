using ECommons;
using Splatoon.ConfigGui.CGuiLayouts;
using Splatoon.PresetHub.Core;
using Splatoon.SplatoonScripting;
using Splatoon.Utility;

namespace Splatoon.Modules.PresetHub;

internal sealed class PresetHubInstaller(InstallationRegistry registry) : IDisposable
{
    private readonly HashSet<string> pendingScripts = [];
    private bool disposed;
    public void Dispose() => disposed = true;
    internal IReadOnlyCollection<InstallationRecord> Records => registry.Records;
    internal IEnumerable<(InstallationRecord Record, Layout Layout)> CoverageInstallations() =>
        registry.Records.Where(x => x.Kind == PresetKind.Layout)
            .SelectMany(record => ManagedLayouts(record).Select(layout => (record, layout)));
    internal IReadOnlyList<PresetEntry> IncludeUnavailableInstallations(IReadOnlyList<PresetEntry> families) =>
        registry.IncludeUnavailableInstallations(families);

    internal InstallationStatus GetStatus(PresetEntry preset)
    {
        var record = registry.Find(preset);
        if(record?.Kind == PresetKind.Layout && ManagedLayouts(record).Length == 0)
            return InstallationStatus.NotInstalled;
        return registry.GetStatus(preset);
    }

    internal InstallationStatus GetFamilyStatus(PresetEntry family)
    {
        var statuses = Choices(family).Select(GetStatus).ToArray();
        if(statuses.Contains(InstallationStatus.Installed)) return InstallationStatus.Installed;
        if(statuses.Contains(InstallationStatus.UpdateAvailable)) return InstallationStatus.UpdateAvailable;
        return InstallationStatus.NotInstalled;
    }

    internal PresetEntry? GetInstalledChoice(PresetEntry family) =>
        Choices(family).FirstOrDefault(choice => GetStatus(choice) != InstallationStatus.NotInstalled);

    internal void CleanupStaleLayoutUi()
    {
        var validGroups = P.Config.LayoutsL
            .Select(x => x.Group)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
        var staleGroups = P.Config.GroupOrder.Where(x => !validGroups.Contains(x)).ToArray();
        var changed = P.Config.GroupOrder.RemoveAll(x => staleGroups.Contains(x)) > 0;
        foreach(var group in staleGroups)
        {
            changed |= P.Config.DisabledGroups.Remove(group);
            CGui.OpenedGroup.Remove(group);
        }
        if(LayoutDrawSelector.CurrentLayout != null && !P.Config.LayoutsL.Contains(LayoutDrawSelector.CurrentLayout))
        {
            LayoutDrawSelector.CurrentLayout = null;
            LayoutDrawSelector.CurrentElement = null;
        }
        if(changed) P.Config.Save();
    }

    internal bool InstallLayout(PresetEntry preset, out string message)
        => InstallLayoutChoice(preset, preset, out message);

    internal bool InstallLayoutChoice(PresetEntry family, PresetEntry choice, out string message)
    {
        if(choice.Kind != PresetKind.Layout || choice.Compatibility == PresetCompatibility.Incompatible ||
           !Choices(family).Any(x => x.Id == choice.Id) || ContentHash.Sha256(choice.Content) != choice.ContentHash)
        {
            message = "This layout is unavailable. Refresh the catalogue and try again.";
            return false;
        }
        var installedChoice = GetInstalledChoice(family);
        var choiceStatus = GetStatus(choice);
        var previousRecord = installedChoice == null ? registry.Find(choice) : registry.Find(installedChoice);
        var previousLayouts = P.Config.LayoutsL.ToArray();
        var previousSelection = LayoutDrawSelector.CurrentLayout;
        var previousElement = LayoutDrawSelector.CurrentElement;
        var previousScroll = CGui.ScrollTo;
        var managed = ManagedLayouts(previousRecord);
        List<Layout> imported;
        try
        {
            P.Config.LayoutsL.RemoveAll(x => managed.Contains(x));
            imported = Utils.ImportLayouts(choice.Content, silent: true, allowDuplicateNames: false);
            if(imported.Count != 1)
                throw new InvalidDataException("The layout could not be imported. Check for an existing layout with the same name.");
            imported[0].PresetHubInstallationId = Guid.NewGuid().ToString("N");
            P.Config.Save();
            registry.MarkInstalled(choice, "hub:" + imported[0].PresetHubInstallationId, installedChoice);
        }
        catch(Exception exception)
        {
            P.Config.LayoutsL.Clear();
            P.Config.LayoutsL.AddRange(previousLayouts);
            LayoutDrawSelector.CurrentLayout = previousSelection;
            LayoutDrawSelector.CurrentElement = previousElement;
            CGui.ScrollTo = previousScroll;
            try { P.Config.Save(); } catch(Exception saveException) { saveException.Log(); }
            exception.Log();
            message = "Import failed; the previous layouts were restored. " + exception.Message;
            return false;
        }

        LayoutDrawSelector.CurrentLayout = imported[^1];
        LayoutDrawSelector.CurrentElement = null;
        foreach(var group in imported.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x))) CGui.OpenedGroup.Add(group);
        CleanupStaleLayoutUi();
        message = installedChoice == null
            ? $"Installed {imported.Count} layout(s)."
            : installedChoice.Id != choice.Id
                ? $"Replaced {installedChoice.RepositoryName} with {choice.RepositoryName}."
                : choiceStatus == InstallationStatus.UpdateAvailable
                    ? $"Updated {choice.RepositoryName}."
                    : $"Reinstalled {imported.Count} layout(s).";
        return true;
    }

    internal PresetHubBatchInstallResult InstallLayoutChoices(IEnumerable<PresetHubLayoutSelection> selections)
    {
        var requests = selections.GroupBy(x => x.Family.Id, StringComparer.Ordinal).Select(x => x.Last()).ToArray();
        var failures = new List<string>();
        var succeeded = 0;
        foreach(var request in requests)
        {
            if(request.Choice.Kind != PresetKind.Layout)
            {
                failures.Add($"{request.Family.Title}: scripts require a separate security review");
                continue;
            }
            if(InstallLayoutChoice(request.Family, request.Choice, out var message)) succeeded++;
            else failures.Add($"{request.Family.Title}: {message}");
        }
        return new(succeeded, requests.Length - succeeded, failures);
    }

    internal bool InstallReviewedScript(PresetEntry preset, ScriptSecurityReport report, out string message)
    {
        if(preset.Compatibility == PresetCompatibility.Incompatible)
        {
            message = string.IsNullOrWhiteSpace(preset.CompatibilityDetail)
                ? "This script is marked as incompatible."
                : preset.CompatibilityDetail;
            return false;
        }
        if(preset.Kind != PresetKind.Script || report.ContentHash != preset.ContentHash ||
           report.ContentHash != ContentHash.Sha256(preset.Content))
        {
            message = "The script changed after review. Review it again before installing.";
            return false;
        }
        if(string.IsNullOrWhiteSpace(preset.RuntimeIdentity))
        {
            message = "No SplatoonScript class could be identified in this file.";
            return false;
        }

        var loaded = ScriptingProcessor.Scripts.FirstOrDefault(x => x.InternalData.FullName == preset.RuntimeIdentity);
        if(loaded != null && !registry.Records.Any(x => x.Kind == PresetKind.Script && x.RuntimeIdentity == preset.RuntimeIdentity))
        {
            message = "A manually installed script already uses this identity. Manage it from the Scripts tab.";
            return false;
        }
        if(!pendingScripts.Add(preset.RuntimeIdentity))
        {
            message = "This script is already being installed.";
            return false;
        }
        ScriptingProcessor.CompileAndLoad(preset.Content, null, false, true,
            identity =>
            {
                if(!disposed && identity == preset.RuntimeIdentity) registry.MarkInstalled(preset, identity);
            },
            () => pendingScripts.Remove(preset.RuntimeIdentity));
        message = "The reviewed script was queued for compilation and installation.";
        return true;
    }

    internal bool Uninstall(PresetEntry preset, out string message)
    {
        var record = registry.Find(preset);
        if(record == null)
        {
            message = "Preset Hub has no installation record for this preset.";
            return false;
        }

        if(record.Kind == PresetKind.Layout)
        {
            var removedLayouts = ManagedLayouts(record);
            var removedGroups = removedLayouts.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
            var removed = P.Config.LayoutsL.RemoveAll(x => removedLayouts.Contains(x));
            if(removedLayouts.Contains(LayoutDrawSelector.CurrentLayout))
            {
                LayoutDrawSelector.CurrentLayout = null;
                LayoutDrawSelector.CurrentElement = null;
            }
            foreach(var group in removedGroups.Where(group => P.Config.LayoutsL.All(x => x.Group != group)))
            {
                P.Config.GroupOrder.RemoveAll(x => x == group);
                P.Config.DisabledGroups.Remove(group);
                CGui.OpenedGroup.Remove(group);
            }
            P.Config.Save();
            registry.Remove(preset);
            message = $"Removed {removed} managed layout(s).";
            return true;
        }

        var script = ScriptingProcessor.Scripts.FirstOrDefault(x => x.InternalData.FullName == record.RuntimeIdentity);
        if(script != null)
        {
            new TickScheduler(() =>
            {
                script.Disable();
                ScriptingProcessor.RemoveScript(script);
                if(!string.IsNullOrWhiteSpace(script.InternalData.Path) &&
                   script.InternalData.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    GenericHelpers.DeleteFileToRecycleBin(script.InternalData.Path);
                }
            });
        }
        registry.Remove(preset);
        message = script == null
            ? "Removed the stale installation record; the script was not loaded."
            : "Removed the managed script and moved its source file to the recycle bin.";
        return true;
    }

    internal bool UninstallFamily(PresetEntry family, out string message)
    {
        var installed = GetInstalledChoice(family);
        if(installed == null)
        {
            message = "Preset Hub has no installation record for this preset family.";
            return false;
        }
        return Uninstall(installed, out message);
    }

    private static HashSet<string> SplitRuntimeIdentity(string? value) =>
        value?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];

    private static Layout[] ManagedLayouts(InstallationRecord? record)
    {
        if(record == null) return [];
        var identities = SplitRuntimeIdentity(record.RuntimeIdentity);
        // New installations retain ownership after a rename. For legacy records,
        // refuse ambiguous names rather than removing several unrelated layouts.
        return identities.Select(identity => identity.StartsWith("hub:", StringComparison.Ordinal)
                ? P.Config.LayoutsL.Where(x => x.PresetHubInstallationId == identity[4..]).ToArray()
                : P.Config.LayoutsL.Where(x => x.Name == identity).ToArray())
            .Where(matches => matches.Length == 1).Select(matches => matches[0]).ToArray();
    }

    private static IReadOnlyList<PresetEntry> Choices(PresetEntry family) =>
        family.Variants.Count > 0 ? family.Variants : [family];
}

internal sealed record PresetHubLayoutSelection(PresetEntry Family, PresetEntry Choice);

internal sealed record PresetHubBatchInstallResult(int Succeeded, int Failed, IReadOnlyList<string> Failures)
{
    internal string Message => Failed == 0
        ? $"Installed or updated {Succeeded} selected preset(s)."
        : $"Installed or updated {Succeeded} preset(s); {Failed} failed. {string.Join(" | ", Failures)}";
}
