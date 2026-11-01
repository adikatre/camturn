using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CamTurn.Core.Config;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Sensors;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using CamTurn.Platform.Windows;
using CamTurn.Simulator;

namespace CamTurn.App;

/// <summary>
/// Owns the running pipeline: settings, log, sensors, camera, frame pipe and engine. UI code talks only to this.
/// All public members are safe to call from the UI thread; events may arrive on any thread.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly CliOptions _options;
    private readonly SemaphoreSlim _rebuild = new(1, 1);
    private readonly SessionWatcher _session = new();
    private readonly DisplayOrientationSource _display = new();
    private IAccelerometer? _accelerometer;
    private SimDevice? _simDevice;
    private Timer? _simTicker;
    private readonly ConcurrentQueue<Action> _simActions = new();
    private CamTurnEngine? _engine;
    private ICameraSource? _camera;
    private bool _locked;
    private bool _paused;

    public AppHost(CliOptions options)
    {
        _options = options;
        var loaded = ConfigStore.Load(AppPaths.ConfigFile);
        Config = loaded.Config;
        Log = new FileLog(AppPaths.LogDirectory, options.LogLevel ?? Enum.Parse<LogLevel>(Config.LogLevel, ignoreCase: true));
        if (loaded.Warning is not null)
        {
            Log.Write(LogLevel.Warn, loaded.Warning);
        }

        if (loaded.Migrated)
        {
            ConfigStore.Save(Config, AppPaths.ConfigFile);
        }
    }

    public AppConfig Config { get; private set; }

    public FileLog Log { get; }

    public IReadOnlyList<CameraInfo> Cameras { get; private set; } = [];

    public EngineStatus Status => _engine?.Status ?? new EngineStatus();

    public bool IsSimulated => _options.TestPattern || _options.SimulateOrientation is not null || _options.SimulateDevice is not null;

    public SimDevice? SimulatedDevice => _simDevice;

    public event EventHandler<EngineStatus>? StatusChanged;

    public event EventHandler<VideoFrame>? PreviewFrame;

    public async Task StartAsync()
    {
        Log.Write(LogLevel.Info, $"CamTurn starting (Windows build {Environment.OSVersion.Version.Build}){(IsSimulated ? " [simulated inputs]" : "")}");
        _accelerometer = CreateAccelerometer();
        Log.Write(LogLevel.Info, _accelerometer is null ? "No accelerometer: Auto rotation unavailable" : $"Orientation source: {_accelerometer.Descriptor.Name}");

        _session.LockedChanged += (_, locked) =>
        {
            _locked = locked;
            Log.Write(LogLevel.Info, locked ? "Session locked" : "Session unlocked");
            _engine?.SetSessionLocked(locked);
        };
        _session.SuspendedChanged += (_, s) =>
        {
            Log.Write(LogLevel.Info, s ? "Suspending" : "Resumed");
            _engine?.SetSuspended(s);
        };

        await RefreshCamerasAsync();
        await RebuildEngineAsync();
    }

    public async Task RefreshCamerasAsync()
    {
        try
        {
            Cameras = await MediaCaptureCamera.FindAllAsync();
        }
        catch (Exception e)
        {
            Log.Write(LogLevel.Error, $"Camera enumeration failed: {e.Message}");
            Cameras = [];
        }
    }

    /// <summary>Applies new settings: saved immediately; the engine is rebuilt only if the camera changed.</summary>
    public async Task UpdateConfigAsync(AppConfig next)
    {
        next = next.Sanitized();
        var cameraChanged = next.CameraId != Config.CameraId;
        var autostartChanged = next.StartWithWindows != Config.StartWithWindows;
        Config = next;
        ConfigStore.Save(next, AppPaths.ConfigFile);
        if (Enum.TryParse<LogLevel>(next.LogLevel, true, out var level))
        {
            Log.Minimum = _options.LogLevel ?? level;
        }

        if (autostartChanged)
        {
            if (next.StartWithWindows)
            {
                Autostart.Enable(Environment.ProcessPath!, minimized: true);
            }
            else
            {
                Autostart.Disable();
            }
        }

        if (cameraChanged)
        {
            await RebuildEngineAsync();
        }
        else
        {
            _engine?.ApplyConfig(next);
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        _engine?.SetPaused(paused);
        if (Config.RunMode == RunMode.Manual)
        {
            _engine?.SetManual(!paused);
        }
    }

    /// <summary>Developer mode (--simulate-device): move the simulated device to a pose preset.</summary>
    public void SetSimulatedPose(string preset)
    {
        if (_simDevice is { } d)
        {
            _simActions.Enqueue(() => d.SetPose(DevicePose.Preset(preset), TimeSpan.FromSeconds(0.6)));
        }
    }

    public void SetPreviewActive(bool active) => _engine?.SetPreviewActive(active);

    public async Task<string> DiagnosticsAsync() => await DiagnosticsReport.BuildAsync(Config, _engine?.Status, Log);

    public async ValueTask DisposeAsync()
    {
        _simTicker?.Dispose();
        if (_engine is not null)
        {
            await _engine.DisposeAsync();
        }

        (_accelerometer as IDisposable)?.Dispose();
        _session.Dispose();
        _display.Dispose();
        Log.Write(LogLevel.Info, "CamTurn stopped");
    }

    private IAccelerometer? CreateAccelerometer()
    {
        if (_options.SimulateDevice is { } profileName)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "sim", "devices");
            _simDevice = new SimDevice(DeviceProfile.Resolve(profileName, dir));
            var last = DateTime.UtcNow;
            _simTicker = new Timer(_ =>
            {
                var now = DateTime.UtcNow;
                lock (_simDevice)
                {
                    // Camera start/stop requests are applied here, on the simulator thread, so the engine never
                    // waits on the simulator lock while holding its own (no lock-order inversion).
                    while (_simActions.TryDequeue(out var action))
                    {
                        action();
                    }

                    _simDevice.Advance(now - last);
                }

                last = now;
            }, null, 0, 16);
            return _simDevice.LidAccelerometer;
        }

        if (_options.SimulateOrientation is { } roll)
        {
            var fixedAccel = new FixedAccelerometer(roll);
            fixedAccel.Start();
            return fixedAccel;
        }

        return WinRtAccelerometer.TryGetDefault();
    }

    private async Task<ICameraSource> CreateCameraAsync()
    {
        if (_options.TestPattern)
        {
            var roll = _options.SimulateOrientation ?? 0;
            return new TestPatternCamera(() => _simDevice?.TrueLidRollDegrees ?? roll);
        }

        if (_simDevice is not null)
        {
            return new SimCameraAdapter(_simDevice, _simActions);
        }

        var info = Cameras.FirstOrDefault(c => c.GroupId == Config.CameraId) ?? Cameras.FirstOrDefault();
        if (info is null)
        {
            Log.Write(LogLevel.Warn, "No physical camera found");
            return new MissingCamera();
        }

        Log.Write(LogLevel.Info, $"Using camera: {info.Name}");
        return await MediaCaptureCamera.CreateAsync(info, probeFormats: true);
    }

    private async Task RebuildEngineAsync()
    {
        await _rebuild.WaitAsync();
        try
        {
            if (_engine is not null)
            {
                await _engine.DisposeAsync();
                _engine = null;
            }

            (_camera as IDisposable)?.Dispose();
            _camera = await CreateCameraAsync();
            var host = new FramePipeHost(FramePipe.Acceptor());
            host.ConsumerConnected += (_, c) => Log.Write(LogLevel.Info, $"Virtual camera consumer connected ({c.Width}x{c.Height}@{c.Fps})");
            host.ConsumerDisconnected += (_, c) => Log.Write(LogLevel.Info, "Virtual camera consumer disconnected");
            host.ProtocolError += (_, m) => Log.Write(LogLevel.Warn, $"Pipe protocol error: {m}");

            var engine = new CamTurnEngine(new EngineDependencies { Camera = _camera, Accelerometer = _accelerometer, Display = _display, Host = host }, Config);
            engine.StatusChanged += (_, s) =>
            {
                if (s.LastError is not null)
                {
                    Log.Write(LogLevel.Warn, s.LastError);
                }

                StatusChanged?.Invoke(this, s);
            };
            engine.PreviewFrame += (_, f) => PreviewFrame?.Invoke(this, f);
            engine.Start();
            engine.SetSessionLocked(_locked);
            engine.SetPaused(_paused);
            _engine = engine;
            StatusChanged?.Invoke(this, engine.Status);
        }
        finally
        {
            _rebuild.Release();
        }
    }

    /// <summary>Stand-in when no physical camera exists: always reports Disconnected.</summary>
    private sealed class MissingCamera : ICameraSource
    {
        public CameraDescriptor Descriptor { get; } = new("none", "No camera", "Unknown", [new(1280, 720, 30, "NV12")]);

        public CameraState State { get; private set; } = CameraState.Closed;

        public event EventHandler<VideoFrame>? FrameArrived
        {
            add { }
            remove { }
        }

        public event EventHandler<CameraState>? StateChanged;

        public bool Start(VideoFormat format)
        {
            State = CameraState.Disconnected;
            StateChanged?.Invoke(this, State);
            return false;
        }

        public void Stop() => State = CameraState.Closed;
    }

    /// <summary>Developer mode: the simulated lid camera, at a fixed 640x480.</summary>
    private sealed class SimCameraAdapter : ICameraSource
    {
        private readonly SimDevice _device;
        private readonly ConcurrentQueue<Action> _actions;

        public SimCameraAdapter(SimDevice device, ConcurrentQueue<Action> actions)
        {
            _device = device;
            _actions = actions;
            _device.Camera.FrameArrived += (_, f) => FrameArrived?.Invoke(this, f);
            _device.Camera.StateChanged += (_, s) => StateChanged?.Invoke(this, s);
        }

        public CameraDescriptor Descriptor => _device.Camera.Descriptor;

        public CameraState State => _device.Camera.State;

        public event EventHandler<VideoFrame>? FrameArrived;

        public event EventHandler<CameraState>? StateChanged;

        public bool Start(VideoFormat format)
        {
            _actions.Enqueue(() => _device.Camera.Start(new VideoFormat(640, 480, 30, "YUY2")));
            return true;
        }

        public void Stop() => _actions.Enqueue(() => _device.Camera.Stop());
    }
}
