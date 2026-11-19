# bump-version.ps1
# Raises the package version in Package.appxmanifest and the csproj.
# Committing, tagging (vX.Y.Z) and pushing are left to you.
#
# Usage (from anywhere in the repo):
#   .\scripts\bump-version.ps1           # patch: 1.0.0 -> 1.0.1
#   .\scripts\bump-version.ps1 minor     # 1.0.1 -> 1.1.0
#   .\scripts\bump-version.ps1 major     # 1.1.0 -> 2.0.0
#   .\scripts\bump-version.ps1 1.2.3     # explicit version
#
# The MSIX version has four parts and the Store reserves the fourth, so
# release 1.0.1 is written as 1.0.1.0 in both files.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Bump = 'patch'
)

$ErrorActionPreference = 'Stop'

function Update-File {
    param([string]$Path, [string]$Pattern, [string]$Replacement)
    $text = [IO.File]::ReadAllText($Path)
    $regex = [regex]$Pattern
    if ($regex.Matches($text).Count -ne 1) { throw "Expected exactly one version match in $Path." }
    [IO.File]::WriteAllText($Path, $regex.Replace($text, $Replacement), (New-Object Text.UTF8Encoding $false))
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $repoRoot 'DockerExtension/Package.appxmanifest'
$csproj   = Join-Path $repoRoot 'DockerExtension/DockerExtension.csproj'
$manifestPattern = '(<Identity\b[^>]*?\bVersion=")(\d+\.\d+\.\d+)\.0(")'
$csprojPattern   = '(<AppxPackageVersion>)(\d+\.\d+\.\d+)\.0(</AppxPackageVersion>)'

$match = [regex]::Match([IO.File]::ReadAllText($manifest), $manifestPattern)
if (-not $match.Success) { throw "Could not read a X.Y.Z.0 version from $manifest." }
$current = [version]$match.Groups[2].Value

$csprojMatch = [regex]::Match([IO.File]::ReadAllText($csproj), $csprojPattern)
if (-not $csprojMatch.Success -or [version]$csprojMatch.Groups[2].Value -ne $current) {
    throw "$csproj AppxPackageVersion does not match the manifest version $current.0."
}

switch ($Bump) {
    'major' { $new = [version]::new($current.Major + 1, 0, 0) }
    'minor' { $new = [version]::new($current.Major, $current.Minor + 1, 0) }
    'patch' { $new = [version]::new($current.Major, $current.Minor, $current.Build + 1) }
    default {
        if ($Bump -notmatch '^\d+\.\d+\.\d+$') { throw "Use major, minor, patch, or a version like 1.2.3 (got '$Bump')." }
        $new = [version]$Bump
    }
}

if ($new -le $current) { throw "New version $new must be higher than the current $current (the Store rejects lower ones)." }

Update-File $manifest $manifestPattern "`${1}$new.0`${3}"
Update-File $csproj   $csprojPattern   "`${1}$new.0`${3}"
Write-Host "Version $current -> $new (package $new.0). Tag it v$new when you release." -ForegroundColor Green
