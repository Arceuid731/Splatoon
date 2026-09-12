using ECommons.ImGuiMethods;
using Newtonsoft.Json;
using Splatoon.PresetHub.Core;
using Splatoon.Utility;

namespace Splatoon.Gui.PresetHub;

/// <summary>Isolated top-down drawing preview. Never injects a live game layout.</summary>
internal static class CoveragePreview
{
    private static string shownFingerprint = "";
    private static Layout layout;
    private static int elementIndex;
    private static float heading;

    internal static void Draw(CoverageContribution contribution)
    {
        if(shownFingerprint != contribution.Fingerprint)
        {
            shownFingerprint = contribution.Fingerprint;
            try { layout = JsonConvert.DeserializeObject<Layout>(contribution.LayoutContent[5..]); }
            catch { layout = null; }
            elementIndex = layout?.ElementsL.FindIndex(x => x.Enabled && !x.Nodraw) ?? 0;
            elementIndex = Math.Max(0, elementIndex);
            heading = 0;
        }
        if(layout == null || layout.ElementsL.Count == 0)
        { ImGui.TextDisabled("Preview unavailable for this source."); return; }
        ImGui.TextUnformatted(contribution.SourceTitle);
        ImGui.TextDisabled("Top-down preview · example positions");
        ImGui.TextDisabled("Timing and encounter conditions are not simulated.");
        if(ImGui.BeginCombo("Drawing", layout.ElementsL[elementIndex].InternationalName.Get(layout.ElementsL[elementIndex].Name)))
        {
            for(var index = 0; index < layout.ElementsL.Count; index++)
            {
                var item = layout.ElementsL[index];
                if(ImGui.Selectable($"{item.InternationalName.Get(item.Name)}##{index}", index == elementIndex)) elementIndex = index;
            }
            ImGui.EndCombo();
        }
        ImGui.SliderFloat("Actor direction", ref heading, -180, 180, "%.0f°");
        var element = layout.ElementsL[elementIndex];
        var shape = CoveragePreviewGeometry.Build(element.Serialize(), heading);
        foreach(var note in shape.Approximations) ImGui.TextDisabled(note);
        if(!shape.Visible) { ImGui.TextDisabled("This element does not draw an overlay."); return; }
        var style = element.GetDisplayStyleWithOverride();
        if(style.originFillColor != style.endFillColor || (int)element.castAnimation != 0)
            ImGui.TextDisabled("Static color preview; gradients and animations are not shown.");
        var size = new Vector2(Math.Clamp(ImGui.GetContentRegionAvail().X, 220f.Scale(), 540f.Scale()), 300f.Scale());
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##DrawingPreview", size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xff201d1b);
        draw.PushClipRect(origin, origin + size, true);
        var extent = Math.Max(12, Math.Max(shape.Start.Length() + shape.Radius,
            Math.Max(shape.End.Length() + shape.Radius, shape.TetherEnd?.Length() ?? 0)) + 3);
        var scale = Math.Min(size.X, size.Y) / (2 * extent);
        var center = origin + size / 2;
        Vector2 Point(Vector2 p) => center + new Vector2(p.X, -p.Y) * scale;
        var anchor = Point(shape.Start);
        var color = style.strokeColor;
        var fill = style.originFillColor;
        var radius = shape.Radius * scale;
        var inner = shape.InnerRadius * scale;
        var thickness = Math.Max(0, style.strokeThickness);
        void Stroke(Vector2 a, Vector2 b) { if(thickness > 0) draw.AddLine(a, b, color, thickness); }
        void Ring(float low, float high, int count)
        {
            for(var i = 0; i < count; i++)
            {
                var a = low + (high - low) * i / count;
                var b = low + (high - low) * (i + 1) / count;
                // Fan angles follow the renderer's world X/Y coordinates.
                var va = new Vector2(MathF.Cos(a), -MathF.Sin(a));
                var vb = new Vector2(MathF.Cos(b), -MathF.Sin(b));
                if(style.filled)
                    draw.AddQuadFilled(anchor + va * inner, anchor + va * radius, anchor + vb * radius, anchor + vb * inner, fill);
                Stroke(anchor + va * radius, anchor + vb * radius);
                if(inner > 0) Stroke(anchor + va * inner, anchor + vb * inner);
                if(i == 0 && high - low < MathF.Tau) Stroke(anchor + va * inner, anchor + va * radius);
                if(i == count - 1 && high - low < MathF.Tau) Stroke(anchor + vb * inner, anchor + vb * radius);
            }
        }
        draw.AddLine(center - new Vector2(size.X / 2, 0), center + new Vector2(size.X / 2, 0), 0xff454545);
        draw.AddLine(center - new Vector2(0, size.Y / 2), center + new Vector2(0, size.Y / 2), 0xff454545);
        if(shape.Type is 0 or 1)
        {
            if(radius > 0) Ring(0, MathF.Tau, 80);
            else if(thickness > 0) draw.AddCircleFilled(anchor, thickness, color);
        }
        else if(shape.Type is 4 or 5)
        {
            var high = Math.Min(shape.AngleMax, shape.AngleMin + MathF.Tau);
            if(high > shape.AngleMin) Ring(shape.AngleMin, high, 64);
        }
        else
        {
            var endpoint = Point(shape.End);
            if(Vector2.DistanceSquared(endpoint, anchor) > .0001f)
            {
                var direction = Vector2.Normalize(endpoint - anchor);
                var normal = new Vector2(-direction.Y, direction.X) * radius;
                if(radius == 0) Stroke(anchor, endpoint);
                else
                {
                    if(style.filled) draw.AddQuadFilled(anchor - normal, anchor + normal, endpoint + normal, endpoint - normal, fill);
                    Stroke(anchor - normal, endpoint - normal);
                    Stroke(anchor + normal, endpoint + normal);
                    Stroke(anchor - normal, anchor + normal);
                    Stroke(endpoint - normal, endpoint + normal);
                }
            }
        }
        if(shape.TetherEnd is { } tetherEnd)
        {
            var end = Point(tetherEnd);
            Stroke(anchor, end);
            if((int)element.LineEndB != 0 && Vector2.DistanceSquared(end, anchor) > .001f)
            {
                var direction = Vector2.Normalize(end - anchor);
                var normal = new Vector2(-direction.Y, direction.X);
                draw.AddTriangleFilled(end, end - direction * 9 + normal * 4, end - direction * 9 - normal * 4, color);
            }
            draw.AddCircleFilled(Point(shape.Player), 3f.Scale(), 0xff60e8ff);
            draw.AddText(Point(shape.Player) + new Vector2(6, 0), 0xff60e8ff, "Player");
        }
        draw.AddCircleFilled(center, 3f.Scale(), 0xffffffff);
        draw.AddText(center + new Vector2(6, 6), 0xffffffff, shape.Fixed ? "Reference point" : "Actor");
        var text = element.overlayTextIntl.Get(element.overlayText).Replace("$ELEMENT", element.Name).Replace("$NAME", "Actor").Replace("\\n", "\n");
        if(text.Length > 0)
        {
            var position = anchor + new Vector2(8, -22);
            var textSize = ImGui.CalcTextSize(text);
            draw.AddRectFilled(position - new Vector2(2, 2), position + textSize + new Vector2(2, 2), element.overlayBGColor);
            draw.AddText(position, element.overlayTextColor, text);
        }
        draw.AddText(origin + new Vector2(8, size.Y - 22), 0xffbbbbbb, $"Radius: {shape.Radius:0.##} m");
        draw.PopClipRect();
    }
}
