using System.Globalization;
using CamTurn.Core.Orientation;
using CamTurn.Core.Sensors;
using CamTurn.Core.Video;

namespace CamTurn.Simulator;

/// <summary>How the runner gets a lid-frame gravity vector from the simulated hardware.</summary>
public enum OrientationSourceKind
{
    /// <summary>Accelerometer in the lid (preferred).</summary>
    LidAccelerometer,

    /// <summary>Base accelerometer plus hinge angle (fallback).</summary>
    BaseAccelerometerPlusHinge,

    /// <summary>No usable sensor: Auto mode unavailable, manual rotation only.</summary>
    None,
}

public sealed record TimelineRow(
    double T,
    DevicePose Pose,
    double TrueRoll,
    AccelReading Reading,
    double MeasuredRoll,
    Quadrant Quadrant,
    bool Holding,
    int DisplayRotation,
    PostureMode Posture,
    SimpleOrientation? Simple,
    CameraState Camera);

public sealed record Keyframe(double T, VideoFrame Raw, VideoFrame Corrected, int CorrectionDegrees, UprightResult Upright, TimelineRow Row);

public sealed record ScenarioResult(
    Scenario Scenario,
    OrientationSourceKind Source,
    IReadOnlyList<TimelineRow> Timeline,
    IReadOnlyList<Keyframe> Keyframes,
    IReadOnlyList<string> Failures)
{
    public bool Passed => Failures.Count == 0;
}

/// <summary>
/// Plays a <see cref="Scenario"/> on a <see cref="SimDevice"/> and feeds the readings through the real
/// CamTurn.Core orientation pipeline (<see cref="OrientationTracker"/> + <see cref="RotationMapping"/>),
/// checking expectations along the way.
/// </summary>
public sealed class ScenarioRunner
{
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    public ScenarioRunner(DeviceProfile profile, RotationMapping? mapping = null, OrientationTrackerOptions? trackerOptions = null)
    {
        Profile = profile;
        Mapping = mapping ?? RotationMapping.Default;
        TrackerOptions = trackerOptions ?? new OrientationTrackerOptions();
    }

    public DeviceProfile Profile { get; }

    public RotationMapping Mapping { get; }

    public OrientationTrackerOptions TrackerOptions { get; }

    /// <summary>Size of keyframe captures. Small by default so tests stay fast.</summary>
    public (int Width, int Height) CaptureSize { get; init; } = (320, 180);

    public static OrientationSourceKind ChooseSource(SimDevice device) =>
        device.LidAccelerometer is not null ? OrientationSourceKind.LidAccelerometer
        : device.BaseAccelerometer is not null && device.HingeSensor is not null ? OrientationSourceKind.BaseAccelerometerPlusHinge
        : OrientationSourceKind.None;

    public ScenarioResult Run(Scenario scenario, bool captureKeyframes = false)
    {
        var device = new SimDevice(Profile, scenario.Seed);
        var tracker = new OrientationTracker(TrackerOptions);
        var source = ChooseSource(device);
        var timeline = new List<TimelineRow>();
        var failures = new List<string>();
        var keyframes = new List<Keyframe>();
        var steps = scenario.Steps.OrderBy(s => s.At).ToList();
        var expectations = scenario.Expect.OrderBy(e => e.At).ToList();
        var keyTimes = (scenario.Keyframes ?? expectations.Select(e => e.At).Distinct().ToList()).OrderBy(t => t).ToList();
        int stepIdx = 0, expIdx = 0, keyIdx = 0;
        AccelReading lastReading = default;

        if (source == OrientationSourceKind.LidAccelerometer)
        {
            device.LidAccelerometer!.ReadingChanged += (_, r) => { lastReading = r; tracker.Update(r); };
        }
        else if (source == OrientationSourceKind.BaseAccelerometerPlusHinge)
        {
            device.BaseAccelerometer!.ReadingChanged += (_, r) =>
            {
                var hinge = device.HingeSensor!.GetCurrentReading()?.AngleDegrees ?? 110;
                lastReading = HingeKinematics.LidFromBase(r, hinge);
                tracker.Update(lastReading);
            };
        }

        var duration = scenario.EffectiveDuration;
        while (true)
        {
            var t = device.Clock.Now.TotalSeconds;
            while (stepIdx < steps.Count && steps[stepIdx].At <= t + 1e-9)
            {
                Apply(device, steps[stepIdx]);
                stepIdx++;
            }

            var row = Snapshot(device, tracker, lastReading);
            timeline.Add(row);

            while (expIdx < expectations.Count && expectations[expIdx].At <= t + 1e-9)
            {
                Check(expectations[expIdx], row, source, device, failures);
                expIdx++;
            }

            while (captureKeyframes && keyIdx < keyTimes.Count && keyTimes[keyIdx] <= t + 1e-9)
            {
                keyframes.Add(Capture(device, row));
                keyIdx++;
            }

            if (t >= duration)
            {
                break;
            }

            device.Advance(Tick);
        }

        for (; expIdx < expectations.Count; expIdx++)
        {
            failures.Add($"expectation at {expectations[expIdx].At}s is after the end of the scenario");
        }

        return new ScenarioResult(scenario, source, timeline, keyframes, failures);
    }

    public Keyframe Capture(SimDevice device, TimelineRow row)
    {
        var raw = device.Camera.Capture(CaptureSize.Width, CaptureSize.Height);
        var correction = Mapping.CorrectionFor(row.Quadrant);
        var corrected = FrameOps.RotateBgraClockwise(raw, correction);
        return new Keyframe(row.T, raw, corrected, correction, UprightDetector.Detect(corrected), row);
    }

    private void Check(ScenarioExpectation e, TimelineRow row, OrientationSourceKind source, SimDevice device, List<string> failures)
    {
        var at = e.At.ToString("0.###", CultureInfo.InvariantCulture);
        if (e.AutoAvailable is { } auto && auto != (source != OrientationSourceKind.None))
        {
            failures.Add($"t={at}s: expected autoAvailable={auto}, orientation source is {source}");
        }

        if (e.Quadrant is { } q && (int)row.Quadrant != q)
        {
            failures.Add($"t={at}s: expected quadrant {q}, got {(int)row.Quadrant} (true roll {row.TrueRoll:0.#}°)");
        }

        if (e.Holding is { } h && row.Holding != h)
        {
            failures.Add($"t={at}s: expected holding={h}, got {row.Holding}");
        }

        if (e.DisplayRotation is { } d && row.DisplayRotation != d)
        {
            failures.Add($"t={at}s: expected Windows display rotation {d}, got {row.DisplayRotation}");
        }

        if (e.Posture is { } p && !string.Equals(p, row.Posture.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"t={at}s: expected posture {p}, got {row.Posture}");
        }

        if (e.CameraState is { } cs && !string.Equals(cs, row.Camera.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"t={at}s: expected camera {cs}, got {row.Camera}");
        }

        if (e.OutputUpright is { } upright)
        {
            var k = Capture(device, row);
            var isUpright = k.Upright.Confident && !k.Upright.Mirrored && OrientationMath.AngularDistance(k.Upright.RotationCwDegrees, 0) < 20;
            if (isUpright != upright)
            {
                failures.Add($"t={at}s: expected output upright={upright}, detector saw rotation {k.Upright.RotationCwDegrees:0.#}° mirrored={k.Upright.Mirrored}");
            }
        }
    }

    private static void Apply(SimDevice device, ScenarioStep step)
    {
        if (step.ResolvePose(device.CurrentPose) is { } pose)
        {
            device.SetPose(pose, TimeSpan.FromSeconds(step.Transition));
        }

        switch (step.Action?.ToLowerInvariant())
        {
            case null:
                break;
            case "lockrotation":
                device.Display.RotationLocked = true;
                break;
            case "unlockrotation":
                device.Display.RotationLocked = false;
                break;
            case "cameradisconnect":
                device.Camera.Disconnect();
                break;
            case "camerareconnect":
                device.Camera.Reconnect();
                break;
            case "camerabusy":
                device.Camera.SetBusy(true);
                break;
            case "camerafree":
                device.Camera.SetBusy(false);
                break;
            case "camerastart":
                device.Camera.Start(device.Camera.Descriptor.Formats[0]);
                break;
            case "camerastop":
                device.Camera.Stop();
                break;
            default:
                throw new InvalidDataException($"Unknown scenario action '{step.Action}'.");
        }
    }

    private static TimelineRow Snapshot(SimDevice device, OrientationTracker tracker, AccelReading reading) =>
        new(
            device.Clock.Now.TotalSeconds,
            device.CurrentPose,
            device.TrueLidRollDegrees,
            reading,
            tracker.State.RollDegrees,
            tracker.State.Quadrant,
            tracker.State.Holding,
            device.Display.RotationDegrees,
            device.Posture.Current,
            device.SimpleOrientationSensor?.GetCurrentOrientation(),
            device.Camera.State);
}
