<#
.SYNOPSIS
  Builds CamTurn on Windows: native DLLs (CMake + MSVC), .NET app (self-contained win-x64), tests, installer.
  Used by CI and by developers; see docs/DEVELOPMENT.md.

.PARAMETER Version
  Version stamped into the app and installer (default 0.1.0; CI passes the tag).

.PARAMETER SkipTests
  Skip dotnet test and ctest.

.PARAMETER SkipInstaller
  Stop after publishing (no Inno Setup).
#>
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$nativeBuild = Join-Path $artifacts 'native'
$publish = Join-Path $artifacts 'publish'
$installerOut = Join-Path $artifacts 'installer'

function Step($name) { Write-Host "==> $name" -ForegroundColor Cyan }
function Check() { if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" } }

Remove-Item -Recurse -Force $publish, $installerOut -ErrorAction SilentlyContinue

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

Step '.NET: publish app (self-contained, win-x64)'
dotnet publish (Join-Path $root 'src/CamTurn.App/CamTurn.App.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish; Check
Copy-Item (Join-Path $nativeBuild 'Release/camturn_mediasource.dll') $publish
Copy-Item (Join-Path $nativeBuild 'Release/camturn_native.dll') $publish
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') $publish

if ($SkipInstaller) { return }

Step 'Installer (Inno Setup 6)'
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw 'Inno Setup 6 not found. Install it (choco install innosetup) or pass -SkipInstaller.' }
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$installerOut" (Join-Path $root 'installer/camturn.iss'); Check

Get-ChildItem $installerOut -Filter *.exe | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)" | Out-File -Encoding ascii -FilePath "$($_.FullName).sha256"
    Write-Host "$($_.Name)  sha256 $hash"
}
