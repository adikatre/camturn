using System.Buffers.Binary;
using System.IO.Compression;
using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>Minimal PNG writer (RGBA, 8-bit, no filtering). Enough for debug output without a dependency.</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(VideoFrame frame, string path)
    {
        if (frame.Format == PixelFormat.Nv12)
        {
            frame = FrameOps.Nv12ToBgra(frame);
        }

        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), frame.Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WriteChunk(file, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var line = new byte[1 + (frame.Width * 4)];
            for (var y = 0; y < frame.Height; y++)
            {
                line[0] = 0; // filter: none
                for (var x = 0; x < frame.Width; x++)
                {
                    var s = (y * frame.Stride) + (x * 4);
                    var d = 1 + (x * 4);
                    line[d] = frame.Data[s + 2];
                    line[d + 1] = frame.Data[s + 1];
                    line[d + 2] = frame.Data[s];
                    line[d + 3] = frame.Data[s + 3];
                }

                z.Write(line);
            }
        }

        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

/// <summary>Drawing helpers for BGRA frames: fills, blits with nearest-neighbour scaling, and a 5x7 bitmap font.</summary>
public static class Canvas
{
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = "01110100011000111111100011000110001",
        ['B'] = "11110100011000111110100011000111110",
        ['C'] = "01110100011000010000100001000101110",
        ['D'] = "11110100011000110001100011000111110",
        ['E'] = "11111100001000011110100001000011111",
        ['F'] = "11111100001000011110100001000010000",
        ['G'] = "01110100011000010111100011000101111",
        ['H'] = "10001100011000111111100011000110001",
        ['I'] = "01110001000010000100001000010001110",
        ['J'] = "00111000100001000010000101001001100",
        ['K'] = "10001100101010011000101001001010001",
        ['L'] = "10000100001000010000100001000011111",
        ['M'] = "10001110111010110101100011000110001",
        ['N'] = "10001110011010110011100011000110001",
        ['O'] = "01110100011000110001100011000101110",
        ['P'] = "11110100011000111110100001000010000",
        ['Q'] = "01110100011000110001101011001001101",
        ['R'] = "11110100011000111110101001001010001",
        ['S'] = "01111100001000001110000010000111110",
        ['T'] = "11111001000010000100001000010000100",
        ['U'] = "10001100011000110001100011000101110",
        ['V'] = "10001100011000110001100010101000100",
        ['W'] = "10001100011000110101101011010101010",
        ['X'] = "10001100010101000100010101000110001",
        ['Y'] = "10001100010101000100001000010000100",
        ['Z'] = "11111000010001000100010001000011111",
        ['0'] = "01110100011001110101110011000101110",
        ['1'] = "00100011000010000100001000010001110",
        ['2'] = "01110100010000100010001000100011111",
        ['3'] = "11111000100010000010000011000101110",
        ['4'] = "00010001100101010010111110001000010",
        ['5'] = "11111100001111000001000011000101110",
        ['6'] = "00110010001000011110100011000101110",
        ['7'] = "11111000010001000100010000100001000",
        ['8'] = "01110100011000101110100011000101110",
        ['9'] = "01110100011000101111000010001001100",
        ['.'] = "00000000000000000000000000110001100",
        [':'] = "00000011000110000000011000110000000",
        ['-'] = "00000000000000011111000000000000000",
        ['='] = "00000000001111100000111110000000000",
        ['/'] = "00001000010001000100010001000010000",
        ['+'] = "00000001000010011111001000010000000",
        ['('] = "00010001000100001000010000010000010",
        [')'] = "01000001000001000010000100010001000",
        ['_'] = "00000000000000000000000000000011111",
        [' '] = "00000000000000000000000000000000000",
    };

    public static void Fill(VideoFrame dst, int x0, int y0, int w, int h, (byte B, byte G, byte R) c)
    {
        for (var y = Math.Max(0, y0); y < Math.Min(dst.Height, y0 + h); y++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(dst.Width, x0 + w); x++)
            {
                var o = (y * dst.Stride) + (x * 4);
                dst.Data[o] = c.B;
                dst.Data[o + 1] = c.G;
                dst.Data[o + 2] = c.R;
                dst.Data[o + 3] = 255;
            }
        }
    }

    /// <summary>Draws <paramref name="src"/> scaled to fit inside the box, centred (letterboxed).</summary>
    public static void BlitFit(VideoFrame dst, VideoFrame src, int bx, int by, int bw, int bh)
    {
        var scale = Math.Min((double)bw / src.Width, (double)bh / src.Height);
        int w = (int)(src.Width * scale), h = (int)(src.Height * scale);
        int ox = bx + ((bw - w) / 2), oy = by + ((bh - h) / 2);
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(src.Height - 1, (int)(y / scale));
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(src.Width - 1, (int)(x / scale));
                int dx = ox + x, dy = oy + y;
                if (dx < 0 || dy < 0 || dx >= dst.Width || dy >= dst.Height)
                {
                    continue;
                }

                Buffer.BlockCopy(src.Data, (sy * src.Stride) + (sx * 4), dst.Data, (dy * dst.Stride) + (dx * 4), 4);
            }
        }
    }

    /// <summary>Draws upper-case text; unknown characters render as spaces.</summary>
    public static void Text(VideoFrame dst, int x, int y, string text, (byte B, byte G, byte R) c, int scale = 2)
    {
        foreach (var ch in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(ch, out var bits))
            {
                for (var i = 0; i < 35; i++)
                {
                    if (bits[i] == '1')
                    {
                        Fill(dst, x + (i % 5 * scale), y + (i / 5 * scale), scale, scale, c);
                    }
                }
            }

            x += 6 * scale;
        }
    }
}
