using System.Text.Json;
using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>A scripted timeline for the simulated device. Loaded from sim/scenarios/*.json.</summary>
public sealed record Scenario
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public string Device { get; init; } = "dell-inspiron-16-7640-2in1";

    public int Seed { get; init; } = 1;

    public double DurationSeconds { get; init; }

    public IReadOnlyList<ScenarioStep> Steps { get; init; } = [];

    public IReadOnlyList<ScenarioExpectation> Expect { get; init; } = [];

    /// <summary>Times (seconds) to capture camera frames for --render. Defaults to every expectation time.</summary>
    public IReadOnlyList<double>? Keyframes { get; init; }

    public static Scenario Load(string path) =>
        JsonSerializer.Deserialize<Scenario>(File.ReadAllText(path), DeviceProfile.JsonOptions)
        ?? throw new InvalidDataException($"Empty scenario: {path}");

    public double EffectiveDuration =>
        DurationSeconds > 0
            ? DurationSeconds
            : Math.Max(Steps.Select(s => s.At + s.Transition).DefaultIfEmpty(0).Max(), Expect.Select(e => e.At).DefaultIfEmpty(0).Max()) + 0.5;
}

/// <summary>
/// At <see cref="At"/> seconds, either move to a pose (a preset name and/or explicit angles, over
/// <see cref="Transition"/> seconds) or perform an <see cref="Action"/>.
/// </summary>
public sealed record ScenarioStep
{
    public double At { get; init; }

    public string? Pose { get; init; }

    public double? Roll { get; init; }

    public double? Tilt { get; init; }

    public double? Hinge { get; init; }

    public double? Shake { get; init; }

    public double Transition { get; init; }

    /// <summary>lockRotation, unlockRotation, cameraDisconnect, cameraReconnect, cameraBusy, cameraFree.</summary>
    public string? Action { get; init; }

    public DevicePose? ResolvePose(DevicePose current)
    {
        if (Pose is null && Roll is null && Tilt is null && Hinge is null && Shake is null)
        {
            return null;
        }

        var p = Pose is null ? current with { ShakeDegrees = 0 } : DevicePose.Preset(Pose);
        return new DevicePose(Roll ?? p.RollDegrees, Tilt ?? p.TiltDegrees, Hinge ?? p.HingeDegrees, Shake ?? p.ShakeDegrees);
    }
}

/// <summary>What must be true at <see cref="At"/> seconds. Unset fields are not checked.</summary>
public sealed record ScenarioExpectation
{
    public double At { get; init; }

    public int? Quadrant { get; init; }

    public bool? Holding { get; init; }

    public bool? AutoAvailable { get; init; }

    public int? DisplayRotation { get; init; }

    public string? Posture { get; init; }

    /// <summary>The CamTurn-corrected camera frame must be upright (checked with <see cref="UprightDetector"/>).</summary>
    public bool? OutputUpright { get; init; }

    public string? CameraState { get; init; }
}
