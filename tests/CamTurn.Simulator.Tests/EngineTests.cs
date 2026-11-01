using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Sensors;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using CamTurn.Testing;

namespace CamTurn.Simulator.Tests;

/// <summary>
/// The whole app pipeline minus Windows: CamTurnEngine + a real named pipe + a managed stand-in for the media
/// source, fed by a test-pattern camera or the simulated Inspiron.
/// </summary>
public class EngineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(what);
            }

            await Task.Delay(20);
        }
    }

    private static UprightResult Upright(byte[] nv12, int w, int h) =>
        UprightDetector.Detect(FrameOps.Nv12ToBgra(new VideoFrame(w, h, PixelFormat.Nv12, nv12, w, 0, default)));

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task TestPatternThroughPipeIsUpright(int simulatedRoll)
    {
        var pipe = TestPipes.UniqueName();
        using var accel = new FixedAccelerometer(simulatedRoll);
        using var camera = new TestPatternCamera(() => simulatedRoll);
        var engine = new CamTurnEngine(
            new EngineDependencies { Camera = camera, Accelerometer = accel, Host = new FramePipeHost(TestPipes.Acceptor(pipe)) },
            new AppConfig());
        await using (engine)
        {
            engine.Start();
            accel.Start();
            Assert.Equal(CameraState.Closed, camera.State); // nobody watching: camera closed

            await using var consumer = new FramePipeClient(pipe);
            await consumer.ConnectAsync(640, 360, 30, Timeout);
            var frame = await consumer.WaitForFrameAsync(h => h.Param == RotationMapping.Default.CorrectionFor((Quadrant)simulatedRoll), Timeout);

            Assert.Equal((640u, 360u), (frame.Header.Width, frame.Header.Height));
            var up = Upright(frame.Payload, 640, 360);
            Assert.True(up.Confident);
            Assert.InRange(OrientationMath.AngularDistance(up.RotationCwDegrees, 0), 0, 3);
            Assert.Equal(1, engine.Status.Consumers);
            Assert.Equal(PipelineStatus.Streaming, engine.Status.Pipeline);
        }
    }

    [Fact]
    public async Task CameraClosesAfterConsumerLeavesAndGracePeriod()
    {
        var pipe = TestPipes.UniqueName();
        var camera = new FakeCamera();
        var engine = new CamTurnEngine(new EngineDependencies { Camera = camera, Host = new FramePipeHost(TestPipes.Acceptor(pipe)) }, new AppConfig());
        await using (engine)
        {
            engine.Start();
            var consumer = new FramePipeClient(pipe);
            await consumer.ConnectAsync(320, 180, 30, Timeout);
            await WaitUntil(() => camera.State == CameraState.Streaming, "camera opened for consumer");
            Assert.False(engine.Status.AutoAvailable);

            await consumer.DisposeAsync();
            await WaitUntil(() => engine.Status.Consumers == 0, "consumer gone");
            await Task.Delay(1000);
            Assert.Equal(CameraState.Streaming, camera.State); // still within the 3 s grace period
            await WaitUntil(() => camera.State == CameraState.Closed, "camera closed after grace");
            Assert.Equal(PipelineStatus.Idle, engine.Status.Pipeline);
        }
    }

    [Fact]
    public async Task BusyCameraIsReportedAndRetried()
    {
        var pipe = TestPipes.UniqueName();
        var camera = new FakeCamera { Busy = true };
        var engine = new CamTurnEngine(new EngineDependencies { Camera = camera, Host = new FramePipeHost(TestPipes.Acceptor(pipe)) }, new AppConfig());
        await using (engine)
        {
            engine.Start();
            await using var consumer = new FramePipeClient(pipe);
            await consumer.ConnectAsync(320, 180, 30, Timeout);
            await WaitUntil(() => engine.Status.Pipeline == PipelineStatus.CameraBusy, "busy reported");
            Assert.Contains("in use", engine.Status.LastError, StringComparison.Ordinal);

            camera.Busy = false;
            await WaitUntil(() => camera.State == CameraState.Streaming, "retry succeeded");
            Assert.True(camera.StartCount >= 2);
        }
    }

    [Fact]
    public async Task SimulatedInspironTentFlipThroughPipe()
    {
        var pipe = TestPipes.UniqueName();
        var device = new SimDevice(TestPaths.Inspiron, seed: 9);
        var engine = new CamTurnEngine(
            new EngineDependencies { Camera = new Nv12Adapter(device.Camera), Accelerometer = device.LidAccelerometer, Host = new FramePipeHost(TestPipes.Acceptor(pipe)) },
            new AppConfig());
        await using (engine)
        {
            engine.Start();
            await using var consumer = new FramePipeClient(pipe);
            await consumer.ConnectAsync(320, 180, 30, Timeout);
            await WaitUntil(() => device.Camera.LedOn, "lid camera opened");

            // Drive virtual time from the test: laptop for 1 s, then fold into tent.
            device.Advance(TimeSpan.FromSeconds(1));
            device.SetPose(DevicePose.Preset("tent"), TimeSpan.FromSeconds(0.6));
            device.Advance(TimeSpan.FromSeconds(1.5));
            var frame = await consumer.WaitForFrameAsync(h => h.Param == 180, Timeout);
            var up = Upright(frame.Payload, 320, 180);
            Assert.InRange(OrientationMath.AngularDistance(up.RotationCwDegrees, 0), 0, 5);
            Assert.Equal(Quadrant.Deg180, engine.Status.Quadrant);
        }
    }

    [Fact]
    public async Task BadHelloIsRejectedWithoutAffectingCamera()
    {
        var pipe = TestPipes.UniqueName();
        var camera = new FakeCamera();
        var host = new FramePipeHost(TestPipes.Acceptor(pipe));
        var errors = 0;
        host.ProtocolError += (_, _) => Interlocked.Increment(ref errors);
        var engine = new CamTurnEngine(new EngineDependencies { Camera = camera, Host = host }, new AppConfig());
        await using (engine)
        {
            engine.Start();
            await using var raw = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.InOut);
            await raw.ConnectAsync(5000);
            await raw.WriteAsync(new byte[FrameProtocol.HeaderSize]); // all zeros: bad magic
            await WaitUntil(() => Volatile.Read(ref errors) == 1, "protocol error raised");
            Assert.Equal(CameraState.Closed, camera.State);
        }
    }

    /// <summary>The simulator's camera emits BGRA; real Windows capture is configured for NV12.</summary>
    private sealed class Nv12Adapter : ICameraSource
    {
        private readonly SimLidCamera _inner;

        public Nv12Adapter(SimLidCamera inner)
        {
            _inner = inner;
            _inner.FrameArrived += (_, f) => FrameArrived?.Invoke(this, f.Format == PixelFormat.Nv12 ? f : FrameOps.BgraToNv12(f));
            _inner.StateChanged += (_, s) => StateChanged?.Invoke(this, s);
        }

        public CameraDescriptor Descriptor => _inner.Descriptor;

        public CameraState State => _inner.State;

        public event EventHandler<VideoFrame>? FrameArrived;

        public event EventHandler<CameraState>? StateChanged;

        public bool Start(VideoFormat format) => _inner.Start(new VideoFormat(640, 480, 30, "YUY2"));

        public void Stop() => _inner.Stop();
    }
}
