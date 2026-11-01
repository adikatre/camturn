using System;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using CamTurn.App;
using CamTurn.Core.Config;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using CamTurn.Platform.Windows;

namespace CamTurn.Windows.Tests;

/// <summary>
/// Run on Windows only (CI windows-latest and the developer's laptop). Tests that need hardware the machine lacks
/// (sensors on a CI VM, a webcam) only check that the API calls succeed and report "none".
/// </summary>
[Trait("Category", "Windows")]
public class WindowsIntegrationTests
{
    [Fact]
    public void AutostartRoundTrip()
    {
        var wasEnabled = Autostart.IsEnabled();
        try
        {
            Autostart.Enable(@"C:\Program Files\CamTurn\CamTurn.exe");
            Assert.True(Autostart.IsEnabled());
            Autostart.Disable();
            Assert.False(Autostart.IsEnabled());
        }
        finally
        {
            if (wasEnabled)
            {
                Autostart.Enable(Environment.ProcessPath!);
            }
        }
    }

    [Fact]
    public void PipeDaclAdmitsUserLocalServiceAndSystemAndDeniesNetwork()
    {
        var rules = FramePipe.CreateSecurity().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        bool Has(WellKnownSidType sid, AccessControlType type) =>
            rules.Any(r => r.IdentityReference.Equals(new SecurityIdentifier(sid, null)) && r.AccessControlType == type);

        Assert.True(Has(WellKnownSidType.LocalServiceSid, AccessControlType.Allow));
        Assert.True(Has(WellKnownSidType.LocalSystemSid, AccessControlType.Allow));
        Assert.True(Has(WellKnownSidType.NetworkSid, AccessControlType.Deny));
        Assert.Contains(rules, r => r.IdentityReference.Equals(WindowsIdentity.GetCurrent().User) && r.AccessControlType == AccessControlType.Allow);
    }

    [Fact]
    public async Task AclPipeRoundTripWithManagedConsumer()
    {
        var name = "CamTurn.Test." + Guid.NewGuid().ToString("N");
        await using var host = new FramePipeHost(FramePipe.Acceptor(name));
        host.Start();
        await using var client = new FramePipeClient(name);
        await client.ConnectAsync(64, 36, 30, TimeSpan.FromSeconds(5));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.Consumers.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        var payload = new byte[FrameProtocol.Nv12Size(64, 36)];
        payload[0] = 42;
        Assert.Equal(1, host.Publish(payload, 64, 36, 7, 0, FrameFlags.None, 90));
        var frame = await client.WaitForFrameAsync(null, TimeSpan.FromSeconds(5));
        Assert.Equal(7UL, frame.Header.FrameCounter);
        Assert.Equal(90u, frame.Header.Param);
        Assert.Equal(42, frame.Payload[0]);
    }

    [Fact]
    public void VirtualCameraStateIsReportable()
    {
        var state = new VirtualCameraRegistrar().GetState();
        Assert.True(Enum.IsDefined(state));
        if (Environment.OSVersion.Version.Build < 22000)
        {
            Assert.Equal(VirtualCameraState.Unsupported, state);
        }
    }

    [Fact]
    public async Task SensorAndCameraEnumerationDoNotThrow()
    {
        var accels = await WinRtSensorEnumerator.FindAccelerometersAsync();
        using var lid = WinRtAccelerometer.TryGetDefault();
        Assert.Equal(accels.Any(a => a.IsDefault), lid is not null);
        var cameras = await MediaCaptureCamera.FindAllAsync();
        Assert.DoesNotContain(cameras, c => c.Name.Contains("CamTurn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticsAreRedacted()
    {
        var text = await DiagnosticsReport.BuildAsync(new AppConfig(), null, null);
        Assert.Contains("CamTurn diagnostics", text, StringComparison.Ordinal);
        if (Environment.UserName.Length >= 3)
        {
            Assert.DoesNotContain(Environment.UserName, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(new[] { "--diagnose" }, CliCommand.Diagnose)]
    [InlineData(new[] { "--register", "--current-user" }, CliCommand.Register)]
    [InlineData(new[] { "--test-pattern", "--simulate-orientation", "180" }, CliCommand.None)]
    public void CliParses(string[] args, CliCommand expected)
    {
        var o = CliOptions.Parse(args);
        Assert.Equal(expected, o.Command);
        if (args.Contains("180"))
        {
            Assert.Equal(180, o.SimulateOrientation);
            Assert.True(o.TestPattern);
        }
    }

    [Fact]
    public void CliRejectsBadOrientation() => Assert.Throws<ArgumentException>(() => CliOptions.Parse(["--simulate-orientation", "45"]));
}
