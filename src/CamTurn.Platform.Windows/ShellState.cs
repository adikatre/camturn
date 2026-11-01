using CamTurn.Core.Sensors;
using Microsoft.Win32;

namespace CamTurn.Platform.Windows;

/// <summary>
/// Desktop rotation from EnumDisplaySettingsW (dmDisplayOrientation) for the "follow display rotation" advanced mode.
/// DMDO_90/180/270 are mapped to 90/180/270; whether that matches the physical roll direction on a given device is
/// unverified (KNOWN_ISSUES K-10), and the mapping table / flip calibration covers a mismatch.
/// </summary>
public sealed class DisplayOrientationSource : IDisplayOrientationSource, IDisposable
{
    public DisplayOrientationSource()
    {
        RotationDegrees = Read();
        SystemEvents.DisplaySettingsChanged += OnChanged;
    }

    public int RotationDegrees { get; private set; }

    public bool RotationLocked =>
        Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell", "RotationLockPreference", 0) is 1;

    public event EventHandler<int>? Changed;

    public static int Read()
    {
        var dm = new NativeMethods.DEVMODE { Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DEVMODE>() };
        return NativeMethods.EnumDisplaySettingsW(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm) ? (dm.DisplayOrientation & 3) * 90 : 0;
    }

    public static (int Width, int Height) PrimaryResolution()
    {
        var dm = new NativeMethods.DEVMODE { Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DEVMODE>() };
        return NativeMethods.EnumDisplaySettingsW(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm) ? (dm.PelsWidth, dm.PelsHeight) : (0, 0);
    }

    public void Dispose() => SystemEvents.DisplaySettingsChanged -= OnChanged;

    private void OnChanged(object? sender, EventArgs e)
    {
        var now = Read();
        if (now != RotationDegrees)
        {
            RotationDegrees = now;
            Changed?.Invoke(this, now);
        }
    }
}

/// <summary>Laptop vs slate posture via GetSystemMetrics(SM_CONVERTIBLESLATEMODE), polled (informational only).</summary>
public sealed class PostureSource : IPostureSource, IDisposable
{
    private readonly Timer _timer;

    public PostureSource()
    {
        Current = Read();
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public PostureMode Current { get; private set; }

    public event EventHandler<PostureMode>? Changed;

    public static PostureMode Read() =>
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CONVERTIBLESLATEMODE) == 0 ? PostureMode.Slate : PostureMode.Laptop;

    public void Dispose() => _timer.Dispose();

    private void Poll()
    {
        var now = Read();
        if (now != Current)
        {
            Current = now;
            Changed?.Invoke(this, now);
        }
    }
}

/// <summary>Session lock/unlock and suspend/resume notifications.</summary>
public sealed class SessionWatcher : IDisposable
{
    public SessionWatcher()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public event EventHandler<bool>? LockedChanged;

    public event EventHandler<bool>? SuspendedChanged;

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                LockedChanged?.Invoke(this, true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                LockedChanged?.Invoke(this, false);
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            SuspendedChanged?.Invoke(this, true);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            SuspendedChanged?.Invoke(this, false);
        }
    }
}

/// <summary>Per-user "start with Windows" via HKCU\...\Run (no admin, no scheduled task).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CamTurn";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Enable(string exePath, bool minimized = true)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{exePath}\"{(minimized ? " --minimized" : "")}");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
