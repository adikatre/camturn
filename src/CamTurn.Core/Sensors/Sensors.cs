namespace CamTurn.Core.Sensors;

/// <summary>
/// One accelerometer sample in g, in the sensor's native frame (Windows convention:
/// +X toward the right edge of the panel, +Y toward the top edge, +Z out of the screen).
/// A device at rest reports the direction of gravity, so a panel standing upright reads y ≈ -1
/// and a panel lying face-up reads z ≈ -1.
/// </summary>
public readonly record struct AccelReading(double X, double Y, double Z, TimeSpan Timestamp)
{
    public double Magnitude => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
}

public readonly record struct HingeAngleReading(double AngleDegrees, TimeSpan Timestamp);

public enum SensorKind
{
    Accelerometer,
    HingeAngle,
    SimpleOrientation,
}

/// <summary>Which chassis part a sensor is physically mounted in, if known.</summary>
public enum SensorLocation
{
    Unknown,
    Lid,
    Base,
}

/// <summary>What sensor enumeration (DeviceInformation.FindAllAsync on Windows) reports for one sensor.</summary>
public sealed record SensorDescriptor(
    string Id,
    SensorKind Kind,
    string Name,
    SensorLocation Location,
    bool IsDefault);

/// <summary>
/// Mirrors Windows.Devices.Sensors.SimpleOrientation. Names follow the Windows enum:
/// the rotation is counterclockwise, so a device turned 90° clockwise reports
/// <see cref="Rotated270DegreesCounterclockwise"/>.
/// </summary>
public enum SimpleOrientation
{
    NotRotated,
    Rotated90DegreesCounterclockwise,
    Rotated180DegreesCounterclockwise,
    Rotated270DegreesCounterclockwise,
    Faceup,
    Facedown,
}

/// <summary>Laptop vs. slate posture (ConvertibleSlateMode / tablet-mode state on Windows).</summary>
public enum PostureMode
{
    Laptop,
    Slate,
}

public interface IAccelerometer
{
    SensorDescriptor Descriptor { get; }

    TimeSpan ReportInterval { get; set; }

    event EventHandler<AccelReading>? ReadingChanged;

    AccelReading? GetCurrentReading();
}

public interface IHingeAngleSensor
{
    SensorDescriptor Descriptor { get; }

    event EventHandler<HingeAngleReading>? ReadingChanged;

    HingeAngleReading? GetCurrentReading();
}

public interface ISimpleOrientationSensor
{
    SensorDescriptor Descriptor { get; }

    event EventHandler<SimpleOrientation>? OrientationChanged;

    SimpleOrientation GetCurrentOrientation();
}

public interface ISensorEnumerator
{
    IReadOnlyList<SensorDescriptor> FindAll(SensorKind kind);
}

public interface IPostureSource
{
    PostureMode Current { get; }

    event EventHandler<PostureMode>? Changed;
}

/// <summary>
/// The rotation Windows has applied to the desktop, as clockwise degrees of the physical panel
/// that the shell is compensating for (0/90/180/270).
/// </summary>
public interface IDisplayOrientationSource
{
    int RotationDegrees { get; }

    bool RotationLocked { get; }

    event EventHandler<int>? Changed;
}
