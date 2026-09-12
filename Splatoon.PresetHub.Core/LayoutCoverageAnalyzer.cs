using System.Text.Json;
using System.Text.Json.Nodes;

namespace Splatoon.PresetHub.Core;

/// <summary>
/// Reads layout predicates rather than the indexer's descriptive ID inventory.
/// Never executes scripts or removes a condition from a generated fragment.
/// </summary>
public static class LayoutCoverageAnalyzer
{
    /// <summary>Scope an already-installed global/blacklist layout to the current
    /// territory without broadening its original activation conditions.</summary>
    public static CoverageAnalysis AnalyzeInstalled(PresetEntry preset, uint territory)
    {
        if(preset.TerritoryIds.Count > 0) return Analyze(preset);
        try
        {
            var start = preset.Content.IndexOf('{');
            if(start < 0 || JsonNode.Parse(preset.Content[start..]) is not JsonObject root) return new([], []);
            var zones = Ids(root, "ZoneLockH").ToArray();
            var allowed = Bool(root, "IsZoneBlacklist") ? !zones.Contains(territory) : zones.Length == 0 || zones.Contains(territory);
            if(!allowed) return new([], []);
            root["IsZoneBlacklist"] = false;
            root["ZoneLockH"] = new JsonArray(JsonValue.Create(territory));
            var content = "~Lv2~" + root.ToJsonString();
            return Analyze(preset with { Content = content, ContentHash = ContentHash.Sha256(content), TerritoryIds = [territory] });
        }
        catch(JsonException) { return new([], []); }
    }

    // These fields affect rendering, not the event that selects an actor. Different
    // renderings with the same predicates are alternatives within the same aid role.
    private static readonly HashSet<string> Appearance = new(StringComparer.Ordinal)
    {
        "Name", "InternationalName", "color", "Filled", "fillIntensity", "overrideFillColor",
        "originFillColor", "endFillColor", "overlayBGColor", "overlayTextColor", "overlayVOffset",
        "overlayFScale", "overlayText", "overlayTextIntl", "overlayPlaceholders", "thicc", "radius",
        "Donut", "coneAngleMin", "coneAngleMax", "type", "offX", "offY", "offZ", "refX", "refY", "refZ",
        "includeHitbox", "includeOwnHitbox", "includeRotation", "AdditionalRotation", "tether",
        "ExtraTetherLength", "LineEndA", "LineEndB", "castAnimation", "animationColor", "pulseSize",
        "pulseFrequency", "FillStep", "LegacyFill", "RenderEngineKind", "mechanicType",
        "EnablePointerLine", "PointerLineStyle",
    };

    public static CoverageAnalysis Analyze(PresetEntry preset)
    {
        if(preset.Kind != PresetKind.Layout) return new([], []);
        if(preset.Compatibility == PresetCompatibility.Incompatible) return new([], [$"{preset.Id}: incompatible layout"]);
        try
        {
            var start = preset.Content.IndexOf('{');
            if(start < 0 || JsonNode.Parse(preset.Content[start..]) is not JsonObject root)
                return new([], [$"{preset.Id}: invalid layout"]);
            if(Bool(root, "IsZoneBlacklist") || preset.TerritoryIds.Count == 0)
                return new([], [$"{preset.Id}: no explicit territory scope"]);

            var elements = Elements(root);
            if(elements.Count == 0) return new([], []);
            var enabled = !False(root, "Enabled") && !Bool(root, "Nodraw") &&
                          (Int(root, "DCond") != 5 || Bool(root, "UseTriggers"));
            var parts = DependencyParts(root, elements);
            var output = new List<CoverageContribution>();
            foreach(var territory in preset.TerritoryIds.Distinct().Order())
            {
                foreach(var part in parts.Where(x => x.Linked))
                {
                    var active = part.Elements.Where(IsDrawing).ToArray();
                    if(active.Length == 0) continue;
                    var claims = active.SelectMany(element => Claims(root, element, territory)).DistinctBy(x => x.AidId).ToArray();
                    output.Add(Build(preset, root, part.Elements, claims, territory, true, enabled));
                }
                {
                    // An OR-list of casts can be split without changing its meaning.
                    // Inverted tests and all linked layouts keep the original predicate.
                    var fragments = parts.Where(x => !x.Linked).SelectMany(x => x.Elements).Where(IsDrawing)
                        .SelectMany(original => SplitCasts(original).Select(element =>
                            (Element: element, Claims: Claims(root, element, territory, original).ToArray())));
                    foreach(var group in fragments.GroupBy(x => string.Join('|', x.Claims.Select(c => c.AidId).Order())))
                    {
                        var fragmentElements = group.Select(x => x.Element).ToArray();
                        var claims = group.SelectMany(x => x.Claims).DistinctBy(x => x.AidId).ToArray();
                        output.Add(Build(preset, root, fragmentElements, claims, territory, false, enabled));
                    }
                }
            }
            return new(output, []);
        }
        catch(Exception exception) when(exception is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return new([], [$"{preset.Id}: {exception.Message}"]);
        }
    }

    private static CoverageContribution Build(PresetEntry preset, JsonObject root,
        IReadOnlyList<JsonObject> elements, IReadOnlyList<CoverageClaim> claims, uint territory,
        bool linked, bool enabled)
    {
        var layout = (JsonObject)root.DeepClone();
        layout.Remove("Elements");
        layout.Remove("PresetHubInstallationId");
        layout["ElementsL"] = new JsonArray(elements.Select(x => x.DeepClone()).ToArray());
        layout["ZoneLockH"] = new JsonArray(JsonValue.Create(territory));
        var identity = string.Join('|', claims.Select(x => x.AidId).Order());
        // Source identity stays stable when geometry/text are corrected upstream.
        var id = ContentHash.Sha256($"{territory}:{preset.Id}:{identity}");
        // Explicit <element:layout:name> references resolve through the original
        // layout name. Preserve it for indivisible dependency groups.
        layout["Name"] = linked ? Text(root, "Name") : "Coverage " + id[..16];
        layout["Group"] = "Preset Hub";
        var behavior = (JsonObject)layout.DeepClone();
        behavior.Remove("Name");
        behavior.Remove("Group");
        behavior.Remove("Description");
        behavior.Remove("InternationalName");
        behavior.Remove("InternationalDescription");
        if(!linked)
            foreach(var element in (JsonArray)behavior["ElementsL"]!)
            {
                ((JsonObject)element!).Remove("Name");
                ((JsonObject)element).Remove("InternationalName");
            }
        return new()
        {
            Id = id, TerritoryId = territory, SourcePresetId = preset.Id,
            RepositoryId = preset.RepositoryId, RepositoryName = preset.RepositoryName,
            SourceUri = preset.SourceUri, SourceTitle = preset.Title,
            LayoutContent = "~Lv2~" + layout.ToJsonString(),
            Fingerprint = ContentHash.Sha256(Canonical(behavior)), Claims = claims,
            Linked = linked, Automatic = enabled, LanguageDependent = preset.LanguageDependent,
            LanguageRank = LanguageRank(elements, preset), Confidence = preset.ConfidenceScore,
            ElementCount = elements.Count,
            Detail = !enabled ? "Disabled in source" : linked ? "Linked drawings" : "",
        };
    }

    private static IEnumerable<CoverageClaim> Claims(JsonObject root, JsonObject element, uint territory, JsonObject? original = null)
    {
        var (actorKey, nameId) = Actor(element);
        var signals = Signals(element).Distinct().OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
        var primary = signals.Any(x => x.Kind == "Action") ? signals.Where(x => x.Kind == "Action").ToArray() : signals;
        var eventKey = primary.Length > 0 ? string.Join('+', primary.Select(x => x.Key)) : "ambient";
        var mechanicId = ContentHash.Sha256($"{territory}:{actorKey}:{eventKey}");
        var predicate = Predicate(element);
        NormalizeReferences(predicate, root, []);
        var shell = (JsonObject)root.DeepClone();
        foreach(var field in new[] { "Name", "Group", "InternationalName", "Description", "InternationalDescription",
                    "PresetHubInstallationId", "Elements", "ElementsL", "ZoneLockH" }) shell.Remove(field);
        var condition = Canonical(shell) + Canonical(predicate) + ConditionalGuard(root, original ?? element);
        var title = Text(element, "Name");
        if(title.Length == 0) title = signals.Length > 0 ? string.Join(" / ", signals.Select(x => x.Key)) : "Other aid";
        var roles = new List<string>();
        if(Float(element, "radius", .35f) > 0 || Int(element, "type") is 2 or 3 or 4 or 5)
            roles.Add(Int(element, "mechanicType") switch
            { 2 => "Safe area", 3 => "Soak", 4 => "Gaze", 5 => "Knockback", 6 => "Information", _ => "Area" });
        if(Text(element, "overlayText").Length > 0 || Nonempty(element, "overlayTextIntl")) roles.Add("Text");
        if(Bool(element, "tether") || Bool(element, "EnablePointerLine")) roles.Add("Link");
        if(roles.Count == 0) roles.Add("Marker");
        // Keep geometry+text of one element together. Splitting properties would
        // silently change placeholder and actor-relative behavior.
        var role = string.Join(" + ", roles);
        // Without a positive event, coordinates are essential to distinguish
        // independent permanent markers in the same arena.
        var spatial = signals.Length == 0 ? string.Join(':', new[] { "refX", "refY", "refZ", "offX", "offY", "offZ" }
            .Select(field => Float(element, field).ToString("R", System.Globalization.CultureInfo.InvariantCulture))) : "";
        yield return new()
        {
            MechanicId = mechanicId, AidId = ContentHash.Sha256($"{mechanicId}:{condition}:{role}:{spatial}"),
            Name = title, ActorKey = actorKey, ActorNameId = nameId, Signals = signals, Role = role,
        };
    }

    private static JsonObject Predicate(JsonObject element)
    {
        var result = (JsonObject)element.DeepClone();
        foreach(var field in Appearance) result.Remove(field);
        if(Int(element, "type") is not (1 or 3 or 4))
            foreach(var field in result.Select(x => x.Key).Where(x => x.StartsWith("refActor", StringComparison.Ordinal)).ToArray()) result.Remove(field);
        // Values left behind by a disabled editor checkbox are not predicates.
        RemoveUnless(result, Bool(element, "refActorRequireCast"), "refActorCastId", "refActorCastReverse", "refActorUseCastTime",
            "refActorCastTimeMin", "refActorCastTimeMax", "refActorUseOvercast");
        RemoveUnless(result, Bool(element, "refActorRequireBuff"), "refActorBuffId", "refActorRequireAllBuffs",
            "refActorRequireBuffsInvert", "refActorUseBuffTime", "refActorBuffTimeMin", "refActorBuffTimeMax",
            "refActorUseBuffParam", "refActorBuffParam");
        RemoveUnless(result, Bool(element, "refActorUseCastTime"), "refActorCastTimeMin", "refActorCastTimeMax");
        RemoveUnless(result, Bool(element, "refActorUseBuffTime"), "refActorBuffTimeMin", "refActorBuffTimeMax");
        RemoveUnless(result, Bool(element, "refActorUseBuffParam"), "refActorBuffParam");
        RemoveUnless(result, Bool(element, "refActorObjectLife"), "refActorLifetimeMin", "refActorLifetimeMax");
        RemoveUnless(result, Bool(element, "refActorUseTransformation"), "refActorTransformationID");
        RemoveUnless(result, Bool(element, "LimitDistance"), "DistanceMin", "DistanceMax", "DistanceSourceX", "DistanceSourceY", "DistanceSourceZ",
            "DistanceSourcePlaceholder", "UseDistanceSourcePlaceholder", "LimitDistanceInvert");
        RemoveUnless(result, Bool(element, "LimitRotation"), "RotationMin", "RotationMax");
        RemoveUnless(result, Bool(element, "UseHitboxRadius"), "HitboxRadiusMin", "HitboxRadiusMax");
        foreach(var field in new[] { "refActorCastId", "refActorBuffId", "ObjectKinds", "AnimationIds", "refActorPlaceholder" })
            if(result[field] is JsonArray values) result[field] = new JsonArray(values.OrderBy(Canonical, StringComparer.Ordinal).Select(x => x?.DeepClone()).ToArray());
        if(!Bool(element, "refActorComparisonAnd"))
        {
            var fields = new[] { "refActorName", "refActorModelID", "refActorObjectID", "refActorDataID", "refActorNPCID",
                "refActorPlaceholder", "refActorNPCNameID", "refActorVFXPath", "refActorObjectEffectData1", "refActorNamePlateIconID" };
            var selected = Int(element, "refActorComparisonType");
            for(var i = 0; i < fields.Length; i++) if(i != selected) result.Remove(fields[i]);
            if(selected != 0) result.Remove("refActorNameIntl");
            if(selected != 7) { result.Remove("refActorVFXMin"); result.Remove("refActorVFXMax"); }
            if(selected != 8)
                foreach(var field in new[] { "refActorObjectEffectData2", "refActorObjectEffectMin", "refActorObjectEffectMax", "refActorObjectEffectLastOnly" }) result.Remove(field);
        }
        return result;
    }

    private static string ConditionalGuard(JsonObject root, JsonObject target)
    {
        var conditions = new List<string>();
        foreach(var element in Elements(root))
        {
            // A reset acts before drawing the resetting element itself.
            if(Bool(element, "Conditional") && Bool(element, "ConditionalReset")) conditions.Clear();
            if(ReferenceEquals(element, target)) break;
            if(Bool(element, "Conditional"))
            {
                var predicate = Predicate(element);
                NormalizeReferences(predicate, root, []);
                conditions.Add(Canonical(predicate));
            }
        }
        return string.Join('|', conditions);
    }

    public static IReadOnlyList<CoverageSignal> ActiveSignals(string elementJson) =>
        Signals(JsonNode.Parse(elementJson)!.AsObject()).Distinct().ToArray();

    private static IEnumerable<CoverageSignal> Signals(JsonObject element)
    {
        // Fixed-coordinate shapes bypass character matching in both renderers.
        if(Int(element, "type") is not (1 or 3 or 4)) yield break;
        if(Bool(element, "refActorRequireCast") && !Bool(element, "refActorCastReverse"))
            foreach(var id in Ids(element, "refActorCastId")) yield return new("Action", id.ToString());
        if(Bool(element, "refActorRequireBuff") && !Bool(element, "refActorRequireBuffsInvert"))
            foreach(var id in Ids(element, "refActorBuffId")) yield return new("Status", id.ToString());
        var all = Bool(element, "refActorComparisonAnd");
        var actorMode = Int(element, "refActorType");
        if(actorMode == 0 && (all || Int(element, "refActorComparisonType") == 7) && Text(element, "refActorVFXPath").Length > 0)
            yield return new("VFX", Text(element, "refActorVFXPath"));
        if(actorMode == 0 && (all || Int(element, "refActorComparisonType") == 8) &&
           (Int(element, "refActorObjectEffectData1") != 0 || Int(element, "refActorObjectEffectData2") != 0))
            yield return new("ObjectEffect", $"{Int(element, "refActorObjectEffectData1")}/{Int(element, "refActorObjectEffectData2")}");
        if(Bool(element, "refActorTether") && !Bool(element, "refActorIsTetherInvert"))
            yield return new("Tether", $"{element["refActorTetherParam1"]}/{element["refActorTetherParam2"]}/{element["refActorTetherParam3"]}");
        if(!Bool(element, "AnimationInverted"))
            foreach(var id in Ids(element, "AnimationIds")) yield return new("Animation", id.ToString());
        // MapEffects is serialized by Element but is not a runtime predicate in
        // this upstream version; do not advertise those values as coverage.
    }

    private static (string Key, uint NameId) Actor(JsonObject element)
    {
        if(Int(element, "type") is not (1 or 3 or 4) || Int(element, "refActorType") != 0) return ("", 0);
        var all = Bool(element, "refActorComparisonAnd");
        var comparison = Int(element, "refActorComparisonType");
        foreach(var (mode, field) in new[] { (6, "refActorNPCNameID"), (4, "refActorNPCID"), (3, "refActorDataID") })
        {
            var value = UInt(element, field);
            if(value > 0 && (all || comparison == mode)) return ($"{field}:{value}", mode == 3 ? 0 : value);
        }
        return ("", 0);
    }

    private static IReadOnlyList<(IReadOnlyList<JsonObject> Elements, bool Linked)> DependencyParts(
        JsonObject root, IReadOnlyList<JsonObject> elements)
    {
        if(Bool(root, "UseTriggers") || Nonempty(root, "Subconfigurations") || Bool(root, "Freezing") ||
           Nonempty(root, "BlacklistedProjectorActions") || Nonempty(root, "ForcedProjectorActions") ||
           root["ProjectionState"] != null) return [(elements, true)];
        var parent = Enumerable.Range(0, elements.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Join(int a, int b) => parent[Find(b)] = Find(a);
        var conditional = -1;
        for(var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            if(Bool(element, "Conditional") && (conditional < 0 || Bool(element, "ConditionalReset"))) conditional = i;
            if(conditional >= 0) Join(conditional, i);
            foreach(var reference in References(element))
            {
                var parts = reference[1..^1].Split(':');
                if(parts.Length > 3 || parts.Length == 3 && parts[1] != Text(root, "Name"))
                    return [(elements, true)]; // External dependency: retain its original scope.
                var target = parts[^1];
                var matches = Enumerable.Range(0, elements.Count).Where(j => Text(elements[j], "Name") == target).ToArray();
                if(matches.Length != 1) return [(elements, true)];
                Join(i, matches[0]);
            }
        }
        return Enumerable.Range(0, elements.Count).GroupBy(Find).Select(group =>
        {
            var part = group.Select(i => elements[i]).ToArray();
            return ((IReadOnlyList<JsonObject>)part, part.Length > 1 || part.Any(e => Bool(e, "Conditional") || Bool(e, "IsCapturing")));
        }).ToArray();
    }

    private static IEnumerable<string> References(JsonNode? node)
    {
        if(node is JsonObject obj) return obj.SelectMany(x => References(x.Value));
        if(node is JsonArray array) return array.SelectMany(References);
        if(node is JsonValue value && value.TryGetValue<string>(out var text))
            return System.Text.RegularExpressions.Regex.Matches(text, @"<element:[^>]+>").Select(x => x.Value);
        return [];
    }

    private static void NormalizeReferences(JsonNode? node, JsonObject root, HashSet<string> visited)
    {
        if(node is JsonObject obj)
            foreach(var property in obj.ToArray())
                if(property.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[property.Key] = NormalizeText(text, root, visited);
                else NormalizeReferences(property.Value, root, visited);
        else if(node is JsonArray array)
            for(var i = 0; i < array.Count; i++)
                if(array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = NormalizeText(text, root, visited);
                else NormalizeReferences(array[i], root, visited);
    }

    private static string NormalizeText(string text, JsonObject root, HashSet<string> visited) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"<element:[^>]+>", match =>
        {
            var parts = match.Value[1..^1].Split(':');
            if(parts.Length > 3 || parts.Length == 3 && parts[1] != Text(root, "Name")) return match.Value;
            var name = parts[^1];
            if(!visited.Add(name)) return "<capture:cycle>";
            var matches = Elements(root).Where(x => Text(x, "Name") == name).ToArray();
            if(matches.Length != 1) { visited.Remove(name); return match.Value; }
            var predicate = Predicate(matches[0]);
            NormalizeReferences(predicate, root, visited);
            visited.Remove(name);
            return "<capture:" + ContentHash.Sha256(Canonical(predicate)) + ">";
        });

    private static IEnumerable<JsonObject> SplitCasts(JsonObject element)
    {
        var ids = Ids(element, "refActorCastId").ToArray();
        if(Int(element, "type") is 1 or 3 or 4 && Bool(element, "refActorRequireCast") && !Bool(element, "refActorCastReverse") && ids.Length > 1)
        {
            foreach(var id in ids)
            {
                var clone = (JsonObject)element.DeepClone();
                clone["refActorCastId"] = new JsonArray(JsonValue.Create(id));
                yield return clone;
            }
        }
        else yield return element;
    }

    private static IReadOnlyList<JsonObject> Elements(JsonObject root) =>
        root["ElementsL"] is JsonArray modern && modern.Count > 0 ? modern.OfType<JsonObject>().ToArray() :
        root["Elements"] is JsonObject legacy ? legacy.Select(x => x.Value).OfType<JsonObject>().ToArray() : [];

    private static bool IsDrawing(JsonObject element) => !False(element, "Enabled") && !Bool(element, "Nodraw");

    private static int LanguageRank(IReadOnlyList<JsonObject> elements, PresetEntry preset)
    {
        var text = string.Join(' ', elements.Select(e => Text(e, "overlayText")));
        // Prefer readable English exports wherever they live, not just in EN Set.
        if(text.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff')) return 0;
        if(text.Any(char.IsLetter)) return 3;
        return preset.Title.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff') ? 1 : 2;
    }

    internal static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(',', obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => JsonSerializer.Serialize(x.Key) + ":" + Canonical(x.Value))) + "}",
        JsonArray array => "[" + string.Join(',', array.Select(Canonical)) + "]",
        JsonValue value when value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number)
            => number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture),
        _ => node?.ToJsonString() ?? "null",
    };

    private static void RemoveUnless(JsonObject node, bool enabled, params string[] fields)
    { if(!enabled) foreach(var field in fields) node.Remove(field); }
    private static IEnumerable<uint> Ids(JsonObject node, string key) => node[key] is JsonArray array
        ? array.OfType<JsonValue>().Select(x => x.TryGetValue<uint>(out var value) ? value : 0).Where(x => x > 0).Distinct().Order()
        : [];
    private static bool Nonempty(JsonObject node, string key) => node[key] switch
    { JsonArray array => array.Count > 0, JsonObject obj => obj.Count > 0, _ => false };
    private static bool Bool(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<bool>(out var b) && b;
    private static bool False(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<bool>(out var b) && !b;
    private static int Int(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;
    private static uint UInt(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<uint>(out var n) ? n : 0;
    private static float Float(JsonObject node, string key, float fallback = 0) => node[key] is JsonValue value && value.TryGetValue<float>(out var n) ? n : fallback;
    private static string Text(JsonObject node, string key) => node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
