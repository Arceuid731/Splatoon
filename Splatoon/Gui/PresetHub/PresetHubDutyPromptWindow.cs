using Dalamud.Interface.Windowing;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Splatoon.Modules.PresetHub;

namespace Splatoon.Gui.PresetHub;

internal sealed class PresetHubDutyPromptWindow(PresetHubModule hub) : Window(
    "Preset Hub - area coverage", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings)
{
    internal uint TerritoryId { get; private set; }

    internal void Show(uint territoryId)
    {
        TerritoryId = territoryId;
        Size = new(820f.Scale(), 580f.Scale());
        SizeCondition = ImGuiCond.Appearing;
        IsOpen = true;
    }

    public override void Draw()
    {
        ImGui.TextUnformatted(ExcelTerritoryHelper.GetName(TerritoryId, true));
        if(ImGui.BeginChild("DutyCoverage", new(0, -ImGui.GetFrameHeightWithSpacing() * 2)))
            TabCoverage.DrawDuty(hub, TerritoryId);
        ImGui.EndChild();
        if(ImGui.Button("Close")) IsOpen = false;
        ImGui.SameLine();
        if(ImGui.Button("Don't show for this area")) hub.SetDutySuppressed(TerritoryId, true);
        var enabled = hub.DutyPromptPreferences.Enabled;
        if(ImGui.Checkbox("Show coverage when entering duties", ref enabled)) hub.SetDutyPromptsEnabled(enabled);
    }
}
