using System.Diagnostics;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Video;
using Xunit.Abstractions;

namespace CamTurn.Simulator.Tests;

/// <summary>Drives the real Core pipeline pieces (tracker, policy, compositor, controller) with simulated hardware.</summary>
public class EndToEndTests(ITestOutputHelper output)
{
    private static UprightResult DetectNv12(byte[] nv12, int w, int h) =>
        UprightDetector.Detect(FrameOps.Nv12ToBgra(new VideoFrame(w, h, PixelFormat.Nv12, nv12, w, 0, default)));

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void TestPatternWithSimulatedOrientationComesOutUpright(int simulated)
    {
        // Acceptance criterion 1, minus Windows: --test-pattern --simulate-orientation N.
        using var cam = new TestPatternCamera(() => simulated);
        var raw = cam.Render(1280, 720);
        var tracker = new OrientationTracker();
        var r = simulated * Math.PI / 180;
        var state = tracker.Update(new(Math.Sin(r), -Math.Cos(r), 0, TimeSpan.Zero));
        var decision = RotationPolicy.Decide(new RotationSettings(), state, null);
        var settings = new CompositorSettings(decision.CorrectionCw, false, FitMode.Fit, 1280, 720);
        var outFrame = new Compositor().Compose(raw, settings);

        var up = DetectNv12(outFrame, 1280, 720);
        Assert.True(up.Confident);
        Assert.False(up.Mirrored);
        Assert.InRange(OrientationMath.AngularDistance(up.RotationCwDegrees, 0), 0, 3);
    }

    [Fact]
    public void TentFlipStreamThroughCompositorIsUprightWithinOneSecond()
    {
        var device = new SimDevice(TestPaths.Inspiron, seed: 3);
        var tracker = new OrientationTracker();
        device.LidAccelerometer!.ReadingChanged += (_, r) => tracker.Update(r);
        var compositor = new Compositor();
        var settings = new RotationSettings();
        double? uprightAgainAt = null;

        device.Advance(TimeSpan.FromSeconds(1));
        device.SetPose(DevicePose.Preset("tent"), TimeSpan.FromSeconds(0.6));
        var flipStart = device.Clock.Now;
        for (var i = 0; i < 60 && uprightAgainAt is null; i++)
        {
            device.Advance(TimeSpan.FromMilliseconds(1000.0 / 30));
            var raw = FrameOps.BgraToNv12(device.Camera.Capture(320, 180));
            var d = RotationPolicy.Decide(settings, tracker.State, null);
            var composed = compositor.Compose(raw, new CompositorSettings(d.CorrectionCw, false, FitMode.Fit, 320, 180));
            var up = DetectNv12(composed, 320, 180);
            if (d.Quadrant == Quadrant.Deg180 && OrientationMath.AngularDistance(up.RotationCwDegrees, 0) < 15)
            {
                uprightAgainAt = (device.Clock.Now - flipStart).TotalSeconds;
            }
        }

        Assert.NotNull(uprightAgainAt);
        output.WriteLine($"upright again {uprightAgainAt:0.00} s after the fold started");
        Assert.InRange(uprightAgainAt!.Value, 0, 1.0);
    }

    [Fact]
    public void PrivacyLightOffAfterGracePeriod()
    {
        // Acceptance criterion 3 against the simulated camera's LED.
        var device = new SimDevice(TestPaths.Inspiron);
        var effects = new CameraEffects(device.Camera);
        var p = new PipelineController(effects);
        device.Camera.FrameArrived += (_, _) => p.FrameArrived(device.Clock.Now);

        p.Tick(device.Clock.Now);
        Assert.False(device.Camera.LedOn);

        p.ConsumerConnected(device.Clock.Now);
        device.Advance(TimeSpan.FromSeconds(1));
        Assert.True(device.Camera.LedOn);
        Assert.Equal(PipelineStatus.Streaming, p.Status);

        p.ConsumerDisconnected(device.Clock.Now);
        for (var i = 0; i < 40; i++)
        {
            device.Advance(TimeSpan.FromMilliseconds(100));
            p.Tick(device.Clock.Now);
        }

        Assert.False(device.Camera.LedOn);
        Assert.Equal(PipelineStatus.Idle, p.Status);
    }

    [Fact]
    public void CompositorCostAt1080pInput()
    {
        var src = FrameOps.BgraToNv12(TestPattern.Render(1920, 1080, 90, default));
        var c = new Compositor();
        var dst = new byte[FrameProtocol.Nv12Size(1280, 720)];
        var settings = new CompositorSettings(270, true, FitMode.Fit, 1280, 720);
        c.Compose(src, settings, dst); // warm up and build tables
        const int n = 60;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < n; i++)
        {
            c.Compose(src, settings, dst);
        }

        var ms = sw.Elapsed.TotalMilliseconds / n;
        output.WriteLine($"compose 1920x1080 NV12 -> 1280x720, rotate 270 + mirror + fit: {ms:0.00} ms/frame ({ms * 30 / 10:0.0}% of one core at 30 fps)");
        Assert.InRange(ms, 0, 33); // must fit in a 30 fps frame budget even on a slow CI runner
    }

    private sealed class CameraEffects(SimLidCamera camera) : IPipelineEffects
    {
        public void OpenCamera() => camera.Start(camera.Descriptor.Formats[0]);

        public void CloseCamera() => camera.Stop();
    }
}
