using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CamTurn.Core.Orientation;
using CamTurn.Core.Pipeline;
using CamTurn.Core.Video;

namespace CamTurn.Core.Config;

/// <summary>User settings, stored as JSON. <see cref="Version"/> drives migrations in <see cref="ConfigStore"/>.</summary>
public sealed record AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Physical camera device id; null = the built-in front camera.</summary>
    public string? CameraId { get; init; }

    public int OutputWidth { get; init; } = 1280;

    public int OutputHeight { get; init; } = 720;

    public int Fps { get; init; } = 30;

    public RotationMode RotationMode { get; init; } = RotationMode.Auto;

    public bool FollowDisplayRotation { get; init; }

    public int ManualRotation { get; init; }

    public MappingConfig Mapping { get; init; } = new();

    public bool Mirror { get; init; }

    public FitMode PortraitFit { get; init; } = FitMode.Fit;

    public bool StartWithWindows { get; init; }

    public bool StartMinimized { get; init; } = true;

    public RunMode RunMode { get; init; } = RunMode.Auto;

    public bool ReleaseOnLock { get; init; } = true;

    public bool FirstRunCompleted { get; init; }

    public string LogLevel { get; init; } = "Info";

    public RotationSettings ToRotationSettings() => new()
    {
        Mode = RotationMode,
        FollowDisplayRotation = FollowDisplayRotation,
        ManualRotation = ManualRotation,
        Mapping = Mapping.ToMapping(),
    };

    /// <summary>Clamps out-of-range values so a hand-edited file cannot break the pipeline.</summary>
    public AppConfig Sanitized()
    {
        static int Even(int v, int min, int max) => Math.Clamp(v, min, max) & ~1;
        return this with
        {
            OutputWidth = Even(OutputWidth, 320, FrameProtocol.MaxWidth),
            OutputHeight = Even(OutputHeight, 240, FrameProtocol.MaxHeight),
            Fps = Math.Clamp(Fps, 5, 60),
            ManualRotation = (((ManualRotation % 360) + 360) % 360) / 90 * 90,
            Mapping = Mapping.Sanitized(),
        };
    }
}

public sealed record MappingConfig
{
    public int For0 { get; init; } = RotationMapping.Default.For0;

    public int For90 { get; init; } = RotationMapping.Default.For90;

    public int For180 { get; init; } = RotationMapping.Default.For180;

    public int For270 { get; init; } = RotationMapping.Default.For270;

    public static MappingConfig From(RotationMapping m) => new() { For0 = m.For0, For90 = m.For90, For180 = m.For180, For270 = m.For270 };

    public RotationMapping ToMapping() => new(For0, For90, For180, For270);

    public MappingConfig Sanitized()
    {
        static int Q(int v) => (((v % 360) + 360) % 360) / 90 * 90;
        return new MappingConfig { For0 = Q(For0), For90 = Q(For90), For180 = Q(For180), For270 = Q(For270) };
    }
}

public sealed record ConfigLoadResult(AppConfig Config, bool Migrated, string? Warning);

/// <summary>Loads, migrates and atomically saves <see cref="AppConfig"/>.</summary>
public static class ConfigStore
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ConfigLoadResult Load(string path)
    {
        if (!File.Exists(path))
        {
            return new ConfigLoadResult(new AppConfig(), false, null);
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            // Keep the broken file for inspection and start from defaults.
            var backup = path + ".invalid";
            File.Copy(path, backup, overwrite: true);
            return new ConfigLoadResult(new AppConfig(), false, $"Settings file was invalid and has been reset (copy kept as {Path.GetFileName(backup)}): {e.Message}");
        }
    }

    public static ConfigLoadResult Parse(string json)
    {
        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException("Settings root must be an object.");
        var version = node["version"]?.GetValue<int>() ?? 0;
        if (version > AppConfig.CurrentVersion)
        {
            throw new JsonException($"Settings version {version} is newer than this CamTurn ({AppConfig.CurrentVersion}).");
        }

        var migrated = false;
        if (version == 0)
        {
            // Pre-release format: "flipDirection": true meant a flipped mapping table.
            if (node["flipDirection"]?.GetValue<bool>() == true && node["mapping"] is null)
            {
                node["mapping"] = JsonSerializer.SerializeToNode(MappingConfig.From(RotationMapping.Default.FlipDirection()), JsonOptions);
            }

            node.Remove("flipDirection");
            node["version"] = 1;
            migrated = true;
        }

        var config = node.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
        return new ConfigLoadResult(config.Sanitized(), migrated, null);
    }

    public static void Save(AppConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config with { Version = AppConfig.CurrentVersion }, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
