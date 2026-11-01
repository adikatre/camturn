using System.Runtime.InteropServices;

namespace CamTurn.Platform.Windows;

internal static partial class NativeMethods
{
    public const int SM_CONVERTIBLESLATEMODE = 0x2003;
    public const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public short LogPixels;
        public int BitsPerPel;
        public int PelsWidth;
        public int PelsHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}

/// <summary>camturn_native.dll (src/CamTurn.MediaSource/src/native), installed next to the app.</summary>
internal static class CamTurnNative
{
    private const string Dll = "camturn_native.dll";

    [DllImport(Dll, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    public static extern int CamTurn_IsVirtualCameraSupported(out int supported);

    [DllImport(Dll, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    public static extern int CamTurn_RegisterVirtualCamera(int allUsers);

    [DllImport(Dll, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    public static extern int CamTurn_UnregisterVirtualCamera(int allUsers);
}
