using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>
/// Procedural test scene placed in front of the lid camera. In world terms (viewer's frame): sky above the
/// horizon, ground below, a yellow "up" arrow in the middle, a red disc upper-left, a magenta disc upper-right,
/// and a white dot that moves along the ground so motion is visible. The scene is asymmetric so both
/// rotation and mirroring can be detected (see <see cref="UprightDetector"/>).
/// </summary>
public static class SceneRenderer
{
    // BGRA
    public static readonly (byte B, byte G, byte R) Sky = (235, 180, 90);
    public static readonly (byte B, byte G, byte R) Ground = (60, 150, 70);
    public static readonly (byte B, byte G, byte R) Arrow = (40, 210, 250);
    public static readonly (byte B, byte G, byte R) Red = (40, 40, 220);
    public static readonly (byte B, byte G, byte R) Magenta = (200, 40, 200);
    public static readonly (byte B, byte G, byte R) Dot = (250, 250, 250);

    /// <summary>
    /// Renders the scene as the camera sees it when the device is rolled <paramref name="rollCwDegrees"/> clockwise:
    /// the world appears rotated clockwise by the same amount in the raw image.
    /// </summary>
    public static VideoFrame Render(int width, int height, double rollCwDegrees, TimeSpan t, long frameNumber = 0)
    {
        var frame = VideoFrame.CreateBgra(width, height, frameNumber, t);
        var rad = rollCwDegrees * Math.PI / 180.0;
        double c = Math.Cos(rad), s = Math.Sin(rad);
        var scale = Math.Min(width, height) / 2.0;
        var dotX = -1.2 + (2.4 * ((t.TotalSeconds / 4.0) % 1.0));
        var data = frame.Data;

        Parallel.For(0, height, py =>
        {
            var up = ((height / 2.0) - py - 0.5) / scale;
            var row = py * frame.Stride;
            for (var px = 0; px < width; px++)
            {
                var right = (px + 0.5 - (width / 2.0)) / scale;
                // Undo the clockwise image rotation to find which world point this pixel shows.
                var wx = (right * c) - (up * s);
                var wy = (right * s) + (up * c);
                var color = Shade(wx, wy, dotX);
                var o = row + (px * 4);
                data[o] = color.B;
                data[o + 1] = color.G;
                data[o + 2] = color.R;
                data[o + 3] = 255;
            }
        });

        return frame;
    }

    private static (byte B, byte G, byte R) Shade(double x, double y, double dotX)
    {
        if (Sq(x + 0.55) + Sq(y - 0.45) < Sq(0.15))
        {
            return Red;
        }

        if (Sq(x - 0.55) + Sq(y - 0.45) < Sq(0.15))
        {
            return Magenta;
        }

        if ((Math.Abs(x) < 0.07 && y > -0.5 && y <= 0.3) || (y > 0.3 && y < 0.7 && Math.Abs(x) < 0.25 * (0.7 - y) / 0.4))
        {
            return Arrow;
        }

        if (Sq(x - dotX) + Sq(y + 0.75) < Sq(0.06))
        {
            return Dot;
        }

        return y >= 0 ? Sky : Ground;
    }

    private static double Sq(double v) => v * v;
}
