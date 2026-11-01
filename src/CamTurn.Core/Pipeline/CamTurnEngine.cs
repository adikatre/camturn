using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;

namespace CamTurn.Core.Pipeline;

/// <summary>Snapshot of everything the tray and status panel show.</summary>
public sealed record EngineStatus
{
    public PipelineStatus Pipeline { get; init; }

    public int Consumers { get; init; }

    public CameraState Camera { get; init; }

    public string CameraName { get; init; } = "";

    public double Fps { get; init; }

    public Quadrant? Quadrant { get; init; }

    public bool Holding { get; init; }

    public double RollDegrees { get; init; } = double.NaN;

    public int CorrectionCw { get; init; }

    public string RotationSource { get; init; } = "";

    public bool AutoAvailable { get; init; }

    public bool PreviewActive { get; init; }

    public bool Paused { get; init; }

    public RunMode RunMode { get; init; }

    public string? LastError { get; init; }
}

/// <summary>Everything the engine talks to. Real Windows implementations or simulator/fakes.</summary>
public sealed record EngineDependencies
{
    public required ICameraSource Camera { get; init; }

    /// <summary>Lid accelerometer (or a base+hinge adapter). Null = no sensor: Auto unavailable.</summary>
    public IAccelerometer? Accelerometer { get; init; }

    public IDisplayOrientationSource? Display { get; init; }

    public required FramePipeHost Host { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>
/// Wires CamTurn together: consumers on the pipe drive <see cref="PipelineController"/>, which opens and closes the
/// camera; camera frames are rotated per <see cref="RotationPolicy"/> by a <see cref="Compositor"/> for each
/// requested output size and published to the pipe; sensor readings feed the <see cref="OrientationTracker"/>.
/// Thread-safe: events arrive from camera, sensor, pipe and timer threads.
/// </summary>
public sealed class CamTurnEngine : IAsyncDisposable, IPipelineEffects
{
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(66);
    private readonly EngineDependencies _deps;
    private readonly object _gate = new();
    private readonly PipelineController _controller;
    private readonly OrientationTracker _tracker = new();
    private readonly Dictionary<(int W, int H), (Compositor C, byte[] Buffer)> _outputs = [];
    private readonly Compositor _previewCompositor = new();
    private readonly long _start;
    private AppConfig _config;
    private ITimer? _timer;
    private ulong _frameCounter;
    private int _framesThisSecond;
    private TimeSpan _fpsWindowStart;
    private double _fps;
    private TimeSpan _lastPreview = TimeSpan.MinValue;
    private bool _previewActive;
    private string? _lastError;
    private EngineStatus _status = new();

    public CamTurnEngine(EngineDependencies deps, AppConfig config)
    {
        _deps = deps;
        _config = config.Sanitized();
        _start = deps.Time.GetTimestamp();
        _controller = new PipelineController(this, _config.RunMode, new PipelineOptions { ReleaseOnLock = _config.ReleaseOnLock });
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    /// <summary>Composed output frames (NV12, half the output size) at ~15 fps while a preview is active.</summary>
    public event EventHandler<VideoFrame>? PreviewFrame;

    public EngineStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public AppConfig Config
    {
        get
        {
            lock (_gate)
            {
                return _config;
            }
        }
    }

    private TimeSpan Now => _deps.Time.GetElapsedTime(_start);

    public void Start()
    {
        _deps.Host.ConsumerConnected += (_, c) => Locked(() => _controller.ConsumerConnected(Now));
        _deps.Host.ConsumerDisconnected += (_, c) => Locked(() => _controller.ConsumerDisconnected(Now));
        _deps.Host.ProtocolError += (_, msg) => Locked(() => _lastError = "Virtual camera connection: " + msg);
        _deps.Camera.FrameArrived += OnFrame;
        _deps.Camera.StateChanged += (_, s) => Locked(() =>
        {
            if (s == CameraState.Busy)
            {
                _lastError = $"{_deps.Camera.Descriptor.Name} is in use by another app.";
            }
            else if (s == CameraState.Disconnected)
            {
                _lastError = $"{_deps.Camera.Descriptor.Name} is not connected.";
            }

            _controller.CameraStateChanged(s, Now);
        });

        if (_deps.Accelerometer is { } accel)
        {
            accel.ReadingChanged += (_, r) => Locked(() => _tracker.Update(r with { Timestamp = Now }));
            if (accel.GetCurrentReading() is { } first)
            {
                Locked(() => _tracker.Update(first with { Timestamp = Now }));
            }
        }

        _deps.Host.Start();
        _timer = _deps.Time.CreateTimer(_ => Locked(() => _controller.Tick(Now)), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        Locked(() => _controller.Tick(Now));
    }

    public void ApplyConfig(AppConfig config)
    {
        Locked(() =>
        {
            _config = config.Sanitized();
            _controller.SetMode(_config.RunMode, Now);
        });
    }

    /// <summary>Manual mode start/stop from the tray.</summary>
    public void SetManual(bool on) => Locked(() => _controller.SetManual(on, Now));

    /// <summary>Tray Start/Stop: pause keeps the camera closed in every run mode.</summary>
    public void SetPaused(bool paused) => Locked(() => _controller.SetPaused(paused, Now));

    public void SetSessionLocked(bool locked) => Locked(() => _controller.SetSessionLocked(locked, Now));

    public void SetSuspended(bool suspended) => Locked(() => _controller.SetSuspended(suspended, Now));

    /// <summary>The settings window's live preview counts as a consumer, so the camera opens for it (and the light shows).</summary>
    public void SetPreviewActive(bool active) => Locked(() =>
    {
        if (active == _previewActive)
        {
            return;
        }

        _previewActive = active;
        if (active)
        {
            _controller.ConsumerConnected(Now);
        }
        else
        {
            _controller.ConsumerDisconnected(Now);
        }
    });

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _deps.Camera.FrameArrived -= OnFrame;
        _deps.Camera.Stop();
        await _deps.Host.DisposeAsync().ConfigureAwait(false);
    }

    void IPipelineEffects.OpenCamera()
    {
        var formats = _deps.Camera.Descriptor.Formats;
        var target = _config;
        var format = formats
            .OrderBy(f => Math.Abs(f.FramesPerSecond - target.Fps))
            .ThenBy(f => f.Width >= target.OutputWidth && f.Height >= target.OutputHeight ? 0 : 1)
            .ThenBy(f => Math.Abs((f.Width * f.Height) - (1920 * 1080)))
            .FirstOrDefault();
        _lastError = null;
        if (!_deps.Camera.Start(format))
        {
            _lastError ??= $"Could not open {_deps.Camera.Descriptor.Name}.";
        }
    }

    void IPipelineEffects.CloseCamera() => _deps.Camera.Stop();

    private void OnFrame(object? sender, VideoFrame frame)
    {
        if (frame.Format != PixelFormat.Nv12)
        {
            frame = FrameOps.BgraToNv12(frame);
        }

        var now = Now;
        RotationDecision decision;
        AppConfig cfg;
        (int W, int H)[] sizes;
        bool preview;
        ulong counter;
        lock (_gate)
        {
            _controller.FrameArrived(now);
            cfg = _config;
            decision = RotationPolicy.Decide(cfg.ToRotationSettings(), _deps.Accelerometer is null ? null : _tracker.State, _deps.Display?.RotationDegrees);
            sizes = _deps.Host.Consumers.Select(c => (c.Width, c.Height)).Distinct().ToArray();
            preview = _previewActive && now - _lastPreview >= PreviewInterval;
            if (preview)
            {
                _lastPreview = now;
            }

            counter = ++_frameCounter;
            _framesThisSecond++;
            if (now - _fpsWindowStart >= TimeSpan.FromSeconds(1))
            {
                _fps = _framesThisSecond / (now - _fpsWindowStart).TotalSeconds;
                _framesThisSecond = 0;
                _fpsWindowStart = now;
            }
        }

        var flags = (cfg.Mirror ? FrameFlags.Mirrored : FrameFlags.None) | (decision.Holding ? FrameFlags.Holding : FrameFlags.None);
        foreach (var size in sizes)
        {
            (Compositor C, byte[] Buffer) output;
            lock (_outputs)
            {
                if (!_outputs.TryGetValue(size, out output))
                {
                    output = (new Compositor(), new byte[FrameProtocol.Nv12Size(size.W, size.H)]);
                    _outputs[size] = output;
                }
            }

            output.C.Compose(frame, new CompositorSettings(decision.CorrectionCw, cfg.Mirror, cfg.PortraitFit, size.W, size.H), output.Buffer);
            _deps.Host.Publish(output.Buffer, size.W, size.H, counter, frame.Timestamp.Ticks, flags, decision.CorrectionCw);
        }

        if (preview)
        {
            int pw = (cfg.OutputWidth / 2) & ~1, ph = (cfg.OutputHeight / 2) & ~1;
            var buf = _previewCompositor.Compose(frame, new CompositorSettings(decision.CorrectionCw, cfg.Mirror, cfg.PortraitFit, pw, ph));
            PreviewFrame?.Invoke(this, new VideoFrame(pw, ph, PixelFormat.Nv12, buf, pw, (long)counter, frame.Timestamp));
        }

        Locked(() => { });
    }

    private void Locked(Action action)
    {
        EngineStatus? changed = null;
        lock (_gate)
        {
            action();
            var next = Snapshot();
            if (next != _status)
            {
                _status = next;
                changed = next;
            }
        }

        if (changed is not null)
        {
            StatusChanged?.Invoke(this, changed);
        }
    }

    private EngineStatus Snapshot()
    {
        var decision = RotationPolicy.Decide(_config.ToRotationSettings(), _deps.Accelerometer is null ? null : _tracker.State, _deps.Display?.RotationDegrees);
        var streaming = _controller.Status == PipelineStatus.Streaming;
        return new EngineStatus
        {
            Pipeline = _controller.Status,
            Consumers = _controller.Consumers - (_previewActive ? 1 : 0),
            Camera = _deps.Camera.State,
            CameraName = _deps.Camera.Descriptor.Name,
            Fps = streaming ? Math.Round(_fps, 1) : 0,
            Quadrant = decision.Quadrant,
            Holding = decision.Holding,
            RollDegrees = Math.Round(_tracker.State.RollDegrees, 0),
            CorrectionCw = decision.CorrectionCw,
            RotationSource = decision.Source,
            AutoAvailable = _deps.Accelerometer is not null,
            PreviewActive = _previewActive,
            Paused = _controller.Paused,
            RunMode = _controller.Mode,
            LastError = _lastError,
        };
    }
}
