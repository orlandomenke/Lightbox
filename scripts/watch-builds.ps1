<#
.SYNOPSIS
  Auto-unblock every file that lands in a folder (and its subfolders), so
  zips or builds copied/moved there lose their Mark-of-the-Web immediately.
  No admin rights needed.

.DESCRIPTION
  Watches the folder with a FileSystemWatcher; each created or renamed file
  is unblocked (with retries while the copy is still in progress). Keep the
  window open, or start it hidden at logon by placing a shortcut in
  shell:startup (Win+R → shell:startup) with the target:

    powershell -WindowStyle Hidden -ExecutionPolicy Bypass -File "<path>\watch-builds.ps1" -Folder "<your Builds folder>"

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File watch-builds.ps1 -Folder C:\Users\me\source\repos\AIAnimationFramework\Builds
#>
param(
    # Folder to watch. Default: the folder this script is in.
    [string]$Folder = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path $Folder).Path

# Clear anything already sitting there, then watch for new arrivals.
Get-ChildItem -Path $resolved -Recurse -File -ErrorAction SilentlyContinue | Unblock-File

$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $resolved
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

$unblock = {
    $path = $Event.SourceEventArgs.FullPath
    # Retry while the copy/download is still writing the file.
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Seconds 1
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
        try {
            Unblock-File -LiteralPath $path -ErrorAction Stop
            return
        } catch {
            # locked mid-copy — try again
        }
    }
}

Register-ObjectEvent -InputObject $watcher -EventName Created -Action $unblock | Out-Null
Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $unblock | Out-Null

Write-Host "Auto-unblocking new files under $resolved  (Ctrl+C to stop)"
while ($true) { Start-Sleep -Seconds 3600 }
