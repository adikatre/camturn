using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;

namespace CamTurn.Simulator;

public sealed class SimAccelerometer : IAccelerometer
{
    private readonly SimDevice _device;
    private readonly AccelerometerSpec _spec;
    private AccelReading? _last;

    internal SimAccelerometer(SimDevice device, AccelerometerSpec spec)
    {
        _device = device;
        _spec = spec;
        Descriptor = new SensorDescriptor(spec.Id, SensorKind.Accelerometer, spec.Name, spec.Location, spec.IsDefault);
        ReportInterval = TimeSpan.FromMilliseconds(spec.ReportIntervalMs);
    }

    public SensorDescriptor Descriptor { get; }

    public TimeSpan ReportInterval { get; set; }

    internal TimeSpan NextDue { get; private set; }

    public event EventHandler<AccelReading>? ReadingChanged;

    public AccelReading? GetCurrentReading() => _last ?? Sample();

    internal void TickIfDue()
    {
        if (_device.Clock.Now < NextDue)
        {
            return;
        }

        _last = Sample();
        NextDue = _device.Clock.Now + ReportInterval;
        ReadingChanged?.Invoke(this, _last.Value);
    }

    private AccelReading Sample()
    {
        var pose = _device.CurrentPose;
        var axes = _spec.Location == SensorLocation.Base ? pose.BaseAxes() : pose.LidAxes();
        var g = DevicePose.GravityIn(axes, _device.Clock.Now);
        double x = g.X + _device.NextGaussian(_spec.NoiseG);
        double y = g.Y + _device.NextGaussian(_spec.NoiseG);
        double z = g.Z + _device.NextGaussian(_spec.NoiseG);
        if (_spec.MirrorX)
        {
            x = -x;
        }

        if (_spec.AxisRotationDegrees != 0)
        {
            var a = _spec.AxisRotationDegrees * Math.PI / 180.0;
            (x, y) = ((x * Math.Cos(a)) - (y * Math.Sin(a)), (x * Math.Sin(a)) + (y * Math.Cos(a)));
        }

        return new AccelReading(x, y, z, _device.Clock.Now);
    }
}

public sealed class SimHingeAngleSensor : IHingeAngleSensor
{
    private HingeAngleReading? _last;

    internal SimHingeAngleSensor(NamedSensorSpec spec)
    {
        Descriptor = new SensorDescriptor(spec.Id, SensorKind.HingeAngle, spec.Name, SensorLocation.Unknown, IsDefault: true);
    }

    public SensorDescriptor Descriptor { get; }

    public event EventHandler<HingeAngleReading>? ReadingChanged;

    public HingeAngleReading? GetCurrentReading() => _last;

    internal void Update(double degrees, TimeSpan t)
    {
        var reading = new HingeAngleReading(Math.Clamp(degrees, 0, 360), t);
        var changed = _last is null || Math.Abs(_last.Value.AngleDegrees - reading.AngleDegrees) >= 1.0;
        _last = reading;
        if (changed)
        {
            ReadingChanged?.Invoke(this, reading);
        }
    }
}

/// <summary>
/// Approximates the Windows SimpleOrientationSensor: quadrant with ~15° of hysteresis past the 45° boundary,
/// Faceup/Facedown when the panel is close to horizontal. Windows' exact thresholds are not documented.
/// </summary>
public sealed class SimSimpleOrientationSensor : ISimpleOrientationSensor
{
    private SimpleOrientation _current = SimpleOrientation.NotRotated;

    internal SimSimpleOrientationSensor(NamedSensorSpec spec)
    {
        Descriptor = new SensorDescriptor(spec.Id, SensorKind.SimpleOrientation, spec.Name, SensorLocation.Lid, IsDefault: true);
    }

    public SensorDescriptor Descriptor { get; }

    public event EventHandler<SimpleOrientation>? OrientationChanged;

    public SimpleOrientation GetCurrentOrientation() => _current;

    public static int ToClockwiseDegrees(SimpleOrientation o) => o switch
    {
        SimpleOrientation.Rotated90DegreesCounterclockwise => 270,
        SimpleOrientation.Rotated180DegreesCounterclockwise => 180,
        SimpleOrientation.Rotated270DegreesCounterclockwise => 90,
        _ => 0,
    };

    internal void Update(AccelReading lid)
    {
        SimpleOrientation next;
        if (OrientationMath.InPlaneFraction(lid) < 0.35)
        {
            next = lid.Z < 0 ? SimpleOrientation.Faceup : SimpleOrientation.Facedown;
        }
        else
        {
            var roll = OrientationMath.RollDegrees(lid);
            var currentDeg = _current is SimpleOrientation.Faceup or SimpleOrientation.Facedown ? (double?)null : ToClockwiseDegrees(_current);
            if (currentDeg is { } c && OrientationMath.AngularDistance(roll, c) <= 60)
            {
                next = _current;
            }
            else
            {
                next = OrientationMath.NearestQuadrant(roll) switch
                {
                    Quadrant.Deg90 => SimpleOrientation.Rotated270DegreesCounterclockwise,
                    Quadrant.Deg180 => SimpleOrientation.Rotated180DegreesCounterclockwise,
                    Quadrant.Deg270 => SimpleOrientation.Rotated90DegreesCounterclockwise,
                    _ => SimpleOrientation.NotRotated,
                };
            }
        }

        if (next != _current)
        {
            _current = next;
            OrientationChanged?.Invoke(this, next);
        }
    }
}

public sealed class SimPosture : IPostureSource
{
    public PostureMode Current { get; private set; } = PostureMode.Laptop;

    public event EventHandler<PostureMode>? Changed;

    internal void Update(PostureMode mode)
    {
        if (mode != Current)
        {
            Current = mode;
            Changed?.Invoke(this, mode);
        }
    }
}

/// <summary>What the Windows shell does to the desktop: auto-rotate (usually only in slate posture) and rotation lock.</summary>
public sealed class SimDisplay : IDisplayOrientationSource
{
    private readonly WindowsBehaviorSpec _spec;

    internal SimDisplay(WindowsBehaviorSpec spec)
    {
        _spec = spec;
        RotationLocked = spec.RotationLockedAtStart;
    }

    public int RotationDegrees { get; private set; }

    public bool RotationLocked { get; set; }

    public event EventHandler<int>? Changed;

    internal void Update(PostureMode posture, SimpleOrientation? orientation)
    {
        int target;
        if (_spec.AutoRotateOnlyInSlate && posture == PostureMode.Laptop)
        {
            target = 0;
        }
        else if (RotationLocked || orientation is null or SimpleOrientation.Faceup or SimpleOrientation.Facedown)
        {
            target = RotationDegrees;
        }
        else
        {
            target = SimSimpleOrientationSensor.ToClockwiseDegrees(orientation.Value);
        }

        if (target != RotationDegrees)
        {
            RotationDegrees = target;
            Changed?.Invoke(this, target);
        }
    }
}

public sealed class SimSensorEnumerator : ISensorEnumerator
{
    private readonly SimDevice _device;

    internal SimSensorEnumerator(SimDevice device) => _device = device;

    public IReadOnlyList<SensorDescriptor> FindAll(SensorKind kind) => kind switch
    {
        SensorKind.Accelerometer => _device.Accelerometers.Select(a => a.Descriptor).ToList(),
        SensorKind.HingeAngle => _device.HingeSensor is { } h ? [h.Descriptor] : [],
        SensorKind.SimpleOrientation => _device.SimpleOrientationSensor is { } s ? [s.Descriptor] : [],
        _ => [],
    };
}
