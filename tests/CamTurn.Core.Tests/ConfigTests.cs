using CamTurn.Core.Config;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Video;

namespace CamTurn.Core.Tests;

public sealed class ConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "camturn-tests-" + Guid.NewGuid().ToString("N"));

    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var r = ConfigStore.Load(PathOf("none.json"));
        Assert.Equal(new AppConfig(), r.Config);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var c = new AppConfig
        {
            CameraId = "cam-1",
            RunMode = RunMode.AlwaysOn,
            RotationMode = RotationMode.Manual,
            ManualRotation = 270,
            Mirror = true,
            PortraitFit = FitMode.Fill,
            Mapping = MappingConfig.From(RotationMapping.Default.FlipDirection()),
            FirstRunCompleted = true,
        };
        ConfigStore.Save(c, PathOf("c.json"));
        var r = ConfigStore.Load(PathOf("c.json"));
        Assert.Equal(c, r.Config);
        Assert.False(r.Migrated);
        Assert.Contains("\"runMode\": \"AlwaysOn\"", File.ReadAllText(PathOf("c.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Version0IsMigrated()
    {
        var r = ConfigStore.Parse("""{ "flipDirection": true, "mirror": true }""");
        Assert.True(r.Migrated);
        Assert.Equal(AppConfig.CurrentVersion, r.Config.Version);
        Assert.True(r.Config.Mirror);
        Assert.Equal(RotationMapping.Default.FlipDirection(), r.Config.Mapping.ToMapping());
    }

    [Fact]
    public void NewerVersionIsRejectedAndFileKept()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf("c.json"), """{ "version": 99 }""");
        var r = ConfigStore.Load(PathOf("c.json"));
        Assert.Equal(new AppConfig(), r.Config);
        Assert.NotNull(r.Warning);
        Assert.True(File.Exists(PathOf("c.json.invalid")));
    }

    [Fact]
    public void GarbageIsReset()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathOf("c.json"), "{ not json");
        Assert.NotNull(ConfigStore.Load(PathOf("c.json")).Warning);
    }

    [Fact]
    public void OutOfRangeValuesAreClamped()
    {
        var c = ConfigStore.Parse("""{ "version": 1, "outputWidth": 99999, "outputHeight": 3, "fps": 500, "manualRotation": -90, "mapping": { "for90": 100 } }""").Config;
        Assert.Equal((3840, 240, 60, 270), (c.OutputWidth, c.OutputHeight, c.Fps, c.ManualRotation));
        Assert.Equal(90, c.Mapping.For90);
    }
}
