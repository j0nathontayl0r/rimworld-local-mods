#!/usr/bin/env bash
#
# Sync this mod repo, then launch RimWorld. Linux half of the pair; see sync-and-play.ps1.
#
# The three local mods are symlinked into RimWorld's Mods/ folder, so a git pull changes what the
# game loads the instant it completes. That is only safe while RimWorld is closed, which is exactly
# what a launcher wrapper can guarantee - hence syncing here rather than from a cron job.
#
# Refuses to do anything clever. Every failure stops and explains itself:
#   - RimWorld already running   -> a pull would swap files under a live game
#   - dirty working tree         -> pulling could conflict; you decide what to do with the changes
#   - diverged history           -> needs a human (see the 2026-08-14 episode in git log)
#   - unpushed commits           -> warns, because the other machine will not see them
#
# Pulls with --ff-only on purpose. The repo commits its compiled DLLs, so a merge is never the right
# answer - two machines building identical source produce different bytes and git cannot reconcile
# them. Failing loudly beats producing a corrupt assembly.
#
# Usage:
#   ./scripts/sync-and-play.sh                 # sync, then launch via Steam
#   ./scripts/sync-and-play.sh --launcher none # sync only
#   ./scripts/sync-and-play.sh --no-pull       # dry run

set -uo pipefail

LAUNCHER="steam"
NO_PULL=0
while [ $# -gt 0 ]; do
    case "$1" in
        --launcher) LAUNCHER="${2:-steam}"; shift 2 ;;
        --no-pull)  NO_PULL=1; shift ;;
        -h|--help)  sed -n '2,25p' "$0"; exit 0 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

# Repo root is derived from this script's own location, so the script needs no per-machine config
# and works from any checkout path on either OS.
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

red()   { printf '  ABORT: %s\n' "$1" >&2; exit 1; }
warn()  { printf '  WARN:  %s\n' "$1"; }
ok()    { printf '  ok:    %s\n' "$1"; }

printf '\nrimworld-local-mods sync\nrepo: %s\n\n' "$REPO"

# 1. RimWorld must be closed - the symlinks make a pull hot-swap the mod files. Only enforced when
#    a pull could actually happen; with --no-pull nothing is written, so reporting is harmless.
if pgrep -f 'RimWorldLinux|RimWorldWin64' >/dev/null 2>&1; then
    if [ "$NO_PULL" -eq 0 ]; then
        red "RimWorld is running. Close it first - pulling would swap mod files under the live game."
    fi
    warn "RimWorld is running (report-only mode, nothing will be written)"
else
    ok "RimWorld not running"
fi

# 2. Working tree must be clean, or a pull may conflict.
DIRTY="$(git -C "$REPO" status --porcelain)"
if [ -n "$DIRTY" ]; then
    printf '  uncommitted changes:\n'
    printf '      %s\n' "$DIRTY"
    red "Working tree is dirty. Commit, stash or discard before syncing."
fi
ok "working tree clean"

# 3. Learn the real remote state. git status alone cannot do this - it compares against the local
#    origin/main ref, which happily reports 'in sync' while being days behind.
if ! git -C "$REPO" fetch --quiet origin; then
    red "git fetch failed - check network or credentials."
fi

AHEAD="$(git -C "$REPO" rev-list --count origin/main..HEAD)"
BEHIND="$(git -C "$REPO" rev-list --count HEAD..origin/main)"

if [ "$AHEAD" -gt 0 ] && [ "$BEHIND" -gt 0 ]; then
    printf '  local ahead %s, behind %s\n' "$AHEAD" "$BEHIND"
    git -C "$REPO" log --oneline origin/main..HEAD | sed 's/^/      local:  /'
    git -C "$REPO" log --oneline HEAD..origin/main | sed 's/^/      remote: /'
    red "History has diverged. Resolve by hand (rebase onto origin/main), then re-run."
fi

# 4. Fast-forward if there is anything to take.
if [ "$BEHIND" -gt 0 ]; then
    if [ "$NO_PULL" -eq 1 ]; then
        warn "$BEHIND commit(s) available; --no-pull set, not pulling"
    else
        if ! git -C "$REPO" pull --ff-only --quiet origin main; then
            red "Fast-forward pull failed."
        fi
        ok "pulled $BEHIND commit(s)"
        git -C "$REPO" log --oneline -"$BEHIND" | sed 's/^/      /'
    fi
else
    ok "already up to date"
fi

# 5. Unpushed work will be invisible to the other machine - the failure mode that cost four days.
if [ "$AHEAD" -gt 0 ]; then
    warn "$AHEAD unpushed commit(s) - the other machine will NOT see these until you push:"
    git -C "$REPO" log --oneline origin/main..HEAD | sed 's/^/      /'
fi

# 6. Confirm the symlinks still resolve. A RimWorld file-verify removes them, and the game would
#    then start with the local mods simply missing rather than erroring.
#    Adjust MODS_DIR if the Linux install lives elsewhere.
MODS_DIR="${RIMWORLD_MODS_DIR:-/mnt/games/SteamLibrary/steamapps/common/RimWorld/Mods}"
if [ -d "$MODS_DIR" ]; then
    for m in LocalPatches LocalCSharpFixes LocalEarlyFixes; do
        if [ ! -e "$MODS_DIR/$m" ]; then
            warn "$m is MISSING from Mods/ - recreate with: ln -s '$REPO/$m' '$MODS_DIR/$m'"
        elif [ ! -L "$MODS_DIR/$m" ]; then
            warn "$m in Mods/ is a real directory, not a symlink - it will not track the repo"
        fi
    done
else
    warn "Mods dir not found at $MODS_DIR - set RIMWORLD_MODS_DIR to check link health"
fi

# 7. Launch.
case "$LAUNCHER" in
    steam)
        printf '\nlaunching via Steam...\n'
        if command -v steam >/dev/null 2>&1; then
            steam steam://rungameid/294100 >/dev/null 2>&1 &
        else
            xdg-open steam://rungameid/294100 >/dev/null 2>&1 &
        fi
        ;;
    rimsort)
        printf '\nlaunching RimSort...\n'
        if command -v rimsort >/dev/null 2>&1; then rimsort >/dev/null 2>&1 &
        elif command -v RimSort >/dev/null 2>&1; then RimSort >/dev/null 2>&1 &
        else red "RimSort not found on PATH."; fi
        ;;
    none) printf '\nsync only, not launching.\n' ;;
    *) red "unknown launcher: $LAUNCHER (expected steam|rimsort|none)" ;;
esac
