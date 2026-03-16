[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [ValidateSet("true", "false")]
    [string]$SelfContained = "true",
    [string]$Project = "src/DentalID.Desktop/DentalID.Desktop.csproj",
    [string]$PublishDir = "publish/win-x64",
    [string]$OutputDir = "publish",
    [string]$NsisScript = "installer/nsis/DentalID.Setup.nsi",
    [string]$MakensisPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-VersionInfo {
    param([Parameter(Mandatory = $true)][string]$RawVersion)

    $normalized = $RawVersion.Trim()
    if ($normalized.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    if ($normalized -notmatch "^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<suffix>[-+].*)?$") {
        throw "Version '$RawVersion' is invalid. Use SemVer style like 1.0.0 or v1.0.0."
    }

    $fileVersion = "{0}.{1}.{2}.0" -f $matches.major, $matches.minor, $matches.patch
    return [PSCustomObject]@{
        ProductVersion = $normalized
        FileVersion = $fileVersion
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    $versionInfo = Resolve-VersionInfo -RawVersion $Version

    $publishDirFull = Join-Path $repoRoot $PublishDir
    $outputDirFull = Join-Path $repoRoot $OutputDir
    $installerFile = Join-Path $outputDirFull ("DentalID-Setup-{0}.exe" -f $versionInfo.ProductVersion)

    if (Test-Path $publishDirFull) {
        Remove-Item -Path $publishDirFull -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishDirFull -Force | Out-Null
    New-Item -ItemType Directory -Path $outputDirFull -Force | Out-Null

    Write-Host ">> Publishing desktop app ($Configuration, $Runtime, self-contained=$SelfContained)..."
    $publishArgs = @(
        "publish", $Project,
        "--configuration", $Configuration,
        "--runtime", $Runtime,
        "--self-contained", $SelfContained,
        "--output", $publishDirFull,
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:PublishTrimmed=false"
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $appExe = Join-Path $publishDirFull "DentalID.Desktop.exe"
    if (-not (Test-Path $appExe)) {
        throw "Publish output does not contain '$appExe'."
    }

    $nsisScriptFull = Join-Path $repoRoot $NsisScript
    if (-not (Test-Path $nsisScriptFull)) {
        throw "NSIS script not found: $nsisScriptFull"
    }

    $makensis = $MakensisPath
    if ([string]::IsNullOrWhiteSpace($makensis)) {
        $cmd = Get-Command "makensis" -ErrorAction SilentlyContinue
        if ($null -eq $cmd) {
            throw "makensis.exe was not found in PATH. Install NSIS 3.x or pass -MakensisPath."
        }
        $makensis = $cmd.Source
    }

    Write-Host ">> Building NSIS installer..."
    $nsisArgs = @(
        "/DAPP_VERSION=$($versionInfo.ProductVersion)",
        "/DAPP_VERSION_FILE=$($versionInfo.FileVersion)",
        "/DAPP_PUBLISH_DIR=$publishDirFull",
        "/DOUTPUT_FILE=$installerFile",
        $nsisScriptFull
    )

    & $makensis @nsisArgs
    if ($LASTEXITCODE -ne 0) {
        throw "makensis failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $installerFile)) {
        throw "Installer build completed but output file was not found: $installerFile"
    }

    Write-Host ">> Installer ready: $installerFile"
}
finally {
    Pop-Location
}
