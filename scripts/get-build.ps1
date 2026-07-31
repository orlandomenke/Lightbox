<#
.SYNOPSIS
  Unpack a downloaded Lightbox CI artifact without Mark-of-the-Web, so
  SmartScreen never triggers. No admin rights needed.

.DESCRIPTION
  Takes the newest Lightbox-win-x64-*.zip from your Downloads folder (or the
  zip you pass with -Zip), unblocks it, and extracts it with tar — which
  writes no Zone.Identifier download tags — into a folder named after the
  zip (so the build kind / branch / commit stays visible). Any tags that
  slip through are stripped afterwards as a belt-and-braces pass.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File get-build.ps1
  # newest artifact zip from Downloads → .\Lightbox-win-x64-<kind>-<branch>-<sha>\

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File get-build.ps1 -Zip C:\tmp\Lightbox-win-x64-release-main-abc1234.zip -Dest D:\Builds
#>
param(
    # The artifact zip. Default: newest Lightbox-win-x64-*.zip in Downloads.
    [string]$Zip,

    # Folder the build is extracted into. Default: the folder this script is in.
    [string]$Dest = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not $Zip) {
    $candidate = Get-ChildItem -Path (Join-Path $env:USERPROFILE 'Downloads') -Filter 'Lightbox-win-x64-*.zip' -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $candidate) {
        throw 'No Lightbox-win-x64-*.zip found in Downloads. Pass the zip explicitly with -Zip <path>.'
    }
    $Zip = $candidate.FullName
}

$name = [IO.Path]::GetFileNameWithoutExtension($Zip)
$target = Join-Path $Dest $name

Write-Host "Zip:    $Zip"
Write-Host "Target: $target"

Unblock-File -LiteralPath $Zip
New-Item -ItemType Directory -Force -Path $target | Out-Null
tar -xf $Zip -C $target
Get-ChildItem -Path $target -Recurse -File | Unblock-File

Write-Host ''
Write-Host 'Build ready:' -ForegroundColor Green
Write-Host "  $(Join-Path $target 'Lightbox.App.exe')"
