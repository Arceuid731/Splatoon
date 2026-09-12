using ECommons.ImGuiMethods;
using Newtonsoft.Json;
using Splatoon.PresetHub.Core;

namespace Splatoon.Gui.PresetHub;

/// <summary>Isolated top-down drawing preview. Never injects a live game layout.</summary>
internal static class CoveragePreview
{
    private static string shownId = "";
    private static Layout layout;
    private static int elementIndex;
    private static float heading;

    internal static void Draw(CoverageContribution contribution)
    {
        if(shownId != contribution.Id)
        {
            shownId = contribution.Id;
            layout = JsonConvert.DeserializeObject<Layout>(contribution.LayoutContent[5..]);
            elementIndex = 0;
            heading = 0;
        }
        if(layout == null || layout.ElementsL.Count == 0) return;
        ImGui.TextUnformatted(contribution.SourceTitle);
        ImGui.TextDisabled("Drawing preview · example actor positions");
        ImGui.TextDisabled("Timing and encounter conditions are not simulated.");
        if(ImGui.BeginCombo("Drawing", layout.ElementsL[elementIndex].Name))
        {
            for(var index = 0; index < layout.ElementsL.Count; index++)
                if(ImGui.Selectable($"{layout.ElementsL[index].Name}##{index}", index == elementIndex)) elementIndex = index;
            ImGui.EndCombo();
        }
        ImGui.SliderFloat("Actor direction", ref heading, -180, 180, "%.0f°");
        var element = layout.ElementsL[elementIndex];
        var size = new Vector2(Math.Clamp(ImGui.GetContentRegionAvail().X, 240f.Scale(), 540f.Scale()), 300f.Scale());
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##DrawingPreview", size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xff201d1b);
        draw.PushClipRect(origin, origin + size, true);
        var center = origin + size / 2;
        var extent = Math.Max(12, element.radius + element.Donut + Math.Max(Math.Abs(element.offX), Math.Abs(element.offY)) + 3);
        var scale = Math.Min(size.X, size.Y) / (2 * extent);
        Vector2 Point(float x, float y) => center + new Vector2(x, -y) * scale;
        Vector2 Rotate(float x, float y)
        {
            var angle = heading * MathF.PI / 180 + element.AdditionalRotation;
            return new(x * MathF.Cos(angle) - y * MathF.Sin(angle), x * MathF.Sin(angle) + y * MathF.Cos(angle));
        }
        var offset = element.includeRotation ? Rotate(element.offX, element.offY) : new Vector2(element.offX, element.offY);
        var anchor = Point(offset.X, offset.Y);
        var color = element.color;
        var fill = (color & 0x00ffffff) | 0x44000000;
        var radius = Math.Max(0, element.radius) * scale;
        var thickness = Math.Max(1, element.thicc);
        draw.AddLine(center - new Vector2(size.X / 2, 0), center + new Vector2(size.X / 2, 0), 0xff454545);
        draw.AddLine(center - new Vector2(0, size.Y / 2), center + new Vector2(0, size.Y / 2), 0xff454545);
        if(element.type is 0 or 1)
        {
            if(element.Donut > 0)
            {
                var outer = radius + element.Donut * scale;
                for(var i = 0; i < 64; i++)
                {
                    var a = i * MathF.Tau / 64;
                    var b = (i + 1) * MathF.Tau / 64;
                    var va = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    var vb = new Vector2(MathF.Cos(b), MathF.Sin(b));
                    if(element.Filled) draw.AddQuadFilled(anchor + va * radius, anchor + va * outer, anchor + vb * outer, anchor + vb * radius, fill);
                }
                draw.AddCircle(anchor, outer, color, 64, thickness);
            }
            else if(element.Filled && radius > 0) draw.AddCircleFilled(anchor, radius, fill, 64);
            draw.AddCircle(anchor, Math.Max(2, radius), color, 64, thickness);
        }
        else if(element.type is 4 or 5)
        {
            var angleBase = (element.includeRotation ? heading : 0) * MathF.PI / 180 + element.AdditionalRotation;
            var low = element.coneAngleMin * MathF.PI / 180 + angleBase;
            var high = element.coneAngleMax * MathF.PI / 180 + angleBase;
            for(var i = 0; i < 48; i++)
            {
                var a = low + (high - low) * i / 48;
                var b = low + (high - low) * (i + 1) / 48;
                var p = anchor + new Vector2(MathF.Sin(a), -MathF.Cos(a)) * radius;
                var q = anchor + new Vector2(MathF.Sin(b), -MathF.Cos(b)) * radius;
                if(element.Filled) draw.AddTriangleFilled(anchor, p, q, fill);
                draw.AddLine(p, q, color, thickness);
                if(i == 0) draw.AddLine(anchor, p, color, thickness);
                if(i == 47) draw.AddLine(anchor, q, color, thickness);
            }
        }
        else
        {
            var end = element.type == 2 ? new Vector2(element.offX - element.refX, element.offY - element.refY) : offset;
            if(end.LengthSquared() < .01f) end = new(0, 8);
            var endpoint = Point(end.X, end.Y);
            var direction = Vector2.Normalize(endpoint - center);
            var normal = new Vector2(-direction.Y, direction.X) * radius;
            if(element.Filled) draw.AddQuadFilled(center - normal, center + normal, endpoint + normal, endpoint - normal, fill);
            draw.AddLine(center - normal, endpoint - normal, color, thickness);
            draw.AddLine(center + normal, endpoint + normal, color, thickness);
            draw.AddLine(center - normal, center + normal, color, thickness);
            draw.AddLine(endpoint - normal, endpoint + normal, color, thickness);
        }
        draw.AddCircleFilled(center, 4f.Scale(), 0xffffffff);
        draw.AddText(center + new Vector2(6, 6), 0xffffffff, "Actor");
        if(element.overlayText.Length > 0) draw.AddText(anchor + new Vector2(8, -20), element.overlayTextColor, element.overlayText);
        draw.AddText(origin + new Vector2(8, size.Y - 22), 0xffbbbbbb, $"Radius: {element.radius:0.##} m");
        draw.PopClipRect();
    }
}
