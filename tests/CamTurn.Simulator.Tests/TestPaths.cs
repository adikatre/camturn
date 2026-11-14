using CamTurn.Simulator;

namespace CamTurn.Simulator.Tests;

internal static class TestPaths
{
    public static string SimDir { get; } = Find();

    public static string Devices => Path.Combine(SimDir, "devices");

    public static string Scenarios => Path.Combine(SimDir, "scenarios");

    public static DeviceProfile Inspiron => DeviceProfile.Resolve("dell-inspiron-16-7640-2in1", Devices);

    private static string Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "sim");
            if (Directory.Exists(Path.Combine(candidate, "devices")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("sim/ folder not found above the test binaries.");
    }
}
