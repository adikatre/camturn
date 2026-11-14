namespace CamTurn.Core.Sensors;

/// <summary>
/// An accelerometer that reports a fixed roll, for <c>--simulate-orientation</c> and tests. Reports the gravity
/// vector of an upright panel rolled clockwise by <see cref="RollDegrees"/> every <see cref="ReportInterval"/>.
/// </summary>
public sealed class FixedAccelerometer : IAccelerometer, IDisposable
{
    private readonly TimeProvider _time;
    private readonly long _start;
    private ITimer? _timer;
    private double _roll;

    public FixedAccelerometer(double rollDegrees, TimeProvider? time = null)
    {
        _roll = rollDegrees;
        _time = time ?? TimeProvider.System;
        _start = _time.GetTimestamp();
        Descriptor = new SensorDescriptor("camturn:simulated", SensorKind.Accelerometer, "Simulated orientation", SensorLocation.Lid, IsDefault: true);
    }

    public SensorDescriptor Descriptor { get; }

    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public double RollDegrees
    {
        get => _roll;
        set => _roll = value;
    }

    public event EventHandler<AccelReading>? ReadingChanged;

    public AccelReading? GetCurrentReading()
    {
        var r = _roll * Math.PI / 180;
        return new AccelReading(Math.Sin(r), -Math.Cos(r), 0, _time.GetElapsedTime(_start));
    }

    public void Start() => _timer ??= _time.CreateTimer(_ => ReadingChanged?.Invoke(this, GetCurrentReading()!.Value), null, TimeSpan.Zero, ReportInterval);

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
