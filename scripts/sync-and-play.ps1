<#
.SYNOPSIS
    Sync this mod repo, then launch RimWorld. Windows half of the pair; see sync-and-play.sh.

.DESCRIPTION
    The three local mods are junctioned into RimWorld's Mods/ folder, so a git pull changes what the
    game loads the instant it completes. That is only safe while RimWorld is closed, which is exactly
    what a launcher wrapper can guarantee - hence syncing here rather than from a scheduled task.

    Refuses to do anything clever. Every failure stops and explains itself:
      - RimWorld already running   -> a pull would swap files under a live game
      - dirty working tree         -> pulling could conflict; you decide what to do with the changes
      - diverged history           -> needs a human (see the 2026-08-14 episode in git log)
      - unpushed commits           -> warns, because the other machine will not see them

    Pulls with --ff-only on purpose. The repo commits its compiled DLLs, so a merge is never the
    right answer - two machines building identical source produce different bytes and git cannot
    reconcile them. Failing loudly beats producing a corrupt assembly.

.PARAMETER Launcher
    steam   (default) - hands off to Steam, preserving the publisher launch args
    rimsort           - launches RimSort instead; set RimSortPath or have it on PATH
    none              - sync only, do not launch

.PARAMETER NoPull
    Check and report, but do not actually pull. Useful for a dry run.

.EXAMPLE
    .\scripts\sync-and-play.ps1
    .\scripts\sync-and-play.ps1 -Launcher none -NoPull
#>
[CmdletBinding()]
param(
    [ValidateSet('steam', 'rimsort', 'none')]
    [string]$Launcher = 'steam',
    [string]$RimSortPath,
    [switch]$NoPull
)

$ErrorActionPreference = 'Stop'

# Repo root is derived from this script's own location, so the script needs no per-machine config
# and works from any checkout path on either OS.
$repo = Split-Path -Parent $PSScriptRoot

function Fail($msg) { Write-Host "  ABORT: $msg" -ForegroundColor Red; exit 1 }
function Warn($msg) { Write-Host "  WARN:  $msg" -ForegroundColor Yellow }
function Ok($msg)   { Write-Host "  ok:    $msg" -ForegroundColor Green }

Write-Host "`nrimworld-local-mods sync" -ForegroundColor Cyan
Write-Host "repo: $repo`n"

# 1. RimWorld must be closed - the junctions make a pull hot-swap the mod files. Only enforced when
#    a pull could actually happen; with -NoPull nothing is written, so reporting is harmless.
$rimworldRunning = [bool](Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue)
if ($rimworldRunning -and -not $NoPull) {
    Fail "RimWorld is running. Close it first - pulling would swap mod files under the live game."
}
if ($rimworldRunning) { Warn "RimWorld is running (report-only mode, nothing will be written)" }
else { Ok "RimWorld not running" }

# 2. Working tree must be clean, or a pull may conflict.
$dirty = & git -C $repo status --porcelain
if ($dirty) {
    Write-Host "  uncommitted changes:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "      $_" }
    Fail "Working tree is dirty. Commit, stash or discard before syncing."
}
Ok "working tree clean"

# 3. Learn the real remote state. git status alone cannot do this - it compares against the local
#    origin/main ref, which happily reports 'in sync' while being days behind.
& git -C $repo fetch --quiet origin
if ($LASTEXITCODE -ne 0) { Fail "git fetch failed - check network or credentials." }

$ahead  = [int](& git -C $repo rev-list --count origin/main..HEAD)
$behind = [int](& git -C $repo rev-list --count HEAD..origin/main)

if ($ahead -gt 0 -and $behind -gt 0) {
    Write-Host "  local ahead $ahead, behind $behind" -ForegroundColor Red
    & git -C $repo log --oneline origin/main..HEAD | ForEach-Object { Write-Host "      local:  $_" }
    & git -C $repo log --oneline HEAD..origin/main | ForEach-Object { Write-Host "      remote: $_" }
    Fail "History has diverged. Resolve by hand (rebase onto origin/main), then re-run."
}

# 4. Fast-forward if there is anything to take.
if ($behind -gt 0) {
    if ($NoPull) {
        Warn "$behind commit(s) available; -NoPull set, not pulling"
    } else {
        & git -C $repo pull --ff-only --quiet origin main
        if ($LASTEXITCODE -ne 0) { Fail "Fast-forward pull failed." }
        Ok "pulled $behind commit(s)"
        & git -C $repo log --oneline -$behind | ForEach-Object { Write-Host "      $_" }
    }
} else {
    Ok "already up to date"
}

# 5. Unpushed work will be invisible to the other machine - the failure mode that cost four days.
if ($ahead -gt 0) {
    Warn "$ahead unpushed commit(s) - the other machine will NOT see these until you push:"
    & git -C $repo log --oneline origin/main..HEAD | ForEach-Object { Write-Host "      $_" }
}

# 6. Confirm the junctions still resolve. A RimWorld file-verify deletes them, and the game would
#    then start with the local mods simply missing rather than erroring.
$modsDir = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods"
foreach ($m in 'LocalPatches', 'LocalCSharpFixes', 'LocalEarlyFixes', 'LocalVisualExceptions') {
    $p = Join-Path $modsDir $m
    $item = Get-Item $p -ErrorAction SilentlyContinue
    if (-not $item)            { Warn "$m is MISSING from Mods/ - recreate with: mklink /J `"$p`" `"$repo\$m`"" }
    elseif (-not $item.LinkType) { Warn "$m in Mods/ is a real directory, not a junction - it will not track the repo" }
}

# 7. Launch.
switch ($Launcher) {
    'steam' {
        Write-Host "`nlaunching via Steam..." -ForegroundColor Cyan
        Start-Process 'steam://rungameid/294100'
    }
    'rimsort' {
        $rs = if ($RimSortPath) { $RimSortPath } else { (Get-Command RimSort -ErrorAction SilentlyContinue).Source }
        if (-not $rs) { Fail "RimSort not found. Pass -RimSortPath 'C:\path\to\RimSort.exe'." }
        Write-Host "`nlaunching RimSort..." -ForegroundColor Cyan
        Start-Process $rs
    }
    'none' { Write-Host "`nsync only, not launching." -ForegroundColor Cyan }
}
