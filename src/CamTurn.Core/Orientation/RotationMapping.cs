namespace CamTurn.Core.Orientation;

/// <summary>
/// Table from measured device quadrant to the clockwise rotation applied to camera frames.
/// The default undoes the roll (a panel rolled 90° clockwise shows the scene turned 90° clockwise,
/// so frames are rotated 270° clockwise). "Flip direction" swaps the 90/270 entries for hardware
/// whose sensor axes or camera mounting disagree with the default.
/// </summary>
public sealed record RotationMapping(int For0, int For90, int For180, int For270)
{
    public static RotationMapping Default { get; } = new(0, 270, 180, 90);

    public int CorrectionFor(Quadrant q) => q switch
    {
        Quadrant.Deg0 => For0,
        Quadrant.Deg90 => For90,
        Quadrant.Deg180 => For180,
        Quadrant.Deg270 => For270,
        _ => throw new ArgumentOutOfRangeException(nameof(q)),
    };

    public RotationMapping FlipDirection() => this with { For90 = For270, For270 = For90 };
}
