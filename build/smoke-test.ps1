<#
.SYNOPSIS
  Installs CamTurn silently, checks what got registered, runs --diagnose and --pipe-probe, uninstalls, and checks
  for leftovers. Used by the experimental CI job and usable on a fresh Windows 11 VM (docs/TESTING.md).
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string]$Installer)

$ErrorActionPreference = 'Stop'
$clsid = '{EF0883BB-0F68-4106-B1D3-3B3B786D600D}'
$app = Join-Path $env:ProgramFiles 'CamTurn'
$failures = @()
function Check($ok, $what) { if ($ok) { Write-Host "PASS $what" } else { Write-Host "FAIL $what" -ForegroundColor Red; $script:failures += $what } }

Write-Host "OS: $((Get-CimInstance Win32_OperatingSystem).Caption) build $([Environment]::OSVersion.Version.Build)"
Write-Host "Frame Server: $((Get-Service FrameServer -ErrorAction SilentlyContinue).Status)"

$p = Start-Process $Installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=""' -Wait -PassThru
Check ($p.ExitCode -eq 0) "installer exit code 0 (got $($p.ExitCode))"
Check (Test-Path "$app\CamTurn.exe") 'CamTurn.exe installed'
Check (Test-Path "$app\camturn_mediasource.dll") 'media source installed'
Check ((Get-ItemProperty "HKLM:\SOFTWARE\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue).'(default)' -eq "$app\camturn_mediasource.dll") 'media source COM class registered in HKLM'

& "$app\CamTurn.exe" --diagnose | Out-String | Tee-Object -Variable diag | Write-Host
Check ($diag -match 'visible to apps\s+True') '"CamTurn Camera" visible to apps (needs Frame Server; may fail on Server SKUs)'

# Start the app with the test pattern and probe the pipe like the media source would.
$run = Start-Process "$app\CamTurn.exe" -ArgumentList '--test-pattern', '--simulate-orientation', '180', '--minimized' -PassThru
Start-Sleep -Seconds 5
& "$app\CamTurn.exe" --pipe-probe | Out-String | Tee-Object -Variable probe | Write-Host
Check ($probe -match 'rotation 180 deg') 'pipe delivers test-pattern frames rotated 180'
Stop-Process -Id $run.Id -Force -ErrorAction SilentlyContinue

$u = Start-Process "$app\unins000.exe" -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
Check ($u.ExitCode -eq 0) 'uninstaller exit code 0'
Start-Sleep -Seconds 3
Check (-not (Test-Path "HKLM:\SOFTWARE\Classes\CLSID\$clsid")) 'COM registration removed'
Check (-not (Test-Path "$app\CamTurn.exe")) 'program files removed'
Check (-not (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CamTurn -ErrorAction SilentlyContinue)) 'autostart entry removed'

if ($failures.Count) { Write-Host "$($failures.Count) check(s) failed"; exit 1 }
Write-Host 'All smoke checks passed.'
