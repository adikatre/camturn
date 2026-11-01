using CamTurn.Core.Video;

namespace CamTurn.Core.Tests;

public class CompositorTests
{
    // NV12 frame whose Y value encodes the pixel's (x, y) block so positions can be checked after transforms.
    private static VideoFrame Marked(int w, int h, Func<int, int, byte> y)
    {
        var data = new byte[FrameProtocol.Nv12Size(w, h)];
        for (var r = 0; r < h; r++)
        {
            for (var c = 0; c < w; c++)
            {
                data[(r * w) + c] = y(c, r);
            }
        }

        for (var i = w * h; i < data.Length; i += 2)
        {
            data[i] = 100; // U
            data[i + 1] = 200; // V
        }

        return new VideoFrame(w, h, PixelFormat.Nv12, data, w, 0, default);
    }

    // Top-left quadrant bright, rest dark: tells us where the original top-left ended up.
    private static VideoFrame Quadrants(int w, int h) => Marked(w, h, (x, y) => x < w / 2 && y < h / 2 ? (byte)235 : (byte)16);

    private static byte Y(byte[] f, int w, int x, int y) => f[(y * w) + x];

    [Theory]
    [InlineData(0, false, 0, 0)] // stays top-left
    [InlineData(90, false, 1, 0)] // clockwise: top-left -> top-right
    [InlineData(180, false, 1, 1)] // -> bottom-right
    [InlineData(270, false, 0, 1)] // -> bottom-left
    [InlineData(0, true, 1, 0)] // mirrored: -> top-right
    [InlineData(90, true, 0, 0)] // rotate then mirror
    public void RotationAndMirrorMoveTheTopLeftCorner(int rot, bool mirror, int qx, int qy)
    {
        // Square input on a square canvas isolates the rotation from scaling.
        var src = Quadrants(64, 64);
        var dst = new Compositor().Compose(src, new CompositorSettings(rot, mirror, FitMode.Fit, 64, 64));
        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var expected = cx == qx && cy == qy ? 235 : 16;
                Assert.Equal(expected, Y(dst, 64, (cx * 32) + 16, (cy * 32) + 16));
            }
        }
    }

    [Fact]
    public void PortraitFitLetterboxesWithBlack()
    {
        // 1280x720 rotated 90 -> 720x1280 portrait, fit into 1280x720 -> 405x720 centred, bars left/right.
        var src = Marked(1280, 720, (_, _) => 200);
        var dst = new Compositor().Compose(src, new CompositorSettings(90, false, FitMode.Fit, 1280, 720));
        Assert.Equal(16, Y(dst, 1280, 10, 360)); // left bar
        Assert.Equal(16, Y(dst, 1280, 1270, 360)); // right bar
        Assert.Equal(200, Y(dst, 1280, 640, 360)); // image
        Assert.Equal(200, Y(dst, 1280, 640 - 200, 360));
        Assert.Equal(16, Y(dst, 1280, 640 - 205, 360));
        var uv = 1280 * 720;
        Assert.Equal(128, dst[uv + (180 * 1280) + 2]); // bar chroma neutral
        Assert.Equal(100, dst[uv + (180 * 1280) + 640]); // image chroma U
        Assert.Equal(200, dst[uv + (180 * 1280) + 641]); // image chroma V
    }

    [Fact]
    public void PortraitFillCoversCanvas()
    {
        var src = Marked(1280, 720, (_, _) => 200);
        var dst = new Compositor().Compose(src, new CompositorSettings(270, false, FitMode.Fill, 1280, 720));
        Assert.All(dst.Take(1280 * 720), b => Assert.Equal(200, b));
    }

    [Fact]
    public void ScalesDownLandscapeWithoutBars()
    {
        var src = Marked(1920, 1080, (x, _) => (byte)(x < 960 ? 50 : 150));
        var dst = new Compositor().Compose(src, new CompositorSettings(0, false, FitMode.Fit, 1280, 720));
        Assert.Equal(50, Y(dst, 1280, 10, 10));
        Assert.Equal(150, Y(dst, 1280, 1270, 710));
    }

    [Fact]
    public void ReconfiguresWhenSettingsChange()
    {
        var c = new Compositor();
        var src = Quadrants(64, 64);
        var a = c.Compose(src, new CompositorSettings(0, false, FitMode.Fit, 64, 64));
        var b = c.Compose(src, new CompositorSettings(180, false, FitMode.Fit, 64, 64));
        Assert.Equal(235, Y(a, 64, 5, 5));
        Assert.Equal(16, Y(b, 64, 5, 5));
        Assert.Equal(235, Y(b, 64, 60, 60));
    }

    [Fact]
    public void RejectsBadInput()
    {
        var c = new Compositor();
        Assert.Throws<ArgumentException>(() => c.Compose(VideoFrame.CreateBgra(4, 4), new CompositorSettings(0, false, FitMode.Fit, 4, 4)));
        Assert.Throws<ArgumentException>(() => c.Compose(Quadrants(4, 4), new CompositorSettings(45, false, FitMode.Fit, 4, 4)));
        Assert.Throws<ArgumentException>(() => c.Compose(Quadrants(4, 4), new CompositorSettings(0, false, FitMode.Fit, 5, 4)));
    }
}
