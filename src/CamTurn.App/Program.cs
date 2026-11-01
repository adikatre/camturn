using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CamTurn.App;

public static class Program
{
    public const string ShowEventName = @"Local\CamTurn.ShowSettings";
    private const string MutexName = @"Local\CamTurn.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            AttachParentConsole();
            Console.Error.WriteLine($"error: {e.Message}");
            return 2;
        }

        if (options.Command != CliCommand.None)
        {
            AttachParentConsole();
            // WinRT calls must not block the STA thread; run commands on the thread pool.
            return Task.Run(() => Cli.RunAsync(options)).GetAwaiter().GetResult();
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var first);
        if (!first)
        {
            // Already running: ask that instance to show its settings window.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show))
            {
                show.Set();
                show.Dispose();
            }

            return 0;
        }

        var app = new App(options);
        app.InitializeComponent();
        return app.Run();
    }

    private static void AttachParentConsole()
    {
        // WinExe has no console; reuse the one we were launched from, if any. When output is redirected (scripts,
        // the installer, CI) keep the inherited handle instead, so the text lands where the caller expects it.
        if (GetStdHandle(StdOutputHandle) == IntPtr.Zero)
        {
            AttachConsole(-1);
        }
    }

    private const int StdOutputHandle = -11;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);
}
