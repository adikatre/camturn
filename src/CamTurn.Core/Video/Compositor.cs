namespace CamTurn.Core.Video;

/// <summary>How a portrait (rotated) image is placed on the fixed landscape output canvas.</summary>
public enum FitMode
{
    /// <summary>Scale to fit entirely, with black bars (letterbox / pillarbox).</summary>
    Fit,

    /// <summary>Scale to fill the canvas, cropping the overflow.</summary>
    Fill,
}

public readonly record struct CompositorSettings(int RotationCw, bool Mirror, FitMode Fit, int CanvasWidth, int CanvasHeight);

/// <summary>
/// Rotates (0/90/180/270 clockwise), optionally mirrors, and scales a packed NV12 frame onto a fixed-size NV12
/// canvas, working directly on the Y and interleaved UV planes (nearest-neighbour). Lookup tables are rebuilt only
/// when the input size or settings change, so the per-frame cost is one indexed copy per output byte.
/// Not thread-safe; use one instance per pipeline.
/// </summary>
public sealed class Compositor
{
    private int _srcW;
    private int _srcH;
    private CompositorSettings _settings;
    private bool _configured;

    // Output pixel (x, y) inside the image area reads src[_colY[x] + _rowY[y]]; -1 marks letterbox.
    private int[] _colY = [];
    private int[] _rowY = [];
    private int[] _colUv = [];
    private int[] _rowUv = [];

    public void Compose(VideoFrame src, CompositorSettings settings, byte[] dst)
    {
        if (src.Format != PixelFormat.Nv12)
        {
            throw new ArgumentException("Expected NV12.", nameof(src));
        }

        int cw = settings.CanvasWidth, ch = settings.CanvasHeight;
        if (cw <= 0 || ch <= 0 || cw % 2 != 0 || ch % 2 != 0)
        {
            throw new ArgumentException("Canvas must have positive even dimensions.", nameof(settings));
        }

        if (dst.Length < FrameProtocol.Nv12Size(cw, ch))
        {
            throw new ArgumentException("Destination too small.", nameof(dst));
        }

        if (!_configured || src.Width != _srcW || src.Height != _srcH || settings != _settings)
        {
            Configure(src.Width, src.Height, src.Stride, settings);
        }

        var s = src.Data;
        var ySize = cw * ch;
        var srcUvBase = src.Stride * src.Height;

        for (var y = 0; y < ch; y++)
        {
            var row = _rowY[y];
            var o = y * cw;
            if (row < 0)
            {
                Array.Fill(dst, (byte)16, o, cw);
                continue;
            }

            for (var x = 0; x < cw; x++)
            {
                var col = _colY[x];
                dst[o + x] = col < 0 ? (byte)16 : s[row + col];
            }
        }

        for (var y = 0; y < ch / 2; y++)
        {
            var row = _rowUv[y];
            var o = ySize + (y * cw);
            for (var x = 0; x < cw / 2; x++)
            {
                var col = _colUv[x];
                if (row < 0 || col < 0)
                {
                    dst[o + (2 * x)] = 128;
                    dst[o + (2 * x) + 1] = 128;
                }
                else
                {
                    var i = srcUvBase + row + col;
                    dst[o + (2 * x)] = s[i];
                    dst[o + (2 * x) + 1] = s[i + 1];
                }
            }
        }
    }

    public byte[] Compose(VideoFrame src, CompositorSettings settings)
    {
        var dst = new byte[FrameProtocol.Nv12Size(settings.CanvasWidth, settings.CanvasHeight)];
        Compose(src, settings, dst);
        return dst;
    }

    private void Configure(int w, int h, int stride, CompositorSettings settings)
    {
        var rot = ((settings.RotationCw % 360) + 360) % 360;
        if (rot % 90 != 0)
        {
            throw new ArgumentException("Rotation must be a multiple of 90.", nameof(settings));
        }

        var swap = rot is 90 or 270;
        int rw = swap ? h : w, rh = swap ? w : h;
        int cw = settings.CanvasWidth, ch = settings.CanvasHeight;
        var scale = settings.Fit == FitMode.Fit
            ? Math.Min((double)cw / rw, (double)ch / rh)
            : Math.Max((double)cw / rw, (double)ch / rh);
        var dw = rw * scale;
        var dh = rh * scale;
        var ox = (cw - dw) / 2;
        var oy = (ch - dh) / 2;

        // Maps a canvas coordinate to a coordinate in the rotated (and mirrored) image, or -1 outside it.
        int ToRotatedX(double cx)
        {
            var u = (int)Math.Floor((cx - ox) / scale);
            if (u < 0 || u >= rw)
            {
                return -1;
            }

            return settings.Mirror ? rw - 1 - u : u;
        }

        int ToRotatedY(double cy)
        {
            var v = (int)Math.Floor((cy - oy) / scale);
            return v < 0 || v >= rh ? -1 : v;
        }

        // For a rotated coordinate (u, v), the source pixel is: 0: (u, v); 90: (v, h-1-u); 180: (w-1-u, h-1-v);
        // 270: (w-1-v, u). Each source coordinate depends on only one of u or v, so the source offset splits into a
        // column term and a row term.
        int ColumnTerm(int u, bool chroma) => rot switch
        {
            0 => SrcX(u, chroma),
            90 => SrcY(h - 1 - u, chroma),
            180 => SrcX(w - 1 - u, chroma),
            _ => SrcY(u, chroma),
        };

        int RowTerm(int v, bool chroma) => rot switch
        {
            0 => SrcY(v, chroma),
            90 => SrcX(v, chroma),
            180 => SrcY(h - 1 - v, chroma),
            _ => SrcX(w - 1 - v, chroma),
        };

        // Byte offsets: Y plane is 1 byte per pixel; UV plane is 2 bytes per 2x2 block.
        int SrcX(int x, bool chroma) => chroma ? (x / 2) * 2 : x;
        int SrcY(int y, bool chroma) => chroma ? (y / 2) * stride : y * stride;

        _colY = new int[cw];
        for (var x = 0; x < cw; x++)
        {
            var u = ToRotatedX(x + 0.5);
            _colY[x] = u < 0 ? -1 : ColumnTerm(u, false);
        }

        _rowY = new int[ch];
        for (var y = 0; y < ch; y++)
        {
            var v = ToRotatedY(y + 0.5);
            _rowY[y] = v < 0 ? -1 : RowTerm(v, false);
        }

        _colUv = new int[cw / 2];
        for (var x = 0; x < cw / 2; x++)
        {
            var u = ToRotatedX((2 * x) + 1.0);
            _colUv[x] = u < 0 ? -1 : ColumnTerm(u, true);
        }

        _rowUv = new int[ch / 2];
        for (var y = 0; y < ch / 2; y++)
        {
            var v = ToRotatedY((2 * y) + 1.0);
            _rowUv[y] = v < 0 ? -1 : RowTerm(v, true);
        }

        _srcW = w;
        _srcH = h;
        _settings = settings;
        _configured = true;
    }
}
