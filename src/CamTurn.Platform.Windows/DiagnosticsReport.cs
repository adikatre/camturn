using System.Globalization;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using Microsoft.Win32;

namespace CamTurn.Platform.Windows;

/// <summary>Builds the redacted text shown by --diagnose and "Copy diagnostics". Reads only; no network.</summary>
public static class DiagnosticsReport
{
    public static async Task<string> BuildAsync(AppConfig config, EngineStatus? status, FileLog? log)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.AppendLine(s);
        string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        Line("CamTurn diagnostics");
        Line($"  CamTurn version     {Assembly.GetEntryAssembly()?.GetName().Version}");
        Line($"  OS                  {Environment.OSVersion.VersionString} (build {Environment.OSVersion.Version.Build}, UBR {Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", "?")})");
        Line($"  Windows 11 22000+   {(Environment.OSVersion.Version.Build >= 22000 ? "yes" : "NO - virtual camera API unavailable")}");
        var bios = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS";
        Line($"  Device              {Registry.GetValue(bios, "SystemManufacturer", "?")} {Registry.GetValue(bios, "SystemProductName", "?")}");
        var (w, h) = DisplayOrientationSource.PrimaryResolution();
        Line($"  Display             {w}x{h}, desktop rotation {DisplayOrientationSource.Read()} deg, posture {PostureSource.Read()}");
        Line();

        Line("Sensors");
        try
        {
            var accels = await WinRtSensorEnumerator.FindAccelerometersAsync().ConfigureAwait(false);
            if (accels.Count == 0)
            {
                Line("  no accelerometer -> Auto rotation unavailable (Manual only)");
            }

            foreach (var a in accels)
            {
                Line($"  accelerometer       {a.Name}  default={a.IsDefault}");
            }

            using var lid = WinRtAccelerometer.TryGetDefault();
            if (lid?.GetCurrentReading() is { } r)
            {
                var roll = OrientationMath.RollDegrees(r);
                Line(F($"  reading (g)         x={r.X:0.000} y={r.Y:0.000} z={r.Z:0.000} roll={roll:0.0} -> quadrant {(int)OrientationMath.NearestQuadrant(roll)}, in-plane {OrientationMath.InPlaneFraction(r):0.00}"));
            }

            var hinge = await WinRtSensorEnumerator.TryReadHingeAngleAsync().ConfigureAwait(false);
            Line($"  hinge angle         {(hinge is { } hv ? F($"{hv:0.0} deg") : "none")}");
            Line($"  SimpleOrientation   {WinRtSimpleOrientation.TryGetDefault()?.GetCurrentOrientation().ToString() ?? "none"}");
        }
        catch (Exception e)
        {
            Line($"  error: {e.GetType().Name}: {e.Message}");
        }

        Line();
        Line("Cameras");
        try
        {
            foreach (var c in await MediaCaptureCamera.FindAllAsync().ConfigureAwait(false))
            {
                Line($"  {c.Name} (panel {c.Panel})");
            }
        }
        catch (Exception e)
        {
            Line($"  error: {e.GetType().Name}: {e.Message}");
        }

        Line();
        Line("Virtual camera");
        var path = VirtualCameraRegistrar.MediaSourcePath();
        Line($"  media source        {(path is null ? "NOT registered" : $"registered ({Path.GetFileName(path)}, file {(File.Exists(path) ? "present" : "MISSING")})")}");
        Line($"  API supported       {VirtualCameraRegistrar.IsSupported()}");
        try
        {
            Line($"  visible to apps     {await VirtualCameraRegistrar.IsVisibleToAppsAsync().ConfigureAwait(false)}");
        }
        catch (Exception e)
        {
            Line($"  visible to apps     error: {e.Message}");
        }

        Line($"  Frame Server        {ServiceState("FrameServer")}; monitor {ServiceState("FrameServerMonitor")}");
        Line();

        if (status is not null)
        {
            Line("Pipeline");
            Line($"  status              {status.Pipeline}, consumers {status.Consumers}, camera {status.CameraName}: {status.Camera}, {status.Fps} fps");
            Line($"  rotation            {status.CorrectionCw} deg cw from {status.RotationSource}, quadrant {(status.Quadrant is { } q ? ((int)q).ToString(CultureInfo.InvariantCulture) : "-")}{(status.Holding ? " (holding: flat)" : "")}");
            if (status.LastError is not null)
            {
                Line($"  last error          {status.LastError}");
            }

            Line();
        }

        Line("Settings");
        Line($"  run mode {config.RunMode}, rotation {config.RotationMode}{(config.FollowDisplayRotation ? " (follow display)" : "")}, manual {config.ManualRotation}, mapping 0/90/180/270 -> {config.Mapping.For0}/{config.Mapping.For90}/{config.Mapping.For180}/{config.Mapping.For270}");
        Line($"  output {config.OutputWidth}x{config.OutputHeight}@{config.Fps}, mirror {config.Mirror}, portrait {config.PortraitFit}, release on lock {config.ReleaseOnLock}, autostart {Autostart.IsEnabled()}");

        if (log is not null)
        {
            Line();
            Line("Recent log");
            foreach (var l in log.Tail(15).Split(Environment.NewLine))
            {
                Line("  " + l);
            }
        }

        return AppPaths.Redact(sb.ToString());
    }

    private static string ServiceState(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            return sc.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "not installed";
        }
    }
}
