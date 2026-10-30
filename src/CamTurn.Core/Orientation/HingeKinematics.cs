using CamTurn.Core.Sensors;

namespace CamTurn.Core.Orientation;

/// <summary>
/// Fallback for devices whose only accelerometer is in the base: estimate what a lid sensor would read
/// from the base reading plus the hinge angle.
/// </summary>
/// <remarks>
/// Assumed base frame (provisional, see docs/DECISIONS.md D-003): +X toward the right side of the keyboard,
/// +Y from the front edge toward the hinge, +Z out of the keyboard. Hinge angle: 0 = closed, 90 = lid
/// upright, 180 = flat, 360 = folded into tablet. The hinge axis is the shared X axis.
/// </remarks>
public static class HingeKinematics
{
    public static AccelReading LidFromBase(AccelReading baseReading, double hingeDegrees)
    {
        var h = hingeDegrees * Math.PI / 180.0;
        double c = Math.Cos(h), s = Math.Sin(h);
        return new AccelReading(
            baseReading.X,
            (-c * baseReading.Y) + (s * baseReading.Z),
            (-s * baseReading.Y) - (c * baseReading.Z),
            baseReading.Timestamp);
    }
}
