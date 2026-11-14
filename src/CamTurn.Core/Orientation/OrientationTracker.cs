using CamTurn.Core.Sensors;

namespace CamTurn.Core.Orientation;

public sealed record OrientationTrackerOptions
{
    /// <summary>Leave the current quadrant only after the roll is this far from its centre.</summary>
    public double SwitchThresholdDegrees { get; init; } = 60;

    /// <summary>The new quadrant must hold this long before it is committed.</summary>
    public TimeSpan HoldTime { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Below this fraction of gravity in the screen plane the device counts as flat and the last quadrant is held.</summary>
    public double FlatInPlaneFraction { get; init; } = 0.4;
}

public readonly record struct OrientationState(Quadrant Quadrant, bool Holding, double RollDegrees, bool HasLock);

/// <summary>
/// Turns raw accelerometer readings into a stable quadrant with hysteresis, a hold time, and
/// flat-device holding. Pure: time comes from the reading timestamps.
/// </summary>
public sealed class OrientationTracker
{
    private readonly OrientationTrackerOptions _options;
    private Quadrant? _pending;
    private TimeSpan _pendingSince;

    public OrientationTracker(OrientationTrackerOptions? options = null, Quadrant initial = Quadrant.Deg0)
    {
        _options = options ?? new OrientationTrackerOptions();
        State = new OrientationState(initial, Holding: true, RollDegrees: double.NaN, HasLock: false);
    }

    public OrientationState State { get; private set; }

    public event EventHandler<OrientationState>? QuadrantChanged;

    public OrientationState Update(AccelReading reading)
    {
        var roll = OrientationMath.RollDegrees(reading);
        if (OrientationMath.InPlaneFraction(reading) < _options.FlatInPlaneFraction)
        {
            _pending = null;
            State = State with { Holding = true, RollDegrees = roll };
            return State;
        }

        if (!State.HasLock)
        {
            // First usable reading: adopt the nearest quadrant immediately.
            Commit(OrientationMath.NearestQuadrant(roll), roll);
            return State;
        }

        var current = State.Quadrant;
        var candidate = OrientationMath.AngularDistance(roll, (int)current) > _options.SwitchThresholdDegrees
            ? OrientationMath.NearestQuadrant(roll)
            : current;

        if (candidate == current)
        {
            _pending = null;
        }
        else if (_pending != candidate)
        {
            _pending = candidate;
            _pendingSince = reading.Timestamp;
        }
        else if (reading.Timestamp - _pendingSince >= _options.HoldTime)
        {
            Commit(candidate, roll);
            return State;
        }

        State = State with { Holding = false, RollDegrees = roll };
        return State;
    }

    private void Commit(Quadrant quadrant, double roll)
    {
        var changed = !State.HasLock || quadrant != State.Quadrant;
        _pending = null;
        State = new OrientationState(quadrant, Holding: false, roll, HasLock: true);
        if (changed)
        {
            QuadrantChanged?.Invoke(this, State);
        }
    }
}
