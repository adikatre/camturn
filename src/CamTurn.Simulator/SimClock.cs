namespace CamTurn.Simulator;

/// <summary>Virtual time. Deterministic; the simulator only moves when <see cref="Advance"/> is called.</summary>
public sealed class SimClock
{
    public TimeSpan Now { get; private set; }

    public void Advance(TimeSpan dt)
    {
        if (dt < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dt));
        }

        Now += dt;
    }
}
