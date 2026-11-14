using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Core.Video;

namespace CamTurn.Simulator.Tests;

public class DeviceModelTests
{
    [Theory]
    [InlineData("laptop", 0, -0.940, -0.342)]
    [InlineData("tent", 0, 0.866, -0.5)]
    [InlineData("stand", 0, -0.966, -0.259)]
    [InlineData("tablet", 0, -0.866, -0.5)]
    [InlineData("tablet-portrait-right", 0.866, 0, -0.5)]
    [InlineData("tablet-portrait-left", -0.866, 0, -0.5)]
    [InlineData("flat-face-up", 0, 0, -1)]
    [InlineData("flat-face-down", 0, 0, 1)]
    public void PresetLidGravity(string preset, double x, double y, double z)
    {
        var g = DevicePose.GravityIn(DevicePose.Preset(preset).LidAxes(), default);
        Assert.Equal(x, g.X, 3);
        Assert.Equal(y, g.Y, 3);
        Assert.Equal(z, g.Z, 3);
    }

    [Theory]
    [InlineData("laptop", 0, 0, -1)] // base flat, keyboard up
    [InlineData("stand", 0, 0, 1)] // keyboard face down on the desk
    [InlineData("tent", 0, -0.866, -0.5)] // base leaning at 60°, hinge at the top
    public void PresetBaseGravity(string preset, double x, double y, double z)
    {
        var g = DevicePose.GravityIn(DevicePose.Preset(preset).BaseAxes(), default);
        Assert.Equal(x, g.X, 3);
        Assert.Equal(y, g.Y, 3);
        Assert.Equal(z, g.Z, 3);
    }

    [Theory]
    [InlineData("laptop", Quadrant.Deg0)]
    [InlineData("tent", Quadrant.Deg180)]
    [InlineData("stand", Quadrant.Deg0)]
    [InlineData("tablet-portrait-right", Quadrant.Deg90)]
    [InlineData("tablet-inverted", Quadrant.Deg180)]
    [InlineData("tablet-portrait-left", Quadrant.Deg270)]
    public void PresetQuadrant(string preset, Quadrant expected)
    {
        var g = DevicePose.GravityIn(DevicePose.Preset(preset).LidAxes(), default);
        Assert.Equal(expected, OrientationMath.NearestQuadrant(OrientationMath.RollDegrees(g)));
    }

    [Theory]
    [InlineData(0, 20, 110)]
    [InlineData(180, 30, 300)]
    [InlineData(37, -10, 250)]
    [InlineData(250, 60, 200)]
    public void BasePlusHingeReconstructsLid(double roll, double tilt, double hinge)
    {
        var pose = new DevicePose(roll, tilt, hinge);
        var lid = DevicePose.GravityIn(pose.LidAxes(), default);
        var est = HingeKinematics.LidFromBase(DevicePose.GravityIn(pose.BaseAxes(), default), hinge);
        Assert.Equal(lid.X, est.X, 9);
        Assert.Equal(lid.Y, est.Y, 9);
        Assert.Equal(lid.Z, est.Z, 9);
    }

    [Fact]
    public void ProfilesLoadAndExposeSensors()
    {
        var device = new SimDevice(TestPaths.Inspiron);
        Assert.Equal(2, device.SensorEnumerator.FindAll(SensorKind.Accelerometer).Count);
        Assert.Equal(SensorLocation.Lid, device.DefaultAccelerometer!.Descriptor.Location);
        Assert.Equal(OrientationSourceKind.LidAccelerometer, ScenarioRunner.ChooseSource(device));

        var baseOnly = new SimDevice(DeviceProfile.Resolve("dell-inspiron-16-7640-2in1-base-only", TestPaths.Devices));
        Assert.Equal(OrientationSourceKind.BaseAccelerometerPlusHinge, ScenarioRunner.ChooseSource(baseOnly));
    }

    [Fact]
    public void NoSensorsMeansManualOnly()
    {
        var profile = TestPaths.Inspiron with { Sensors = new SensorSpec() };
        Assert.Equal(OrientationSourceKind.None, ScenarioRunner.ChooseSource(new SimDevice(profile)));
    }

    [Fact]
    public void AccelerometerReportsAtItsIntervalWithNoise()
    {
        var device = new SimDevice(TestPaths.Inspiron, seed: 7);
        var readings = new List<AccelReading>();
        device.LidAccelerometer!.ReadingChanged += (_, r) => readings.Add(r);
        device.Advance(TimeSpan.FromSeconds(1));

        Assert.InRange(readings.Count, 60, 64); // 16 ms interval
        var meanY = readings.Average(r => r.Y);
        Assert.Equal(-0.94, meanY, 1);
        Assert.True(readings.Select(r => r.Y).Distinct().Count() > 50, "readings should be noisy");
    }

    [Fact]
    public void SameSeedIsDeterministic()
    {
        static double[] Run()
        {
            var d = new SimDevice(TestPaths.Inspiron, seed: 42);
            var xs = new List<double>();
            d.LidAccelerometer!.ReadingChanged += (_, r) => xs.Add(r.X);
            d.Advance(TimeSpan.FromMilliseconds(500));
            return [.. xs];
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void WindowsAutoRotateOnlyInSlateAndRespectsLock()
    {
        var d = new SimDevice(TestPaths.Inspiron, initialPose: new DevicePose(90, 20, 110));
        d.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(PostureMode.Laptop, d.Posture.Current);
        Assert.Equal(0, d.Display.RotationDegrees);

        d.SetPose(DevicePose.Preset("tablet-portrait-right"));
        d.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(PostureMode.Slate, d.Posture.Current);
        Assert.Equal(90, d.Display.RotationDegrees);

        d.Display.RotationLocked = true;
        d.SetPose(DevicePose.Preset("tablet-portrait-left"));
        d.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(90, d.Display.RotationDegrees);
        Assert.Equal(SimpleOrientation.Rotated90DegreesCounterclockwise, d.SimpleOrientationSensor!.GetCurrentOrientation());
    }

    [Fact]
    public void CameraLedOnlyWhileStreaming()
    {
        var d = new SimDevice(TestPaths.Inspiron);
        var cam = d.Camera;
        var frames = 0;
        cam.FrameArrived += (_, _) => frames++;
        Assert.False(cam.LedOn);

        Assert.True(cam.Start(new VideoFormat(640, 480, 30, "YUY2")));
        d.Advance(TimeSpan.FromSeconds(1));
        Assert.True(cam.LedOn);
        Assert.InRange(frames, 29, 31);

        cam.Stop();
        Assert.False(cam.LedOn);
        d.Advance(TimeSpan.FromSeconds(1));
        Assert.InRange(frames, 29, 31);
    }

    [Fact]
    public void CameraBusyAndDisconnect()
    {
        var d = new SimDevice(TestPaths.Inspiron);
        var fmt = d.Camera.Descriptor.Formats[0];
        d.Camera.SetBusy(true);
        Assert.False(d.Camera.Start(fmt));
        Assert.Equal(CameraState.Busy, d.Camera.State);

        d.Camera.SetBusy(false);
        Assert.True(d.Camera.Start(fmt));
        d.Camera.Disconnect();
        Assert.Equal(CameraState.Disconnected, d.Camera.State);
        Assert.False(d.Camera.Start(fmt));
        d.Camera.Reconnect();
        Assert.True(d.Camera.Start(fmt));
    }

    [Fact]
    public void Nv12FormatDeliversNv12Frames()
    {
        var d = new SimDevice(TestPaths.Inspiron);
        VideoFrame? got = null;
        d.Camera.FrameArrived += (_, f) => got = f;
        d.Camera.Start(new VideoFormat(1280, 720, 30, "NV12"));
        d.Advance(TimeSpan.FromMilliseconds(40));
        Assert.NotNull(got);
        Assert.Equal(PixelFormat.Nv12, got!.Format);
        Assert.Equal(0, UprightDetector.Detect(got).RotationCwDegrees, 0);
    }
}
