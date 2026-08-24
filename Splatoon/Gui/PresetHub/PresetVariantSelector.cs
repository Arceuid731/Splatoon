using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;

namespace Splatoon.Gui.PresetHub;

internal static class PresetVariantSelector
{
    internal static IReadOnlyList<PresetEntry> Choices(PresetEntry family) =>
        family.Variants.Count > 0 ? family.Variants : [family];

    internal static PresetEntry Recommended(PresetEntry family) => Choices(family)[0];

    internal static PresetEntry Resolve(PresetEntry family, string? choiceId) =>
        Choices(family).FirstOrDefault(x => x.Id == choiceId) ?? Recommended(family);

    internal static void DrawCompactSummary(PresetEntry family, PresetEntry choice)
    {
        ImGui.TextDisabled($"{choice.RepositoryName} · confidence {choice.ConfidenceScore}/100 · " +
                           PresetVariantComparison.ContentSummary(choice));
        if(choice.Id == Recommended(family).Id && family.VariantCount > 1)
            ImGui.TextColored(ImGuiColors.HealerGreen, "Recommended choice");
        if(choice.LanguageDependent)
            ImGui.TextColored(ImGuiColors.DalamudYellow, "May depend on client language");
    }

    internal static void DrawInstaller(
        PresetHubModule hub,
        PresetEntry family,
        string tableId,
        ref string actionMessage,
        Action<PresetEntry> reviewScript,
        float width = 0f)
    {
        DrawHeader(family);
        DrawTable(hub, family, tableId, null, installImmediately: true, ref actionMessage, reviewScript, width);
    }

    internal static string DrawSelection(
        PresetHubModule hub,
        PresetEntry family,
        string tableId,
        string selectedChoiceId,
        Action<PresetEntry> reviewScript,
        float width = 0f)
    {
        var ignoredMessage = "";
        DrawHeader(family);
        return DrawTable(hub, family, tableId, selectedChoiceId, installImmediately: false,
            ref ignoredMessage, reviewScript, width);
    }

    private static void DrawHeader(PresetEntry family)
    {
        ImGui.TextUnformatted($"Choose a version: {family.Title}");
        ImGuiEx.TextWrapped("Versions share the same duty and normalized preset name, but their actual overlay content differs. " +
                            "The recommendation favors compatibility, territory metadata, language-independent matching, and source confidence.");
    }

    private static string DrawTable(
        PresetHubModule hub,
        PresetEntry family,
        string tableId,
        string? selectedChoiceId,
        bool installImmediately,
        ref string actionMessage,
        Action<PresetEntry> reviewScript,
        float width)
    {
        var choices = Choices(family);
        var recommended = Recommended(family);
        var selected = Resolve(family, selectedChoiceId);
        var installed = hub.Installer.GetInstalledChoice(family);
        if(ImGui.BeginTable(tableId, 6,
               ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
               new(width, Math.Min(330f.Scale(), (choices.Count + 1) * 95f.Scale()))))
        {
            ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthFixed, 155f.Scale());
            ImGui.TableSetupColumn("Confidence", ImGuiTableColumnFlags.WidthFixed, 145f.Scale());
            ImGui.TableSetupColumn("Contains", ImGuiTableColumnFlags.WidthFixed, 160f.Scale());
            ImGui.TableSetupColumn("Compared with recommended", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Compatibility", ImGuiTableColumnFlags.WidthFixed, 145f.Scale());
            ImGui.TableSetupColumn(installImmediately ? "Action" : "Choice", ImGuiTableColumnFlags.WidthFixed, 100f.Scale());
            ImGui.TableHeadersRow();
            foreach(var choice in choices)
            {
                ImGui.PushID($"variant-{choice.Id}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(choice.RepositoryName);
                if(choice.Id == recommended.Id) ImGui.TextColored(ImGuiColors.HealerGreen, "Recommended");
                if(choice.Sources.Count > 1) ImGui.TextDisabled($"Also in {choice.Sources.Count - 1} mirror(s)");
                ImGui.TextDisabled(choice.RelativePath);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{choice.ConfidenceScore}/100");
                foreach(var note in choice.ConfidenceNotes.Take(3)) ImGui.TextDisabled(note);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(PresetVariantComparison.ContentSummary(choice));
                foreach(var name in choice.Summary.ElementNames.Take(3)) ImGui.TextDisabled(name);
                if(choice.Summary.ElementNames.Count > 3) ImGui.TextDisabled($"+{choice.Summary.ElementNames.Count - 3} more");

                ImGui.TableNextColumn();
                foreach(var difference in PresetVariantComparison.DescribeDifferences(choice, recommended))
                    ImGui.TextDisabled(difference);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(choice.Compatibility.ToString());
                ImGui.TextDisabled(choice.Format.ToString());
                if(choice.LanguageDependent) ImGui.TextColored(ImGuiColors.DalamudYellow, "Language-dependent");

                ImGui.TableNextColumn();
                if(!installImmediately)
                {
                    if(choice.Kind == PresetKind.Script)
                    {
                        if(ImGui.Button("Review")) reviewScript(choice);
                    }
                    else if(ImGui.RadioButton("Select", selected.Id == choice.Id))
                    {
                        selected = choice;
                    }
                }
                else
                {
                    DrawInstallAction(hub, family, choice, installed, ref actionMessage, reviewScript);
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        return selected.Id;
    }

    private static void DrawInstallAction(
        PresetHubModule hub,
        PresetEntry family,
        PresetEntry choice,
        PresetEntry? installed,
        ref string actionMessage,
        Action<PresetEntry> reviewScript)
    {
        var status = hub.Installer.GetStatus(choice);
        if(status == InstallationStatus.Installed)
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, "Installed");
        }
        else if(choice.Kind == PresetKind.Script)
        {
            if(installed != null)
            {
                if(ImGui.Button("Uninstall first")) hub.Installer.UninstallFamily(family, out actionMessage);
            }
            else if(ImGui.Button("Review"))
            {
                reviewScript(choice);
            }
        }
        else
        {
            var action = installed == null ? "Install" : status == InstallationStatus.UpdateAvailable ? "Update" : "Replace";
            if(ImGui.Button(action)) hub.Installer.InstallLayoutChoice(family, choice, out actionMessage);
        }
    }
}
