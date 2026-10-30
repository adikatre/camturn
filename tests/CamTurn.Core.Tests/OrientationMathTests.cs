using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;

namespace CamTurn.Core.Tests;

public class OrientationMathTests
{
    [Theory]
    [InlineData(0, -1, 0)] // upright
    [InlineData(1, 0, 90)] // right edge down
    [InlineData(0, 1, 180)] // upside down
    [InlineData(-1, 0, 270)] // left edge down
    public void RollFollowsWindowsAxisConvention(double x, double y, double expected) =>
        Assert.Equal(expected, OrientationMath.RollDegrees(x, y), 6);

    [Theory]
    [InlineData(0, Quadrant.Deg0)]
    [InlineData(44.9, Quadrant.Deg0)]
    [InlineData(45.1, Quadrant.Deg90)]
    [InlineData(134, Quadrant.Deg90)]
    [InlineData(180, Quadrant.Deg180)]
    [InlineData(250, Quadrant.Deg270)]
    [InlineData(316, Quadrant.Deg0)]
    [InlineData(-30, Quadrant.Deg0)]
    [InlineData(-60, Quadrant.Deg270)]
    public void NearestQuadrant(double degrees, Quadrant expected) =>
        Assert.Equal(expected, OrientationMath.NearestQuadrant(degrees));

    [Theory]
    [InlineData(10, 350, 20)]
    [InlineData(0, 180, 180)]
    [InlineData(90, 270, 180)]
    [InlineData(-10, 10, 20)]
    public void AngularDistanceWraps(double a, double b, double expected) =>
        Assert.Equal(expected, OrientationMath.AngularDistance(a, b), 9);

    [Fact]
    public void InPlaneFractionIsOneUprightAndZeroFlat()
    {
        Assert.Equal(1, OrientationMath.InPlaneFraction(new AccelReading(0, -1, 0, default)), 9);
        Assert.Equal(0, OrientationMath.InPlaneFraction(new AccelReading(0, 0, -1, default)), 9);
        Assert.Equal(0, OrientationMath.InPlaneFraction(default));
    }
}
