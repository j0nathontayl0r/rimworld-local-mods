# QA Report: Local Replace Stuff

Spec: `specs/local-replace-stuff.md`
Plan: `plans/local-replace-stuff.md` (the "Amendment" section overrides the base choice)
Commit / diff under test: working tree on `main` at `023eda9` (uncommitted: new `LocalReplaceStuff/`,
edits to `README.md`, `CLAUDE.md`, `scripts/sync-and-play.sh`). Round 1: DLL `b484317e…3547d`.
Round 2: DLL `4a2a1a3fadba8955bbfa72517b85b0850f4af93e110f8df8ff0f25295746125a`.

## Summary
Offline: PASS (re-test round 2, DLL sha256 `4a2a1a3f…5746125a`). D1, D2 and D3 are fixed and
verified; see "Re-test (round 2)". Criteria 20–27: PENDING USER (in-game).

Round 1 result (kept below for the record): PARTIAL, with D1, D2 (low) and D3 (low, decision).

## Re-test (round 2)

Changes under test (developer, after QA D1–D3 and review R1–R5): cache-hit validity test in
`NewThingFrame.cs:281-296`; `LoadFolders.xml` marker; README lines 82, 100–112, 128–150, 224;
`Source/Properties/launchSettings.json` deleted. QA re-ran every check itself.

### Commands and results
| Check | Result |
| --- | --- |
| `dotnet build LocalReplaceStuff/Source/Replace_Stuff.csproj --no-incremental` (in place) | exit 0, 0 warnings, 0 errors; DLL sha256 `4a2a1a3f…5746125a` (matches the developer's) |
| Same build from a fresh copy of the untracked files (no `obj/`, DLL deleted) | exit 0, 0 errors; `ilspycmd -il` identical to the committed DLL; `1.7/Assemblies/` holds only `Replace_Stuff.dll` |
| QA's own harness build (source unchanged since round 1) on the new DLL | 76 patch classes, 24 transpiler applications, 0 problems (exit 0) |
| Base still equals the live Workshop folder (`diff -rq`) | yes |
| `diffcheck.py base/Source LocalReplaceStuff/Source` | 16 hunks, all marked; 1 problem = `Only in base: Properties` (the R3 deletion). With `--allow-drop Properties`: 0 problems |
| `find … -name '*.dll' -o -name '*.pdb' -o -name '*.deps.json'` (no `obj/`) | one line: `1.7/Assemblies/Replace_Stuff.dll` |
| `1.1`–`1.6` folders | none |
| Every `.xml` in the mod parses (`xml.etree`) | yes |
| Old packageIds (`/usr/bin/grep -rni`) | only `About.xml:26-27` (`incompatibleWith`) and `About.xml:22` (reason comment) |
| New tick methods | none (`TickScheduler.GameComponentTick` and the commented `TickRare` are base code) |
| `ResetPerGameCache` | definition `NewThingFrame.cs:264`, call `TickScheduler.cs:24` |
| `bash -n scripts/sync-and-play.sh`; loop line 139 | exit 0; lists `LocalReplaceStuff` |
| Extracted link-check block against a scratch `Mods/` | linked: no output; link removed: `WARN: LocalReplaceStuff is MISSING from Mods/ - recreate with: ln -s …` |
| `{{` placeholders in README | 0 |
| Resolved load-folder file set vs Workshop `<v1.7>` | same 5 documented differences as round 1 |

### Defect status
| Defect | Status | Evidence |
| --- | --- | --- |
| D1 `LoadFolders.xml` unmarked | FIXED | `LoadFolders.xml:3-4` holds a `LOCAL 1.7 PORT` comment; file parses. `README.md:128-130` now says rewritten files (`LoadFolders.xml`, `About/About.xml`, the csproj) carry a marker |
| D2 README hunk text | FIXED | `README.md:82`: "skip a rock whose own position already has a Mine designation", with the double-add log line as the reason. Matches `CanPlaceBlueprintOverMineable.cs:81` |
| D3 stale cached hit | FIXED | See the analysis below. README lines 104–111 describe the change; it carries `LOCAL 1.7 PORT` |
| Review R3 (`launchSettings.json`) | FIXED | file and `Source/Properties/` gone; README line 147 lists the deletion |
| Review R4, R5 (README wording) | FIXED | `README.md:224` "Four C# mods" (count is right: CSharpFixes, EarlyFixes, VisualExceptions, ReplaceStuff); `README.md:100-103` states the constructor runs twice on a load and the second clear does no harm (true per the 1.7 IL) |
| Review R2 (entries until next game) | ACCEPTED by the conductor | memory only; thing IDs do not repeat inside one game, so no wrong result |

### D3 fix: correctness analysis
New hit rule (`NewThingFrame.cs:286-291`): keep the cached `true` only if the old thing is not
destroyed, is spawned, is on the new thing's map, and its `OccupiedRect()` overlaps
`GenAdj.OccupiedRect(newThing.Position, newThing.Rotation, newThing.def.Size)`. Otherwise remove the
entry and fall through to the normal scan, which then caches its own result.

- **Stale `true`:** none found. The uncached scan returns `true` exactly when some thing in the
  footprint cells' thing lists has a def that `CanReplace` accepts. A thing is in a cell's list
  only while it is spawned on that map and its `OccupiedRect()` covers the cell, so "spawned, same
  map, rects overlap" is the same membership test. `CanReplace` depends only on the two defs (and
  `building.deconstructible`, also a def field), and a thing's def never changes, so the cached
  old thing still passes it. Hence a kept hit is a hit that the scan would also give. (The scan
  can pick a different old thing if several qualify; the fork already had that.)
- **Recompute on every call (performance):** none found.
  - A legitimately non-replacing frame or blueprint stores `null`; the next call takes the
    `oldThing == null` branch and returns `false` at once, with no scan.
  - A stale positive entry is removed and rescanned once. The rescan result is cached. A thing
    that the scan returns always passes the validity test (same membership test as above), so
    the test cannot fail again on the same thing and no remove/rescan loop can form.
  - A valid hit adds four field tests, two `CellRect` structs and one `Overlaps`. No allocation,
    no thing-list walk.
- **Residual (cosmetic, no defect):** a positive entry whose weak target is already collected
  still returns `false` without a rescan. A target is collected only after it leaves every map,
  and nothing new can stand under a frame meanwhile, so the result equals the scan's in practice.
- **Not testable offline:** the reinstall, uninstall and minify paths run only in game. They are
  the new step 28 below.

## How QA verified in round 1 (independent of the developer reports)

- Build: `dotnet build LocalReplaceStuff/Source/Replace_Stuff.csproj` exits 0 (0 warnings,
  0 errors). A second build from a fresh copy of the untracked files (no `obj/`, DLL deleted,
  `--no-incremental`) also exits 0. Its IL (`ilspycmd -il`) is identical to the committed DLL
  (sha256 `b484317e…3547d`). The sha256 differs only because the build path differs.
- Base: `<scratch>/replace-stuff/base/` is byte-identical to the live Workshop folder `3526354009`
  (`diff -rq`, mtime 2026-10-17 16:37:17, DLL sha256 `fb130724…58720e5`). QA did a full
  `diff -ru base/Source LocalReplaceStuff/Source` and read every hunk. The developer's
  `diffcheck.py` also reports 12 files, 16 hunks, 0 problems.
- Fork: QA fetched `compare/MemeGoddess:master...Hexnet111:master` with `gh api` (head
  `c153cdb1…`, ahead 11, behind 3, merge base `3639256`) and compared each `.cs` hunk with the
  port. QA also fetched the 3 upstream commits that the fork lacks and found each one in the base.
- Harness: QA copied `harness/` to its own folder, built it, and ran it on the port DLL. Result:
  76 patch classes applied with Harmony 3.0.0-rc.3, 5 static initialisers bound, 24 transpiler
  applications each match the expected IL diff, 0 problems (same as `harness-run5.txt`).
  Positive control: the same harness on the Workshop DLL reports 3 problems (`FramesDontBlock`
  +0/-0, `CanPlaceBlueprintRotDoesntMatter` on the forwarder +0/-0, missing `_NewTemp`
  application).
- IL spot-checks with `ilspycmd` on the 1.7 `Assembly-CSharp.dll` (sha256 `eb0826b8…1c9f23`),
  against `target-checklist.md`:
  - `GenConstruct.CanPlaceBlueprintAt` is a 25-byte forwarder to `_NewTemp`.
  - `_NewTemp` holds one `Rot4::op_Inequality` (IL_0101, then `brtrue` = `continue`) and no
    `Rot4::op_Equality`. `ldarg.0` is `BuildableDef entDef`.
  - `_NewTemp`: `call GridsUtility::Fogged(IntVec3, Map)` (IL_009b), then `brfalse.s` (fogged
    transpiler, criterion 14).
  - `GenConstruct.BlocksConstruction`: `callvirt get_Fillage; ldc.i4.1; blt.s IL_0242`, and
    IL_0242 is `ldc.i4.0; ret`.
  - `PlaceWorker_PreventInteractionSpotOverlap.AllowsPlacing`: `ldloc.s 9; ldarg.s thingToIgnore;
    beq IL_017b`.
  - `GenSpawn.Spawn` 7-parameter overload parameter names match `TransferSettings`.
  - Fields `Designator_Place.placingRot`, `Designator_Build.entDef`,
    `StatWorker.temporaryStatCache`, `immutableStatCache`, `ShortHashGiver.takenHashesPerDeftype`
    have the types that the source binds.
- Load folders: QA decompiled `DirectXmlLoader.XmlAssetsInModFolder` and `LoadedLanguage`. Both
  keep the first file per relative path, in descending folder order. A script compared the
  resolved file set of the Workshop `<v1.7>` list (8 folders) with the port (`/`, `1.7`). The only
  differences are the documented ones: `Patches/ReplaceStuff15Patches.xml` (1.5),
  `News/UpdateFeatures.xml`, the HugsLib `DefInjected` file, and the English and Japanese
  `Keyed/AutoEnglish.xml` (root text in the port, `1.3/` text in the Workshop mod; Japanese differs
  only in line endings).
- Compatibility scan: QA wrote its own scan of the 273 active mods in `ModsConfig.xml`
  (read-only). It found the same references as the README table and no others.

## Coverage matrix
| AC # | Test | Status |
| --- | --- | --- |
| 1 | `find LocalReplaceStuff`: `About/About.xml`, `LICENSE`, `Source/Replace_Stuff.csproj`, `1.7/Assemblies/Replace_Stuff.dll` exist | PASS |
| 2 | Read `About/About.xml`; parse with `xml.etree`; search all local, `Mods/`, `Data/` and Workshop `About.xml` for `jdtay.replacestuff` (only this mod) | PASS |
| 3 | `LICENSE` lines 1–19 equal Workshop, MemeGoddess and Hexnet111 `LICENSE.txt` (CR removed; all three identical); line 21 adds the port notice. alextd's repo has no licence file | PASS |
| 4 | `find … -name '*.dll' -o -name '*.pdb' -o -name '*.deps.json' -not -path '*/obj/*'` gives one line; no `1.1`–`1.6` folder; committed DLL IL = fresh build IL | PASS |
| 5 | `dotnet build` in place and from a clean copy: exit 0, 0 errors, output in `1.7/Assemblies/` | PASS |
| 6 | `ls 1.7/Assemblies` gives only `Replace_Stuff.dll` (no pdb, deps.json or copied references) | PASS |
| 7 | `LoadFolders.xml` `<v1.7>` = `/`, `1.7`; no root `Assemblies/`; resolved-file-set comparison above | PASS |
| 8 | Upstream `5e0c881` (Basic Dropdowns, `DesignatorBuildDropdownStuffFix.cs`), `7951289` (Gloomy, `About.xml` `incompatibleWith`), `0ea5dc9` (v4850, `BlueprintOverFogged` and `RememberWasNewThing`) are in the source | PASS |
| 9 | Full base diff: each hunk carries `LOCAL 1.7 PORT` or the amendment's `LOCAL PORT (Hexnet111 performance fork)` marker; `About.xml` and csproj marked | PASS (round 1: D1, fixed in round 2) |
| 10 | `ilspycmd -l cisde` on the Workshop DLL and the port DLL: identical except 3 compiler-generated closure classes; assembly name `Replace_Stuff` | PASS |
| 11 | `/usr/bin/grep -rni` for both old ids: only `About.xml:26-27` (`incompatibleWith`) and `About.xml:22` (reason comment); none in `Source/` | PASS |
| 12 | `ResetPerGameCache()` at `NewThingFrame.cs:264`, called from `TickScheduler(Game)` at `TickScheduler.cs:24`. 1.7 `Game()` calls `FillComponents()` (`Activator.CreateInstance(type, this)`); `LoadGame()` runs `ExposeSmallComponents()` (components deep-loaded with `this`) before `maps`. `thingReplacementCache` is the only static keyed on thing IDs | PASS (D3 in-game staleness fixed in round 2) |
| 13 | Harness: 76 classes patched with Harmony 3, 16 `ReservationManager` methods, 0 failures; IL and field spot-checks above | PASS |
| 14 | Harness: 24 transpiler applications match the expected diff; positive control fails on the Workshop DLL as expected; fogged transpiler pattern found in `_NewTemp` | PASS |
| 15 | Base diff `+` lines hold no `Tick`, `TickRare`, `TickLong`, `GameComponentTick`, `MapComponentTick`; the two tick methods in the tree are base code | PASS |
| 16 | QA scan of 273 active mods (text case-insensitive, DLL UTF-8 and UTF-16LE): Research Reinvented (About + DLL `HarmonyBefore`), [BTD] Gravship Blueprints DLL, Use This Instead JSON, VFE Architect 1.3/1.4 DLLs. README table lists each with a decision; BTD and RR DLL claims checked with `ilspycmd` | PASS |
| 17 | `bash -n scripts/sync-and-play.sh` exits 0; loop at line 139 lists `LocalReplaceStuff`; QA ran the extracted link-check block against an empty scratch `Mods/`: prints `WARN: LocalReplaceStuff is MISSING from Mods/ - recreate with: ln -s '…/LocalReplaceStuff' '…/Mods/LocalReplaceStuff'`, and nothing once linked. Full script not run (needs a clean tree) | PASS (block level) |
| 18 | `README.md` section: packageId, base record, fork commit, upstream commits, cache fix, retirement, credits with links, compat table, Deployment line 248, Building line 230; 0 `{{` placeholders | PASS (round 1: D2, fixed in round 2) |
| 19 | `CLAUDE.md` lines 52–56 and 61–64 hold the three rules | PASS |
| 20 | — | PENDING USER |
| 21 | — | PENDING USER |
| 22 | — | PENDING USER |
| 23 | — | PENDING USER |
| 24 | — | PENDING USER |
| 25 | — | PENDING USER |
| 26 | — | PENDING USER |
| 27 | — | PENDING USER |

## Defects (round 1; all fixed in round 2)

### D1 — `LoadFolders.xml` changes the base but has no `LOCAL 1.7 PORT` marker
- **Severity:** Low.
- **AC:** 9.
- **Expected:** every change from the base marks itself with `LOCAL 1.7 PORT`.
- **Actual:** `LocalReplaceStuff/LoadFolders.xml:1-7` replaces the Workshop file (7 version
  blocks, 8 folders for `<v1.7>`) with one `<v1.7>` block and has no marker. README line 128
  says the layout changes "are moves and deletions, so they carry no marker", but this file is
  rewritten, not moved.
- **Repro:** `diff <scratch>/replace-stuff/base/LoadFolders.xml LocalReplaceStuff/LoadFolders.xml`;
  `/usr/bin/grep -c "LOCAL 1.7 PORT" LocalReplaceStuff/LoadFolders.xml` gives 0.
- **Fix:** add `<!-- LOCAL 1.7 PORT: 1.7 only; loads / and 1.7, which holds every file the
  Workshop 1.1-1.7 list resolved to. -->`. Optionally reword README line 128.

### D2 — README fork table describes the `CanPlaceBlueprintOverMineable` hunk incorrectly
- **Severity:** Low (documentation).
- **AC:** 18.
- **Expected:** the hunk table gives what each hunk does.
- **Actual:** `README.md:82` says "skip a cell that already has a Mine designation". The base
  already skipped such a cell. The fork hunk skips a rock whose own position already has a Mine
  designation (`DesignationAt(mineThing.Position, …)`). On a rock that covers more than one cell,
  this stops a second `AddDesignation`, which 1.7 logs as "Tried to double-add designation at
  location". The "Reason" column is correct; `port-notes.md` has the correct text.
- **Repro:** compare `README.md:82` with `LocalReplaceStuff/Source/OverMineable/CanPlaceBlueprintOverMineable.cs:81`
  and the base file.
- **Fix:** change the Hunk cell to "Test the def type once; skip a rock whose position already has
  a Mine designation".

### D3 — Ported fork cache keeps a stale positive entry when the old thing despawns without destruction
- **Severity:** Low (fork behaviour; not a failure of criterion 12, which is about cross-game data).
- **AC:** none directly. Spec non-goal: "No change to gameplay behaviour, except the cache fix and
  1.7 compatibility".
- **Expected (base behaviour):** `IsNewThingReplacement(Thing)` scans the footprint each call, so
  it returns `false` after the old thing leaves the cell.
- **Actual:** `LocalReplaceStuff/Source/NewThing/NewThingFrame.cs:279-292` returns the cached
  `true` while `oldThing.Destroyed` is false. Uninstall, reinstall (move) and minify despawn a
  building without destroying it. After that, for the new-thing frame or blueprint:
  - `WorkToBuild` / `WorkTotal` keep the old thing's deconstruct work.
  - `ReserveSharing.Prefix` (`Replace/ReserveSharing.cs:42`) redirects every reservation on the
    frame to the old thing, which can now stand at another position. A pawn that uses the moved
    building then blocks builders on the frame.
  - `RememberWasNewThing.Prefix` sets `__STATIC_STUPID_WAS_NEW_THING = true` at completion, so
    `GenSpawn.Refund` uses `DestroyMode.Deconstruct` for that spawn.
  The entry stays until the frame completes or the next game starts.
- **Repro (in game):** place a new-thing blueprint that replaces an existing building (for example
  a different bed over a bed), let it become a frame, reinstall the old building elsewhere, then
  watch the frame's work total and whether builders can reserve it while a colonist uses the
  moved building.
- **Suggested fix (needs a conductor or PO decision, because it changes a fork hunk):** treat
  `!oldThing.Spawned || oldThing.Map != newThing.Map` (or the old position outside the new
  footprint) like `Destroyed`: remove the entry and fall through to a fresh scan. Mark it
  `LOCAL 1.7 PORT`.

## Edge cases tested beyond the spec
- Per-game clear reach: `Game()` constructor and `LoadGame()` order confirmed in the 1.7 IL. Other
  statics checked: `_replacementCache`, `compCache`, `replaceFrameDefs`, `cachedReplaceCosts`,
  `handlers` use defs, types or strings; `__STATIC_STUPID_WAS_NEW_THING` resets in the `Refund`
  postfix; `TickScheduler._inst` and its queues are new per game. Only `thingReplacementCache` is
  per game.
- Cache callers: only `Frame` and `Blueprint_Build` (`DeconstructWork.cs`, `DeconstructOriginal.cs`,
  `ReserveSharing.cs`). Both always get a real `thingIDNumber`, so key collisions on `-1` cannot
  occur.
- Null entries: a `null` value (cached negative) returns `false` with `oldThing = null` and does
  not throw. A negative result stays for the life of the frame or blueprint. QA found no ordinary
  path where a frame's answer turns from false to true (frames are not edifices and do not match
  the replacement predicates). Blueprint entries are never removed in game; growth is one small
  entry per queried blueprint, cleared per game.
- Weak references: a collected target returns `false` and leaves the entry (harmless).
- `FramesDontBlock` semantics: `AndNotFrame(fillage, 1, t)` + `brfalse IL_0242` is the exact
  inverse of `blt.s IL_0242` for non-frames, and skips the block (returns `false`) for frames. The
  replaced `blt.s` carries no label.
- `CanPlaceBlueprintRotDoesntMatter` semantics: `ne && !dontMatter` is the De Morgan inverse of the
  1.6 `eq || dontMatter`. `ldarg.0` is `entDef` in `_NewTemp`. The kept `op_Equality` branch matches
  nothing in 1.7. Both `_NewTemp` transpilers apply together without error (harness).
- Dropping `1.5/Patches/ReplaceStuff15Patches.xml`: vanilla 1.7 `Door` has a `<building>` node, so
  the 1.6 file alone works. The Workshop mod loads both files and adds `Autodoor` to
  `relatedBuildCommands` twice; the port adds it once.
- Vanilla and DLC compatibility defs (`1.7/Defs/Vanilla.xml`, `Royalty.xml`, `Ideology.xml`,
  `Biotech.xml`, `Odyssey.xml`): every unconditioned `items` defName exists in 1.7 `Data/`.
- Encoding: every edited `.cs` file keeps its BOM and LF line endings (no ugrep damage).
- `git status --ignored`: all 160 mod files are trackable; only `Source/obj/` is ignored.

## Pending in-game checks (criteria 20–27, plus step 28) — exact steps for the player
Before you start: run `ln -s '/home/jt/src/rimworld-local-mods/LocalReplaceStuff' '/mnt/games/SteamLibrary/steamapps/common/RimWorld/Mods/LocalReplaceStuff'`.
In RimSort, turn off "Replace Stuff - Continued" and turn on "Replace Stuff (1.7 Port)" after Harmony.
Log: `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log`.

20. Start RimWorld. Run `/usr/bin/grep -n -i -e harmony -e replace_stuff -e "replace stuff" -e TypeInitializationException Player.log`.
    Expect no Harmony error and no exception from `Replace_Stuff`. Add any line to README
    "Known log lines".
21. In RimSort, turn on Workshop `3526354009` too. Expect an incompatibility warning in RimSort and
    in the RimWorld mod list. Then turn it off again.
22. Build a wood wall. Place a stone wall blueprint on it. Expect a replace frame, no Deconstruct
    designation, and a stone wall with no designation after completion.
23. Place a replacement over a floor, a door and a furniture item. Expect each to be replaced in
    place. Extra (1.7 fix): place a door blueprint of the same def and stuff over a door with a
    different rotation; expect "Identical thing exists".
24. Place a cooler and a vent blueprint on a wall. Expect colonists to build each in the wall cell.
25. Place a bridge, then a building blueprint, on shallow and deep water. Expect the bridge first,
    then the building.
26. Load save A. Without a restart, load save B. Repeat step 22 in save B. Expect the same result
    and no Replace Stuff log lines.
27. With Smarter Construction, Fix Styled Blueprints, Vehicle Framework and the gravship mods on,
    place and complete one replacement, including one on a gravship substructure. Extra (1.7 fix):
    place a wall blueprint over a power-conduit frame; expect builders not to deconstruct the
    conduit frame. Expect no errors from Replace Stuff or those mods.
28. (Developer's step for the D3 fix; supports 22, 23 and 26.) Place a different bed blueprint over
    an existing bed. Let it become a frame. Note the frame's work total. Reinstall the old bed in
    another room. Expect the frame's work total to lose the deconstruct part, and builders to
    reserve and build the frame while a colonist sleeps in the moved bed. Repeat with Uninstall
    in place of Reinstall. Expect no Replace Stuff log lines.

## Recommendations
- PO: criterion 9 names only `LOCAL 1.7 PORT`, but the amendment adds a second marker for fork
  hunks. Update the criterion text so a plain `grep "LOCAL 1.7 PORT"` is not read as the full
  change list.
- D3: decided and fixed in round 2 (validity test on each cached hit).
- The two restored transpilers (`FramesDontBlock`, `CanPlaceBlueprintRotDoesntMatter`) are no-ops in
  the Workshop 1.7 DLL, so no 1.7 player has run them. The source intent is clear and the IL
  semantics are right, but the extra steps in 23 and 27 are the only behavioural evidence.
- `Source/Properties/launchSettings.json`: deleted in round 2.
- `diffcheck.py` needs `--allow-drop Properties` now; record that flag wherever the base-diff
  command is documented for a later re-run.
- `thingReplacementCache` and `_replacementCache` are plain `Dictionary` objects without a lock.
  The offline checks cannot show if 1.7 calls the work getters or `ReservationManager` off the main
  thread. Watch for `InvalidOperationException` or `IndexOutOfRangeException` from
  `Replace_Stuff` in step 20–27 logs.
- `packageId` uniqueness is confirmed only on this machine. Search the Steam Workshop for
  `jdtay.replacestuff` before a release.
