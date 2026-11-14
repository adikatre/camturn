namespace CamTurn.Core.Video;

public enum PixelFormat
{
    /// <summary>32 bits per pixel, byte order B, G, R, A.</summary>
    Bgra32,

    /// <summary>8-bit Y plane followed by an interleaved half-resolution UV plane.</summary>
    Nv12,
}

public readonly record struct VideoFormat(int Width, int Height, int FramesPerSecond, string Subtype)
{
    public override string ToString() => $"{Subtype} {Width}x{Height}@{FramesPerSecond}";
}

public sealed record CameraDescriptor(
    string Id,
    string Name,
    string Panel,
    IReadOnlyList<VideoFormat> Formats);

/// <summary>One frame. <see cref="Data"/> is tightly owned by the frame; producers must not reuse it after raising.</summary>
public sealed class VideoFrame
{
    public VideoFrame(int width, int height, PixelFormat format, byte[] data, int stride, long frameNumber, TimeSpan timestamp)
    {
        Width = width;
        Height = height;
        Format = format;
        Data = data;
        Stride = stride;
        FrameNumber = frameNumber;
        Timestamp = timestamp;
    }

    public int Width { get; }

    public int Height { get; }

    public PixelFormat Format { get; }

    public byte[] Data { get; }

    public int Stride { get; }

    public long FrameNumber { get; }

    public TimeSpan Timestamp { get; }

    public static VideoFrame CreateBgra(int width, int height, long frameNumber = 0, TimeSpan timestamp = default) =>
        new(width, height, PixelFormat.Bgra32, new byte[width * height * 4], width * 4, frameNumber, timestamp);
}

public enum CameraState
{
    Closed,
    Streaming,
    Busy,
    Disconnected,
}

public interface ICameraSource
{
    CameraDescriptor Descriptor { get; }

    CameraState State { get; }

    event EventHandler<VideoFrame>? FrameArrived;

    event EventHandler<CameraState>? StateChanged;

    /// <summary>Opens the camera. Returns false (and sets <see cref="State"/>) if busy or disconnected.</summary>
    bool Start(VideoFormat format);

    void Stop();
}
