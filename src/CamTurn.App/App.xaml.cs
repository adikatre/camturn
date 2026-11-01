using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CamTurn.App.Views;
using CamTurn.Platform.Windows;

namespace CamTurn.App;

public partial class App : Application
{
    private readonly CliOptions _options;
    private AppHost? _host;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;

    public App(CliOptions options)
    {
        _options = options;
        DispatcherUnhandledException += (_, e) =>
        {
            _host?.Log.Write(LogLevel.Error, $"Unhandled UI exception: {e.Exception}");
            e.Handled = true; // keep the tray app alive; the error is in the log and diagnostics
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => _host?.Log.Write(LogLevel.Error, $"Unhandled exception: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _host?.Log.Write(LogLevel.Error, $"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _host = new AppHost(_options);

        // Second launches signal this event to bring up settings.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);

        _tray = new TrayIcon(_host, ShowSettings, Quit);
        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            _host.Log.Write(LogLevel.Error, $"Startup failed: {ex}");
        }

        if (!_host.Config.FirstRunCompleted)
        {
            new FirstRunWindow(_host).Show();
        }
        else if (!_options.Minimized && !_host.Config.StartMinimized)
        {
            ShowSettings();
        }
    }

    public void ShowSettings()
    {
        if (_host is null)
        {
            return;
        }

        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_host);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private async void Quit()
    {
        _tray?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        Shutdown();
    }
}
