using System;
using System.Globalization;
using CamTurn.Platform.Windows;

namespace CamTurn.App;

public enum CliCommand
{
    None,
    Help,
    Diagnose,
    ListCameras,
    SensorDump,
    Register,
    Unregister,
    PipeProbe,
    SetAutostart,
}

/// <summary>Command-line flags (handoff section 4). Commands print to the parent console and exit.</summary>
public sealed record CliOptions
{
    public const string Usage = """
        CamTurn - auto-rotating webcam for Windows 11 convertibles

        Usage: CamTurn.exe [options]

        Commands (print and exit):
          --diagnose                  Redacted report for bug reports (OS, sensors, cameras, virtual camera, settings)
          --list-cameras              Physical cameras and their formats
          --sensor-dump [--duration s] Live accelerometer x/y/z, roll and quadrant as CSV (default 10 s)
          --register [--current-user] Create "CamTurn Camera" (all users needs administrator; the installer does this)
          --unregister [--current-user] Remove "CamTurn Camera"
          --pipe-probe                Connect to a running CamTurn like the virtual camera would and report frames
          --set-autostart on|off      Start CamTurn when you sign in (per user; used by the installer)
          --help

        App options:
          --minimized                 Start in the tray without opening settings
          --test-pattern              Use a built-in test pattern instead of the physical camera
          --simulate-orientation N    Pretend the device is rotated N degrees clockwise (0, 90, 180, 270)
          --simulate-device PROFILE   Developer: drive sensors and camera from the device simulator
          --log-level LEVEL           Debug, Info, Warn or Error
        """;

    public CliCommand Command { get; init; }

    public bool Minimized { get; init; }

    public bool TestPattern { get; init; }

    public int? SimulateOrientation { get; init; }

    public string? SimulateDevice { get; init; }

    public LogLevel? LogLevel { get; init; }

    public bool CurrentUserOnly { get; init; }

    public double DurationSeconds { get; init; } = 10;

    public bool AutostartOn { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            o = args[i].ToLowerInvariant() switch
            {
                "--help" or "-h" or "/?" => o with { Command = CliCommand.Help },
                "--diagnose" => o with { Command = CliCommand.Diagnose },
                "--list-cameras" => o with { Command = CliCommand.ListCameras },
                "--sensor-dump" => o with { Command = CliCommand.SensorDump },
                "--register" => o with { Command = CliCommand.Register },
                "--unregister" => o with { Command = CliCommand.Unregister },
                "--pipe-probe" => o with { Command = CliCommand.PipeProbe },
                "--set-autostart" => o with { Command = CliCommand.SetAutostart, AutostartOn = Next().ToLowerInvariant() switch { "on" => true, "off" => false, _ => throw new ArgumentException("--set-autostart takes on or off.") } },
                "--current-user" => o with { CurrentUserOnly = true },
                "--minimized" => o with { Minimized = true },
                "--test-pattern" => o with { TestPattern = true },
                "--simulate-orientation" => o with { SimulateOrientation = ParseQuadrant(Next()) },
                "--simulate-device" => o with { SimulateDevice = Next() },
                "--duration" => o with { DurationSeconds = double.Parse(Next(), CultureInfo.InvariantCulture) },
                "--log-level" => o with { LogLevel = Enum.Parse<LogLevel>(Next(), ignoreCase: true) },
                _ => throw new ArgumentException($"Unknown option '{args[i]}'. Use --help."),
            };
        }

        return o;
    }

    private static int ParseQuadrant(string s)
    {
        var v = int.Parse(s, CultureInfo.InvariantCulture);
        return v is 0 or 90 or 180 or 270 ? v : throw new ArgumentException("--simulate-orientation must be 0, 90, 180 or 270.");
    }
}
