using System;
using System.Drawing;
using System.Windows.Forms;
using CamTurn.Core.Pipeline;
using CamTurn.Simulator;

namespace CamTurn.App;

/// <summary>
/// Notification-area icon. Three icon states (idle / streaming / error) that differ in shape as well as colour,
/// and a tooltip that always says the state in words.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppHost _host;
    private readonly NotifyIcon _icon;
    private readonly Icon _idle;
    private readonly Icon _streaming;
    private readonly Icon _error;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _orientationItem;
    private readonly ToolStripMenuItem _startStopItem;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    private bool _paused;

    public TrayIcon(AppHost host, Action openSettings, Action quit)
    {
        _host = host;
        _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _idle = LoadIcon("tray-idle.ico");
        _streaming = LoadIcon("tray-streaming.ico");
        _error = LoadIcon("tray-error.ico");

        _statusItem = new ToolStripMenuItem("Starting…") { Enabled = false };
        _orientationItem = new ToolStripMenuItem("Orientation: –") { Enabled = false };
        _startStopItem = new ToolStripMenuItem("Stop CamTurn", null, (_, _) => TogglePaused());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_orientationItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startStopItem);
        menu.Items.Add(new ToolStripMenuItem("Open settings", null, (_, _) => openSettings()));
        if (host.IsSimulated)
        {
            var poses = new ToolStripMenuItem("Simulated pose (developer)");
            foreach (var name in DevicePose.Presets.Keys)
            {
                poses.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => host.SetSimulatedPose(name)));
            }

            menu.Items.Add(poses);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit CamTurn", null, (_, _) => quit()));

        _icon = new NotifyIcon { Icon = _idle, Text = "CamTurn", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => openSettings();
        host.StatusChanged += (_, s) => _dispatcher.BeginInvoke(() => Update(s));
        Update(host.Status);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _idle.Dispose();
        _streaming.Dispose();
        _error.Dispose();
    }

    public static string Describe(EngineStatus s) => s.Pipeline switch
    {
        PipelineStatus.Streaming => s.Consumers switch
        {
            0 => "Camera on (preview)",
            1 => "Streaming to 1 app",
            var n => $"Streaming to {n} apps",
        },
        PipelineStatus.Starting => "Opening camera…",
        PipelineStatus.Paused => s.Paused ? "Stopped" : "Paused (locked or asleep)",
        PipelineStatus.CameraBusy => "Camera busy or blocked",
        PipelineStatus.CameraMissing => "Camera not found",
        _ => s.RunMode == RunMode.Auto ? "Idle, camera off until an app uses CamTurn Camera" : "Idle, camera off",
    };

    public static string DescribeOrientation(EngineStatus s)
    {
        if (!s.AutoAvailable && s.RotationSource != "manual")
        {
            return $"Orientation: no sensor, rotating {s.CorrectionCw}°";
        }

        var q = s.Quadrant is { } quadrant ? $"{(int)quadrant}°" : "unknown";
        return s.RotationSource switch
        {
            "manual" => $"Orientation: manual {s.CorrectionCw}°",
            "display" => $"Orientation: following display ({q})",
            _ => $"Orientation: {q}{(s.Holding ? ", holding (device flat)" : "")}",
        };
    }

    private static Icon LoadIcon(string name)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded icon {name}.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    private void TogglePaused()
    {
        _paused = !_paused;
        _host.SetPaused(_paused);
        _startStopItem.Text = _paused ? "Start CamTurn" : "Stop CamTurn";
    }

    private void Update(EngineStatus s)
    {
        var error = s.Pipeline is PipelineStatus.CameraBusy or PipelineStatus.CameraMissing;
        _icon.Icon = error ? _error : s.Pipeline == PipelineStatus.Streaming ? _streaming : _idle;
        var text = "CamTurn: " + Describe(s);
        _icon.Text = text.Length > 127 ? text[..127] : text;
        _statusItem.Text = Describe(s);
        _orientationItem.Text = DescribeOrientation(s);
    }
}
