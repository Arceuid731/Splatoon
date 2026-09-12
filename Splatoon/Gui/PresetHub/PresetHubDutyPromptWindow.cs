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
    private readonly HashSet<string> selectedFamilies = [];
    private readonly Dictionary<string, string> selectedChoices = new(StringComparer.Ordinal);
    private IReadOnlyList<PresetEntry> suggestions = [];
    private string expandedFamilyId = "";
    private string actionMessage = "";

    internal uint TerritoryId { get; private set; }

    internal void Show(uint territoryId, IReadOnlyList<PresetEntry> dutySuggestions)
    {
        TerritoryId = territoryId;
        SetSuggestions(dutySuggestions, selectAllLayouts: true);
        actionMessage = "";
        IsOpen = true;
    }

    public override void Draw()
    {
        var dutyName = ExcelTerritoryHelper.GetName(TerritoryId, true);
        ImGui.TextUnformatted($"Preset Hub found presets for {dutyName}");
        ImGuiEx.TextWrapped("A selection is ready to install. Other versions remain available below.");
        ImGui.Separator();

        if(ImGui.BeginTable("DutyPresetSuggestions", 5,
               ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
               new(1000f.Scale(), Math.Min(420f.Scale(), (suggestions.Count + 1) * ImGui.GetTextLineHeightWithSpacing() * 3f))))
        {
            ImGui.TableSetupColumn("Use", ImGuiTableColumnFlags.WidthFixed, 42f.Scale());
            ImGui.TableSetupColumn("Preset", ImGuiTableColumnFlags.WidthFixed, 220f.Scale());
            ImGui.TableSetupColumn("Selected version");
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 70f.Scale());
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 115f.Scale());
            ImGui.TableHeadersRow();
            foreach(var family in suggestions)
            {
                var selectedChoice = PresetVariantSelector.Resolve(family, selectedChoices.GetValueOrDefault(family.Id));
                ImGui.PushID(family.Id);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if(family.Kind == PresetKind.Layout)
                {
                    var selected = selectedFamilies.Contains(family.Id);
                    if(ImGui.Checkbox("##selected", ref selected))
                    {
                        if(selected) selectedFamilies.Add(family.Id);
                        else selectedFamilies.Remove(family.Id);
                    }
                }
                else
                {
                    ImGui.TextDisabled("-");
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(family.Title);
                ImGui.TextDisabled(family.VariantCount > 1 ? $"{family.VariantCount} available choices" : family.Duty);
                ImGui.TableNextColumn();
                PresetVariantSelector.DrawCompactSummary(family, selectedChoice);
                if(family.VariantCount > 1 && selectedChoice.Id != PresetVariantSelector.Recommended(family).Id)
                {
                    foreach(var difference in PresetVariantComparison
                                .DescribeDifferences(selectedChoice, PresetVariantSelector.Recommended(family)).Take(2))
                        ImGui.TextDisabled(difference);
                }
                ImGui.TableNextColumn();
                ImGui.TextColored(family.Kind == PresetKind.Script ? ImGuiColors.DalamudYellow : ImGuiColors.HealerGreen,
                    family.Kind.ToString());
                ImGui.TextDisabled(hub.Installer.GetFamilyStatus(family).ToString());
                ImGui.TableNextColumn();
                if(family.Kind == PresetKind.Script && family.VariantCount == 1)
                {
                    if(ImGui.Button("Review")) OpenScriptReview(selectedChoice);
                }
                else
                {
                    var expanded = expandedFamilyId == family.Id;
                    var label = family.VariantCount > 1 ? (expanded ? "Hide choices" : $"Choose ({family.VariantCount})") :
                        (expanded ? "Hide details" : "Details");
                    if(ImGui.Button(label)) expandedFamilyId = expanded ? "" : family.Id;
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        var expandedFamily = suggestions.FirstOrDefault(x => x.Id == expandedFamilyId);
        if(expandedFamily != null)
        {
            ImGui.Separator();
            var previousChoice = selectedChoices.GetValueOrDefault(expandedFamily.Id,
                PresetVariantSelector.Recommended(expandedFamily).Id);
            var selectedChoice = PresetVariantSelector.DrawSelection(hub, expandedFamily,
                $"DutyPresetVariantPicker-{expandedFamily.Id}", previousChoice, OpenScriptReview, 1000f.Scale());
            selectedChoices[expandedFamily.Id] = selectedChoice;
            if(selectedChoice != previousChoice && expandedFamily.Kind == PresetKind.Layout)
                selectedFamilies.Add(expandedFamily.Id);
        }

        if(!string.IsNullOrWhiteSpace(actionMessage)) ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, actionMessage);
        if(ImGui.Button("Recommended selection"))
        {
            selectedFamilies.Clear();
            foreach(var recommendation in hub.RecommendDutyLayouts(TerritoryId, suggestions))
            {
                selectedFamilies.Add(recommendation.Key);
                selectedChoices[recommendation.Key] = recommendation.Value;
            }
        }
        ImGui.SameLine();
        if(ImGui.Button("Clear selection")) selectedFamilies.Clear();

        var selectedCount = suggestions.Count(x => x.Kind == PresetKind.Layout && selectedFamilies.Contains(x.Id));
        if(selectedCount == 0) ImGui.BeginDisabled();
        if(ImGui.Button($"Install selected presets ({selectedCount})"))
        {
            var selections = suggestions
                .Where(x => x.Kind == PresetKind.Layout && selectedFamilies.Contains(x.Id))
                .Select(family => new PresetHubLayoutSelection(family,
                    PresetVariantSelector.Resolve(family, selectedChoices.GetValueOrDefault(family.Id))))
                .ToArray();
            var previousSelected = selectedFamilies.ToHashSet(StringComparer.Ordinal);
            var previousChoices = new Dictionary<string, string>(selectedChoices, StringComparer.Ordinal);
            var result = hub.Installer.InstallLayoutChoices(selections);
            actionMessage = result.Message;
            SetSuggestions(hub.GetDutySuggestions(TerritoryId), selectAllLayouts: false, previousSelected, previousChoices);
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

    private void SetSuggestions(
        IReadOnlyList<PresetEntry> dutySuggestions,
        bool selectAllLayouts,
        IReadOnlySet<string>? previousSelected = null,
        IReadOnlyDictionary<string, string>? previousChoices = null)
    {
        suggestions = dutySuggestions;
        selectedFamilies.Clear();
        selectedChoices.Clear();
        var recommended = hub.RecommendDutyLayouts(TerritoryId, suggestions);
        foreach(var family in suggestions)
        {
            var previousChoice = previousChoices?.GetValueOrDefault(family.Id) ?? recommended.GetValueOrDefault(family.Id);
            selectedChoices[family.Id] = PresetVariantSelector.Resolve(family, previousChoice).Id;
            if(family.Kind == PresetKind.Layout &&
               ((selectAllLayouts && recommended.ContainsKey(family.Id)) || previousSelected?.Contains(family.Id) == true))
                selectedFamilies.Add(family.Id);
        }
        if(suggestions.All(x => x.Id != expandedFamilyId)) expandedFamilyId = "";
    }

    private void OpenScriptReview(PresetEntry preset)
    {
        TabPresetHub.OpenScriptReview(preset);
        P.ConfigGui.TabRequest = "Preset Hub";
        P.ConfigGui.IsOpen = true;
        IsOpen = false;
    }
}
