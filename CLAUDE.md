See README.md for the mods, build, deployment and machine-sync workflow.

## Keep this file current
Update this file in the same commit as any change that alters how the repo is worked on: a new mod
or build target, a new script, a changed path, or a rule learned the hard way. Keep it to rules and
gotchas; descriptions of the mods belong in README.md. If an entry here turns out to be wrong,
fix or delete it rather than adding a contradicting one.

## Committing
This repo is public. Every commit must be authored as `Jonathon Taylor <github@jdtay.com>`, never
`jonathon@jdtay.com`. Check `git config user.email` before committing; if it isn't
`github@jdtay.com`, run `git config user.email github@jdtay.com` first, and don't run the commit in
the same command as the check. Never push from a clone whose history predates 2026-10-16 (it still
holds the old email); reset it to `origin/main` instead. One fix per commit; push only when asked.

## RimWorld 1.7
- 1.7 runs on the Waylith engine on .NET 10 (CoreCLR, not Mono). The binary is plain `RimWorld`
  (`RimWorld.exe` on Windows), not `RimWorldLinux` / `RimWorldWin64`.
- The Linux install is 1.7-only, with no `RimWorldLinux_Data/Managed`, so the net472 (1.6)
  projects can't build there. Build the `Source/v1.7/*.csproj` projects, which write
  `1.7/Assemblies/`. Each C# mod's `LoadFolders.xml` loads exactly one DLL per game version.
- Harmony on 1.7 is 3.x and checks patch parameter types. Patches whose types were wrong but
  tolerated by Harmony 2 now throw, and one bad class aborts a mod's whole `PatchAll`.
- Workshop mods without a `1.7/` folder fall back to their 1.6 build. Treat their DLLs as untested
  on 1.7.

## Triage
- The live log is `<config>/Logs/Player.log`, not `Player.log` in the config root. A huge log
  usually means a per-frame exception loop; find the first error, not the most frequent one.
- Verse stops logging after "Reached max messages limit", so later errors can be invisible.
- Check where a fix has to live by log line order: anything that fires before
  "[LocalCSharpFixes] Harmony patches applied." needs LocalEarlyFixes (Mod-constructor patching).
- Verify packageIds from the target's own About.xml. Several are misleading (e.g. The Generic Ammo
  Experience is `Rosalina.Mating.Press`), and some DLLs ship inside unrelated mods (Enlist.dll is
  in `[RH2] Faction: V.O.I.D.`).
- When a fix targets a new mod, add its packageId to that local mod's About.xml `loadAfter` (or
  `loadBefore` for LocalEarlyFixes).

## Local mods and links
- Every local mod is linked into the game's `Mods/` folder (symlinks on Linux, junctions on
  Windows). When adding a local mod, add it to the link list in `scripts/sync-and-play.sh` and the
  Deployment section of README.md.
- `LocalVisualExceptions` shares its packageId with workshop 2538411704. Two copies in `Mods/` with
  the same packageId make RimWorld pick one at random, so remove stray SteamCMD/RimSort downloads
  of it from `Mods/`.
- `scripts/sync-and-play.sh` is the single launcher for both OSes (Git Bash on Windows); there is
  no PowerShell version any more.
- After changing the active mod list, run `./scripts/export-modlist.py` and commit the new
  `modlists/` snapshot.
