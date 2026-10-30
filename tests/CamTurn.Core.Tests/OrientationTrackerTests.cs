using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;

namespace CamTurn.Core.Tests;

public class OrientationTrackerTests
{
    private static AccelReading AtRoll(double rollDeg, int ms, double inPlane = 1.0)
    {
        var r = rollDeg * Math.PI / 180;
        var z = -Math.Sqrt(Math.Max(0, 1 - (inPlane * inPlane)));
        return new AccelReading(inPlane * Math.Sin(r), -inPlane * Math.Cos(r), z, TimeSpan.FromMilliseconds(ms));
    }

    [Theory]
    [InlineData(0, Quadrant.Deg0)]
    [InlineData(90, Quadrant.Deg90)]
    [InlineData(180, Quadrant.Deg180)]
    [InlineData(270, Quadrant.Deg270)]
    public void FirstReadingLocksImmediately(double roll, Quadrant expected)
    {
        var t = new OrientationTracker();
        var s = t.Update(AtRoll(roll, 0));
        Assert.Equal(expected, s.Quadrant);
        Assert.True(s.HasLock);
        Assert.False(s.Holding);
    }

    [Fact]
    public void SwitchRequiresThresholdAndHoldTime()
    {
        var t = new OrientationTracker();
        t.Update(AtRoll(0, 0));

        // 55° is past the 45° boundary but inside the 60° hysteresis band: no switch.
        for (var ms = 10; ms <= 1000; ms += 10)
        {
            Assert.Equal(Quadrant.Deg0, t.Update(AtRoll(55, ms)).Quadrant);
        }

        // 80° must be held for 300 ms.
        t.Update(AtRoll(80, 1010));
        Assert.Equal(Quadrant.Deg0, t.Update(AtRoll(80, 1300)).Quadrant);
        Assert.Equal(Quadrant.Deg90, t.Update(AtRoll(80, 1310)).Quadrant);
    }

    [Fact]
    public void BriefSpikeDoesNotSwitch()
    {
        var t = new OrientationTracker();
        t.Update(AtRoll(0, 0));
        t.Update(AtRoll(90, 100));
        t.Update(AtRoll(90, 250));
        t.Update(AtRoll(5, 300));
        Assert.Equal(Quadrant.Deg0, t.Update(AtRoll(90, 500)).Quadrant);
    }

    [Fact]
    public void FlatHoldsLastQuadrant()
    {
        var t = new OrientationTracker();
        t.Update(AtRoll(270, 0));
        for (var ms = 10; ms < 2000; ms += 10)
        {
            var s = t.Update(AtRoll(ms % 360, ms, inPlane: 0.1));
            Assert.Equal(Quadrant.Deg270, s.Quadrant);
            Assert.True(s.Holding);
        }
    }

    [Fact]
    public void FlatAtStartHasNoLock()
    {
        var t = new OrientationTracker();
        var s = t.Update(AtRoll(90, 0, inPlane: 0.05));
        Assert.False(s.HasLock);
        Assert.True(s.Holding);
        Assert.Equal(Quadrant.Deg90, t.Update(AtRoll(90, 10)).Quadrant);
    }

    [Fact]
    public void QuadrantChangedFiresOncePerChange()
    {
        var t = new OrientationTracker();
        var events = new List<Quadrant>();
        t.QuadrantChanged += (_, s) => events.Add(s.Quadrant);
        for (var ms = 0; ms <= 1000; ms += 10)
        {
            t.Update(AtRoll(ms < 500 ? 0 : 180, ms));
        }

        Assert.Equal([Quadrant.Deg0, Quadrant.Deg180], events);
    }
}
