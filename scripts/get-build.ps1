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

  It then repoints a fixed-name junction — .\Lightbox by default — at the
  build just extracted, so there is one stable path that always means "the
  newest build". That exists for anything holding a path across builds, and
  the case that prompted it is the Claude Desktop MCP config: its `command`
  is a string typed once, a wrong one fails *silently* (the server never
  starts and the tools are simply absent), and a folder named after the
  commit guarantees it is wrong by the next build. Point it at
  <Dest>\Lightbox\mcp\Lightbox.Mcp.exe once and it stays true.

  A junction rather than a copy or a rename, because both halves are wanted:
  every build stays on disk under its own name, tellable apart when
  something regresses, and one name still resolves to the newest. Directory
  junctions need no admin rights and no Developer Mode — unlike symlinks,
  which is the whole reason this is a junction.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File get-build.ps1
  # newest artifact zip from Downloads → .\Lightbox-win-x64-<kind>-<branch>-<sha>\
  #                                      .\Lightbox → that folder

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File get-build.ps1 -Zip C:\tmp\Lightbox-win-x64-release-main-abc1234.zip -Dest D:\Builds

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File get-build.ps1 -LinkName ''
  # extract only, leave no junction
#>
param(
    # The artifact zip. Default: newest Lightbox-win-x64-*.zip in Downloads.
    [string]$Zip,

    # Folder the build is extracted into. Default: the folder this script is in.
    [string]$Dest = $PSScriptRoot,

    # Fixed-name junction under $Dest, repointed at the build just extracted.
    # Pass '' to skip creating one.
    [string]$LinkName = 'Lightbox'
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

# ---- the fixed name that always means "the newest build" --------------------

$link = $null
if ($LinkName) {
    $candidateLink = Join-Path $Dest $LinkName

    # A zip actually called Lightbox.zip would make these the same folder, and
    # a junction to itself is not a thing worth finding out about later.
    if ([IO.Path]::GetFullPath($candidateLink) -eq [IO.Path]::GetFullPath($target)) {
        Write-Host "Extracted folder is already named '$LinkName' — no junction needed."
    }
    else {
        $existing = Get-Item -LiteralPath $candidateLink -Force -ErrorAction SilentlyContinue
        $isJunction = $existing -and $existing.PSIsContainer -and
                      (($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)

        if ($existing -and -not $isJunction) {
            # Something real is sitting on the name — an old extract from before
            # this script grew junctions, or a folder somebody put there. Deleting
            # it would take a build (or worse) with it, so refuse and say so. The
            # extraction above already succeeded; only the convenience is skipped.
            Write-Warning "'$candidateLink' exists and is not a junction — leaving it alone."
            Write-Warning "Move or delete it, then re-run, to get the fixed-name link."
        }
        else {
            # Delete the *link*, never what it points at. Directory.Delete with
            # recursive:$false removes a reparse point without following it,
            # which is the property that matters here.
            if ($isJunction) { [IO.Directory]::Delete($candidateLink, $false) }

            # Every attempt below is judged by whether the link is *there*
            # afterwards, never by whether the call threw. New-Item -ItemType
            # Junction returns $null and raises nothing at all on a platform
            # that has no junctions, so trusting the absence of an exception
            # reports a link that was never made — the same silent-success this
            # whole junction exists to keep out of the MCP config.
            $why = $null
            try {
                # -Value, not -Target: -Target is an alias that arrived later, and
                # this script has to run under the Windows PowerShell 5.1 that is
                # already on the machine.
                New-Item -ItemType Junction -Path $candidateLink -Value $target -ErrorAction Stop | Out-Null
            }
            catch { $why = $_.Exception.Message }

            if (-not (Test-Path -LiteralPath $candidateLink)) {
                # Junctions are refused on a few filesystems (notably FAT32 and
                # some network shares). mklink fails for the same reasons, but it
                # is a different code path and costs one line to try.
                try { cmd /c mklink /J "$candidateLink" "$target" 2>&1 | Out-Null } catch { }
            }

            if (Test-Path -LiteralPath $candidateLink) {
                $link = $candidateLink
            }
            else {
                Write-Warning "Could not create the '$LinkName' junction$(if ($why) { ": $why" })."
                Write-Warning 'The build itself is fine — use the full path below.'
            }
        }
    }
}

Write-Host ''
Write-Host 'Build ready:' -ForegroundColor Green
Write-Host "  $(Join-Path $target 'Lightbox.App.exe')"
if ($link) {
    Write-Host ''
    Write-Host "  $LinkName -> $(Split-Path -Leaf $target)"
    # Printed in full because it is meant to be pasted into
    # claude_desktop_config.json, where a wrong path fails without a word.
    Write-Host "  Claude Desktop command: $(Join-Path $link 'mcp\Lightbox.Mcp.exe')"
}
