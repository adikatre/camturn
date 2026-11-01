using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using CamTurn.Platform.Windows;

namespace CamTurn.App.ViewModels;

/// <summary>Settings window state. Every change is applied and saved immediately (no OK/Apply button).</summary>
public sealed class SettingsViewModel : Observable
{
    private readonly AppHost _host;
    private bool _loading;
    private string? _cameraId;
    private Choice<(int W, int H)> _resolution;
    private Choice<int> _fps;
    private bool _autoRotation;
    private bool _followDisplay;
    private Choice<int> _manualRotation;
    private bool _mirror;
    private bool _portraitFill;
    private bool _startWithWindows;
    private bool _startMinimized;
    private Choice<RunMode> _runMode;
    private bool _releaseOnLock;
    private RotationMapping _mapping;
    private EngineStatus _status = new();
    private string _virtualCamera = "Checking…";
    private string _mediaSource = "Checking…";
    private bool _repairNeeded;
    private string _copyLabel = "Copy diagnostics";

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        Resolutions = [new((1280, 720), "1280 × 720 (recommended)"), new((1920, 1080), "1920 × 1080")];
        FrameRates = [new(15, "15 fps"), new(24, "24 fps"), new(30, "30 fps")];
        ManualRotations = [new(0, "0° (no rotation)"), new(90, "90° clockwise"), new(180, "180° (upside down)"), new(270, "270° clockwise")];
        RunModes =
        [
            new(RunMode.Auto, "Automatic: camera on only while an app uses CamTurn Camera"),
            new(RunMode.AlwaysOn, "Always on while CamTurn runs"),
            new(RunMode.Manual, "Manual: start and stop from the tray"),
        ];
        _resolution = Resolutions[0];
        _fps = FrameRates[2];
        _manualRotation = ManualRotations[0];
        _runMode = RunModes[0];
        _mapping = RotationMapping.Default;

        FlipDirectionCommand = new RelayCommand(() =>
        {
            _mapping = _mapping.FlipDirection();
            RaiseMapping();
            Save();
        });
        ResetMappingCommand = new RelayCommand(() =>
        {
            _mapping = RotationMapping.Default;
            RaiseMapping();
            Save();
        });
        CopyDiagnosticsCommand = new RelayCommand(async () => await CopyDiagnosticsAsync());
        RepairCommand = new RelayCommand(Repair);
        RefreshCamerasCommand = new RelayCommand(async () => await LoadCamerasAsync());

        LoadFrom(host.Config);
        _status = host.Status;
        host.StatusChanged += (_, s) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _status = s;
            RaiseStatus();
        });
    }

    public ObservableCollection<Choice<string?>> Cameras { get; } = [];

    public IReadOnlyList<Choice<(int W, int H)>> Resolutions { get; }

    public IReadOnlyList<Choice<int>> FrameRates { get; }

    public IReadOnlyList<Choice<int>> ManualRotations { get; }

    public IReadOnlyList<Choice<RunMode>> RunModes { get; }

    public ICommand FlipDirectionCommand { get; }

    public ICommand ResetMappingCommand { get; }

    public ICommand CopyDiagnosticsCommand { get; }

    public ICommand RepairCommand { get; }

    public ICommand RefreshCamerasCommand { get; }

    public Choice<string?>? SelectedCamera
    {
        get => Cameras.FirstOrDefault(c => c.Value == _cameraId) ?? Cameras.FirstOrDefault();
        set
        {
            if (value is not null && value.Value != _cameraId)
            {
                _cameraId = value.Value;
                Raise();
                Save();
            }
        }
    }

    public Choice<(int W, int H)> Resolution
    {
        get => _resolution;
        set => SetAndSave(ref _resolution, value);
    }

    public Choice<int> FrameRate
    {
        get => _fps;
        set => SetAndSave(ref _fps, value);
    }

    public bool AutoRotation
    {
        get => _autoRotation;
        set
        {
            if (SetAndSave(ref _autoRotation, value))
            {
                Raise(nameof(ManualRotationEnabled));
            }
        }
    }

    public bool ManualRotationEnabled => !_autoRotation;

    public bool FollowDisplay
    {
        get => _followDisplay;
        set => SetAndSave(ref _followDisplay, value);
    }

    public Choice<int> ManualRotation
    {
        get => _manualRotation;
        set => SetAndSave(ref _manualRotation, value);
    }

    public bool Mirror
    {
        get => _mirror;
        set => SetAndSave(ref _mirror, value);
    }

    public bool PortraitFill
    {
        get => _portraitFill;
        set => SetAndSave(ref _portraitFill, value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetAndSave(ref _startWithWindows, value);
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set => SetAndSave(ref _startMinimized, value);
    }

    public Choice<RunMode> RunModeChoice
    {
        get => _runMode;
        set => SetAndSave(ref _runMode, value);
    }

    public bool ReleaseOnLock
    {
        get => _releaseOnLock;
        set => SetAndSave(ref _releaseOnLock, value);
    }

    public string Mapping0 => $"Upright (0°) → rotate {_mapping.For0}°";

    public string Mapping90 => $"Right edge down (90°) → rotate {_mapping.For90}°";

    public string Mapping180 => $"Upside down / tent (180°) → rotate {_mapping.For180}°";

    public string Mapping270 => $"Left edge down (270°) → rotate {_mapping.For270}°";

    public bool MappingIsDefault => _mapping == RotationMapping.Default;

    // Status panel. Every line is text, never colour alone.
    public string OrientationBadge => TrayIcon.DescribeOrientation(_status).Replace("Orientation: ", "", StringComparison.Ordinal);

    public string PipelineText => TrayIcon.Describe(_status);

    public string CameraText => $"{_status.CameraName}: {_status.Camera}";

    public string ConsumerText => _status.Consumers switch
    {
        0 => "No app is using CamTurn Camera",
        1 => "1 app is using CamTurn Camera",
        var n => $"{n} apps are using CamTurn Camera",
    };

    public string FpsText => _status.Fps > 0 ? $"{_status.Fps:0.#} fps" : "–";

    public string ErrorText => _status.LastError ?? "None";

    public string RotationText => $"{_status.CorrectionCw}° clockwise ({_status.RotationSource})";

    public string SensorText => _status.AutoAvailable ? "Accelerometer found" : "No accelerometer: automatic rotation is unavailable, use Manual";

    public string VirtualCameraText
    {
        get => _virtualCamera;
        private set => Set(ref _virtualCamera, value);
    }

    public string MediaSourceText
    {
        get => _mediaSource;
        private set => Set(ref _mediaSource, value);
    }

    public bool RepairNeeded
    {
        get => _repairNeeded;
        private set => Set(ref _repairNeeded, value);
    }

    public string CopyLabel
    {
        get => _copyLabel;
        private set => Set(ref _copyLabel, value);
    }

    public async Task LoadAsync()
    {
        await LoadCamerasAsync();
        await RefreshVirtualCameraAsync();
    }

    public async Task RefreshVirtualCameraAsync()
    {
        var (state, path) = await Task.Run(() => (new VirtualCameraRegistrar().GetState(), VirtualCameraRegistrar.MediaSourcePath()));
        MediaSourceText = path is null ? "Not installed" : "Installed";
        VirtualCameraText = state switch
        {
            VirtualCameraState.Registered => "Registered: select \"CamTurn Camera\" in your apps",
            VirtualCameraState.NotRegistered => "Not registered",
            VirtualCameraState.MediaSourceMissing => "Media source missing: reinstall CamTurn",
            _ => "Not supported on this Windows version (Windows 11 required)",
        };
        RepairNeeded = state is VirtualCameraState.NotRegistered;
    }

    private async Task LoadCamerasAsync()
    {
        await _host.RefreshCamerasAsync();
        _loading = true;
        Cameras.Clear();
        foreach (var c in _host.Cameras)
        {
            Cameras.Add(new Choice<string?>(c.GroupId, $"{c.Name}{(c.Panel == "Front" ? " (front)" : c.Panel == "Back" ? " (back)" : "")}"));
        }

        if (Cameras.Count == 0)
        {
            Cameras.Add(new Choice<string?>(null, "No camera found"));
        }

        Raise(nameof(SelectedCamera));
        _loading = false;
    }

    private void LoadFrom(AppConfig c)
    {
        _loading = true;
        _cameraId = c.CameraId;
        _resolution = Resolutions.FirstOrDefault(r => r.Value == (c.OutputWidth, c.OutputHeight)) ?? Resolutions[0];
        _fps = FrameRates.FirstOrDefault(f => f.Value == c.Fps) ?? FrameRates[2];
        _autoRotation = c.RotationMode == RotationMode.Auto;
        _followDisplay = c.FollowDisplayRotation;
        _manualRotation = ManualRotations.FirstOrDefault(m => m.Value == c.ManualRotation) ?? ManualRotations[0];
        _mirror = c.Mirror;
        _portraitFill = c.PortraitFit == FitMode.Fill;
        _startWithWindows = Autostart.IsEnabled();
        _startMinimized = c.StartMinimized;
        _runMode = RunModes.First(r => r.Value == c.RunMode);
        _releaseOnLock = c.ReleaseOnLock;
        _mapping = c.Mapping.ToMapping();
        _loading = false;
    }

    private bool SetAndSave<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name))
        {
            return false;
        }

        Save();
        return true;
    }

    private void Save()
    {
        if (_loading)
        {
            return;
        }

        var next = _host.Config with
        {
            CameraId = _cameraId,
            OutputWidth = _resolution.Value.W,
            OutputHeight = _resolution.Value.H,
            Fps = _fps.Value,
            RotationMode = _autoRotation ? RotationMode.Auto : RotationMode.Manual,
            FollowDisplayRotation = _followDisplay,
            ManualRotation = _manualRotation.Value,
            Mirror = _mirror,
            PortraitFit = _portraitFill ? FitMode.Fill : FitMode.Fit,
            StartWithWindows = _startWithWindows,
            StartMinimized = _startMinimized,
            RunMode = _runMode.Value,
            ReleaseOnLock = _releaseOnLock,
            Mapping = MappingConfig.From(_mapping),
        };
        _ = SaveAsync(next);
    }

    private async Task SaveAsync(AppConfig next)
    {
        try
        {
            await _host.UpdateConfigAsync(next);
        }
        catch (Exception e)
        {
            _host.Log.Write(LogLevel.Error, $"Saving settings failed: {e.Message}");
        }
    }

    private void RaiseMapping()
    {
        foreach (var p in new[] { nameof(Mapping0), nameof(Mapping90), nameof(Mapping180), nameof(Mapping270), nameof(MappingIsDefault) })
        {
            Raise(p);
        }
    }

    private void RaiseStatus()
    {
        foreach (var p in new[] { nameof(OrientationBadge), nameof(PipelineText), nameof(CameraText), nameof(ConsumerText), nameof(FpsText), nameof(ErrorText), nameof(RotationText), nameof(SensorText) })
        {
            Raise(p);
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        CopyLabel = "Collecting…";
        var text = await _host.DiagnosticsAsync();
        Clipboard.SetText(text);
        CopyLabel = "Copied to clipboard";
        await Task.Delay(2000);
        CopyLabel = "Copy diagnostics";
    }

    /// <summary>Runs "CamTurn --register" elevated (the only UAC prompt outside the installer).</summary>
    private async void Repair()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--register") { UseShellExecute = true, Verb = "runas" });
            if (p is not null)
            {
                await p.WaitForExitAsync();
            }
        }
        catch (Win32Exception)
        {
            // UAC prompt declined.
        }

        await RefreshVirtualCameraAsync();
    }
}
