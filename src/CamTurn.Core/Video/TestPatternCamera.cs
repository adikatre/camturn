namespace CamTurn.Core.Video;

/// <summary>
/// A camera that needs no hardware: renders <see cref="TestPattern"/> as a lid-mounted camera would see it when the
/// device is rolled by <c>rollProvider()</c> degrees, so CamTurn's correction can be checked end to end
/// (<c>--test-pattern --simulate-orientation 180</c>). Frames are NV12 at the requested format.
/// </summary>
public sealed class TestPatternCamera : ICameraSource, IDisposable
{
    private readonly Func<double> _rollProvider;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private VideoFormat _format;
    private long _frameNumber;
    private long _startTicks;
    private int _rendering;

    public TestPatternCamera(Func<double>? rollProvider = null, TimeProvider? time = null, IReadOnlyList<VideoFormat>? formats = null)
    {
        _rollProvider = rollProvider ?? (() => 0);
        _time = time ?? TimeProvider.System;
        Descriptor = new CameraDescriptor(
            "camturn:test-pattern",
            "CamTurn test pattern",
            "Front",
            formats ?? [new(1280, 720, 30, "NV12"), new(1920, 1080, 30, "NV12"), new(640, 360, 30, "NV12")]);
    }

    public CameraDescriptor Descriptor { get; }

    public CameraState State { get; private set; } = CameraState.Closed;

    public event EventHandler<VideoFrame>? FrameArrived;

    public event EventHandler<CameraState>? StateChanged;

    public bool Start(VideoFormat format)
    {
        lock (_gate)
        {
            _format = format.Width > 0 ? format : Descriptor.Formats[0];
            _frameNumber = 0;
            _startTicks = _time.GetTimestamp();
            _timer?.Dispose();
            var period = TimeSpan.FromSeconds(1.0 / Math.Max(1, _format.FramesPerSecond));
            _timer = _time.CreateTimer(_ => Tick(), null, TimeSpan.Zero, period);
        }

        SetState(CameraState.Streaming);
        return true;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }

        SetState(CameraState.Closed);
    }

    /// <summary>Renders one frame for the current roll (also used directly by tests).</summary>
    public VideoFrame Render(int width, int height)
    {
        var t = _time.GetElapsedTime(_startTicks);
        var bgra = TestPattern.Render(width, height, _rollProvider(), t, Interlocked.Increment(ref _frameNumber));
        return FrameOps.BgraToNv12(bgra);
    }

    public void Dispose() => Stop();

    private void Tick()
    {
        // Drop the tick if the previous frame is still rendering; otherwise slow machines pile up timer callbacks
        // and starve the thread pool.
        if (Interlocked.Exchange(ref _rendering, 1) == 1)
        {
            return;
        }

        try
        {
            VideoFormat f;
            lock (_gate)
            {
                if (_timer is null)
                {
                    return;
                }

                f = _format;
            }

            FrameArrived?.Invoke(this, Render(f.Width, f.Height));
        }
        finally
        {
            Volatile.Write(ref _rendering, 0);
        }
    }

    private void SetState(CameraState s)
    {
        if (s != State)
        {
            State = s;
            StateChanged?.Invoke(this, s);
        }
    }
}
