<#
.SYNOPSIS
  Read-only hardware probe for CamTurn. Reports the device model, Windows build, orientation sensors,
  display/posture state and cameras, so the simulator profile can be replaced with real data.

.DESCRIPTION
  Changes nothing, installs nothing, needs no admin rights and makes no network calls.
  Output is written to the console and to camturn-probe.txt next to the script.
  Identifiers that could be unique to this machine (serial numbers, device instance paths, user and
  computer names) are redacted.

  Run it in *Windows PowerShell* (powershell.exe, version 5.1), not PowerShell 7, because WinRT
  types are only available there:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\probe-device.ps1 -Poses

.PARAMETER Poses
  Guided mode: asks you to hold the device in several poses and records the sensor readings in each.

.PARAMETER SkipCameraFormats
  Do not open the cameras to list their formats (opening them may briefly turn on the camera light).
#>
[CmdletBinding()]
param(
    [switch]$Poses,
    [switch]$SkipCameraFormats
)

$ErrorActionPreference = 'Stop'
$script:Lines = New-Object System.Collections.Generic.List[string]
$OutFile = Join-Path $PSScriptRoot 'camturn-probe.txt'

function Out-Line([string]$Text = '') {
    $t = $Text
    foreach ($secret in @($env:USERNAME, $env:COMPUTERNAME, $env:USERPROFILE)) {
        if ($secret) { $t = $t.Replace($secret, '<redacted>') }
    }
    $script:Lines.Add($t)
    Write-Host $t
}

function Redact-Id([string]$Id) {
    if (-not $Id) { return '' }
    $m = [regex]::Match($Id, 'VID_[0-9A-Fa-f]{4}&PID_[0-9A-Fa-f]{4}')
    if ($m.Success) { return $m.Value.ToUpperInvariant() }
    $m = [regex]::Match($Id, '^\\\\\?\\([A-Za-z0-9]+)#')
    if ($m.Success) { return "$($m.Groups[1].Value)#<redacted>" }
    return '<redacted>'
}

function Fmt([double]$v) { return $v.ToString('0.000', [Globalization.CultureInfo]::InvariantCulture) }

function Roll([double]$x, [double]$y) {
    # Clockwise roll as seen by the viewer, CamTurn convention (docs/DECISIONS.md D-002).
    $deg = [Math]::Atan2($x, -$y) * 180.0 / [Math]::PI
    if ($deg -lt 0) { $deg += 360 }
    return $deg.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture)
}

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Warning 'Run this in Windows PowerShell 5.1 (powershell.exe); WinRT sensor APIs are not available in PowerShell 7.'
}

# ---- WinRT plumbing -------------------------------------------------------------------------------
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Devices.Sensors.Accelerometer, Windows.Devices.Sensors, ContentType = WindowsRuntime]
$null = [Windows.Devices.Sensors.SimpleOrientationSensor, Windows.Devices.Sensors, ContentType = WindowsRuntime]
$null = [Windows.Devices.Enumeration.DeviceInformation, Windows.Devices.Enumeration, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Display.DisplayOrientations, Windows.Graphics.Display, ContentType = WindowsRuntime]
$null = [Windows.Media.Capture.Frames.MediaFrameSourceGroup, Windows.Media.Capture, ContentType = WindowsRuntime]
$null = [Windows.Media.Capture.MediaCapture, Windows.Media.Capture, ContentType = WindowsRuntime]

$AsTaskOp = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1
$AsTaskAction = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
} | Select-Object -First 1

function Await($Operation, [Type]$ResultType) {
    $task = $AsTaskOp.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $null = $task.Wait(15000)
    return $task.Result
}

function Await-Action($Action) {
    $task = $AsTaskAction.Invoke($null, @($Action))
    $null = $task.Wait(15000)
}

# ---- Win32 helpers (display orientation, slate mode) ---------------------------------------------
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CamTurnProbeNative {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);
    public static int[] PrimaryDisplay() {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettingsW(null, -1, ref dm)) return new int[0];
        return new[] { dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayOrientation };
    }
}
'@

# ---- System ---------------------------------------------------------------------------------------
Out-Line '=== CamTurn device probe (read-only) ==='
Out-Line ("Probe version 2; run at {0:yyyy-MM-dd HH:mm} local time" -f (Get-Date))
$cs = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$enc = Get-CimInstance Win32_SystemEnclosure | Select-Object -First 1
$ubr = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue).UBR
$display = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue).DisplayVersion
Out-Line ''
Out-Line '[System]'
Out-Line "Manufacturer        : $($cs.Manufacturer)"
Out-Line "Model               : $($cs.Model)"
Out-Line "System family       : $($cs.SystemFamily)"
Out-Line "Chassis types       : $($enc.ChassisTypes -join ', ')   (31 = Convertible, 32 = Detachable, 10 = Notebook)"
Out-Line "OS                  : $($os.Caption) $display, build $($os.BuildNumber).$ubr"
Out-Line "Architecture        : $($os.OSArchitecture)"

# ---- Posture / display -----------------------------------------------------------------------------
$SM_CONVERTIBLESLATEMODE = 0x2003
$SM_SYSTEMDOCKED = 0x2004
function Get-DisplayState {
    $d = [CamTurnProbeNative]::PrimaryDisplay()
    $slate = [CamTurnProbeNative]::GetSystemMetrics($SM_CONVERTIBLESLATEMODE)
    $orient = 'unknown'
    if ($d.Length -eq 3) { $orient = @('0 (DMDO_DEFAULT)', '1 (DMDO_90)', '2 (DMDO_180)', '3 (DMDO_270)')[$d[2]] }
    $mode = 'laptop'
    if ($slate -eq 0) { $mode = 'slate/tablet' }
    return [pscustomobject]@{ Width = $d[0]; Height = $d[1]; Orientation = $orient; Slate = $slate; Mode = $mode }
}
$ds = Get-DisplayState
Out-Line ''
Out-Line '[Display and posture]'
Out-Line "Primary display     : $($ds.Width)x$($ds.Height), dmDisplayOrientation = $($ds.Orientation)"
Out-Line "ConvertibleSlateMode: $($ds.Slate)  -> $($ds.Mode)   (0 = slate)"
Out-Line "SystemDocked        : $([CamTurnProbeNative]::GetSystemMetrics($SM_SYSTEMDOCKED))"
$rot = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AutoRotation' -ErrorAction SilentlyContinue
if ($rot) { Out-Line "AutoRotation Enable : $($rot.Enable)   SlateEnable: $($rot.SlateEnable)" }
$lock = Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ImmersiveShell' -ErrorAction SilentlyContinue
if ($lock -and ($lock.PSObject.Properties.Name -contains 'RotationLockPreference')) { Out-Line "RotationLockPref    : $($lock.RotationLockPreference)" }

# ---- Sensors ----------------------------------------------------------------------------------------
Out-Line ''
Out-Line '[PnP sensor devices]'
try {
    Get-PnpDevice -Class Sensor -ErrorAction Stop | Sort-Object FriendlyName | ForEach-Object {
        Out-Line ("  {0,-50} status={1,-8} {2}" -f $_.FriendlyName, $_.Status, (Redact-Id $_.InstanceId))
    }
} catch { Out-Line "  (none or not accessible: $($_.Exception.Message))" }

$Standard = [Windows.Devices.Sensors.AccelerometerReadingType]::Standard
$Landscape = [Windows.Graphics.Display.DisplayOrientations]::Landscape
$accels = @()
Out-Line ''
Out-Line '[WinRT accelerometers: Accelerometer.GetDeviceSelector(Standard)]'
try {
    $selector = [Windows.Devices.Sensors.Accelerometer]::GetDeviceSelector($Standard)
    $infos = Await ([Windows.Devices.Enumeration.DeviceInformation]::FindAllAsync($selector)) ([Windows.Devices.Enumeration.DeviceInformationCollection])
    $default = [Windows.Devices.Sensors.Accelerometer]::GetDefault($Standard)
    $i = 0
    foreach ($info in $infos) {
        $acc = Await ([Windows.Devices.Sensors.Accelerometer]::FromIdAsync($info.Id)) ([Windows.Devices.Sensors.Accelerometer])
        $isDefault = ($default -ne $null -and $acc -ne $null -and $default.DeviceId -eq $acc.DeviceId)
        $panel = 'unknown'
        if ($info.EnclosureLocation) { $panel = $info.EnclosureLocation.Panel }
        Out-Line ("  #{0} {1}  default={2}  panel={3}  id={4}" -f $i, $info.Name, $isDefault, $panel, (Redact-Id $info.Id))
        if ($acc) {
            Out-Line ("      min report interval {0} ms, reading transform default = {1}" -f $acc.MinimumReportInterval, $acc.ReadingTransform)
            $accels += [pscustomobject]@{ Index = $i; Sensor = $acc; Default = $isDefault; Original = $acc.ReadingTransform }
        }
        $i++
    }
    if ($infos.Count -eq 0) { Out-Line '  none found -> CamTurn Auto mode would be unavailable' }
} catch { Out-Line "  error: $($_.Exception.Message)" }

$hinge = $null
Out-Line ''
Out-Line '[Hinge angle sensor]'
try {
    $null = [Windows.Devices.Sensors.HingeAngleSensor, Windows.Devices.Sensors, ContentType = WindowsRuntime]
    $hinge = Await ([Windows.Devices.Sensors.HingeAngleSensor]::GetDefaultAsync()) ([Windows.Devices.Sensors.HingeAngleSensor])
    if ($hinge) {
        $hr = Await ($hinge.GetCurrentReadingAsync()) ([Windows.Devices.Sensors.HingeAngleReading])
        Out-Line "  present, angle now = $(Fmt $hr.AngleInDegrees) deg"
    } else { Out-Line '  none' }
} catch { Out-Line "  not available: $($_.Exception.Message)" }

$simple = [Windows.Devices.Sensors.SimpleOrientationSensor]::GetDefault()
Out-Line ''
Out-Line '[SimpleOrientationSensor]'
if ($simple) { Out-Line "  present, now = $($simple.GetCurrentOrientation())" } else { Out-Line '  none' }

function Sample-Pose([string]$Label) {
    Out-Line ''
    Out-Line "--- $Label ---"
    $st = Get-DisplayState
    Out-Line "  display orientation $($st.Orientation), mode $($st.Mode)"
    if ($simple) { Out-Line "  SimpleOrientation   $($simple.GetCurrentOrientation())" }
    if ($hinge) {
        try {
            $hr = Await ($hinge.GetCurrentReadingAsync()) ([Windows.Devices.Sensors.HingeAngleReading])
            Out-Line "  hinge angle         $(Fmt $hr.AngleInDegrees) deg"
        } catch { Out-Line '  hinge angle         (read failed)' }
    }
    foreach ($a in $accels) {
        foreach ($transform in @('default', 'Landscape')) {
            if ($transform -eq 'Landscape') { $a.Sensor.ReadingTransform = $Landscape } else { $a.Sensor.ReadingTransform = $a.Original }
            $sx = 0.0; $sy = 0.0; $sz = 0.0; $n = 0
            for ($k = 0; $k -lt 20; $k++) {
                $r = $a.Sensor.GetCurrentReading()
                if ($r) { $sx += $r.AccelerationX; $sy += $r.AccelerationY; $sz += $r.AccelerationZ; $n++ }
                Start-Sleep -Milliseconds 100
            }
            if ($n -gt 0) {
                $x = $sx / $n; $y = $sy / $n; $z = $sz / $n
                Out-Line ("  accel #{0} [{1,-9}] x={2} y={3} z={4}  roll={5} deg  (n={6})" -f $a.Index, $transform, (Fmt $x), (Fmt $y), (Fmt $z), (Roll $x $y), $n)
            } else {
                Out-Line ("  accel #{0} [{1}] no readings" -f $a.Index, $transform)
            }
        }
    }
}

Sample-Pose 'As the device is right now'

if ($Poses) {
    $steps = @(
        @{ Name = 'LAPTOP';  Text = 'Laptop mode on a desk, screen upright facing you.' },
        @{ Name = 'TENT';    Text = 'Tent mode (upside-down V, hinge on top), screen facing you.' },
        @{ Name = 'STAND';   Text = 'Stand mode (keyboard face down on the desk), screen facing you.' },
        @{ Name = 'TABLET-PORTRAIT-RIGHT'; Text = 'Tablet mode, turned so the RIGHT edge of the screen points DOWN (portrait).' },
        @{ Name = 'TABLET-PORTRAIT-LEFT';  Text = 'Tablet mode, turned so the LEFT edge of the screen points DOWN (portrait).' },
        @{ Name = 'FLAT';    Text = 'Tablet mode lying flat on the desk, screen up.' },
        @{ Name = 'LAPTOP-DESKTOP-ROTATED'; Text = 'Back to laptop mode. Then open Settings > System > Display and set Display orientation to "Portrait" (keep it for ~5 s; it reverts automatically if you do not confirm).' }
    )
    foreach ($s in $steps) {
        Write-Host ''
        Write-Host "Pose: $($s.Text)" -ForegroundColor Cyan
        $null = Read-Host 'Hold it still and press Enter (readings take ~4 s per sensor)'
        Sample-Pose $s.Name
    }
}

# ---- Cameras -----------------------------------------------------------------------------------------
Out-Line ''
Out-Line '[Cameras: MediaFrameSourceGroup.FindAllAsync]'
try {
    $groups = Await ([Windows.Media.Capture.Frames.MediaFrameSourceGroup]::FindAllAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Media.Capture.Frames.MediaFrameSourceGroup]])
    foreach ($g in $groups) {
        Out-Line "  group: $($g.DisplayName)  id=$(Redact-Id $g.Id)"
        foreach ($si in $g.SourceInfos) {
            $panel = 'unknown'
            if ($si.DeviceInformation -and $si.DeviceInformation.EnclosureLocation) { $panel = $si.DeviceInformation.EnclosureLocation.Panel }
            Out-Line "    source: kind=$($si.SourceKind) stream=$($si.MediaStreamType) panel=$panel"
        }
        if ($SkipCameraFormats) { continue }
        $mc = New-Object Windows.Media.Capture.MediaCapture
        $settings = New-Object Windows.Media.Capture.MediaCaptureInitializationSettings
        $settings.SourceGroup = $g
        $settings.SharingMode = [Windows.Media.Capture.MediaCaptureSharingMode]::SharedReadOnly
        $settings.MemoryPreference = [Windows.Media.Capture.MediaCaptureMemoryPreference]::Cpu
        $settings.StreamingCaptureMode = [Windows.Media.Capture.StreamingCaptureMode]::Video
        try {
            Await-Action ($mc.InitializeAsync($settings))
            # FrameSources is a WinRT map; enumerate key/value pairs (.Values is not projected in PowerShell 5.1).
            $sources = @()
            foreach ($kv in $mc.FrameSources) { $sources += $kv.Value }
            if ($sources.Count -eq 0) { Out-Line '    (camera opened but reported no frame sources)' }
            foreach ($fs in $sources) {
                Out-Line "    formats for $($fs.Info.SourceKind)/$($fs.Info.MediaStreamType):"
                $seen = @{}
                foreach ($f in $fs.SupportedFormats) {
                    if (-not $f.VideoFormat) { continue }
                    $fps = 0
                    if ($f.FrameRate.Denominator -ne 0) { $fps = [Math]::Round($f.FrameRate.Numerator / $f.FrameRate.Denominator, 2) }
                    $key = "$($f.Subtype) $($f.VideoFormat.Width)x$($f.VideoFormat.Height)@$fps"
                    if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; Out-Line "      $key" }
                }
            }
        } catch {
            Out-Line "    could not open (in use, blocked by privacy settings, or not a video source): $($_.Exception.Message)"
        } finally {
            $mc.Dispose()
        }
    }
} catch { Out-Line "  error: $($_.Exception.Message)" }

Out-Line ''
Out-Line '=== end of probe ==='
$script:Lines | Set-Content -Path $OutFile -Encoding UTF8
Write-Host ''
Write-Host "Saved to $OutFile. Paste its contents back to CamTurn's developer." -ForegroundColor Green
