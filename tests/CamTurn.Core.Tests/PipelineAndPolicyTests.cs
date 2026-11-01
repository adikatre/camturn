using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Video;

namespace CamTurn.Core.Tests;

public class PipelineControllerTests
{
    private sealed class Effects : IPipelineEffects
    {
        public int Opens;
        public int Closes;
        public bool Open;

        public void OpenCamera()
        {
            Opens++;
            Open = true;
        }

        public void CloseCamera()
        {
            Closes++;
            Open = false;
        }
    }

    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void AutoOpensOnlyWhileConsumerConnectedPlusGrace()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.Tick(S(0));
        Assert.False(fx.Open);
        Assert.Equal(PipelineStatus.Idle, p.Status);

        p.ConsumerConnected(S(1));
        Assert.True(fx.Open);
        Assert.Equal(PipelineStatus.Starting, p.Status);
        p.FrameArrived(S(1.2));
        Assert.Equal(PipelineStatus.Streaming, p.Status);

        p.ConsumerDisconnected(S(5));
        p.Tick(S(7.9));
        Assert.True(fx.Open); // grace period
        p.Tick(S(8.1));
        Assert.False(fx.Open); // privacy light off
        Assert.Equal(PipelineStatus.Idle, p.Status);
        Assert.Equal((1, 1), (fx.Opens, fx.Closes));
    }

    [Fact]
    public void ReconnectDuringGraceKeepsCameraOpen()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.ConsumerConnected(S(0));
        p.ConsumerDisconnected(S(1));
        p.ConsumerConnected(S(2));
        p.Tick(S(10));
        Assert.True(fx.Open);
        Assert.Equal(1, fx.Opens);
    }

    [Fact]
    public void MultipleConsumersCounted()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.ConsumerConnected(S(0));
        p.ConsumerConnected(S(0));
        p.ConsumerDisconnected(S(1));
        p.Tick(S(10));
        Assert.True(fx.Open);
        Assert.Equal(1, p.Consumers);
    }

    [Fact]
    public void LockAndSuspendReleaseTheCamera()
    {
        var fx = new Effects();
        var p = new PipelineController(fx, RunMode.AlwaysOn);
        p.Tick(S(0));
        Assert.True(fx.Open);

        p.SetSessionLocked(true, S(1));
        Assert.False(fx.Open);
        Assert.Equal(PipelineStatus.Paused, p.Status);
        p.SetSessionLocked(false, S(2));
        Assert.True(fx.Open);

        p.SetSuspended(true, S(3));
        Assert.False(fx.Open);
        p.SetSuspended(false, S(4));
        Assert.True(fx.Open);
    }

    [Fact]
    public void LockDoesNotReleaseWhenOptionOff()
    {
        var fx = new Effects();
        var p = new PipelineController(fx, RunMode.AlwaysOn, new PipelineOptions { ReleaseOnLock = false });
        p.Tick(S(0));
        p.SetSessionLocked(true, S(1));
        Assert.True(fx.Open);
    }

    [Fact]
    public void ManualModeFollowsTrayToggle()
    {
        var fx = new Effects();
        var p = new PipelineController(fx, RunMode.Manual);
        p.ConsumerConnected(S(0));
        Assert.False(fx.Open);
        p.SetManual(true, S(1));
        Assert.True(fx.Open);
        p.SetManual(false, S(2));
        Assert.False(fx.Open);
    }

    [Fact]
    public void BusyCameraIsRetried()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.ConsumerConnected(S(0));
        p.CameraStateChanged(CameraState.Busy, S(0.1));
        Assert.Equal(PipelineStatus.CameraBusy, p.Status);
        p.Tick(S(1));
        Assert.Equal(1, fx.Opens);
        p.Tick(S(2.1));
        Assert.Equal(2, fx.Opens);
        p.FrameArrived(S(2.5));
        Assert.Equal(PipelineStatus.Streaming, p.Status);
    }

    [Fact]
    public void DisconnectedCameraReportsMissing()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.ConsumerConnected(S(0));
        p.CameraStateChanged(CameraState.Disconnected, S(0.5));
        Assert.Equal(PipelineStatus.CameraMissing, p.Status);
    }

    [Fact]
    public void PauseOverridesEveryMode()
    {
        var fx = new Effects();
        var p = new PipelineController(fx, RunMode.AlwaysOn);
        p.Tick(S(0));
        p.SetPaused(true, S(1));
        Assert.False(fx.Open);
        Assert.Equal(PipelineStatus.Paused, p.Status);
        p.ConsumerConnected(S(2));
        Assert.False(fx.Open);
        p.SetPaused(false, S(3));
        Assert.True(fx.Open);
    }

    [Fact]
    public void SwitchingModesTakesEffectImmediately()
    {
        var fx = new Effects();
        var p = new PipelineController(fx);
        p.SetMode(RunMode.AlwaysOn, S(0));
        Assert.True(fx.Open);
        p.SetMode(RunMode.Auto, S(1));
        Assert.False(fx.Open);
    }
}

public class RotationPolicyTests
{
    private static readonly OrientationState Tent = new(Quadrant.Deg180, false, 180, true);

    [Fact]
    public void AutoUsesSensorThroughMapping()
    {
        var d = RotationPolicy.Decide(new RotationSettings(), new OrientationState(Quadrant.Deg90, false, 90, true), 0);
        Assert.Equal(270, d.CorrectionCw);
        Assert.Equal("sensor", d.Source);
    }

    [Fact]
    public void FlippedMappingChangesDirection()
    {
        var s = new RotationSettings { Mapping = RotationMapping.Default.FlipDirection() };
        Assert.Equal(90, RotationPolicy.Decide(s, new OrientationState(Quadrant.Deg90, false, 90, true), null).CorrectionCw);
    }

    [Fact]
    public void ManualOverridesSensor() =>
        Assert.Equal(90, RotationPolicy.Decide(new RotationSettings { Mode = RotationMode.Manual, ManualRotation = 90 }, Tent, 0).CorrectionCw);

    [Fact]
    public void FollowDisplayUsesDesktopRotation()
    {
        var d = RotationPolicy.Decide(new RotationSettings { FollowDisplayRotation = true }, Tent, 0);
        Assert.Equal(0, d.CorrectionCw);
        Assert.Equal("display", d.Source);
    }

    [Fact]
    public void NoSensorFallsBackToManualAngle()
    {
        var d = RotationPolicy.Decide(new RotationSettings { ManualRotation = 180 }, null, null);
        Assert.Equal(180, d.CorrectionCw);
        Assert.Equal("no-sensor", d.Source);
    }

    [Fact]
    public void HoldingIsReported() =>
        Assert.True(RotationPolicy.Decide(new RotationSettings(), Tent with { Holding = true }, null).Holding);
}
