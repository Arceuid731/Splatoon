using System.Numerics;
using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class CoveragePreviewTests
{
    [Fact]
    public void RelativeRectanglePreservesBothEndpointsAndRendererRotation()
    {
        var shape = CoveragePreviewGeometry.Build("{\"type\":3,\"refX\":2,\"refY\":3,\"offX\":4,\"offY\":20,\"radius\":5,\"includeRotation\":true}", 0);
        Assert.Equal(new Vector2(-2, 3), shape.Start);
        Assert.Equal(new Vector2(-4, 20), shape.End);
        Assert.Equal(5, shape.Radius);
        var rotated = CoveragePreviewGeometry.Build("{\"type\":3,\"offY\":20,\"includeRotation\":true}", 90);
        Assert.InRange(Vector2.Distance(rotated.End, new Vector2(20, 0)), 0, .0001f);
    }

    [Fact]
    public void FixedLineKeepsDistanceAndIgnoresActorHeading()
    {
        var shape = CoveragePreviewGeometry.Build("{\"type\":2,\"refX\":100,\"refY\":100,\"offX\":103,\"offY\":104}", 90);
        Assert.True(shape.Fixed);
        Assert.Equal(new Vector2(3, 4), shape.End);
    }

    [Fact]
    public void DonutAndKnockbackPredictionRetainInnerRadiusAndExtraLength()
    {
        var shape = CoveragePreviewGeometry.Build("{\"type\":1,\"radius\":5,\"Donut\":3,\"tether\":true,\"ExtraTetherLength\":13}", 0);
        Assert.Equal(5, shape.InnerRadius);
        Assert.Equal(8, shape.Radius);
        Assert.Equal(new Vector2(0, -21), shape.TetherEnd);
    }

    [Fact]
    public void ConeHasCorrectAnglesAndExplicitDynamicLimitations()
    {
        var shape = CoveragePreviewGeometry.Build("{\"type\":4,\"coneAngleMin\":-45,\"coneAngleMax\":45,\"FaceMe\":true,\"includeHitbox\":true,\"radius\":8}", 90);
        Assert.InRange(shape.AngleMin, -3 * MathF.PI / 4 - .0001f, -3 * MathF.PI / 4 + .0001f);
        Assert.Equal(10, shape.Radius);
        Assert.Equal(2, shape.Approximations.Count);
    }

    [Fact]
    public void CaptureOnlyElementDoesNotPretendToRenderADrawing()
    {
        Assert.False(CoveragePreviewGeometry.Build("{\"type\":1,\"Nodraw\":true,\"IsCapturing\":true}", 0).Visible);
    }
}
