using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using CamTurn.Platform.Windows;

namespace CamTurn.App;

/// <summary>Console commands. Exit codes: 0 ok, 1 failed, 2 usage error.</summary>
public static class Cli
{
    public static async Task<int> RunAsync(CliOptions o)
    {
        try
        {
            return o.Command switch
            {
                CliCommand.Help => Help(),
                CliCommand.Diagnose => await DiagnoseAsync(),
                CliCommand.ListCameras => await ListCamerasAsync(),
                CliCommand.SensorDump => await SensorDumpAsync(o),
                CliCommand.Register => Register(o),
                CliCommand.Unregister => Unregister(o),
                CliCommand.PipeProbe => await PipeProbeAsync(),
                CliCommand.SetAutostart => SetAutostart(o.AutostartOn),
                CliCommand.ProbeVirtualCamera => await ProbeVirtualCameraAsync(),
                _ => 2,
            };
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException or DllNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {e.Message} (0x{e.HResult:X8})");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine(CliOptions.Usage);
        return 0;
    }

    private static async Task<int> DiagnoseAsync()
    {
        var config = ConfigStore.Load(AppPaths.ConfigFile).Config;
        var log = new FileLog(AppPaths.LogDirectory, LogLevel.Info);
        Console.WriteLine(await DiagnosticsReport.BuildAsync(config, null, log));
        return 0;
    }

    private static async Task<int> ListCamerasAsync()
    {
        var cams = await MediaCaptureCamera.FindAllAsync();
        if (cams.Count == 0)
        {
            Console.WriteLine("No physical cameras found.");
            return 1;
        }

        foreach (var info in cams)
        {
            Console.WriteLine($"{info.Name} (panel {info.Panel})");
            var cam = await MediaCaptureCamera.CreateAsync(info, probeFormats: true);
            foreach (var f in cam.Descriptor.Formats)
            {
                Console.WriteLine($"    {f}");
            }
        }

        return 0;
    }

    private static async Task<int> SensorDumpAsync(CliOptions o)
    {
        IAccelerometer? accel = o.SimulateOrientation is { } sim ? new FixedAccelerometer(sim) : WinRtAccelerometer.TryGetDefault();
        if (accel is null)
        {
            Console.Error.WriteLine("No accelerometer found: Auto rotation is unavailable on this device (Manual only).");
            return 1;
        }

        var tracker = new OrientationTracker();
        var done = new TaskCompletionSource();
        var start = DateTime.UtcNow;
        Console.WriteLine("t_ms,x,y,z,roll_deg,in_plane,quadrant,holding");
        var lastPrint = TimeSpan.MinValue;
        accel.ReadingChanged += (_, r) =>
        {
            var s = tracker.Update(r);
            if (r.Timestamp - lastPrint < TimeSpan.FromMilliseconds(100))
            {
                return;
            }

            lastPrint = r.Timestamp;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{r.Timestamp.TotalMilliseconds:0},{r.X:0.0000},{r.Y:0.0000},{r.Z:0.0000},{OrientationMath.RollDegrees(r):0.0},{OrientationMath.InPlaneFraction(r):0.000},{(int)s.Quadrant},{s.Holding.ToString().ToLowerInvariant()}"));
        };
        if (accel is FixedAccelerometer f)
        {
            f.Start();
        }

        await Task.Delay(TimeSpan.FromSeconds(o.DurationSeconds));
        (accel as IDisposable)?.Dispose();
        return 0;
    }

    private static int Register(CliOptions o)
    {
        var registrar = new VirtualCameraRegistrar();
        var state = registrar.GetState();
        if (state is VirtualCameraState.Unsupported)
        {
            Console.Error.WriteLine("This Windows version has no virtual camera support (Windows 11 build 22000 or newer is required).");
            return 1;
        }

        if (state is VirtualCameraState.MediaSourceMissing)
        {
            Console.Error.WriteLine("The CamTurn media source is not installed. Reinstall CamTurn.");
            return 1;
        }

        registrar.Register(allUsers: !o.CurrentUserOnly);
        Console.WriteLine("\"CamTurn Camera\" registered.");
        return 0;
    }

    private static int Unregister(CliOptions o)
    {
        new VirtualCameraRegistrar().Unregister(allUsers: !o.CurrentUserOnly);
        Console.WriteLine("\"CamTurn Camera\" removed.");
        return 0;
    }

    /// <summary>
    /// End-to-end check of the Windows side: opening "CamTurn Camera" makes Frame Server load the media source, which
    /// connects to the running CamTurn app. With the app running --test-pattern, the picture must be the upright test
    /// pattern; without the app, it must be the placeholder.
    /// </summary>
    private static async Task<int> ProbeVirtualCameraAsync()
    {
        var info = await MediaCaptureCamera.FindVirtualCameraAsync();
        if (info is null)
        {
            Console.Error.WriteLine("\"CamTurn Camera\" is not listed. Run CamTurn.exe --register as administrator.");
            return 1;
        }

        Console.WriteLine($"Opening {info.Name}...");
        var camera = await MediaCaptureCamera.CreateAsync(info);
        var frames = 0;
        VideoFrame? last = null;
        camera.FrameArrived += (_, f) =>
        {
            Interlocked.Increment(ref frames);
            Volatile.Write(ref last, f);
        };
        camera.Start(new VideoFormat(1280, 720, 30, "NV12"));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref frames) < 60 && camera.State is not (CameraState.Busy or CameraState.Disconnected))
        {
            await Task.Delay(100);
        }

        // Keep reading a little longer so the media source has time to connect to the app's pipe.
        await Task.Delay(TimeSpan.FromSeconds(2));
        camera.Stop();
        var frame = Volatile.Read(ref last);
        if (frame is null)
        {
            Console.Error.WriteLine($"No frames received (camera state {camera.State}; {camera.LastError ?? "no error reported"}).");
            return 1;
        }

        Console.WriteLine($"Received {frames} frames, last {frame.Width}x{frame.Height}.");
        if (UprightDetector.IsPlaceholder(frame))
        {
            Console.WriteLine("Picture: placeholder (media source works; CamTurn app not connected).");
            return 3;
        }

        var up = UprightDetector.Detect(frame);
        if (!up.Confident)
        {
            Console.WriteLine("Picture: live image (not the test pattern); orientation not checked.");
            return 0;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Picture: test pattern, rotated {up.RotationCwDegrees:0.0} deg, mirrored {up.Mirrored}."));
        return Math.Abs(up.RotationCwDegrees) < 10 && !up.Mirrored ? 0 : 4;
    }

    private static int SetAutostart(bool on)
    {
        var path = AppPaths.ConfigFile;
        var config = ConfigStore.Load(path).Config;
        ConfigStore.Save(config with { StartWithWindows = on }, path);
        if (on)
        {
            Autostart.Enable(Environment.ProcessPath!, minimized: true);
        }
        else
        {
            Autostart.Disable();
        }

        Console.WriteLine(on ? "CamTurn will start when you sign in." : "CamTurn will not start automatically.");
        return 0;
    }

    private static async Task<int> PipeProbeAsync()
    {
        await using var client = new FramePipeClient();
        try
        {
            await client.ConnectAsync(1280, 720, 30, TimeSpan.FromSeconds(3));
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not connect to CamTurn's frame pipe ({e.GetType().Name}). Is CamTurn running?");
            return 1;
        }

        Console.WriteLine("Connected as a 1280x720@30 consumer; CamTurn should now open the camera.");
        await Task.Delay(TimeSpan.FromSeconds(5));
        var frames = client.FramesReceived;
        var last = client.Latest;
        Console.WriteLine($"Received {frames} frames in 5 s (~{frames / 5.0:0.0} fps).");
        if (last is { } l)
        {
            Console.WriteLine($"Last frame: {l.Header.Width}x{l.Header.Height}, rotation {l.Header.Param} deg, flags {l.Header.Flags}.");
        }

        return frames > 0 ? 0 : 1;
    }
}
