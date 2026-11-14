using CamTurn.Core.Sensors;

namespace CamTurn.Core.Orientation;

/// <summary>Clockwise device roll snapped to a quadrant, as seen by a person facing the screen.</summary>
public enum Quadrant
{
    Deg0 = 0,
    Deg90 = 90,
    Deg180 = 180,
    Deg270 = 270,
}

public static class OrientationMath
{
    /// <summary>
    /// Clockwise roll of the panel in degrees [0, 360), as seen by a person facing the screen,
    /// from an accelerometer reading in the panel's native frame.
    /// Upright = 0, right edge down = 90, upside down = 180, left edge down = 270.
    /// </summary>
    /// <remarks>
    /// The handoff sketched <c>atan2(x, y)</c>. With the Windows sign convention (upright reads y = -1)
    /// that formula returns 180° for an upright panel, so this uses <c>atan2(x, -y)</c>, which is the same
    /// angle shifted so upright is 0. See docs/DECISIONS.md (D-002). Still to be confirmed on hardware.
    /// </remarks>
    public static double RollDegrees(double x, double y) => Normalize(Math.Atan2(x, -y) * 180.0 / Math.PI);

    public static double RollDegrees(AccelReading r) => RollDegrees(r.X, r.Y);

    /// <summary>Fraction of gravity lying in the screen plane (1 = upright, 0 = flat).</summary>
    public static double InPlaneFraction(AccelReading r)
    {
        var mag = r.Magnitude;
        return mag < 1e-9 ? 0 : Math.Sqrt((r.X * r.X) + (r.Y * r.Y)) / mag;
    }

    public static Quadrant NearestQuadrant(double degrees)
    {
        var q = (int)Math.Round(Normalize(degrees) / 90.0) % 4;
        return (Quadrant)(q * 90);
    }

    /// <summary>Smallest absolute angle between two directions, in [0, 180].</summary>
    public static double AngularDistance(double a, double b)
    {
        var d = Math.Abs(Normalize(a) - Normalize(b));
        return d > 180 ? 360 - d : d;
    }

    public static double Normalize(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    public static Quadrant Add(Quadrant a, int degrees) => (Quadrant)(int)Normalize((int)a + degrees);
}
