using ECommons;
using Splatoon.ConfigGui.CGuiLayouts;
using Splatoon.PresetHub.Core;
using Splatoon.SplatoonScripting;
using Splatoon.Utility;

namespace Splatoon.Modules.PresetHub;

internal sealed class PresetHubInstaller(InstallationRegistry registry)
{
    internal IReadOnlyCollection<InstallationRecord> Records => registry.Records;

    internal InstallationStatus GetStatus(PresetEntry preset) => registry.GetStatus(preset);

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
        var installedChoice = GetInstalledChoice(family);
        var previousRecord = installedChoice == null ? registry.Find(choice) : registry.Find(installedChoice);
        var previousNames = SplitRuntimeIdentity(previousRecord?.RuntimeIdentity);
        var previousLayouts = P.Config.LayoutsL.Where(x => previousNames.Contains(x.Name)).ToArray();
        P.Config.LayoutsL.RemoveAll(x => previousNames.Contains(x.Name));

        var imported = Utils.ImportLayouts(choice.Content, silent: true);
        if(imported.Count == 0)
        {
            P.Config.LayoutsL.AddRange(previousLayouts);
            message = "Splatoon rejected the layout. An existing layout may use the same name.";
            return false;
        }

        LayoutDrawSelector.CurrentLayout = imported[^1];
        LayoutDrawSelector.CurrentElement = null;
        foreach(var group in imported.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x))) CGui.OpenedGroup.Add(group);
        var runtimeIdentity = string.Join('\n', imported.Select(x => x.Name));
        if(installedChoice != null && installedChoice.Id != choice.Id) registry.Remove(installedChoice);
        registry.MarkInstalled(choice, runtimeIdentity);
        P.Config.Save();
        CleanupStaleLayoutUi();
        message = installedChoice != null && installedChoice.Id != choice.Id
            ? $"Replaced {installedChoice.RepositoryName} with {choice.RepositoryName}."
            : $"Installed {imported.Count} layout(s).";
        return true;
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
        if(report.ContentHash != preset.ContentHash)
        {
            message = "The script changed after review. Review it again before installing.";
            return false;
        }
        if(string.IsNullOrWhiteSpace(preset.RuntimeIdentity))
        {
            message = "No SplatoonScript class could be identified in this file.";
            return false;
        }

        ScriptingProcessor.CompileAndLoad(preset.Content, null, false, true);
        registry.MarkInstalled(preset, preset.RuntimeIdentity);
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
            var names = SplitRuntimeIdentity(record.RuntimeIdentity);
            var removedLayouts = P.Config.LayoutsL.Where(x => names.Contains(x.Name)).ToArray();
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
        value?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];

    private static IReadOnlyList<PresetEntry> Choices(PresetEntry family) =>
        family.Variants.Count > 0 ? family.Variants : [family];
}
