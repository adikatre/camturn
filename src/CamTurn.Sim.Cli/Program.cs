using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Simulator;

namespace CamTurn.Sim.Cli;

public static class Program
{
    private const string Usage = """
        camturn-sim: software model of a 2-in-1 convertible for developing CamTurn without the hardware.

        Usage: camturn-sim [command] [options]

        Commands (pick one; default --diagnose):
          --diagnose                 Report what CamTurn's --diagnose would see on this device
          --sensor-dump              Stream accelerometer x/y/z, roll, quadrant (CSV)
          --list-cameras             Cameras and formats the device exposes
          --list-poses               Named pose presets
          --snapshot <dir>           Write raw and corrected camera PNGs for the current pose
          --scenario <name|file>     Play a scripted timeline and check its expectations
          --run-all                  Play every scenario in sim/scenarios, print pass/fail

        Options:
          --device <name|file>       Device profile (default dell-inspiron-16-7640-2in1)
          --pose <preset>            Start pose (default laptop)
          --roll/--tilt/--hinge <deg> Override pose angles; --shake <deg> adds hand jitter
          --duration <s>             --sensor-dump length (default 3)
          --rate <ms>                --sensor-dump sample spacing (default 100)
          --all-sensors              --sensor-dump: include base accelerometer and hinge
          --realtime                 --sensor-dump: run at wall-clock speed
          --render <dir>             --scenario: write a contact-sheet PNG and keyframes
          --csv <file>               --scenario: write the full timeline as CSV
          --flip-direction           Use the flipped rotation mapping (calibration button)
          --seed <n>                 Noise seed (default 1)
          --sim-dir <dir>            Folder containing devices/ and scenarios/ (default: auto)
        """;

    public static int Main(string[] args)
    {
        try
        {
            return Run(new Args(args));
        }
        catch (Exception e) when (e is ArgumentException or FileNotFoundException or InvalidDataException or FormatException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }
    }

    private static int Run(Args a)
    {
        if (a.Flag("--help") || a.Flag("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var simDir = a.Value("--sim-dir") ?? FindSimDir();
        var mapping = a.Flag("--flip-direction") ? RotationMapping.Default.FlipDirection() : RotationMapping.Default;

        if (a.Flag("--list-poses"))
        {
            foreach (var (name, pose) in DevicePose.Presets)
            {
                Console.WriteLine($"{name,-24} {pose}");
            }

            return 0;
        }

        if (a.Flag("--run-all"))
        {
            return RunAll(simDir, mapping, a.Value("--render"));
        }

        if (a.Value("--scenario") is { } scenarioArg)
        {
            var scenario = LoadScenario(scenarioArg, simDir);
            var profile = DeviceProfile.Resolve(a.Value("--device") ?? scenario.Device, Path.Combine(simDir, "devices"));
            return RunScenario(scenario, profile, mapping, a.Value("--render"), a.Value("--csv")) ? 0 : 1;
        }

        var device = new SimDevice(
            DeviceProfile.Resolve(a.Value("--device") ?? "dell-inspiron-16-7640-2in1", Path.Combine(simDir, "devices")),
            a.Int("--seed") ?? 1,
            PoseFrom(a));
        device.Advance(TimeSpan.FromMilliseconds(50));

        if (a.Flag("--sensor-dump"))
        {
            SensorDump(device, a);
        }
        else if (a.Flag("--list-cameras"))
        {
            ListCameras(device);
        }
        else if (a.Value("--snapshot") is { } dir)
        {
            Snapshot(device, mapping, dir);
        }
        else
        {
            Diagnose(device);
        }

        return 0;
    }

    private static DevicePose PoseFrom(Args a)
    {
        var p = DevicePose.Preset(a.Value("--pose") ?? "laptop");
        return new DevicePose(
            a.Double("--roll") ?? p.RollDegrees,
            a.Double("--tilt") ?? p.TiltDegrees,
            a.Double("--hinge") ?? p.HingeDegrees,
            a.Double("--shake") ?? p.ShakeDegrees);
    }

    private static void Diagnose(SimDevice d)
    {
        var p = d.Profile;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine("CamTurn diagnostics  [SIMULATED - no real hardware was read]");
        Console.WriteLine($"  camturn-sim version   {version}");
        Console.WriteLine($"  OS build              {p.Windows.OsBuild}");
        Console.WriteLine($"  Device model          {p.DisplayName}");
        Console.WriteLine($"  Panel                 {p.Panel.NativeWidth}x{p.Panel.NativeHeight} native, {p.Panel.DiagonalInches}\", touch={p.Panel.Touch}");
        Console.WriteLine($"  Pose (simulator)      {d.CurrentPose}");
        Console.WriteLine();
        Console.WriteLine("Sensors (Accelerometer.GetDeviceSelector):");
        foreach (var s in d.SensorEnumerator.FindAll(SensorKind.Accelerometer))
        {
            Console.WriteLine($"  - {s.Name,-28} location={s.Location,-7} default={s.IsDefault}  id={s.Id}");
        }

        var hinge = d.SensorEnumerator.FindAll(SensorKind.HingeAngle);
        Console.WriteLine($"  Hinge angle sensor    {(hinge.Count > 0 ? $"{hinge[0].Name} = {d.HingeSensor!.GetCurrentReading()?.AngleDegrees:0.0}°" : "none")}");
        Console.WriteLine($"  SimpleOrientation     {d.SimpleOrientationSensor?.GetCurrentOrientation().ToString() ?? "none"} (diagnostic cross-check only)");
        var source = ScenarioRunner.ChooseSource(d);
        Console.WriteLine($"  Orientation source    {source}{(source == OrientationSourceKind.None ? "  -> Auto mode unavailable, manual rotation only" : "")}");
        Console.WriteLine();
        Console.WriteLine("Orientation:");
        foreach (var acc in d.Accelerometers)
        {
            var r = acc.GetCurrentReading()!.Value;
            Console.WriteLine(F($"  {acc.Descriptor.Location,-5} accel (g)       x={r.X,6:0.000} y={r.Y,6:0.000} z={r.Z,6:0.000}  |g|={r.Magnitude:0.000}"));
        }

        if (Lid(d) is { } lid)
        {
            var roll = OrientationMath.RollDegrees(lid);
            var flat = OrientationMath.InPlaneFraction(lid) < new OrientationTrackerOptions().FlatInPlaneFraction;
            Console.WriteLine(F($"  Lid roll              {roll:0.0}° -> quadrant {(int)OrientationMath.NearestQuadrant(roll)}{(flat ? " (flat: holding last orientation)" : "")}"));
        }

        Console.WriteLine($"  Posture               {d.Posture.Current}");
        Console.WriteLine($"  Windows display       rotation={d.Display.RotationDegrees}° locked={d.Display.RotationLocked}");
        Console.WriteLine();
        Console.WriteLine("Cameras:");
        ListCameras(d);
        Console.WriteLine();
        Console.WriteLine("Virtual camera:");
        Console.WriteLine("  Registration / media source / Frame Server: not modelled by the simulator (Windows-only; see docs/TESTING.md).");
        if (p.Assumptions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Unverified profile assumptions:");
            foreach (var x in p.Assumptions)
            {
                Console.WriteLine($"  [{x.Id}] {x.About}: {x.Assumed}");
                Console.WriteLine($"        verify with: {x.VerifyWith}");
            }
        }
    }

    private static AccelReading? Lid(SimDevice d) =>
        d.LidAccelerometer?.GetCurrentReading()
        ?? (d.BaseAccelerometer?.GetCurrentReading() is { } b && d.HingeSensor?.GetCurrentReading() is { } h
            ? HingeKinematics.LidFromBase(b, h.AngleDegrees)
            : null);

    private static void ListCameras(SimDevice d)
    {
        var c = d.Camera.Descriptor;
        Console.WriteLine($"  - {c.Name} ({c.Panel})  id={c.Id}");
        foreach (var f in c.Formats)
        {
            Console.WriteLine($"      {f}");
        }
    }

    private static void SensorDump(SimDevice d, Args a)
    {
        var duration = TimeSpan.FromSeconds(a.Double("--duration") ?? 3);
        var rate = TimeSpan.FromMilliseconds(a.Double("--rate") ?? 100);
        var all = a.Flag("--all-sensors");
        var realtime = a.Flag("--realtime");
        var tracker = new OrientationTracker();
        var source = ScenarioRunner.ChooseSource(d);
        if (source == OrientationSourceKind.None)
        {
            Console.Error.WriteLine("No usable accelerometer: Auto mode unavailable (manual rotation only).");
        }

        Console.WriteLine(all
            ? "t_ms,x,y,z,roll_deg,in_plane,quadrant,holding,base_x,base_y,base_z,hinge_deg"
            : "t_ms,x,y,z,roll_deg,in_plane,quadrant,holding");
        var end = d.Clock.Now + duration;
        var wall = Stopwatch.StartNew();
        var start = d.Clock.Now;
        while (d.Clock.Now < end)
        {
            d.Advance(rate);
            if (Lid(d) is not { } r)
            {
                continue;
            }

            var s = tracker.Update(r);
            var line = F($"{r.Timestamp.TotalMilliseconds:0},{r.X:0.0000},{r.Y:0.0000},{r.Z:0.0000},{OrientationMath.RollDegrees(r):0.0},{OrientationMath.InPlaneFraction(r):0.000},{(int)s.Quadrant},{s.Holding.ToString().ToLowerInvariant()}");
            if (all)
            {
                var b = d.BaseAccelerometer?.GetCurrentReading();
                var h = d.HingeSensor?.GetCurrentReading();
                line += F($",{b?.X:0.0000},{b?.Y:0.0000},{b?.Z:0.0000},{h?.AngleDegrees:0.0}");
            }

            Console.WriteLine(line);
            if (realtime)
            {
                var ahead = (d.Clock.Now - start) - wall.Elapsed;
                if (ahead > TimeSpan.Zero)
                {
                    Thread.Sleep(ahead);
                }
            }
        }
    }

    private static void Snapshot(SimDevice d, RotationMapping mapping, string dir)
    {
        Directory.CreateDirectory(dir);
        var fmt = d.Camera.Descriptor.Formats[0];
        var raw = d.Camera.Capture(fmt.Width, fmt.Height);
        var tracker = new OrientationTracker();
        if (Lid(d) is { } r)
        {
            tracker.Update(r);
        }

        var correction = mapping.CorrectionFor(tracker.State.Quadrant);
        var corrected = Core.Video.FrameOps.RotateBgraClockwise(raw, correction);
        var up = UprightDetector.Detect(corrected);
        PngWriter.Write(raw, Path.Combine(dir, "raw.png"));
        PngWriter.Write(corrected, Path.Combine(dir, "corrected.png"));
        Console.WriteLine(F($"pose {d.CurrentPose}: quadrant {(int)tracker.State.Quadrant}{(tracker.State.Holding ? " (holding)" : "")}, rotate {correction} CW; corrected frame rotation {up.RotationCwDegrees:0.0}° mirrored={up.Mirrored}"));
        Console.WriteLine($"wrote {Path.Combine(dir, "raw.png")} and corrected.png");
    }

    private static bool RunScenario(Scenario scenario, DeviceProfile profile, RotationMapping mapping, string? renderDir, string? csvPath)
    {
        var runner = new ScenarioRunner(profile, mapping) { CaptureSize = renderDir is null ? (320, 180) : (640, 360) };
        var result = runner.Run(scenario, captureKeyframes: renderDir is not null);
        Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")}  {scenario.Name,-22} device={profile.Id} source={result.Source}");
        foreach (var f in result.Failures)
        {
            Console.WriteLine($"      {f}");
        }

        if (csvPath is not null)
        {
            using var w = new StreamWriter(csvPath);
            w.WriteLine("t_s,roll_true,tilt,hinge,x,y,z,roll_measured,quadrant,holding,display_rotation,posture,simple_orientation,camera");
            foreach (var r in result.Timeline)
            {
                w.WriteLine(F($"{r.T:0.000},{r.TrueRoll:0.0},{r.Pose.TiltDegrees:0.0},{r.Pose.HingeDegrees:0.0},{r.Reading.X:0.0000},{r.Reading.Y:0.0000},{r.Reading.Z:0.0000},{r.MeasuredRoll:0.0},{(int)r.Quadrant},{r.Holding},{r.DisplayRotation},{r.Posture},{r.Simple},{r.Camera}"));
            }

            Console.WriteLine($"      timeline -> {csvPath}");
        }

        if (renderDir is not null)
        {
            Directory.CreateDirectory(renderDir);
            var sheetPath = Path.Combine(renderDir, $"{scenario.Name}-contact-sheet.png");
            PngWriter.Write(ContactSheet.Build(result), sheetPath);
            foreach (var k in result.Keyframes)
            {
                var stem = F($"{scenario.Name}-t{k.T:00.00}");
                PngWriter.Write(k.Raw, Path.Combine(renderDir, stem + "-raw.png"));
                PngWriter.Write(k.Corrected, Path.Combine(renderDir, stem + "-corrected.png"));
            }

            Console.WriteLine($"      contact sheet -> {sheetPath}");
        }

        return result.Passed;
    }

    private static int RunAll(string simDir, RotationMapping mapping, string? renderDir)
    {
        var failed = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(simDir, "scenarios"), "*.json").Order(StringComparer.Ordinal))
        {
            var scenario = Scenario.Load(file);
            var profile = DeviceProfile.Resolve(scenario.Device, Path.Combine(simDir, "devices"));
            if (!RunScenario(scenario, profile, mapping, renderDir, csvPath: null))
            {
                failed++;
            }
        }

        Console.WriteLine(failed == 0 ? "All scenarios passed." : $"{failed} scenario(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    private static Scenario LoadScenario(string arg, string simDir)
    {
        if (File.Exists(arg))
        {
            return Scenario.Load(arg);
        }

        var path = Path.Combine(simDir, "scenarios", arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? arg : arg + ".json");
        return File.Exists(path) ? Scenario.Load(path) : throw new FileNotFoundException($"Scenario '{arg}' not found in {Path.GetDirectoryName(path)}.");
    }

    private static string FindSimDir()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "sim");
                if (Directory.Exists(Path.Combine(candidate, "devices")))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not find the sim/ folder; pass --sim-dir.");
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private sealed class Args(string[] args)
    {
        public bool Flag(string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

        public string? Value(string name)
        {
            var i = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public double? Double(string name) =>
            Value(name) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : null;

        public int? Int(string name) =>
            Value(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
    }
}
