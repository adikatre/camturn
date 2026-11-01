namespace CamTurn.Core.Orientation;

public enum RotationMode
{
    /// <summary>Follow the device orientation (raw accelerometer by default).</summary>
    Auto,

    /// <summary>Fixed rotation chosen by the user.</summary>
    Manual,
}

public sealed record RotationSettings
{
    public RotationMode Mode { get; init; } = RotationMode.Auto;

    /// <summary>Advanced: in Auto mode, follow the Windows desktop rotation instead of the sensor.</summary>
    public bool FollowDisplayRotation { get; init; }

    /// <summary>Clockwise correction applied in Manual mode.</summary>
    public int ManualRotation { get; init; }

    public RotationMapping Mapping { get; init; } = RotationMapping.Default;
}

public readonly record struct RotationDecision(int CorrectionCw, Quadrant? Quadrant, bool Holding, string Source);

/// <summary>Combines settings, sensor state and display state into the clockwise rotation applied to frames.</summary>
public static class RotationPolicy
{
    /// <param name="sensor">Tracker state, or null when no usable sensor exists.</param>
    /// <param name="displayRotation">Windows desktop rotation (clockwise degrees), or null if unknown.</param>
    public static RotationDecision Decide(RotationSettings settings, OrientationState? sensor, int? displayRotation)
    {
        if (settings.Mode == RotationMode.Manual)
        {
            return new RotationDecision(Normalize(settings.ManualRotation), null, false, "manual");
        }

        if (settings.FollowDisplayRotation && displayRotation is { } d)
        {
            var q = OrientationMath.NearestQuadrant(d);
            return new RotationDecision(settings.Mapping.CorrectionFor(q), q, false, "display");
        }

        if (sensor is { HasLock: true } s)
        {
            return new RotationDecision(settings.Mapping.CorrectionFor(s.Quadrant), s.Quadrant, s.Holding, "sensor");
        }

        if (sensor is { } flat)
        {
            // Sensor present but no reading yet, or flat since start: no rotation until it locks.
            return new RotationDecision(0, null, flat.Holding, "sensor");
        }

        // No sensor: Auto is unavailable; behave like Manual.
        return new RotationDecision(Normalize(settings.ManualRotation), null, false, "no-sensor");
    }

    private static int Normalize(int degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        return d - (d % 90);
    }
}
