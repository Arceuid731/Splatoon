using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;

namespace Splatoon.Gui.PresetHub;

internal sealed class PresetHubDutyPromptWindow(PresetHubModule hub) : Window(
    "Preset Hub - duty suggestions",
    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)
{
    private readonly HashSet<string> selectedLayouts = [];
    private IReadOnlyList<PresetEntry> suggestions = [];
    private string actionMessage = "";

    internal uint TerritoryId { get; private set; }

    internal void Show(uint territoryId, IReadOnlyList<PresetEntry> dutySuggestions)
    {
        TerritoryId = territoryId;
        suggestions = dutySuggestions;
        selectedLayouts.Clear();
        selectedLayouts.UnionWith(dutySuggestions.Where(x => x.Kind == PresetKind.Layout).Select(x => x.Id));
        actionMessage = "";
        IsOpen = true;
    }

    public override void Draw()
    {
        var dutyName = ExcelTerritoryHelper.GetName(TerritoryId, true);
        ImGui.TextUnformatted($"Preset Hub found presets for {dutyName}");
        ImGuiEx.TextWrapped("Choose the overlays you want. Nothing is installed automatically.");
        ImGui.Separator();

        if(ImGui.BeginTable("DutyPresetSuggestions", 4,
               ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
               new(700f.Scale(), Math.Min(420f.Scale(), (suggestions.Count + 1) * ImGui.GetTextLineHeightWithSpacing() * 2f))))
        {
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 42f.Scale());
            ImGui.TableSetupColumn("Preset");
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 70f.Scale());
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 100f.Scale());
            ImGui.TableHeadersRow();
            foreach(var preset in suggestions)
            {
                ImGui.PushID(preset.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if(preset.Kind == PresetKind.Layout)
                {
                    var selected = selectedLayouts.Contains(preset.Id);
                    if(ImGui.Checkbox("##selected", ref selected))
                    {
                        if(selected) selectedLayouts.Add(preset.Id);
                        else selectedLayouts.Remove(preset.Id);
                    }
                }
                else
                {
                    ImGui.TextDisabled("-");
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(preset.Title);
                var detail = $"{preset.RepositoryName} · confidence {preset.ConfidenceScore}/100";
                if(preset.VariantCount > 1) detail += $" · {preset.VariantCount} versions";
                if(preset.Sources.Count > 1) detail += $" · {preset.Sources.Count} sources";
                ImGui.TextDisabled(detail);
                if(preset.LanguageDependent) ImGui.TextColored(ImGuiColors.DalamudYellow, "May depend on client language");
                ImGui.TableNextColumn();
                ImGui.TextColored(preset.Kind == PresetKind.Script ? ImGuiColors.DalamudYellow : ImGuiColors.HealerGreen,
                    preset.Kind.ToString());
                ImGui.TableNextColumn();
                if(preset.Kind == PresetKind.Script && ImGui.Button("Review"))
                {
                    TabPresetHub.OpenScriptReview(preset);
                    P.ConfigGui.TabRequest = "Preset Hub";
                    P.ConfigGui.IsOpen = true;
                    IsOpen = false;
                }
                else if(preset.Kind == PresetKind.Layout)
                {
                    ImGui.TextDisabled(hub.Installer.GetStatus(preset) == InstallationStatus.UpdateAvailable ? "Update" : "Install");
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        if(!string.IsNullOrWhiteSpace(actionMessage)) ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, actionMessage);
        var selectedCount = suggestions.Count(x => x.Kind == PresetKind.Layout && selectedLayouts.Contains(x.Id));
        if(selectedCount == 0) ImGui.BeginDisabled();
        if(ImGui.Button($"Install selected layouts ({selectedCount})"))
        {
            var installed = 0;
            foreach(var preset in suggestions.Where(x => x.Kind == PresetKind.Layout && selectedLayouts.Contains(x.Id)))
            {
                if(hub.Installer.InstallLayout(preset, out actionMessage)) installed++;
            }
            actionMessage = installed == selectedCount
                ? $"Installed {installed} selected layout(s)."
                : $"Installed {installed} of {selectedCount} selected layout(s). {actionMessage}";
            suggestions = hub.GetDutySuggestions(TerritoryId);
            selectedLayouts.IntersectWith(suggestions.Select(x => x.Id));
            if(suggestions.Count == 0) IsOpen = false;
        }
        if(selectedCount == 0) ImGui.EndDisabled();
        ImGui.SameLine();
        if(ImGui.Button("Later")) IsOpen = false;
        ImGui.SameLine();
        if(ImGui.Button("Don't suggest for this duty")) hub.SetDutySuppressed(TerritoryId, true);

        ImGui.Separator();
        var enabled = hub.DutyPromptPreferences.Enabled;
        if(ImGui.Checkbox("Suggest presets when entering duties", ref enabled)) hub.SetDutyPromptsEnabled(enabled);
    }
}
