using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Core.Video;

namespace CamTurn.Core.Tests;

public class FrameAndMappingTests
{
    private static VideoFrame Numbered(int w, int h)
    {
        var f = VideoFrame.CreateBgra(w, h);
        for (var i = 0; i < w * h; i++)
        {
            BitConverter.GetBytes(i).CopyTo(f.Data, i * 4);
        }

        return f;
    }

    private static int At(VideoFrame f, int x, int y) => BitConverter.ToInt32(f.Data, (y * f.Stride) + (x * 4));

    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(90, 2, 3)]
    [InlineData(180, 3, 2)]
    [InlineData(270, 2, 3)]
    public void RotateSwapsDimensionsForQuarterTurns(int deg, int w, int h)
    {
        var r = FrameOps.RotateBgraClockwise(Numbered(3, 2), deg);
        Assert.Equal((w, h), (r.Width, r.Height));
    }

    [Fact]
    public void RotateMovesCornersClockwise()
    {
        var src = Numbered(3, 2); // top-left = 0, top-right = 2, bottom-left = 3
        var r90 = FrameOps.RotateBgraClockwise(src, 90);
        Assert.Equal(0, At(r90, 1, 0)); // top-left -> top-right
        Assert.Equal(2, At(r90, 1, 2)); // top-right -> bottom-right
        Assert.Equal(3, At(r90, 0, 0)); // bottom-left -> top-left

        var r180 = FrameOps.RotateBgraClockwise(src, 180);
        Assert.Equal(0, At(r180, 2, 1));

        var r270 = FrameOps.RotateBgraClockwise(src, 270);
        Assert.Equal(0, At(r270, 0, 2)); // top-left -> bottom-left
    }

    [Fact]
    public void FourQuarterTurnsAreIdentity()
    {
        var src = Numbered(5, 3);
        var r = src;
        for (var i = 0; i < 4; i++)
        {
            r = FrameOps.RotateBgraClockwise(r, 90);
        }

        Assert.Equal(src.Data, r.Data);
    }

    [Fact]
    public void Nv12RoundTripIsClose()
    {
        var src = VideoFrame.CreateBgra(4, 4);
        for (var i = 0; i < 16; i++)
        {
            src.Data[(i * 4) + 0] = 200;
            src.Data[(i * 4) + 1] = 120;
            src.Data[(i * 4) + 2] = 40;
            src.Data[(i * 4) + 3] = 255;
        }

        var back = FrameOps.Nv12ToBgra(FrameOps.BgraToNv12(src));
        for (var i = 0; i < src.Data.Length; i++)
        {
            Assert.InRange(Math.Abs(src.Data[i] - back.Data[i]), 0, 4);
        }
    }

    [Fact]
    public void DefaultMappingUndoesRollAndFlipSwapsQuarterTurns()
    {
        var m = RotationMapping.Default;
        foreach (var q in Enum.GetValues<Quadrant>())
        {
            Assert.Equal(0, ((int)q + m.CorrectionFor(q)) % 360);
        }

        var f = m.FlipDirection();
        Assert.Equal(90, f.CorrectionFor(Quadrant.Deg90));
        Assert.Equal(270, f.CorrectionFor(Quadrant.Deg270));
        Assert.Equal(180, f.CorrectionFor(Quadrant.Deg180));
        Assert.Equal(m, f.FlipDirection());
    }

    [Theory]
    [InlineData(110)]
    [InlineData(180)]
    [InlineData(300)]
    [InlineData(360)]
    public void LidFromBaseWithFlatBase(double hinge)
    {
        // Base flat on a desk reads z = -1; lid rises at (hinge - 90) from vertical.
        var lid = HingeKinematics.LidFromBase(new AccelReading(0, 0, -1, default), hinge);
        var h = hinge * Math.PI / 180;
        Assert.Equal(0, lid.X, 9);
        Assert.Equal(-Math.Sin(h), lid.Y, 9);
        Assert.Equal(Math.Cos(h), lid.Z, 9);
    }
}
