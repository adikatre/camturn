namespace CamTurn.Core.Video;

public static class FrameOps
{
    /// <summary>Rotates a BGRA frame clockwise by 0, 90, 180 or 270 degrees.</summary>
    public static VideoFrame RotateBgraClockwise(VideoFrame src, int degrees)
    {
        if (src.Format != PixelFormat.Bgra32)
        {
            throw new ArgumentException("Expected BGRA32.", nameof(src));
        }

        degrees = ((degrees % 360) + 360) % 360;
        var swap = degrees is 90 or 270;
        var dw = swap ? src.Height : src.Width;
        var dh = swap ? src.Width : src.Height;
        var dst = new VideoFrame(dw, dh, PixelFormat.Bgra32, new byte[dw * dh * 4], dw * 4, src.FrameNumber, src.Timestamp);
        var s = MemoryMarshalUInts(src.Data);
        var d = MemoryMarshalUInts(dst.Data);
        int w = src.Width, h = src.Height, sStride = src.Stride / 4;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (dx, dy) = degrees switch
                {
                    90 => (h - 1 - y, x),
                    180 => (w - 1 - x, h - 1 - y),
                    270 => (y, w - 1 - x),
                    _ => (x, y),
                };
                d[(dy * dw) + dx] = s[(y * sStride) + x];
            }
        }

        return dst;
    }

    /// <summary>BGRA to NV12 using BT.601 limited-range coefficients (the usual webcam colour space).</summary>
    public static VideoFrame BgraToNv12(VideoFrame src)
    {
        if (src.Format != PixelFormat.Bgra32 || src.Width % 2 != 0 || src.Height % 2 != 0)
        {
            throw new ArgumentException("Expected BGRA32 with even dimensions.", nameof(src));
        }

        int w = src.Width, h = src.Height;
        var data = new byte[w * h * 3 / 2];
        var uvBase = w * h;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * src.Stride) + (x * 4);
                int b = src.Data[i], g = src.Data[i + 1], r = src.Data[i + 2];
                data[(y * w) + x] = ClampByte((((66 * r) + (129 * g) + (25 * b) + 128) >> 8) + 16);
                if ((y & 1) == 0 && (x & 1) == 0)
                {
                    var uv = uvBase + ((y / 2) * w) + x;
                    data[uv] = ClampByte((((-38 * r) - (74 * g) + (112 * b) + 128) >> 8) + 128);
                    data[uv + 1] = ClampByte((((112 * r) - (94 * g) - (18 * b) + 128) >> 8) + 128);
                }
            }
        }

        return new VideoFrame(w, h, PixelFormat.Nv12, data, w, src.FrameNumber, src.Timestamp);
    }

    public static VideoFrame Nv12ToBgra(VideoFrame src)
    {
        if (src.Format != PixelFormat.Nv12)
        {
            throw new ArgumentException("Expected NV12.", nameof(src));
        }

        int w = src.Width, h = src.Height;
        var dst = VideoFrame.CreateBgra(w, h, src.FrameNumber, src.Timestamp);
        var uvBase = src.Stride * h;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = src.Data[(y * src.Stride) + x] - 16;
                var uv = uvBase + ((y / 2) * src.Stride) + (x & ~1);
                int d = src.Data[uv] - 128, e = src.Data[uv + 1] - 128;
                var o = (y * dst.Stride) + (x * 4);
                dst.Data[o] = ClampByte(((298 * c) + (516 * d) + 128) >> 8);
                dst.Data[o + 1] = ClampByte(((298 * c) - (100 * d) - (208 * e) + 128) >> 8);
                dst.Data[o + 2] = ClampByte(((298 * c) + (409 * e) + 128) >> 8);
                dst.Data[o + 3] = 255;
            }
        }

        return dst;
    }

    private static Span<uint> MemoryMarshalUInts(byte[] data) =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.AsSpan());

    private static byte ClampByte(int v) => (byte)Math.Clamp(v, 0, 255);
}
