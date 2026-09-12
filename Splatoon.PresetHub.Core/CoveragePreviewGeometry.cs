using System.Numerics;
using System.Text.Json;

namespace Splatoon.PresetHub.Core;

/// <summary>Top-down geometry using the DirectX renderer's X/Y (Z-height) conventions.</summary>
public sealed record CoveragePreviewGeometry
{
    public int Type { get; init; }
    public bool Fixed { get; init; }
    public Vector2 Start { get; init; }
    public Vector2 End { get; init; }
    public Vector2 Player { get; init; }
    public Vector2? TetherEnd { get; init; }
    public float Radius { get; init; }
    public float InnerRadius { get; init; }
    public float AngleMin { get; init; }
    public float AngleMax { get; init; }
    public bool Visible { get; init; }
    public IReadOnlyList<string> Approximations { get; init; } = [];

    public static CoveragePreviewGeometry Build(string json, float headingDegrees, float actorHitbox = 2, float playerHitbox = .5f)
    {
        using var document = JsonDocument.Parse(json);
        var e = document.RootElement;
        float N(string key, float fallback = 0) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetSingle(out var n) && float.IsFinite(n) ? n : fallback;
        bool B(string key, bool fallback = false) => e.TryGetProperty(key, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;
        var type = (int)N("type");
        var angle = headingDegrees * MathF.PI / 180;
        var extra = N("AdditionalRotation");
        var relative = type is 1 or 3 or 4;
        var own = (int)N("refActorType") == 1;
        var player = own ? Vector2.Zero : new Vector2(0, -8);
        Vector2 Rotate(Vector2 point, float radians) => new(point.X * MathF.Cos(radians) - point.Y * MathF.Sin(radians),
            point.X * MathF.Sin(radians) + point.Y * MathF.Cos(radians));
        var radius = Math.Max(0, N("radius", .35f));
        if(relative)
        {
            if(B("includeOwnHitbox")) radius += playerHitbox;
            if(B("includeHitbox") && !own) radius += actorHitbox;
        }
        var start = Vector2.Zero;
        var end = Vector2.Zero;
        if(type is 0 or 1)
            start = B("includeRotation")
                ? Rotate(new(-N("offX"), N("offY")), -(relative ? angle : 0) + extra)
                : new(N("offX"), N("offY"));
        else if(type == 2)
            end = new(N("offX") - N("refX"), N("offY") - N("refY"));
        else if(type == 3)
        {
            var rotated = B("includeRotation");
            start = new((rotated ? -1 : 1) * N("refX"), N("refY"));
            end = new((rotated ? -1 : 1) * N("offX"), N("offY"));
            if(radius == 0)
            {
                start += new Vector2(B("LineAddHitboxLengthXA") ? actorHitbox : 0, B("LineAddHitboxLengthYA") ? actorHitbox : 0)
                    + new Vector2(B("LineAddPlayerHitboxLengthXA") ? playerHitbox : 0, B("LineAddPlayerHitboxLengthYA") ? playerHitbox : 0);
                end += new Vector2(B("LineAddHitboxLengthX") ? actorHitbox : 0, B("LineAddHitboxLengthY") ? actorHitbox : 0)
                    + new Vector2(B("LineAddPlayerHitboxLengthX") ? playerHitbox : 0, B("LineAddPlayerHitboxLengthY") ? playerHitbox : 0);
            }
            if(rotated) { start = Rotate(start, -angle + extra); end = Rotate(end, -angle + extra); }
        }
        else if(type is 4 or 5)
            start = Rotate(new(-N("offX"), N("offY")), type == 4 ? -angle : 0);

        var donut = type is 0 or 1 or 4 or 5 && N("Donut") > 0;
        var inner = donut ? radius : 0;
        if(donut) radius += N("Donut");
        Vector2? tether = null;
        if(B("tether") && type is 0 or 1 or 4 or 5)
        {
            tether = player;
            if(N("ExtraTetherLength") > 0 && Vector2.DistanceSquared(player, start) > .0001f)
                tether += Vector2.Normalize(player - start) * N("ExtraTetherLength");
        }
        var notes = new List<string>();
        if(B("includeHitbox") || B("includeOwnHitbox")) notes.Add("Example hitbox sizes");
        if(B("FaceMe") || B("RotationOverride") || B("UseCastRotation")) notes.Add("Direction uses the preview slider");
        if(B("UsePlaceholderAsRefPosition") || B("UsePlaceholderAsOffPosition") || B("UseCastPosition") || B("UseCastTarget"))
            notes.Add("Position requires encounter data");
        if(B("EnablePointerLine")) notes.Add("Pointer line is not previewed");
        return new()
        {
            Type = type, Fixed = !relative, Start = start, End = end, Player = player, TetherEnd = tether,
            Radius = radius, InnerRadius = inner,
            AngleMin = -(type == 4 ? angle : 0) + extra + N("coneAngleMin") * MathF.PI / 180,
            AngleMax = -(type == 4 ? angle : 0) + extra + N("coneAngleMax") * MathF.PI / 180,
            Visible = B("Enabled", true) && !B("Nodraw") && type is >= 0 and <= 5,
            Approximations = notes,
        };
    }
}
