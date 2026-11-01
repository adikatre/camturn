using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>The simulator's lid-camera scene; the implementation lives in <see cref="TestPattern"/> so the app can ship it.</summary>
public static class SceneRenderer
{
    public static VideoFrame Render(int width, int height, double rollCwDegrees, TimeSpan t, long frameNumber = 0) =>
        TestPattern.Render(width, height, rollCwDegrees, t, frameNumber);
}
