<#
.SYNOPSIS
  Checks the portable app and optional camera on a fresh Windows CI runner or test VM.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string]$PortableExe)

$ErrorActionPreference = 'Stop'
$clsid = '{EF0883BB-0F68-4106-B1D3-3B3B786D600D}'
$exe = (Resolve-Path -LiteralPath $PortableExe).Path
$app = Join-Path $env:ProgramFiles 'CamTurn Camera'
$failures = @()
function Invoke-CamTurn([string[]]$Arguments) {
    # CamTurn.exe is a GUI-subsystem app: run it with redirected output and wait, so we get its text and exit code.
    $out = New-TemporaryFile
    $p = Start-Process $exe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out
    $text = Get-Content $out -Raw
    Remove-Item $out
    Write-Host $text
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Text = "$text" }
}
function Check($ok, $what) { if ($ok) { Write-Host "PASS $what" } else { Write-Host "FAIL $what" -ForegroundColor Red; $script:failures += $what } }

Write-Host "OS: $((Get-CimInstance Win32_OperatingSystem).Caption) build $([Environment]::OSVersion.Version.Build)"
Write-Host "Frame Server: $((Get-Service FrameServer -ErrorAction SilentlyContinue).Status)"

if (Test-Path "HKLM:\SOFTWARE\Classes\CLSID\$clsid") { throw 'The smoke check requires a fresh VM without an existing camera registration.' }
$before = Invoke-CamTurn @('--diagnose')
Check ($before.ExitCode -eq 0) 'portable EXE runs before camera registration'
Check (-not (Test-Path "HKLM:\SOFTWARE\Classes\CLSID\$clsid")) 'ordinary startup commands leave the camera unregistered'
$registration = Invoke-CamTurn @('--register')
Check ($registration.ExitCode -eq 0) 'explicit camera registration succeeds'
Check (Test-Path "$app\camturn_mediasource.dll") 'media source installed'
Check ((Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue).'(default)' -eq "$app\camturn_mediasource.dll") 'media source COM class registered in HKLM'

$diag = Invoke-CamTurn @('--diagnose')
Check ($diag.ExitCode -eq 0) 'CamTurn.exe --diagnose runs'
Check ($diag.Text -match 'visible to apps\s+True') '"CamTurn Camera" visible to apps (needs Frame Server; may fail on Server SKUs)'

# Open "CamTurn Camera" like an app would while CamTurn is NOT running: Frame Server must load our media source,
# which serves its placeholder.
$probe0 = Invoke-CamTurn @('--probe-virtual-camera')
Check ($probe0.ExitCode -eq 3) "Frame Server loads the media source and serves the placeholder (exit $($probe0.ExitCode); 3 expected)"

# Start the app with the test pattern and probe the pipe like the media source would.
$run = Start-Process $exe -ArgumentList '--test-pattern', '--simulate-orientation', '180', '--minimized' -PassThru
Start-Sleep -Seconds 5
$probe = Invoke-CamTurn @('--pipe-probe')
Check ($probe.Text -match 'rotation 180 deg') 'pipe delivers test-pattern frames rotated 180'

# Full path: app -> pipe -> media source in Frame Server -> a capture app. Acceptance criterion 1 without a human.
$probe1 = Invoke-CamTurn @('--probe-virtual-camera')
Check ($probe1.ExitCode -eq 0) "CamTurn Camera shows the upright test pattern end to end (exit $($probe1.ExitCode); 0 expected)"
Stop-Process -Id $run.Id -Force -ErrorAction SilentlyContinue

$u = Invoke-CamTurn @('--unregister')
Check ($u.ExitCode -eq 0) 'camera component removal succeeds'
Start-Sleep -Seconds 3
Check (-not (Test-Path "HKLM:\SOFTWARE\Classes\CLSID\$clsid")) 'COM registration removed'
Check (Test-Path $exe) 'portable executable remains available'
Check (-not (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CamTurn -ErrorAction SilentlyContinue)) 'autostart stays off'

if ($failures.Count) { Write-Host "$($failures.Count) check(s) failed"; exit 1 }
Write-Host 'All smoke checks passed.'
