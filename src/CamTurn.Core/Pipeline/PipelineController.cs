using CamTurn.Core.Video;

namespace CamTurn.Core.Pipeline;

public enum RunMode
{
    /// <summary>Open the physical camera only while an app uses CamTurn Camera (plus a grace period).</summary>
    Auto,

    /// <summary>Keep the physical camera open whenever CamTurn runs.</summary>
    AlwaysOn,

    /// <summary>Open/close from the tray menu only.</summary>
    Manual,
}

public enum PipelineStatus
{
    /// <summary>Nothing needs the camera; it is closed (privacy light off).</summary>
    Idle,

    /// <summary>Camera should be open and is being opened.</summary>
    Starting,

    /// <summary>Camera open and frames flowing.</summary>
    Streaming,

    /// <summary>Camera closed because the session is locked or the machine is suspending.</summary>
    Paused,

    /// <summary>Camera is held exclusively by another app; retrying.</summary>
    CameraBusy,

    /// <summary>Selected camera is not present; retrying.</summary>
    CameraMissing,
}

/// <summary>Side effects the controller asks the host to perform. Implemented by the app (or fakes in tests).</summary>
public interface IPipelineEffects
{
    void OpenCamera();

    void CloseCamera();
}

public sealed record PipelineOptions
{
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(2);

    public bool ReleaseOnLock { get; init; } = true;
}

/// <summary>
/// Decides when the physical camera is open. Pure: time is passed in, effects go through
/// <see cref="IPipelineEffects"/>. Call <see cref="Tick"/> periodically (e.g. every 250 ms) for the grace
/// period and retries.
/// </summary>
public sealed class PipelineController
{
    private readonly IPipelineEffects _effects;
    private readonly PipelineOptions _options;
    private int _consumers;
    private TimeSpan _graceUntil = TimeSpan.MinValue;
    private TimeSpan _lastAttempt = TimeSpan.MinValue;
    private bool _manualOn;
    private bool _locked;
    private bool _suspended;
    private bool _paused;
    private bool _cameraRequested;
    private CameraState _cameraState = CameraState.Closed;

    public PipelineController(IPipelineEffects effects, RunMode mode = RunMode.Auto, PipelineOptions? options = null)
    {
        _effects = effects;
        Mode = mode;
        _options = options ?? new PipelineOptions();
    }

    public RunMode Mode { get; private set; }

    public int Consumers => _consumers;

    public PipelineStatus Status { get; private set; } = PipelineStatus.Idle;

    public event EventHandler<PipelineStatus>? StatusChanged;

    public void SetMode(RunMode mode, TimeSpan now)
    {
        Mode = mode;
        Evaluate(now);
    }

    public void ConsumerConnected(TimeSpan now)
    {
        _consumers++;
        Evaluate(now);
    }

    public void ConsumerDisconnected(TimeSpan now)
    {
        _consumers = Math.Max(0, _consumers - 1);
        if (_consumers == 0)
        {
            _graceUntil = now + _options.GracePeriod;
        }

        Evaluate(now);
    }

    public void SetManual(bool on, TimeSpan now)
    {
        _manualOn = on;
        Evaluate(now);
    }

    public void SetSessionLocked(bool locked, TimeSpan now)
    {
        _locked = locked;
        Evaluate(now);
    }

    public void SetSuspended(bool suspended, TimeSpan now)
    {
        _suspended = suspended;
        Evaluate(now);
    }

    /// <summary>Tray "Stop": keep the camera closed in every run mode until resumed.</summary>
    public void SetPaused(bool paused, TimeSpan now)
    {
        _paused = paused;
        Evaluate(now);
    }

    public bool Paused => _paused;

    public void CameraStateChanged(CameraState state, TimeSpan now)
    {
        _cameraState = state;
        if (state is CameraState.Busy or CameraState.Disconnected)
        {
            // The host's open attempt failed; it is no longer open. Retry from Tick.
            _cameraRequested = false;
        }

        Evaluate(now);
    }

    /// <summary>A frame arrived from the physical camera.</summary>
    public void FrameArrived(TimeSpan now)
    {
        if (_cameraState != CameraState.Streaming)
        {
            CameraStateChanged(CameraState.Streaming, now);
        }
    }

    public void Tick(TimeSpan now) => Evaluate(now);

    public bool WantsCamera(TimeSpan now)
    {
        if (_paused || _suspended || (_locked && _options.ReleaseOnLock))
        {
            return false;
        }

        return Mode switch
        {
            RunMode.AlwaysOn => true,
            RunMode.Manual => _manualOn,
            _ => _consumers > 0 || now < _graceUntil,
        };
    }

    private void Evaluate(TimeSpan now)
    {
        var want = WantsCamera(now);
        if (want && !_cameraRequested)
        {
            var failed = _cameraState is CameraState.Busy or CameraState.Disconnected;
            if (!failed || now - _lastAttempt >= _options.RetryInterval)
            {
                _cameraRequested = true;
                _lastAttempt = now;
                _effects.OpenCamera();
            }
        }
        else if (!want && _cameraRequested)
        {
            _cameraRequested = false;
            _effects.CloseCamera();
            _cameraState = CameraState.Closed;
        }

        SetStatus(ComputeStatus(want));
    }

    private PipelineStatus ComputeStatus(bool want)
    {
        if (!want)
        {
            return _paused || _suspended || (_locked && _options.ReleaseOnLock) ? PipelineStatus.Paused : PipelineStatus.Idle;
        }

        return _cameraState switch
        {
            CameraState.Busy => PipelineStatus.CameraBusy,
            CameraState.Disconnected => PipelineStatus.CameraMissing,
            CameraState.Streaming => PipelineStatus.Streaming,
            _ => PipelineStatus.Starting,
        };
    }

    private void SetStatus(PipelineStatus status)
    {
        if (status != Status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }
    }
}
