using CamTurn.Core.Sensors;

namespace CamTurn.Simulator;

/// <summary>
/// Physical pose of a convertible, described from the lid (where the camera and screen are).
/// World frame: +X to the viewer's right, +Y away from the viewer, +Z up.
/// </summary>
/// <param name="RollDegrees">Clockwise rotation of the screen as seen by the viewer (0 = upright, 90 = right edge down).</param>
/// <param name="TiltDegrees">How far the top of the screen leans away from the viewer (0 = vertical, 90 = lying face up, -90 = face down).</param>
/// <param name="HingeDegrees">0 = closed, 90 = lid upright over a flat base, 180 = flat, 360 = folded into tablet.</param>
/// <param name="ShakeDegrees">Amplitude of hand jitter added to roll and tilt (simulated holding in the hand).</param>
public sealed record DevicePose(double RollDegrees, double TiltDegrees, double HingeDegrees, double ShakeDegrees = 0)
{
    public static IReadOnlyDictionary<string, DevicePose> Presets { get; } = new Dictionary<string, DevicePose>(StringComparer.OrdinalIgnoreCase)
    {
        ["laptop"] = new(0, 20, 110),
        ["laptop-flat"] = new(0, 90, 180),
        ["tent"] = new(180, 30, 300),
        ["stand"] = new(0, 15, 285),
        ["tablet"] = new(0, 30, 360),
        ["tablet-portrait-right"] = new(90, 30, 360),
        ["tablet-portrait-left"] = new(270, 30, 360),
        ["tablet-inverted"] = new(180, 30, 360),
        ["flat-face-up"] = new(0, 90, 360),
        ["flat-face-down"] = new(0, -90, 360),
    };

    public static DevicePose Preset(string name) =>
        Presets.TryGetValue(name, out var p)
            ? p
            : throw new ArgumentException($"Unknown pose preset '{name}'. Known: {string.Join(", ", Presets.Keys)}", nameof(name));

    /// <summary>Lid (panel) axes expressed in world coordinates.</summary>
    public (Vec3 X, Vec3 Y, Vec3 Z) LidAxes()
    {
        var r = Rad(RollDegrees);
        var t = -Rad(TiltDegrees);
        // Upright panel facing the viewer, then roll about the screen normal, then tilt about world X.
        Vec3 x0 = new(1, 0, 0), y0 = new(0, 0, 1), z0 = new(0, -1, 0);
        var x1 = (Math.Cos(r) * x0) - (Math.Sin(r) * y0);
        var y1 = (Math.Sin(r) * x0) + (Math.Cos(r) * y0);
        return (x1.RotateX(t), y1.RotateX(t), z0.RotateX(t));
    }

    /// <summary>Base (keyboard) axes in world coordinates, derived from the lid and the hinge angle.</summary>
    public (Vec3 X, Vec3 Y, Vec3 Z) BaseAxes()
    {
        var (lx, ly, lz) = LidAxes();
        var h = Rad(HingeDegrees);
        double c = Math.Cos(h), s = Math.Sin(h);
        return (lx, (-c * ly) - (s * lz), (s * ly) - (c * lz));
    }

    /// <summary>Noise-free accelerometer reading (gravity direction in g) for a sensor with the given axes.</summary>
    public static AccelReading GravityIn((Vec3 X, Vec3 Y, Vec3 Z) axes, TimeSpan t)
    {
        Vec3 down = new(0, 0, -1);
        return new AccelReading(down.Dot(axes.X), down.Dot(axes.Y), down.Dot(axes.Z), t);
    }

    public static DevicePose Lerp(DevicePose a, DevicePose b, double k)
    {
        // Roll takes the short way round; everything else is linear.
        var dr = (((b.RollDegrees - a.RollDegrees) % 360) + 540) % 360 - 180;
        return new DevicePose(
            a.RollDegrees + (dr * k),
            a.TiltDegrees + ((b.TiltDegrees - a.TiltDegrees) * k),
            a.HingeDegrees + ((b.HingeDegrees - a.HingeDegrees) * k),
            a.ShakeDegrees + ((b.ShakeDegrees - a.ShakeDegrees) * k));
    }

    public override string ToString() =>
        FormattableString.Invariant($"roll={RollDegrees:0.#} tilt={TiltDegrees:0.#} hinge={HingeDegrees:0.#}{(ShakeDegrees > 0 ? $" shake={ShakeDegrees:0.#}" : "")}");

    private static double Rad(double deg) => deg * Math.PI / 180.0;
}
