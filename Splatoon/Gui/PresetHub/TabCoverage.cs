using Dalamud.Interface.Colors;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Lumina.Excel.Sheets;
using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;
using Splatoon.SplatoonScripting;

namespace Splatoon.Gui.PresetHub;

internal static class TabCoverage
{
    private sealed record Area(uint Id, string Name, string Expansion, string Category);
    private static IReadOnlyList<Area> areas = [];
    private static CoverageLibrary lastLibrary;
    private static string search = "";
    private static bool withAidsOnly;
    private static uint selectedTerritory;
    private static string expandedMechanic = "";
    private static CoverageContribution preview;

    internal static void Draw(PresetHubModule hub)
    {
        if(!ReferenceEquals(lastLibrary, hub.Coverage))
        {
            lastLibrary = hub.Coverage;
            var covered = lastLibrary.Contributions.Select(x => x.TerritoryId).ToHashSet();
            areas = Svc.Data.GetExcelSheet<TerritoryType>()
                .Where(row => !string.IsNullOrWhiteSpace(row.ContentFinderCondition.ValueNullable?.Name.ToString()) || covered.Contains(row.RowId))
                .Select(row => new Area(row.RowId, ExcelTerritoryHelper.GetName(row.RowId, true),
                    row.ExVersion.ValueNullable?.Name.ToString() ?? "Other",
                    row.ContentFinderCondition.ValueNullable?.ContentType.ValueNullable?.Name.ToString() ?? "Other areas"))
                .OrderBy(x => x.Expansion).ThenBy(x => x.Category).ThenBy(x => x.Name).ToArray();
        }
        if(ImGui.Button(hub.IsSyncing ? "Updating coverage..." : "Update coverage") && !hub.IsSyncing) _ = hub.SyncAllAsync();
        ImGui.SameLine();
        var enabled = hub.CoveragePreferences.Enabled;
        if(ImGui.Checkbox("Enable aids", ref enabled)) hub.SetCoveragePreferences(hub.CoveragePreferences with { Enabled = enabled });
        ImGui.SameLine();
        if(ImGui.Button("Current area")) selectedTerritory = Svc.ClientState.TerritoryType;
        ImGui.TextDisabled(hub.CoverageMessage);
        ImGui.SetNextItemWidth(320f.Scale());
        ImGui.InputTextWithHint("##CoverageSearch", "Search instances...", ref search, 160);
        ImGui.SameLine();
        ImGui.Checkbox("With available aids", ref withAidsOnly);
        var availableTerritories = hub.Coverage.Contributions.Select(x => x.TerritoryId).ToHashSet();
        var filtered = areas.Where(x => (!withAidsOnly || availableTerritories.Contains(x.Id)) &&
            (search.Length == 0 || (x.Name + " " + x.Expansion + " " + x.Category).Contains(search, StringComparison.OrdinalIgnoreCase)));
        if(ImGui.BeginTable("CoveragePanels", 2, ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Areas", ImGuiTableColumnFlags.WidthFixed, 320f.Scale());
            ImGui.TableSetupColumn("Coverage", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if(ImGui.BeginChild("CoverageAreas", new(0, 0)))
            {
                foreach(var expansion in filtered.GroupBy(x => x.Expansion))
                {
                    if(!ImGui.TreeNodeEx(expansion.Key, search.Length > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) continue;
                    foreach(var category in expansion.GroupBy(x => x.Category))
                    {
                        if(!ImGui.TreeNodeEx(category.Key, search.Length > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None)) continue;
                        foreach(var area in category)
                        {
                            if(ImGui.Selectable($"{area.Name}##{area.Id}", selectedTerritory == area.Id)) selectedTerritory = area.Id;
                        }
                        ImGui.TreePop();
                    }
                    ImGui.TreePop();
                }
            }
            ImGui.EndChild();
            ImGui.TableNextColumn();
            if(ImGui.BeginChild("CoverageDetails", new(0, 0)))
            {
                if(selectedTerritory == 0) ImGui.TextDisabled("Select an instance or area.");
                else
                {
                    ImGui.TextUnformatted(ExcelTerritoryHelper.GetName(selectedTerritory, true));
                    DrawDuty(hub, selectedTerritory);
                }
            }
            ImGui.EndChild();
            ImGui.EndTable();
        }
    }

    internal static void DrawDuty(PresetHubModule hub, uint territory)
    {
        var contributions = hub.ContributionsFor(territory);
        var plan = hub.CoveragePlanFor(territory);
        var enabled = !hub.CoveragePreferences.DisabledTerritories.Contains(territory);
        if(ImGui.Checkbox("Enable this area's aids", ref enabled)) hub.SetTerritoryEnabled(territory, enabled);
        var known = contributions.SelectMany(x => x.Claims).DistinctBy(x => x.MechanicId).Count();
        var active = plan.Selected.SelectMany(x => x.Claims).Select(x => x.MechanicId).ToHashSet();
        ImGui.TextDisabled($"{active.Count} active · {known} identified mechanics");
        ImGui.TextDisabled("Coverage reflects the available sources.");
        if(contributions.Count == 0) ImGui.TextDisabled("No drawing found in the enabled sources.");
        var claims = contributions.SelectMany(option => option.Claims.Select(claim => (Claim: claim, Option: option))).ToArray();
        foreach(var actor in claims.GroupBy(x => x.Claim.ActorKey).OrderBy(x => ActorName(x.First().Claim)))
        {
            ImGui.PushID(actor.Key.Length == 0 ? "other" : actor.Key);
            var actorEnabled = !hub.CoveragePreferences.DisabledActors.Contains(CoveragePreferences.ActorPreferenceKey(territory, actor.Key));
            if(ImGui.Checkbox("##actor", ref actorEnabled)) hub.SetActorEnabled(territory, actor.Key, actorEnabled);
            ImGui.SameLine();
            var actorMechanics = actor.Select(x => x.Claim.MechanicId).Distinct().ToArray();
            var open = ImGui.TreeNodeEx($"{ActorName(actor.First().Claim)} ({actorMechanics.Count(active.Contains)}/{actorMechanics.Length})",
                ImGuiTreeNodeFlags.DefaultOpen);
            if(open)
            {
                foreach(var mechanic in actor.GroupBy(x => x.Claim.MechanicId).OrderBy(x => MechanicName(x.First().Claim)))
                {
                    var claim = mechanic.First().Claim;
                    var options = mechanic.Select(x => x.Option).DistinctBy(x => x.Id).ToArray();
                    ImGui.PushID(mechanic.Key);
                    var mechanicEnabled = !hub.CoveragePreferences.DisabledMechanics.Contains(mechanic.Key);
                    if(ImGui.Checkbox("##mechanic", ref mechanicEnabled)) hub.SetMechanicEnabled(mechanic.Key, mechanicEnabled);
                    ImGui.SameLine();
                    ImGui.TextUnformatted(MechanicName(claim));
                    ImGui.SameLine();
                    ImGui.TextColored(active.Contains(mechanic.Key) ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey,
                        active.Contains(mechanic.Key) ? "Active" : "Inactive");
                    ImGui.SameLine();
                    if(ImGui.SmallButton("Details")) expandedMechanic = expandedMechanic == mechanic.Key ? "" : mechanic.Key;
                    if(expandedMechanic == mechanic.Key)
                    {
                        foreach(var option in options.OrderByDescending(x => plan.Selected.Any(s => s.Id == x.Id)).ThenByDescending(x => x.LanguageRank))
                        {
                            ImGui.PushID(option.Id);
                            var selected = plan.Selected.Any(x => x.Id == option.Id);
                            ImGui.TextUnformatted($"{(selected ? "• " : "")}{option.SourceTitle} · {option.RepositoryName}");
                            ImGui.TextDisabled(string.Join(", ", option.Claims.Where(x => x.MechanicId == mechanic.Key).Select(x => x.Role).Distinct()));
                            if(option.Detail.Length > 0) ImGui.TextDisabled(option.Detail);
                            if(option.Linked) ImGui.TextDisabled($"{option.Claims.Select(x => x.MechanicId).Distinct().Count()} mechanics share these drawings.");
                            if(ImGui.SmallButton("Preview")) preview = option;
                            ImGui.SameLine();
                            if(ImGui.SmallButton("Source")) ECommons.GenericHelpers.ShellStart(option.SourceUri);
                            if(!selected && option.Automatic)
                            {
                                ImGui.SameLine();
                                if(ImGui.SmallButton("Use this version"))
                                {
                                    var alternatives = new Dictionary<string, string>(hub.CoveragePreferences.SelectedAlternatives)
                                        { [CoveragePlanner.CoverageKey(option)] = option.Id };
                                    hub.SetCoveragePreferences(hub.CoveragePreferences with { SelectedAlternatives = alternatives });
                                }
                            }
                            ImGui.PopID();
                        }
                    }
                    ImGui.PopID();
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
        var scripts = hub.Presets.Where(x => x.Kind == PresetKind.Script && x.TerritoryIds.Contains(territory)).ToArray();
        if(scripts.Length > 0 && ImGui.TreeNodeEx("Scripted aids", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Listed by script; individual mechanics are not indexed.");
            foreach(var script in scripts)
            {
                ImGui.PushID(script.Id);
                var running = ScriptingProcessor.Scripts.FirstOrDefault(x => x.InternalData.FullName == script.RuntimeIdentity);
                ImGui.TextUnformatted(script.Title);
                ImGui.SameLine();
                ImGui.TextDisabled(running?.IsEnabled == true ? "Active" : "Inactive");
                ImGui.SameLine();
                if(ImGui.SmallButton("Review"))
                {
                    TabPresetHub.OpenScriptReview(script);
                    P.ConfigGui.TabRequest = "Preset Hub";
                    P.ConfigGui.IsOpen = true;
                }
                ImGui.PopID();
            }
            ImGui.TreePop();
        }
        if(preview != null && preview.TerritoryId == territory)
        {
            ImGui.Separator();
            CoveragePreview.Draw(preview);
            if(ImGui.Button("Close preview")) preview = null;
        }
    }

    private static string ActorName(CoverageClaim claim)
    {
        if(claim.ActorNameId > 0)
        {
            var name = Svc.Data.GetExcelSheet<BNpcName>().GetRowOrDefault(claim.ActorNameId)?.Singular.ToString();
            if(!string.IsNullOrWhiteSpace(name)) return name;
        }
        return claim.ActorKey.Length == 0 ? "Other aids in this area" : $"Other encounter · {claim.ActorKey}";
    }

    private static string MechanicName(CoverageClaim claim)
    {
        var names = claim.Signals.Select(signal =>
        {
            if(!uint.TryParse(signal.Value, out var id)) return "";
            return signal.Kind switch
            {
                "Action" => Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id)?.Name.ToString(),
                "Status" => Svc.Data.GetExcelSheet<Status>().GetRowOrDefault(id)?.Name.ToString(),
                _ => "",
            };
        }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        return names.Length > 0 ? string.Join(" / ", names) : claim.Name;
    }
}
