[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version = "latest",

    [Parameter()]
    [ValidateSet("amd64", "x86", "arm64")]
    [string]$Architecture = $(
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            "X64" { "amd64" }
            "X86" { "x86" }
            "Arm64" { "arm64" }
            default { throw "Unsupported OS architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)" }
        }
    ),

    [Parameter()]
    [string]$DestinationDirectory,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$indexUrl = "https://download.wireguard.com/wireguard-nt/"
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } elseif ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { (Get-Location).Path }

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $scriptRoot "..\runtime"
}

function Resolve-WireGuardNtVersion {
    param(
        [Parameter(Mandatory)]
        [string]$RequestedVersion
    )

    if ($RequestedVersion -ne "latest") {
        return $RequestedVersion
    }

    $response = Invoke-WebRequest -Uri $indexUrl -UseBasicParsing
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $response.Content,
        'wireguard-nt-([0-9]+(?:\.[0-9]+)*)\.zip',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if ($matches.Count -eq 0) {
        throw "Could not determine the latest WireGuardNT version from $indexUrl"
    }

    return $matches |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object { [version]$_ } -Descending |
        Select-Object -First 1
}

function Get-FileVersionSummary {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    [pscustomobject]@{
        FilePath       = $Path
        FileVersion    = $info.FileVersion
        ProductVersion = $info.ProductVersion
    }
}

function Confirm-DownloadedVersion {
    param(
        [Parameter(Mandatory)]
        [string]$DownloadedPath,

        [Parameter(Mandatory)]
        [string]$RequestedVersion
    )

    $summary = Get-FileVersionSummary -Path $DownloadedPath
    if ([string]::IsNullOrWhiteSpace($summary.FileVersion) -and [string]::IsNullOrWhiteSpace($summary.ProductVersion)) {
        throw "Downloaded wireguard.dll does not expose a file version."
    }

    return $summary
}

$resolvedVersion = Resolve-WireGuardNtVersion -RequestedVersion $Version
$downloadUrl = "https://download.wireguard.com/wireguard-nt/wireguard-nt-$resolvedVersion.zip"

$destinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$destinationDllPath = Join-Path $destinationDirectory "wireguard.dll"
if ((-not $Force) -and (Test-Path $destinationDllPath)) {
    $existingVersion = Get-FileVersionSummary -Path $destinationDllPath
    Write-Host "Existing wireguard.dll detected at $destinationDllPath"
    Write-Host "Existing version: FileVersion=$($existingVersion.FileVersion); ProductVersion=$($existingVersion.ProductVersion)"
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mywireguard-wireguardnt-" + [guid]::NewGuid().ToString("N"))
$zipPath = Join-Path $tempRoot "wireguard-nt-$resolvedVersion.zip"
$extractPath = Join-Path $tempRoot "extract"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

    Write-Host "Downloading WireGuardNT SDK $resolvedVersion from $downloadUrl"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing

    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    $sourceDllPath = Join-Path $extractPath "wireguard-nt\bin\$Architecture\wireguard.dll"
    if (-not (Test-Path $sourceDllPath)) {
        throw "wireguard.dll for architecture '$Architecture' was not found in downloaded archive."
    }

    Copy-Item -Path $sourceDllPath -Destination $destinationDllPath -Force

    $versionSummary = Confirm-DownloadedVersion -DownloadedPath $destinationDllPath -RequestedVersion $resolvedVersion

    Write-Host "Downloaded wireguard.dll to $destinationDllPath"
    Write-Host "Archive version: $resolvedVersion"
    Write-Host "FileVersion: $($versionSummary.FileVersion)"
    Write-Host "ProductVersion: $($versionSummary.ProductVersion)"

    if ($PassThru) {
        [pscustomobject]@{
            ArchiveVersion   = $resolvedVersion
            Architecture     = $Architecture
            DestinationPath  = $destinationDllPath
            FileVersion      = $versionSummary.FileVersion
            ProductVersion   = $versionSummary.ProductVersion
            DownloadUrl      = $downloadUrl
        }
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}