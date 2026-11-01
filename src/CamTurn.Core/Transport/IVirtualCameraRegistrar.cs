namespace CamTurn.Core.Transport;

public enum VirtualCameraState
{
    /// <summary>Windows build has no software virtual camera support (Windows 10, or Windows 11 before 22000).</summary>
    Unsupported,

    /// <summary>The media source COM class is not registered (CamTurn not installed properly).</summary>
    MediaSourceMissing,

    /// <summary>Media source installed but "CamTurn Camera" is not registered.</summary>
    NotRegistered,

    /// <summary>"CamTurn Camera" is registered and visible to apps.</summary>
    Registered,
}

/// <summary>Registers/removes the "CamTurn Camera" virtual camera. Windows implementation in CamTurn.Platform.Windows.</summary>
public interface IVirtualCameraRegistrar
{
    VirtualCameraState GetState();

    /// <summary>Creates the camera. allUsers needs administrator rights (installer / repair).</summary>
    void Register(bool allUsers);

    void Unregister(bool allUsers);
}
