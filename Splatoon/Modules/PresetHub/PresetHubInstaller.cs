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
    {
        var previousRecord = registry.Find(preset);
        var previousNames = SplitRuntimeIdentity(previousRecord?.RuntimeIdentity);
        var previousLayouts = P.Config.LayoutsL.Where(x => previousNames.Contains(x.Name)).ToArray();
        P.Config.LayoutsL.RemoveAll(x => previousNames.Contains(x.Name));

        var imported = Utils.ImportLayouts(preset.Content, silent: true);
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
        registry.MarkInstalled(preset, runtimeIdentity);
        P.Config.Save();
        message = $"Installed {imported.Count} layout(s).";
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

    private static HashSet<string> SplitRuntimeIdentity(string? value) =>
        value?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal) ?? [];
}
