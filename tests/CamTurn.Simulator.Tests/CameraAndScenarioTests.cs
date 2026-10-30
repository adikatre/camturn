using CamTurn.Core.Orientation;
using CamTurn.Core.Video;

namespace CamTurn.Simulator.Tests;

public class CameraAndScenarioTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    public void DetectorMeasuresSceneRotation(double roll)
    {
        var frame = SceneRenderer.Render(320, 180, roll, default);
        var r = UprightDetector.Detect(frame);
        Assert.True(r.Confident);
        Assert.False(r.Mirrored);
        Assert.InRange(OrientationMath.AngularDistance(r.RotationCwDegrees, roll), 0, 3);
    }

    [Fact]
    public void DetectorSeesMirroring()
    {
        var frame = SceneRenderer.Render(320, 180, 0, default);
        var mirrored = VideoFrame.CreateBgra(320, 180);
        for (var y = 0; y < 180; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                Buffer.BlockCopy(frame.Data, (y * frame.Stride) + (x * 4), mirrored.Data, (y * mirrored.Stride) + ((319 - x) * 4), 4);
            }
        }

        Assert.True(UprightDetector.Detect(mirrored).Mirrored);
    }

    [Theory]
    [InlineData("laptop")]
    [InlineData("tent")]
    [InlineData("stand")]
    [InlineData("tablet-portrait-right")]
    [InlineData("tablet-inverted")]
    [InlineData("tablet-portrait-left")]
    public void RawFrameIsRotatedByLidRollAndDefaultMappingFixesIt(string preset)
    {
        var d = new SimDevice(TestPaths.Inspiron, initialPose: DevicePose.Preset(preset));
        d.Advance(TimeSpan.FromMilliseconds(50));
        var raw = d.Camera.Capture(320, 180);
        var q = OrientationMath.NearestQuadrant(d.TrueLidRollDegrees);

        Assert.InRange(OrientationMath.AngularDistance(UprightDetector.Detect(raw).RotationCwDegrees, (int)q), 0, 3);
        var fixedFrame = FrameOps.RotateBgraClockwise(raw, RotationMapping.Default.CorrectionFor(q));
        Assert.InRange(OrientationMath.AngularDistance(UprightDetector.Detect(fixedFrame).RotationCwDegrees, 0), 0, 3);
    }

    [Fact]
    public void MirroredSensorAxisIsFixedByFlipDirection()
    {
        // A driver that reports X with the wrong sign swaps clockwise and counter-clockwise.
        var bad = TestPaths.Inspiron with
        {
            Sensors = TestPaths.Inspiron.Sensors with
            {
                Accelerometers = [TestPaths.Inspiron.Sensors.Accelerometers[0] with { MirrorX = true }],
            },
        };
        var scenario = Scenario.Load(Path.Combine(TestPaths.Scenarios, "tablet-spin.json"));

        Assert.False(new ScenarioRunner(bad).Run(scenario).Passed);
        var flipped = new ScenarioRunner(bad, RotationMapping.Default.FlipDirection()).Run(scenario);
        Assert.Contains(flipped.Failures, f => f.Contains("quadrant", StringComparison.Ordinal));
        Assert.DoesNotContain(flipped.Failures, f => f.Contains("upright", StringComparison.Ordinal));
    }

    public static TheoryData<string> AllScenarios()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(TestPaths.Scenarios, "*.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileNameWithoutExtension(f));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public void ScenarioPasses(string name)
    {
        var scenario = Scenario.Load(Path.Combine(TestPaths.Scenarios, name + ".json"));
        var profile = DeviceProfile.Resolve(scenario.Device, TestPaths.Devices);
        var result = new ScenarioRunner(profile).Run(scenario);
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
    }

    [Fact]
    public void TentFlipSettlesWithinOneSecond()
    {
        var scenario = Scenario.Load(Path.Combine(TestPaths.Scenarios, "tent-flip.json"));
        var result = new ScenarioRunner(TestPaths.Inspiron).Run(scenario);
        var motionStart = scenario.Steps.Single(s => s.Pose == "tent").At;
        var settled = result.Timeline.First(r => r.T > motionStart && r.Quadrant == Quadrant.Deg180).T;
        Assert.InRange(settled - motionStart, 0, 1.0);
    }

    [Fact]
    public void PngWriterProducesValidSignature()
    {
        var path = Path.Combine(Path.GetTempPath(), $"camturn-{Guid.NewGuid():N}.png");
        try
        {
            PngWriter.Write(SceneRenderer.Render(64, 36, 0, default), path);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
            Assert.True(bytes.Length > 100);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
