using System.Globalization;
using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>One row per keyframe: raw lid-camera frame | CamTurn-corrected frame | state text.</summary>
public static class ContactSheet
{
    private const int Cell = 300;
    private const int TextWidth = 520;
    private const int Header = 44;

    public static VideoFrame Build(ScenarioResult result)
    {
        var rows = result.Keyframes;
        var sheet = VideoFrame.CreateBgra((Cell * 2) + TextWidth, Header + (rows.Count * (Cell + 8)));
        Canvas.Fill(sheet, 0, 0, sheet.Width, sheet.Height, (32, 28, 28));
        (byte, byte, byte) white = (240, 240, 240), grey = (170, 170, 170), green = (110, 220, 110), red = (90, 90, 240);

        Canvas.Text(sheet, 10, 8, $"SCENARIO {result.Scenario.Name}  SOURCE {result.Source}", white);
        Canvas.Text(sheet, 10, 26, "RAW LID CAMERA", grey);
        Canvas.Text(sheet, Cell + 10, 26, "CAMTURN OUTPUT", grey);

        for (var i = 0; i < rows.Count; i++)
        {
            var k = rows[i];
            var y = Header + (i * (Cell + 8));
            Canvas.Fill(sheet, 0, y, Cell * 2, Cell, (12, 12, 12));
            Canvas.BlitFit(sheet, k.Raw, 4, y + 4, Cell - 8, Cell - 8);
            Canvas.BlitFit(sheet, k.Corrected, Cell + 4, y + 4, Cell - 8, Cell - 8);

            var r = k.Row;
            var tx = (Cell * 2) + 14;
            var upright = k.Upright.Confident && !k.Upright.Mirrored && Core.Orientation.OrientationMath.AngularDistance(k.Upright.RotationCwDegrees, 0) < 20;
            Canvas.Text(sheet, tx, y + 10, F($"T={r.T:0.00}S"), white, 3);
            Canvas.Text(sheet, tx, y + 44, F($"ROLL {r.TrueRoll:0} TILT {r.Pose.TiltDegrees:0} HINGE {r.Pose.HingeDegrees:0}"), grey);
            Canvas.Text(sheet, tx, y + 66, F($"ACCEL {r.Reading.X:0.00} {r.Reading.Y:0.00} {r.Reading.Z:0.00}"), grey);
            Canvas.Text(sheet, tx, y + 88, F($"QUADRANT {(int)r.Quadrant}{(r.Holding ? " HOLDING" : "")}"), white);
            Canvas.Text(sheet, tx, y + 110, F($"ROTATE OUTPUT {k.CorrectionDegrees} CW"), white);
            Canvas.Text(sheet, tx, y + 132, F($"WINDOWS DISPLAY {r.DisplayRotation}  {r.Posture}"), grey);
            Canvas.Text(sheet, tx, y + 154, $"CAMERA {r.Camera}", grey);
            Canvas.Text(sheet, tx, y + 186, upright ? "OUTPUT UPRIGHT" : F($"OUTPUT OFF BY {k.Upright.RotationCwDegrees:0}"), upright ? green : red, 3);
        }

        return sheet;
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
