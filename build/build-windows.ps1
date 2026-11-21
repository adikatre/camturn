<#
.SYNOPSIS
  Builds CamTurn on Windows: native DLLs (CMake + MSVC), .NET app (self-contained win-x64), tests, portable EXE.
  Used by CI and by developers; see docs/DEVELOPMENT.md.

.PARAMETER Version
  Version stamped into the app and camera component (default 0.1.0; CI passes the tag).

.PARAMETER SkipTests
  Skip tests and the launch check.
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$nativeBuild = Join-Path $artifacts 'native'
$publish = Join-Path $artifacts 'portable'
$payload = Join-Path $artifacts 'camera-component'

function Step($name) { Write-Host "==> $name" -ForegroundColor Cyan }
function Check() { if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" } }

New-Item -ItemType Directory -Path $payload, $publish -Force | Out-Null

Step 'Native: media source and helper DLL (CMake, MSVC x64, static CRT)'
cmake -S (Join-Path $root 'src/CamTurn.MediaSource') -B $nativeBuild -A x64; Check
cmake --build $nativeBuild --config Release; Check
if (-not $SkipTests) {
    ctest --test-dir $nativeBuild -C Release --output-on-failure; Check
}

Step '.NET: build and test'
dotnet build (Join-Path $root 'CamTurn.sln') -c Release -p:Version=$Version; Check
if (-not $SkipTests) {
    # Coverage instrumentation distorts timing, so performance tests run separately without it.
    dotnet test (Join-Path $root 'CamTurn.sln') -c Release --no-build --filter 'Category!=Performance' --collect:"XPlat Code Coverage" --settings (Join-Path $root 'coverlet.runsettings') --results-directory (Join-Path $artifacts 'test-results') --blame-hang-timeout 5m; Check
    dotnet test (Join-Path $root 'tests/CamTurn.Simulator.Tests') -c Release --no-build --filter 'Category=Performance'; Check
}

Step 'Package the optional Windows camera component'
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 is required to package the optional camera component.' }
$nativeDir = Join-Path $nativeBuild 'Release'
& $iscc "/DAppVersion=$Version" "/DNativeDir=$nativeDir" "/DOutputDir=$payload" (Join-Path $root 'installer/camturn.iss'); Check
Copy-Item (Join-Path $nativeDir 'camturn_native.dll') $payload -Force

Step 'Publish portable win-x64 CamTurn.exe'
$componentHash = (Get-FileHash (Join-Path $payload 'CamTurn.CameraComponent.exe') -Algorithm SHA256).Hash
dotnet publish (Join-Path $root 'src/CamTurn.App/CamTurn.App.csproj') -c Release -r win-x64 --self-contained true "-p:CameraPayload=$payload" "-p:CameraComponentHash=$componentHash" "-p:Version=$Version" -p:DebugType=embedded -o $publish; Check
$exe = Join-Path $publish 'CamTurn.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  CamTurn.exe" | Out-File -Encoding ascii -FilePath "$exe.sha256"

if (-not $SkipTests) {
    Step 'Check the portable EXE in isolation'
    $isolated = Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $isolated | Out-Null
    try {
        Copy-Item $exe $isolated
        $out = Join-Path $artifacts 'diagnose.txt'
        $p = Start-Process (Join-Path $isolated 'CamTurn.exe') -WorkingDirectory $isolated -ArgumentList '--diagnose' -Wait -PassThru -NoNewWindow -RedirectStandardOutput $out
        if ($p.ExitCode -ne 0 -or -not (Select-String -Path $out -Pattern 'CamTurn diagnostics' -Quiet)) {
            throw "CamTurn.exe --diagnose failed (exit code $($p.ExitCode))"
        }
    } finally {
        Remove-Item -LiteralPath (Join-Path $isolated 'CamTurn.exe') -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $isolated
    }
}
