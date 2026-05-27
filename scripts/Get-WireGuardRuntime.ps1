[CmdletBinding()]
param(
    [Parameter()]
    [string]$WireGuardNtVersion = "latest",

    [Parameter()]
    [string]$WireGuardWindowsRef = "master",

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
    [string]$TunnelDllUrl,

    [Parameter()]
    [switch]$SkipTunnelBuild,

    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$PassThru
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } elseif ($PSCommandPath) { Split-Path -Parent $PSCommandPath } else { (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $scriptRoot "..\runtime"
}

$destinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$destinationTunnelDllPath = Join-Path $destinationDirectory "tunnel.dll"

function Get-FileVersionSummary {
    param([Parameter(Mandatory)][string]$Path)

    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    [pscustomobject]@{
        FilePath       = $Path
        FileVersion    = $info.FileVersion
        ProductVersion = $info.ProductVersion
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash
}

function Invoke-CmdOrThrow {
    param(
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$CommandLine
    )

    Push-Location $WorkingDirectory
    try {
        & cmd.exe /c $CommandLine
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed with exit code ${LASTEXITCODE}: $CommandLine"
        }
    }
    finally {
        Pop-Location
    }
}

function Save-TunnelDllFromUrl {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutputPath
    )

    Write-Host "Downloading tunnel.dll from $Url"
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $OutputPath
}

function Build-TunnelDllFromSource {
    param(
        [Parameter(Mandatory)][string]$Ref,
        [Parameter(Mandatory)][string]$Arch,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mywireguard-tunnel-build-" + [guid]::NewGuid().ToString("N"))
    $sourceZipPath = Join-Path $tempRoot "wireguard-windows-$Ref.zip"
    $extractPath = Join-Path $tempRoot "extract"

    try {
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

        $sourceUrl = "https://codeload.github.com/WireGuard/wireguard-windows/zip/refs/heads/$Ref"
        Write-Host "Downloading wireguard-windows source from $sourceUrl"
        Invoke-WebRequest -UseBasicParsing -Uri $sourceUrl -OutFile $sourceZipPath

        Expand-Archive -Path $sourceZipPath -DestinationPath $extractPath -Force

        $sourceRoot = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
        if ($null -eq $sourceRoot) {
            throw "Failed to locate extracted wireguard-windows source directory."
        }

        $embeddableDir = Join-Path $sourceRoot.FullName "embeddable-dll-service"
        if (-not (Test-Path $embeddableDir)) {
            throw "embeddable-dll-service directory was not found in downloaded source."
        }

        Write-Host "Building tunnel.dll from upstream source. This may take several minutes."
        Invoke-CmdOrThrow -WorkingDirectory $embeddableDir -CommandLine "build.bat"

        $builtDllPath = Join-Path $embeddableDir "$Arch\tunnel.dll"
        if (-not (Test-Path $builtDllPath)) {
            throw "Build completed but $builtDllPath was not found."
        }

        Copy-Item -Path $builtDllPath -Destination $OutputPath -Force
    }
    finally {
        if (Test-Path $tempRoot) {
            Remove-Item -Path $tempRoot -Recurse -Force
        }
    }
}

New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$wireguardScript = Join-Path $scriptRoot "Get-WireGuardDll.ps1"
if (-not (Test-Path $wireguardScript)) {
    throw "Expected companion script was not found: $wireguardScript"
}

Write-Host "Ensuring wireguard.dll is present"
$wireguardResult = & $wireguardScript -Version $WireGuardNtVersion -Architecture $Architecture -DestinationDirectory $destinationDirectory -Force:$Force -PassThru

$tunnelBuilt = $false
if ((-not $Force) -and (Test-Path $destinationTunnelDllPath)) {
    Write-Host "Existing tunnel.dll detected at $destinationTunnelDllPath"
}
else {
    if (-not [string]::IsNullOrWhiteSpace($TunnelDllUrl)) {
        Save-TunnelDllFromUrl -Url $TunnelDllUrl -OutputPath $destinationTunnelDllPath
        $tunnelBuilt = $false
    }
    elseif (-not $SkipTunnelBuild) {
        Build-TunnelDllFromSource -Ref $WireGuardWindowsRef -Arch $Architecture -OutputPath $destinationTunnelDllPath
        $tunnelBuilt = $true
    }
    else {
        throw "tunnel.dll is missing, no TunnelDllUrl was provided, and -SkipTunnelBuild was specified."
    }
}

if (-not (Test-Path $destinationTunnelDllPath)) {
    throw "tunnel.dll is still missing after runtime acquisition."
}

$wireguardVersion = Get-FileVersionSummary -Path $wireguardResult.DestinationPath
$tunnelHash = Get-Sha256 -Path $destinationTunnelDllPath

Write-Host "Runtime ready in $destinationDirectory"
Write-Host "wireguard.dll version: $($wireguardVersion.FileVersion)"
Write-Host "tunnel.dll sha256: $tunnelHash"
if ($tunnelBuilt) {
    Write-Host "tunnel.dll source: built from upstream wireguard-windows/$WireGuardWindowsRef"
}
elseif (-not [string]::IsNullOrWhiteSpace($TunnelDllUrl)) {
    Write-Host "tunnel.dll source: $TunnelDllUrl"
}

if ($PassThru) {
    [pscustomobject]@{
        DestinationDirectory = $destinationDirectory
        Architecture        = $Architecture
        WireGuardDllPath    = $wireguardResult.DestinationPath
        WireGuardVersion    = $wireguardVersion.FileVersion
        TunnelDllPath       = $destinationTunnelDllPath
        TunnelDllSha256     = $tunnelHash
        TunnelBuilt         = $tunnelBuilt
        WireGuardWindowsRef = $WireGuardWindowsRef
        TunnelDllUrl        = $TunnelDllUrl
    }
}