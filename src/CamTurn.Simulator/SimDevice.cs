using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;

namespace CamTurn.Simulator;

/// <summary>
/// The whole simulated convertible: pose (with smooth transitions and hand shake), its sensors,
/// the Windows auto-rotate behaviour, and the lid webcam. Driven by a virtual clock.
/// </summary>
public sealed class SimDevice
{
    private static readonly TimeSpan ShellTick = TimeSpan.FromMilliseconds(16);
    private readonly Random _rng;
    private DevicePose _from;
    private DevicePose _to;
    private TimeSpan _transitionStart;
    private TimeSpan _transitionDuration;
    private TimeSpan _nextShellTick;

    public SimDevice(DeviceProfile profile, int seed = 1, DevicePose? initialPose = null)
    {
        Profile = profile;
        _rng = new Random(seed);
        _from = _to = initialPose ?? DevicePose.Preset("laptop");
        Accelerometers = profile.Sensors.Accelerometers.Select(s => new SimAccelerometer(this, s)).ToList();
        HingeSensor = profile.Sensors.HingeAngle is { } h ? new SimHingeAngleSensor(h) : null;
        SimpleOrientationSensor = profile.Sensors.SimpleOrientation is { } so ? new SimSimpleOrientationSensor(so) : null;
        Posture = new SimPosture();
        Display = new SimDisplay(profile.Windows);
        Camera = new SimLidCamera(this);
        SensorEnumerator = new SimSensorEnumerator(this);
        UpdateShell();
    }

    public DeviceProfile Profile { get; }

    public SimClock Clock { get; } = new();

    public IReadOnlyList<SimAccelerometer> Accelerometers { get; }

    /// <summary>What Accelerometer.GetDefault() would return, or null if the device has none.</summary>
    public SimAccelerometer? DefaultAccelerometer => Accelerometers.FirstOrDefault(a => a.Descriptor.IsDefault);

    public SimAccelerometer? LidAccelerometer => Accelerometers.FirstOrDefault(a => a.Descriptor.Location == SensorLocation.Lid);

    public SimAccelerometer? BaseAccelerometer => Accelerometers.FirstOrDefault(a => a.Descriptor.Location == SensorLocation.Base);

    public SimHingeAngleSensor? HingeSensor { get; }

    public SimSimpleOrientationSensor? SimpleOrientationSensor { get; }

    public SimPosture Posture { get; }

    public SimDisplay Display { get; }

    public SimLidCamera Camera { get; }

    public SimSensorEnumerator SensorEnumerator { get; }

    /// <summary>The pose right now, including any in-progress transition and hand shake.</summary>
    public DevicePose CurrentPose
    {
        get
        {
            var k = _transitionDuration <= TimeSpan.Zero
                ? 1.0
                : Math.Clamp((Clock.Now - _transitionStart) / _transitionDuration, 0, 1);
            var pose = DevicePose.Lerp(_from, _to, k * k * (3 - (2 * k)));
            if (pose.ShakeDegrees <= 0)
            {
                return pose;
            }

            var t = Clock.Now.TotalSeconds;
            var a = pose.ShakeDegrees;
            return pose with
            {
                RollDegrees = pose.RollDegrees + (a * ((0.6 * Math.Sin(2 * Math.PI * 3.1 * t)) + (0.4 * Math.Sin((2 * Math.PI * 7.3 * t) + 1)))),
                TiltDegrees = pose.TiltDegrees + (a * 0.5 * Math.Sin((2 * Math.PI * 2.3 * t) + 2)),
            };
        }
    }

    /// <summary>True clockwise roll of the lid as seen by the viewer (noise-free), in [0, 360).</summary>
    public double TrueLidRollDegrees
    {
        get
        {
            var g = DevicePose.GravityIn(CurrentPose.LidAxes(), Clock.Now);
            return OrientationMath.RollDegrees(g);
        }
    }

    public void SetPose(DevicePose target, TimeSpan transition = default)
    {
        _from = CurrentPose with { ShakeDegrees = _to.ShakeDegrees };
        _to = target;
        _transitionStart = Clock.Now;
        _transitionDuration = transition;
    }

    /// <summary>Advances virtual time, firing every sensor report and camera frame that falls due.</summary>
    public void Advance(TimeSpan dt)
    {
        var end = Clock.Now + dt;
        while (true)
        {
            var next = end;
            foreach (var a in Accelerometers)
            {
                next = Min(next, a.NextDue);
            }

            next = Min(next, _nextShellTick);
            if (Camera.NextDue is { } cam)
            {
                next = Min(next, cam);
            }

            if (next > Clock.Now)
            {
                Clock.Advance(next - Clock.Now);
            }

            foreach (var a in Accelerometers)
            {
                a.TickIfDue();
            }

            if (Clock.Now >= _nextShellTick)
            {
                UpdateShell();
            }

            Camera.TickIfDue();

            if (Clock.Now >= end)
            {
                break;
            }
        }
    }

    internal double NextGaussian(double sigma)
    {
        // Box-Muller.
        var u1 = 1.0 - _rng.NextDouble();
        var u2 = _rng.NextDouble();
        return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private void UpdateShell()
    {
        var pose = CurrentPose;
        var lid = DevicePose.GravityIn(pose.LidAxes(), Clock.Now);
        var noisy = lid with { X = lid.X + NextGaussian(0.01), Y = lid.Y + NextGaussian(0.01), Z = lid.Z + NextGaussian(0.01) };
        HingeSensor?.Update(pose.HingeDegrees + NextGaussian(0.5), Clock.Now);
        SimpleOrientationSensor?.Update(noisy);
        Posture.Update(pose.HingeDegrees > Profile.Windows.SlateWhenHingeAboveDegrees ? PostureMode.Slate : PostureMode.Laptop);
        Display.Update(Posture.Current, SimpleOrientationSensor?.GetCurrentOrientation());
        _nextShellTick = Clock.Now + ShellTick;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
