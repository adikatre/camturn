using System.Diagnostics;
using CamTurn.Core.Sensors;
using WinSensors = Windows.Devices.Sensors;

namespace CamTurn.Platform.Windows;

/// <summary>
/// Windows.Devices.Sensors.Accelerometer. Readings are kept in the panel's native frame by setting
/// <c>ReadingTransform</c> to the native (landscape) orientation, so they don't change when the desktop rotates
/// (DECISIONS D-006; confirmed equal to the default on the Inspiron by the probe).
/// </summary>
public sealed class WinRtAccelerometer : IAccelerometer, IDisposable
{
    private readonly WinSensors.Accelerometer _sensor;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private WinRtAccelerometer(WinSensors.Accelerometer sensor, SensorDescriptor descriptor)
    {
        _sensor = sensor;
        Descriptor = descriptor;
        _sensor.ReadingTransform = global::Windows.Graphics.Display.DisplayOrientations.Landscape;
        var interval = Math.Max(_sensor.MinimumReportInterval, 16u);
        _sensor.ReportInterval = interval;
        _sensor.ReadingChanged += OnReading;
    }

    public SensorDescriptor Descriptor { get; }

    public TimeSpan ReportInterval
    {
        get => TimeSpan.FromMilliseconds(_sensor.ReportInterval);
        set => _sensor.ReportInterval = Math.Max(_sensor.MinimumReportInterval, (uint)value.TotalMilliseconds);
    }

    public event EventHandler<AccelReading>? ReadingChanged;

    /// <summary>The default accelerometer (assumed to be in the lid; see KNOWN_ISSUES), or null if none.</summary>
    public static WinRtAccelerometer? TryGetDefault()
    {
        var sensor = WinSensors.Accelerometer.GetDefault(WinSensors.AccelerometerReadingType.Standard);
        return sensor is null
            ? null
            : new WinRtAccelerometer(sensor, new SensorDescriptor(sensor.DeviceId, SensorKind.Accelerometer, "Accelerometer", SensorLocation.Lid, IsDefault: true));
    }

    public AccelReading? GetCurrentReading()
    {
        var r = _sensor.GetCurrentReading();
        return r is null ? null : Convert(r);
    }

    public void Dispose() => _sensor.ReadingChanged -= OnReading;

    private void OnReading(WinSensors.Accelerometer sender, WinSensors.AccelerometerReadingChangedEventArgs args) =>
        ReadingChanged?.Invoke(this, Convert(args.Reading));

    private AccelReading Convert(WinSensors.AccelerometerReading r) =>
        new(r.AccelerationX, r.AccelerationY, r.AccelerationZ, _clock.Elapsed);
}

public sealed class WinRtSimpleOrientation : ISimpleOrientationSensor
{
    private readonly WinSensors.SimpleOrientationSensor _sensor;

    private WinRtSimpleOrientation(WinSensors.SimpleOrientationSensor sensor)
    {
        _sensor = sensor;
        _sensor.OrientationChanged += (_, e) => OrientationChanged?.Invoke(this, Map(e.Orientation));
        Descriptor = new SensorDescriptor(sensor.DeviceId, SensorKind.SimpleOrientation, "Simple Device Orientation Sensor", SensorLocation.Lid, true);
    }

    public SensorDescriptor Descriptor { get; }

    public event EventHandler<SimpleOrientation>? OrientationChanged;

    public static WinRtSimpleOrientation? TryGetDefault() =>
        WinSensors.SimpleOrientationSensor.GetDefault() is { } s ? new WinRtSimpleOrientation(s) : null;

    public SimpleOrientation GetCurrentOrientation() => Map(_sensor.GetCurrentOrientation());

    private static SimpleOrientation Map(WinSensors.SimpleOrientation o) => o switch
    {
        WinSensors.SimpleOrientation.Rotated90DegreesCounterclockwise => SimpleOrientation.Rotated90DegreesCounterclockwise,
        WinSensors.SimpleOrientation.Rotated180DegreesCounterclockwise => SimpleOrientation.Rotated180DegreesCounterclockwise,
        WinSensors.SimpleOrientation.Rotated270DegreesCounterclockwise => SimpleOrientation.Rotated270DegreesCounterclockwise,
        WinSensors.SimpleOrientation.Faceup => SimpleOrientation.Faceup,
        WinSensors.SimpleOrientation.Facedown => SimpleOrientation.Facedown,
        _ => SimpleOrientation.NotRotated,
    };
}

/// <summary>Enumerates sensors the way --diagnose reports them.</summary>
public static class WinRtSensorEnumerator
{
    public static async Task<IReadOnlyList<SensorDescriptor>> FindAccelerometersAsync()
    {
        var selector = WinSensors.Accelerometer.GetDeviceSelector(WinSensors.AccelerometerReadingType.Standard);
        var infos = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
        var defaultId = WinSensors.Accelerometer.GetDefault(WinSensors.AccelerometerReadingType.Standard)?.DeviceId;
        return infos.Select(i => new SensorDescriptor(
            i.Id,
            SensorKind.Accelerometer,
            string.IsNullOrWhiteSpace(i.Name) ? "Accelerometer" : i.Name,
            i.Id == defaultId ? SensorLocation.Lid : SensorLocation.Unknown,
            i.Id == defaultId)).ToList();
    }

    public static async Task<double?> TryReadHingeAngleAsync()
    {
        try
        {
            var hinge = await WinSensors.HingeAngleSensor.GetDefaultAsync();
            if (hinge is null)
            {
                return null;
            }

            var reading = await hinge.GetCurrentReadingAsync();
            return reading?.AngleInDegrees;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
