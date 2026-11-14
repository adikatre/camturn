namespace CamTurn.Core.Video;

/// <param name="RotationCwDegrees">How far the scene appears rotated clockwise in the image, in (-180, 180]; 0 means upright.</param>
/// <param name="Mirrored">True if left and right are swapped.</param>
/// <param name="Confident">False if too few scene pixels were found (not a simulator frame, or corrupted).</param>
public readonly record struct UprightResult(double RotationCwDegrees, bool Mirrored, bool Confident);

/// <summary>
/// Measures how a frame of the <see cref="TestPattern"/> scene is oriented, so tests (and
/// <c>CamTurn --probe-virtual-camera</c> on a real Windows machine) can check "the output is upright" without anyone
/// looking at it.
/// </summary>
public static class UprightDetector
{
    /// <summary>
    /// True for the media source's "waiting for CamTurn" placeholder: neutral chroma everywhere (pure greyscale).
    /// Live camera images and the test pattern always carry colour.
    /// </summary>
    public static bool IsPlaceholder(VideoFrame nv12)
    {
        if (nv12.Format != PixelFormat.Nv12)
        {
            throw new ArgumentException("Expected NV12.", nameof(nv12));
        }

        var uv = nv12.Stride * nv12.Height;
        var end = uv + (nv12.Stride * nv12.Height / 2);
        for (var i = uv; i < end; i += 7)
        {
            if (Math.Abs(nv12.Data[i] - 128) > 3)
            {
                return false;
            }
        }

        return true;
    }

    public static UprightResult Detect(VideoFrame frame)
    {
        if (frame.Format == PixelFormat.Nv12)
        {
            frame = FrameOps.Nv12ToBgra(frame);
        }

        int w = frame.Width, h = frame.Height;
        var radius = Math.Min(w, h) / 2.0 * 0.95;
        var sky = new Acc();
        var ground = new Acc();
        var red = new Acc();
        var magenta = new Acc();
        const int step = 2;

        for (var py = 0; py < h; py += step)
        {
            var up = (h / 2.0) - py - 0.5;
            for (var px = 0; px < w; px += step)
            {
                var right = px + 0.5 - (w / 2.0);
                var o = (py * frame.Stride) + (px * 4);
                int b = frame.Data[o], g = frame.Data[o + 1], r = frame.Data[o + 2];

                if (r > 170 && g < 100 && b < 100)
                {
                    red.Add(right, up);
                }
                else if (r > 150 && b > 150 && g < 100)
                {
                    magenta.Add(right, up);
                }

                // Sky/ground only inside a centred disc, so the halves are symmetric for any rotation.
                if ((right * right) + (up * up) > radius * radius)
                {
                    continue;
                }

                if (b > r + 60 && b > g + 20)
                {
                    sky.Add(right, up);
                }
                else if (g > r + 40 && g > b + 40)
                {
                    ground.Add(right, up);
                }
            }
        }

        var minCount = w * h / (step * step) / 50;
        if (sky.N < minCount || ground.N < minCount)
        {
            return new UprightResult(double.NaN, false, false);
        }

        var ux = sky.MeanX - ground.MeanX;
        var uy = sky.MeanY - ground.MeanY;
        var rotation = Math.Atan2(ux, uy) * 180.0 / Math.PI;

        var mirrored = false;
        var confident = red.N > 10 && magenta.N > 10;
        if (confident)
        {
            var vx = magenta.MeanX - red.MeanX;
            var vy = magenta.MeanY - red.MeanY;
            mirrored = (ux * vy) - (uy * vx) > 0;
        }

        return new UprightResult(rotation, mirrored, confident);
    }

    private sealed class Acc
    {
        public long N;
        private double _sx;
        private double _sy;

        public double MeanX => _sx / N;

        public double MeanY => _sy / N;

        public void Add(double x, double y)
        {
            N++;
            _sx += x;
            _sy += y;
        }
    }
}
