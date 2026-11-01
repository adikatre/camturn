using CamTurn.Core.Video;

namespace CamTurn.Core.Tests;

public class FrameProtocolTests
{
    private static readonly FrameHeader Golden = FrameProtocol.FrameFor(1280, 720, 0x0102030405060708, -1234567890123, FrameFlags.Mirrored | FrameFlags.Holding, 270);

    private static byte[] Fixture()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "fixtures", "frame-header-v1.bin");
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        throw new FileNotFoundException("frame-header-v1.bin");
    }

    [Fact]
    public void EncodeMatchesGoldenBytesSharedWithCpp()
    {
        var buf = new byte[FrameProtocol.HeaderSize];
        FrameProtocol.Encode(Golden, buf);
        Assert.Equal(Fixture(), buf);
    }

    [Fact]
    public void DecodeGoldenBytes()
    {
        Assert.Equal(FrameDecodeResult.Ok, FrameProtocol.Decode(Fixture(), out var h));
        Assert.Equal(Golden, h);
    }

    [Fact]
    public void HelloRoundTrips()
    {
        var buf = new byte[FrameProtocol.HeaderSize];
        FrameProtocol.Encode(FrameProtocol.Hello(1920, 1080, 30), buf);
        Assert.Equal(FrameDecodeResult.Ok, FrameProtocol.Decode(buf, out var h));
        Assert.Equal(FrameMessageType.Hello, h.Type);
        Assert.Equal((1920u, 1080u, 30u), (h.Width, h.Height, h.Param));
    }

    [Theory]
    [InlineData(0, 0xFF, FrameDecodeResult.BadMagic)]
    [InlineData(4, 2, FrameDecodeResult.UnsupportedVersion)]
    [InlineData(6, 32, FrameDecodeResult.BadHeaderSize)]
    [InlineData(12, 0x01, FrameDecodeResult.BadDimensions)] // width 1281 (odd)
    [InlineData(28, 0x01, FrameDecodeResult.BadPayloadSize)]
    public void DecodeRejectsCorruptHeaders(int offset, byte value, FrameDecodeResult expected)
    {
        var buf = Fixture();
        buf[offset] = offset is 12 or 28 ? (byte)(buf[offset] | value) : value;
        Assert.Equal(expected, FrameProtocol.Decode(buf, out _));
    }

    [Fact]
    public void DecodeRejectsOversizedFrames()
    {
        var buf = new byte[FrameProtocol.HeaderSize];
        FrameProtocol.Encode(FrameProtocol.FrameFor(7680, 4320, 0, 0), buf);
        Assert.Equal(FrameDecodeResult.BadDimensions, FrameProtocol.Decode(buf, out _));
    }
}
