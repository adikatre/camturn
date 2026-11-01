using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using CamTurn.Core.Transport;
using CamTurn.Core.Video;
using Microsoft.Win32;

namespace CamTurn.Platform.Windows;

/// <summary>
/// Creates the frame pipe (DECISIONS D-007) with a DACL that admits only the current user, LocalService (the Frame
/// Server account that hosts the media source) and SYSTEM, and explicitly denies network logons.
/// </summary>
public static class FramePipe
{
    public static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No user SID.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    public static Func<CancellationToken, ValueTask<Stream>> Acceptor(string name = FrameProtocol.PipeName)
    {
        var security = CreateSecurity();
        return async ct =>
        {
            var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                return server;
            }
            catch
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        };
    }
}

/// <summary>Registers "CamTurn Camera" through camturn_native.dll and reports its state.</summary>
public sealed class VirtualCameraRegistrar : IVirtualCameraRegistrar
{
    public const string MediaSourceClsid = "{EF0883BB-0F68-4106-B1D3-3B3B786D600D}";
    public const string FriendlyName = "CamTurn Camera";

    /// <summary>Path of the registered media source DLL (HKLM), or null.</summary>
    public static string? MediaSourcePath()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\CLSID\{MediaSourceClsid}\InprocServer32");
        return key?.GetValue(null) as string;
    }

    public static bool IsSupported()
    {
        try
        {
            return CamTurnNative.CamTurn_IsVirtualCameraSupported(out var supported) >= 0 && supported != 0;
        }
        catch (DllNotFoundException)
        {
            return Environment.OSVersion.Version.Build >= 22000;
        }
    }

    public static async Task<bool> IsVisibleToAppsAsync()
    {
        var devices = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(global::Windows.Devices.Enumeration.DeviceClass.VideoCapture);
        return devices.Any(d => d.Name.StartsWith(FriendlyName, StringComparison.OrdinalIgnoreCase));
    }

    public VirtualCameraState GetState()
    {
        if (!IsSupported())
        {
            return VirtualCameraState.Unsupported;
        }

        var path = MediaSourcePath();
        if (path is null || !File.Exists(path))
        {
            return VirtualCameraState.MediaSourceMissing;
        }

        return IsVisibleToAppsAsync().GetAwaiter().GetResult() ? VirtualCameraState.Registered : VirtualCameraState.NotRegistered;
    }

    public void Register(bool allUsers) => Marshal.ThrowExceptionForHR(CamTurnNative.CamTurn_RegisterVirtualCamera(allUsers ? 1 : 0));

    public void Unregister(bool allUsers) => Marshal.ThrowExceptionForHR(CamTurnNative.CamTurn_UnregisterVirtualCamera(allUsers ? 1 : 0));
}

public static class AppPaths
{
    public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CamTurn");

    public static string ConfigFile => Path.Combine(ConfigDirectory, "config.json");

    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamTurn", "logs");

    /// <summary>Replaces the user profile path and user name so logs and diagnostics can be shared.</summary>
    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var secret in new[] { Environment.UserName, Environment.MachineName })
        {
            if (secret.Length >= 3)
            {
                text = text.Replace(secret, "<redacted>", StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }
}

/// <summary>Small rolling file log (1 MB x 3), redacted. No network, no telemetry.</summary>
public sealed class FileLog
{
    private readonly object _gate = new();
    private readonly string _path;

    public FileLog(string directory, LogLevel minimum)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "camturn.log");
        Minimum = minimum;
    }

    public LogLevel Minimum { get; set; }

    public void Write(LogLevel level, string message)
    {
        if (level < Minimum)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {AppPaths.Redact(message)}{Environment.NewLine}";
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000)
                {
                    for (var i = 2; i >= 1; i--)
                    {
                        var from = $"{_path}.{i}";
                        if (File.Exists(from))
                        {
                            File.Move(from, $"{_path}.{i + 1}", overwrite: true);
                        }
                    }

                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(_path, line);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }

    public string Tail(int lines)
    {
        lock (_gate)
        {
            return File.Exists(_path) ? string.Join(Environment.NewLine, File.ReadLines(_path).TakeLast(lines)) : "";
        }
    }
}

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}
