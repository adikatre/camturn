using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CamTurn.Platform.Windows;

namespace CamTurn.App;

/// <summary>Manages the optional machine-wide camera without elevating the tray process.</summary>
internal static class CameraComponent
{
    private static string ComponentDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CamTurn Camera");

    public static async Task ChangeAsync(bool register, bool currentUserOnly)
    {
        if (register && !VirtualCameraRegistrar.IsSupported())
        {
            throw new InvalidOperationException("The Windows virtual camera API is unavailable.");
        }

        var registrar = new VirtualCameraRegistrar();
        var source = VirtualCameraRegistrar.MediaSourcePath();
        var setup = Path.Combine(AppContext.BaseDirectory, "CamTurn.CameraComponent.exe");
        var remove = Path.Combine(ComponentDirectory, "unins000.exe");

        var componentHash = typeof(CameraComponent).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CameraComponentHash")?.Value;

        // Keep the existing development and per-user commands for an already configured media source.
        if (currentUserOnly || componentHash is null)
        {
            if (register)
            {
                if (!File.Exists(source))
                {
                    throw new InvalidOperationException("Register the machine-wide component using the portable CamTurn.exe first.");
                }

                registrar.Register(!currentUserOnly);
            }
            else
            {
                registrar.Unregister(!currentUserOnly);
            }

            return;
        }

        if (source is not null && !string.Equals(source, Path.Combine(ComponentDirectory, "camturn_mediasource.dll"), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("An older CamTurn installation manages the camera. Remove it through Windows Installed apps before changing the camera component.");
        }

        if (!register && !File.Exists(remove))
        {
            if (source is not null)
            {
                throw new InvalidOperationException("Register the camera again to restore its removal tool.");
            }

            return;
        }

        // Hold the checked file open without write/delete sharing through the elevation prompt.
        using var component = register ? File.OpenRead(setup) : null;
        if (component is not null && !string.Equals(Convert.ToHexString(SHA256.HashData(component)), componentHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The extracted camera component does not match this CamTurn build.");
        }

        var start = new ProcessStartInfo(register ? setup : remove, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART")
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.SystemDirectory,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not open the camera component tool.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Camera component operation failed (exit {process.ExitCode}). Close apps using the camera and try again.");
        }
    }
}
