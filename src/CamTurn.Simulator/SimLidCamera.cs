using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>
/// The webcam built into the lid. Frames show the test scene as a lid-fixed camera would see it: when the
/// device is rolled, the scene appears rotated by the same amount. Supports busy/disconnect fault injection.
/// </summary>
public sealed class SimLidCamera : ICameraSource
{
    private readonly SimDevice _device;
    private VideoFormat _format;
    private long _frameNumber;
    private bool _busy;

    internal SimLidCamera(SimDevice device)
    {
        _device = device;
        var spec = device.Profile.Camera;
        Descriptor = new CameraDescriptor(spec.Id, spec.Name, spec.Panel, spec.Formats.Select(f => f.ToVideoFormat()).ToList());
    }

    public CameraDescriptor Descriptor { get; }

    public CameraState State { get; private set; } = CameraState.Closed;

    /// <summary>True when the privacy LED would be on.</summary>
    public bool LedOn => State == CameraState.Streaming;

    internal TimeSpan? NextDue { get; private set; }

    public event EventHandler<VideoFrame>? FrameArrived;

    public event EventHandler<CameraState>? StateChanged;

    public bool Start(VideoFormat format)
    {
        if (State == CameraState.Disconnected)
        {
            return false;
        }

        if (_busy)
        {
            SetState(CameraState.Busy);
            return false;
        }

        if (!Descriptor.Formats.Any(f => f.Width == format.Width && f.Height == format.Height && f.FramesPerSecond == format.FramesPerSecond))
        {
            throw new ArgumentException($"Format {format} is not offered by {Descriptor.Name}.", nameof(format));
        }

        _format = format;
        NextDue = _device.Clock.Now;
        SetState(CameraState.Streaming);
        return true;
    }

    public void Stop()
    {
        NextDue = null;
        if (State is CameraState.Streaming or CameraState.Busy)
        {
            SetState(CameraState.Closed);
        }
    }

    /// <summary>Another app holds the camera exclusively.</summary>
    public void SetBusy(bool busy) => _busy = busy;

    public void Disconnect()
    {
        NextDue = null;
        SetState(CameraState.Disconnected);
    }

    public void Reconnect()
    {
        if (State == CameraState.Disconnected)
        {
            SetState(CameraState.Closed);
        }
    }

    /// <summary>Renders what the camera sees right now, regardless of streaming state.</summary>
    public VideoFrame Capture(int width, int height)
    {
        var roll = _device.TrueLidRollDegrees + _device.Profile.Camera.MountRotationDegrees;
        return SceneRenderer.Render(width, height, roll, _device.Clock.Now, _frameNumber);
    }

    internal void TickIfDue()
    {
        if (NextDue is not { } due || _device.Clock.Now < due)
        {
            return;
        }

        NextDue = due + TimeSpan.FromSeconds(1.0 / _format.FramesPerSecond);
        _frameNumber++;
        if (FrameArrived is null)
        {
            return;
        }

        var frame = Capture(_format.Width, _format.Height);
        FrameArrived(this, _format.Subtype.Equals("NV12", StringComparison.OrdinalIgnoreCase) ? FrameOps.BgraToNv12(frame) : frame);
    }

    private void SetState(CameraState state)
    {
        if (state != State)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
