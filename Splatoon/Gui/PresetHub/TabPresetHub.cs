using Dalamud.Interface.Colors;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;

namespace Splatoon.Gui.PresetHub;

internal static class TabPresetHub
{
    private static string search = "";
    private static PresetKind? kindFilter;
    private static InstallationStatus? statusFilter;
    private static string expansionFilter = "";
    private static string categoryFilter = "";
    private static string dutyFilter = "";
    private static PresetEntry? reviewedScript;
    private static PresetEntry? selectedFamily;
    private static ScriptSecurityReport? reviewReport;
    private static bool reviewConfirmed;
    private static string repositoryInput = "";
    private static string referenceInput = "main";
    private static string pathsInput = "";
    private static string repositoryError = "";
    private static string actionMessage = "";
    private static bool openSourceLibrary;

    internal static void Draw()
    {
        var hub = P.PresetHub;
        if(ImGui.BeginTabBar("PresetHubTabs"))
        {
            if(ImGui.BeginTabItem("Coverage"))
            {
                TabCoverage.Draw(hub);
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Source library", openSourceLibrary ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                openSourceLibrary = false;
                DrawBrowser(hub, installedOnly: false);
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Installed"))
            {
                DrawBrowser(hub, installedOnly: true);
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Repositories"))
            {
                DrawRepositories(hub);
                ImGui.EndTabItem();
            }
            if(ImGui.BeginTabItem("Duty prompts"))
            {
                DrawDutyPrompts(hub);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    internal static void OpenScriptReview(PresetEntry preset)
    {
        openSourceLibrary = true;
        selectedFamily = null;
        reviewedScript = preset;
        reviewReport = P.PresetHub.SecurityAnalyzer.Analyze(preset.Content);
        reviewConfirmed = false;
    }

    internal static void OpenVariantPicker(PresetEntry family)
    {
        selectedFamily = family;
        reviewedScript = null;
        reviewReport = null;
        reviewConfirmed = false;
    }

    private static void DrawBrowser(PresetHubModule hub, bool installedOnly)
    {
        ImGui.SetNextItemWidth(260f.Scale());
        ImGui.InputTextWithHint("##PresetHubSearch", "Search title, duty, author...", ref search, 200);
        ImGui.SameLine();
        DrawNullableEnumCombo("Type", ref kindFilter);
        ImGui.SameLine();
        DrawNullableEnumCombo("Status", ref statusFilter);

        var presets = installedOnly ? hub.InstalledPresets : hub.Presets;
        if(DrawStringCombo("Expansion", ref expansionFilter, presets.Select(x => x.Expansion)))
        {
            categoryFilter = "";
            dutyFilter = "";
        }
        ImGui.SameLine();
        var categoryPresets = presets.Where(x => expansionFilter.Length == 0 || x.Expansion == expansionFilter);
        if(DrawStringCombo("Category", ref categoryFilter, categoryPresets.Select(x => x.Category))) dutyFilter = "";
        ImGui.SameLine();
        var dutyPresets = categoryPresets.Where(x => categoryFilter.Length == 0 || x.Category == categoryFilter);
        DrawStringCombo("Duty", ref dutyFilter, dutyPresets.Select(x => x.Duty), 210f);
        ImGui.SameLine();
        if(ImGui.Button(hub.IsSyncing ? "Refreshing..." : "Refresh") && !hub.IsSyncing)
        {
            _ = hub.SyncAllAsync();
        }

        ImGui.TextDisabled(hub.LastMessage);
        if(!string.IsNullOrWhiteSpace(actionMessage)) ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, actionMessage);
        ImGui.Separator();

        if(ImGui.BeginTable("PresetHubBrowser", 6,
               ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY |
               ImGuiTableFlags.Sortable | ImGuiTableFlags.SortTristate,
               new(0, reviewedScript == null && selectedFamily == null ? 0 : 300f.Scale())))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Preset", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 70f.Scale());
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.DefaultSort);
            ImGui.TableSetupColumn("Repository", ImGuiTableColumnFlags.WidthFixed, 130f.Scale());
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100f.Scale());
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 190f.Scale());
            ImGui.TableHeadersRow();

            var sortColumn = PresetSortColumn.Content;
            var sortAscending = true;
            if(ImGuiEx.TryGetTableSortDirection(out var requestedAscending, out var requestedColumn) && requestedColumn is >= 0 and <= 4)
            {
                sortColumn = (PresetSortColumn)requestedColumn;
                sortAscending = requestedAscending;
            }
            var filtered = PresetQuery.Apply(
                presets,
                hub.Installer.GetFamilyStatus,
                new()
                {
                    Search = search,
                    Kind = kindFilter,
                    Status = statusFilter,
                    Expansion = expansionFilter,
                    Category = categoryFilter,
                    Duty = dutyFilter,
                    InstalledOnly = installedOnly,
                    SortColumn = sortColumn,
                    SortAscending = sortAscending,
                });
            foreach(var preset in filtered)
            {
                DrawPresetRow(hub, preset);
            }
            ImGui.EndTable();
        }

        if(selectedFamily != null) DrawVariantPicker(hub, selectedFamily);
        if(reviewedScript != null && reviewReport != null) DrawScriptReview(hub, reviewedScript, reviewReport);
    }

    private static void DrawPresetRow(PresetHubModule hub, PresetEntry preset)
    {
        var status = hub.Installer.GetFamilyStatus(preset);
        ImGui.PushID(preset.Id);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(preset.Title);
        if(!string.IsNullOrWhiteSpace(preset.Author)) ImGui.TextDisabled(preset.Author);
        ImGui.TextDisabled(preset.VariantCount > 1
            ? $"{preset.VariantCount} source versions"
            : $"Confidence {preset.ConfidenceScore}/100");
        if(preset.LanguageDependent) ImGui.TextColored(ImGuiColors.DalamudYellow, "May depend on client language");
        if(preset.Format == PresetFormat.LegacyLayout) ImGui.TextDisabled("Legacy layout format");
        ImGui.TableNextColumn();
        ImGui.TextColored(preset.Kind == PresetKind.Script ? ImGuiColors.DalamudYellow : ImGuiColors.HealerGreen, preset.Kind.ToString());
        if(preset.Kind == PresetKind.Script && preset.Compatibility != PresetCompatibility.Compatible)
        {
            ImGui.TextColored(preset.Compatibility == PresetCompatibility.Incompatible
                    ? ImGuiColors.DalamudRed
                    : ImGuiColors.DalamudYellow,
                preset.Compatibility.ToString());
        }
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(string.Join(" / ", new[] { preset.Expansion, preset.Category, preset.Duty }.Where(x => x.Length > 0)));
        ImGui.TableNextColumn();
        var repositories = preset.Variants.Select(x => x.RepositoryName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ImGui.TextUnformatted(preset.VariantCount > 1 ? $"{repositories.Length} source(s)" : preset.RepositoryName);
        ImGui.TextDisabled(preset.VariantCount > 1
            ? $"{preset.VariantCount} versions available"
            : preset.Sources.Count > 1
                ? $"{preset.Trust} · {preset.Sources.Count - 1} mirror(s) collapsed"
                : preset.Trust.ToString());
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(status.ToString());
        ImGui.TableNextColumn();

        var primaryActionDrawn = false;
        if(preset.VariantCount > 1)
        {
            if(ImGui.Button($"Choose ({preset.VariantCount})")) selectedFamily = preset;
            primaryActionDrawn = true;
        }
        else if(preset.Kind == PresetKind.Script)
        {
            if(ImGui.Button(status == InstallationStatus.UpdateAvailable ? "Review update" : "Review"))
            {
                reviewedScript = preset;
                reviewReport = hub.SecurityAnalyzer.Analyze(preset.Content);
                reviewConfirmed = false;
            }
            primaryActionDrawn = true;
        }
        else if(status != InstallationStatus.Installed && ImGui.Button(status == InstallationStatus.UpdateAvailable ? "Update" : "Install"))
        {
            hub.Installer.InstallLayout(preset, out actionMessage);
            primaryActionDrawn = true;
        }
        else if(status != InstallationStatus.Installed)
        {
            primaryActionDrawn = true;
        }

        if(status != InstallationStatus.NotInstalled)
        {
            if(primaryActionDrawn) ImGui.SameLine();
            if(ImGui.Button("Uninstall")) hub.Installer.UninstallFamily(preset, out actionMessage);
        }
        ImGui.PopID();
    }

    private static void DrawVariantPicker(PresetHubModule hub, PresetEntry family)
    {
        ImGui.Separator();
        PresetVariantSelector.DrawInstaller(hub, family, "PresetHubVariantPicker", ref actionMessage, OpenScriptReview);

        if(ImGui.Button("Close choices")) selectedFamily = null;
    }

    private static void DrawScriptReview(PresetHubModule hub, PresetEntry preset, ScriptSecurityReport report)
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"Security review: {preset.Title}");
        ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow,
            "C# scripts run with Splatoon's access. Static analysis reduces surprises but cannot prove that a script is safe.");
        ImGui.TextUnformatted($"Source: {preset.RepositoryName} ({preset.Trust})");
        ImGui.TextUnformatted($"Compatibility: {preset.Compatibility}");
        if(!string.IsNullOrWhiteSpace(preset.CompatibilityDetail))
            ImGuiEx.TextWrapped(preset.Compatibility == PresetCompatibility.Incompatible
                ? ImGuiColors.DalamudRed
                : ImGuiColors.DalamudYellow, preset.CompatibilityDetail);
        ImGui.TextUnformatted($"SHA-256: {report.ContentHash}");

        foreach(var finding in report.Findings)
        {
            var color = finding.Severity switch
            {
                SecuritySeverity.High => ImGuiColors.DalamudRed,
                SecuritySeverity.Warning => ImGuiColors.DalamudYellow,
                _ => ImGuiColors.DalamudGrey,
            };
            ImGui.TextColored(color, $"[{finding.Severity}] {finding.Title}{(finding.Line is null ? "" : $" (line {finding.Line})")}");
            ImGui.SameLine();
            ImGui.TextDisabled(finding.Detail);
        }

        if(ImGui.TreeNode("Reviewed source code"))
        {
            var source = preset.Content;
            ImGui.InputTextMultiline("##PresetHubSource", ref source, Math.Max(source.Length + 1, 2),
                new(-1, 180f.Scale()), ImGuiInputTextFlags.ReadOnly);
            ImGui.TreePop();
        }

        ImGui.Checkbox("I reviewed this exact hash and accept running it", ref reviewConfirmed);
        var installationBlocked = !reviewConfirmed || preset.Compatibility == PresetCompatibility.Incompatible;
        if(installationBlocked) ImGui.BeginDisabled();
        if(ImGui.Button(hub.Installer.GetStatus(preset) == InstallationStatus.UpdateAvailable ? "Install reviewed update" : "Install reviewed script"))
        {
            hub.Installer.InstallReviewedScript(preset, report, out actionMessage);
            reviewedScript = null;
            reviewReport = null;
            reviewConfirmed = false;
        }
        if(installationBlocked) ImGui.EndDisabled();
        ImGui.SameLine();
        if(ImGui.Button("Close review"))
        {
            reviewedScript = null;
            reviewReport = null;
            reviewConfirmed = false;
        }
    }

    private static void DrawRepositories(PresetHubModule hub)
    {
        ImGuiEx.TextWrapped("Choose the sources to include in your catalogue.");
        if(ImGui.BeginTable("PresetHubRepositories", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 65f.Scale());
            ImGui.TableSetupColumn("Repository");
            ImGui.TableSetupColumn("Ref", ImGuiTableColumnFlags.WidthFixed, 90f.Scale());
            ImGui.TableSetupColumn("Trust", ImGuiTableColumnFlags.WidthFixed, 90f.Scale());
            ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthFixed, 100f.Scale());
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 80f.Scale());
            ImGui.TableHeadersRow();
            foreach(var repository in hub.Repositories)
            {
                ImGui.PushID(repository.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var enabled = repository.Enabled;
                if(ImGui.Checkbox("##enabled", ref enabled)) hub.SetEnabled(repository.Id, enabled);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(repository.FullName);
                ImGui.TextDisabled(string.Join("; ", repository.PathPrefixes));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(repository.Ref);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(repository.Trust.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(repository.Role.ToString());
                if(!repository.AllowScriptInstallation) ImGui.TextDisabled("Scripts blocked");
                ImGui.TableNextColumn();
                if(repository.Id != "punishxiv-splatoon" && ImGui.Button("Remove")) hub.RemoveRepository(repository.Id);
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Add GitHub repository");
        ImGui.SetNextItemWidth(260f.Scale());
        ImGui.InputTextWithHint("##repo", "owner/repository", ref repositoryInput, 200);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100f.Scale());
        ImGui.InputTextWithHint("##ref", "branch/tag", ref referenceInput, 100);
        ImGui.SetNextItemWidth(360f.Scale());
        ImGui.InputTextWithHint("##paths", "Folders, separated by ; (optional)", ref pathsInput, 500);
        ImGui.SameLine();
        if(ImGui.Button("Add and index"))
        {
            if(hub.AddRepository(repositoryInput, referenceInput, pathsInput, out repositoryError))
            {
                repositoryInput = "";
                repositoryError = "";
            }
        }
        if(!string.IsNullOrWhiteSpace(repositoryError)) ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, repositoryError);
    }

    private static void DrawDutyPrompts(PresetHubModule hub)
    {
        var preferences = hub.DutyPromptPreferences;
        var enabled = preferences.Enabled;
        if(ImGui.Checkbox("Show coverage when entering duties", ref enabled)) hub.SetDutyPromptsEnabled(enabled);
        ImGuiEx.TextWrapped("Choose available presets and updates when entering a duty.");

        ImGui.Separator();
        ImGui.TextUnformatted("Areas with a hidden coverage panel");
        if(preferences.SuppressedTerritoryIds.Count == 0)
        {
            ImGui.TextDisabled("None");
            return;
        }

        foreach(var territoryId in preferences.SuppressedTerritoryIds.Order())
        {
            ImGui.PushID((int)territoryId);
            ImGui.TextUnformatted(ExcelTerritoryHelper.GetName(territoryId, true));
            ImGui.SameLine();
            if(ImGui.SmallButton("Show panel")) hub.SetDutySuppressed(territoryId, false);
            ImGui.PopID();
        }
    }

    private static void DrawNullableEnumCombo<T>(string label, ref T? value) where T : struct, Enum
    {
        ImGui.SetNextItemWidth(120f.Scale());
        if(ImGui.BeginCombo($"##{label}", value?.ToString() ?? label))
        {
            if(ImGui.Selectable($"All {label.ToLowerInvariant()}", value == null)) value = null;
            foreach(var option in Enum.GetValues<T>())
            {
                if(ImGui.Selectable(option.ToString(), EqualityComparer<T?>.Default.Equals(value, option))) value = option;
            }
            ImGui.EndCombo();
        }
    }

    private static bool DrawStringCombo(string label, ref string value, IEnumerable<string> values, float width = 150f)
    {
        var changed = false;
        ImGui.SetNextItemWidth(width.Scale());
        if(ImGui.BeginCombo($"##{label}", value.Length == 0 ? label : value))
        {
            if(ImGui.Selectable($"All {label.ToLowerInvariant()}", value.Length == 0))
            {
                value = "";
                changed = true;
            }
            foreach(var option in values.Where(x => x.Length > 0).Distinct().Order())
            {
                if(ImGui.Selectable(option, value == option))
                {
                    value = option;
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        return changed;
    }
}
