using System.Buffers.Binary;

namespace CamTurn.Core.Video;

/// <summary>
/// CamTurn frame protocol v1, byte-for-byte identical to src/CamTurn.MediaSource/include/camturn/protocol.h.
/// The app hosts the pipe <see cref="PipeName"/>. The media source connects when an app starts the virtual
/// camera, sends a Hello with the size and rate it wants, then receives Frame messages (header + NV12 payload).
/// </summary>
public static class FrameProtocol
{
    public const uint Magic = 0x4E525443; // "CTRN"
    public const ushort Version = 1;
    public const int HeaderSize = 64;
    public const uint FourccNv12 = 0x3231564E; // "NV12"
    public const string PipeName = "CamTurn.Video.v1";
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;

    public static int Nv12Size(int width, int height) => width * height * 3 / 2;

    public static void Encode(in FrameHeader h, Span<byte> dst)
    {
        if (dst.Length < HeaderSize)
        {
            throw new ArgumentException("Destination too small.", nameof(dst));
        }

        dst[..HeaderSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(dst, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(dst[6..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[8..], (uint)h.Type);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[12..], h.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[16..], h.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[20..], h.Stride);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[24..], h.PixelFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[28..], h.PayloadSize);
        BinaryPrimitives.WriteUInt64LittleEndian(dst[32..], h.FrameCounter);
        BinaryPrimitives.WriteInt64LittleEndian(dst[40..], h.Timestamp100ns);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[48..], (uint)h.Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(dst[52..], h.Param);
    }

    public static FrameDecodeResult Decode(ReadOnlySpan<byte> src, out FrameHeader h)
    {
        h = default;
        if (src.Length < HeaderSize)
        {
            return FrameDecodeResult.BadHeaderSize;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(src);
        var version = BinaryPrimitives.ReadUInt16LittleEndian(src[4..]);
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(src[6..]);
        h = new FrameHeader
        {
            Type = (FrameMessageType)BinaryPrimitives.ReadUInt32LittleEndian(src[8..]),
            Width = BinaryPrimitives.ReadUInt32LittleEndian(src[12..]),
            Height = BinaryPrimitives.ReadUInt32LittleEndian(src[16..]),
            Stride = BinaryPrimitives.ReadUInt32LittleEndian(src[20..]),
            PixelFormat = BinaryPrimitives.ReadUInt32LittleEndian(src[24..]),
            PayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(src[28..]),
            FrameCounter = BinaryPrimitives.ReadUInt64LittleEndian(src[32..]),
            Timestamp100ns = BinaryPrimitives.ReadInt64LittleEndian(src[40..]),
            Flags = (FrameFlags)BinaryPrimitives.ReadUInt32LittleEndian(src[48..]),
            Param = BinaryPrimitives.ReadUInt32LittleEndian(src[52..]),
        };

        if (magic != Magic)
        {
            return FrameDecodeResult.BadMagic;
        }

        if (version != Version)
        {
            return FrameDecodeResult.UnsupportedVersion;
        }

        if (headerSize != HeaderSize)
        {
            return FrameDecodeResult.BadHeaderSize;
        }

        if (h.Width > MaxWidth || h.Height > MaxHeight || h.Width % 2 != 0 || h.Height % 2 != 0)
        {
            return FrameDecodeResult.BadDimensions;
        }

        if (h.Type == FrameMessageType.Frame)
        {
            if (h.PixelFormat != FourccNv12 || h.Stride != h.Width || h.PayloadSize != Nv12Size((int)h.Width, (int)h.Height))
            {
                return FrameDecodeResult.BadPayloadSize;
            }
        }
        else if (h.PayloadSize != 0)
        {
            return FrameDecodeResult.BadPayloadSize;
        }

        return FrameDecodeResult.Ok;
    }

    public static FrameHeader FrameFor(int width, int height, ulong counter, long timestamp100ns, FrameFlags flags = FrameFlags.None, uint rotation = 0) => new()
    {
        Type = FrameMessageType.Frame,
        Width = (uint)width,
        Height = (uint)height,
        Stride = (uint)width,
        PixelFormat = FourccNv12,
        PayloadSize = (uint)Nv12Size(width, height),
        FrameCounter = counter,
        Timestamp100ns = timestamp100ns,
        Flags = flags,
        Param = rotation,
    };

    public static FrameHeader Hello(int width, int height, int fps) => new()
    {
        Type = FrameMessageType.Hello,
        Width = (uint)width,
        Height = (uint)height,
        PixelFormat = FourccNv12,
        Param = (uint)fps,
    };
}

public enum FrameMessageType : uint
{
    Hello = 1,
    Frame = 2,
}

[Flags]
public enum FrameFlags : uint
{
    None = 0,
    Mirrored = 1,
    Holding = 2,
}

public enum FrameDecodeResult
{
    Ok,
    BadMagic,
    UnsupportedVersion,
    BadHeaderSize,
    BadDimensions,
    BadPayloadSize,
}

public readonly record struct FrameHeader
{
    public FrameMessageType Type { get; init; }

    public uint Width { get; init; }

    public uint Height { get; init; }

    public uint Stride { get; init; }

    public uint PixelFormat { get; init; }

    public uint PayloadSize { get; init; }

    public ulong FrameCounter { get; init; }

    public long Timestamp100ns { get; init; }

    public FrameFlags Flags { get; init; }

    /// <summary>Hello: frames per second. Frame: applied rotation in degrees.</summary>
    public uint Param { get; init; }
}
