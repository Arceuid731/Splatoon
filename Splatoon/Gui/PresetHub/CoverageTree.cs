#nullable enable
using Dalamud.Bindings.ImGui;

namespace Splatoon.Gui.PresetHub;

internal static class CoverageTree
{
    internal static string Label(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    internal static bool Node(string id, string? label, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        // Empty spans reach a null pointer in Dalamud's FindRenderedTextEnd binding.
        // Keep identity independent of translated labels and changing coverage counts.
        return ImGui.TreeNodeEx(id, flags, Label(label, "Other"));
    }
}
