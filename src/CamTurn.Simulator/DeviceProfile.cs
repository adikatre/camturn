using System.Text.Json;
using System.Text.Json.Serialization;
using CamTurn.Core.Sensors;
using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>Static description of a simulated device. Loaded from sim/devices/*.json.</summary>
public sealed record DeviceProfile
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Notes { get; init; }

    public required PanelSpec Panel { get; init; }

    public required CameraSpec Camera { get; init; }

    public required SensorSpec Sensors { get; init; }

    public WindowsBehaviorSpec Windows { get; init; } = new();

    public IReadOnlyList<Assumption> Assumptions { get; init; } = [];

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static DeviceProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<DeviceProfile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Empty device profile: {path}");
        profile.Validate();
        return profile;
    }

    /// <summary>Resolves "dell-inspiron-16-7640-2in1" or a path to a profile file.</summary>
    public static DeviceProfile Resolve(string nameOrPath, string devicesDirectory)
    {
        if (File.Exists(nameOrPath))
        {
            return Load(nameOrPath);
        }

        var candidate = Path.Combine(devicesDirectory, nameOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + ".json");
        return File.Exists(candidate)
            ? Load(candidate)
            : throw new FileNotFoundException($"Device profile '{nameOrPath}' not found in {devicesDirectory}.");
    }

    public void Validate()
    {
        if (Camera.Formats.Count == 0)
        {
            throw new InvalidDataException($"{Id}: camera has no formats.");
        }

        if (Sensors.Accelerometers.Count(a => a.IsDefault) > 1)
        {
            throw new InvalidDataException($"{Id}: more than one default accelerometer.");
        }

        if (Sensors.Accelerometers.Any(a => a.ReportIntervalMs <= 0))
        {
            throw new InvalidDataException($"{Id}: accelerometer report interval must be positive.");
        }
    }
}

public sealed record PanelSpec(int NativeWidth, int NativeHeight, double DiagonalInches, bool Touch);

public sealed record CameraFormatSpec(string Subtype, int Width, int Height, int Fps)
{
    public VideoFormat ToVideoFormat() => new(Width, Height, Fps, Subtype);
}

public sealed record CameraSpec
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Panel { get; init; } = "Front";

    /// <summary>Clockwise rotation of the image sensor relative to the panel (0 for a normally mounted webcam).</summary>
    public int MountRotationDegrees { get; init; }

    public IReadOnlyList<CameraFormatSpec> Formats { get; init; } = [];
}

public sealed record AccelerometerSpec
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public SensorLocation Location { get; init; } = SensorLocation.Lid;

    public bool IsDefault { get; init; }

    public double ReportIntervalMs { get; init; } = 16;

    /// <summary>Per-axis Gaussian noise, in g.</summary>
    public double NoiseG { get; init; } = 0.02;

    /// <summary>Sensor axes rotated clockwise about Z relative to the chassis part (models a driver/mounting mismatch).</summary>
    public int AxisRotationDegrees { get; init; }

    /// <summary>X axis reported with the wrong sign (models a driver bug; swaps CW/CCW).</summary>
    public bool MirrorX { get; init; }
}

public sealed record NamedSensorSpec(string Id, string Name);

public sealed record SensorSpec
{
    public IReadOnlyList<AccelerometerSpec> Accelerometers { get; init; } = [];

    public NamedSensorSpec? HingeAngle { get; init; }

    public NamedSensorSpec? SimpleOrientation { get; init; }
}

public sealed record WindowsBehaviorSpec
{
    public string OsBuild { get; init; } = "10.0.26100 (simulated)";

    /// <summary>Most convertibles only auto-rotate the desktop in slate/tablet posture.</summary>
    public bool AutoRotateOnlyInSlate { get; init; } = true;

    public double SlateWhenHingeAboveDegrees { get; init; } = 190;

    public bool RotationLockedAtStart { get; init; }
}

/// <summary>A device fact the profile assumes but nobody has checked on the real hardware yet.</summary>
public sealed record Assumption(string Id, string About, string Assumed, string VerifyWith);
