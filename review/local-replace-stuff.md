# Code Review: Local Replace Stuff

Spec: `specs/local-replace-stuff.md`
Plan: `plans/local-replace-stuff.md` (the Amendment section governs: base = Workshop 3526354009 1.7 tree, fork hunks ported by hand)
Diff under review: uncommitted work tree against `main` (`023eda9`); port diff against the scratchpad base repo `base/` (commit `7370c9f`, byte copy of Workshop 3526354009)

## Summary
APPROVE — the port matches the plan, the two 1.7 transpiler fixes match the 1.7 IL, the per-game cache clear runs before any thing loads, and the remaining findings are minor.

## What I checked

- Port diff: 12 files, 16 hunks, each next to a `LOCAL PORT (Hexnet111 performance fork)` or `LOCAL 1.7 PORT` marker. No `+` line adds a `Tick`, `TickRare`, `TickLong`, `GameComponentTick` or `MapComponentTick` method.
- Fork hunks against `gh api .../compare` output (head `c153cdb`): the ported code equals the fork's code, except the recorded adaptation in `FindReplace` (null check kept; the fork's `DoReplace(null)` throws) and the recorded skips.
- `Other/FramesDontBlock.cs:30`: 1.7 IL of `GenConstruct.BlocksConstruction` holds `callvirt get_Fillage; ldc.i4.1; blt.s IL_0242` at IL_01c8–IL_01ce and no other `blt*`. The `Blt || Blt_S` test matches it. The replacement `ldarg.1; call AndNotFrame; brfalse <label>` consumes the two operands plus `t` and leaves one bool, so the stack stays balanced.
- `Replace/CanReplaceAnyStuff.cs:63`: in 1.7, `GenConstruct.CanPlaceBlueprintAt` is a 25-byte forwarder; `CanPlaceBlueprintAt_NewTemp` holds exactly one `Rot4::op_Inequality` (IL_0101) and no `op_Equality`. Each name has one overload, so the bare-name `[HarmonyPatch]` resolves. `ldarg.0` is `entDef` (static method). `AndRotMatters(result, entDef) = result && !PlacingRotationDoesntMatter(entDef)` is the exact inverse of the 1.6 `OrRotDoesntMatter` rule applied to the `!= rot` → `continue` test, so doors and non-rotatable things at another rotation again count as "identical thing exists".
- Cache clear: 1.7 `Game()` calls `FillComponents()` (constructs `TickScheduler`), and `Game.LoadGame()` calls `ExposeSmallComponents()` (deep-loads `components`, constructing `TickScheduler` again) before `Scribe_Collections.Look(ref maps, …)`. So `ResetPerGameCache()` runs before any thing exists, for a new game and for a loaded game. Between games `Thing.Spawned` is false (`Find.Maps == null`), so no lookup reaches the dictionary.
- Threading: every game-code caller of `Frame.get_WorkToBuild` and `Blueprint*.get_WorkTotal` is `JobDriver_ConstructFinishFrame`, `Frame` itself (`WorkLeft`, `PercentComplete`, `FailConstruction`) or `Blueprint.GetInspectString`. The other callers are the mod's own `CompleteConstruction` prefix and `ReservationManager` prefixes. All run on the main thread, so the unlocked `Dictionary` is safe in vanilla.
- `About.xml` parses; packageId, `supportedVersions`, `url` (matches `origin`), `modDependencies`, `loadAfter`, `incompatibleWith`, credits and source URLs match the plan's interface. `LICENSE` keeps the one base notice and adds the port notice. `LoadFolders.xml` is `/` + `1.7`. `Textures/TDReplaceStuffIcon.png` exists for `modIconPath`.
- `/usr/bin/grep -rni` for the old packageIds: only the two `incompatibleWith` lines and one reasoned comment. One DLL under the mod; `obj/` is ignored. `bash -n scripts/sync-and-play.sh` passes; `--launcher none` and `--no-pull` exist, so the README steps are valid.
- csproj: a copy of `VisualExceptions.csproj` minus the steamworks reference, plus the plan's name/version/output values. `Properties/` holds no `AssemblyInfo.cs`, so `GenerateAssemblyInfo` needs no change.
- CLAUDE.md: the three required rules exist; the `LocalVisualExceptions` "no `LoadFolders.xml`" statement is true.

## Findings

### R1 — Cache hit ignores a despawned old thing
- **Severity:** minor
- **Location:** `LocalReplaceStuff/Source/NewThing/NewThingFrame.cs:281-293`
- **Problem:** A positive entry is only dropped when `oldThing.Destroyed` is true. An old thing that despawns without destruction (uninstall / minify) keeps its entry. The base (uncached) code rescans the cell and returns false.
- **Failure:** Place a replacement blueprint over a minifiable building whose def `CanReplace`s the new def, then uninstall the old building. `Blueprint_Build.WorkTotal` and `Frame.WorkToBuild` keep adding `WorkToDeconstructDef(old)`, `RememberWasNewThing` sets `__STATIC_STUPID_WAS_NEW_THING = true` at completion, and `ReserveSharing` redirects reservations to the despawned thing. Visible effect: extra construction work on that frame. Fork behaviour; the spec accepts the fork's memo, so this is minor.
- **Suggested fix:** `if (oldThing.Destroyed || !oldThing.Spawned) { thingReplacementCache.Remove(thingID); return false; }` with a `LOCAL 1.7 PORT` marker, if the Architect wants the base semantics back.

### R2 — Entries for blueprints and cancelled frames stay until the next game
- **Severity:** minor
- **Location:** `LocalReplaceStuff/Source/NewThing/NewThingFrame.cs:296`, `LocalReplaceStuff/Source/NewThing/DeconstructOriginal.cs:24`
- **Problem:** The only removal is in the `Frame.CompleteConstruction` prefix, keyed on the frame's ID. The `Blueprint_Build` that preceded the frame has a different ID, and a frame that the player cancels never completes. Those entries (an `int` plus a `WeakReference` GC handle for positive results) stay until `ResetPerGameCache()`.
- **Failure:** A long session that places many blueprints grows the dictionary by one entry per blueprint and per cancelled frame. Thing IDs never repeat inside one game, so no wrong result follows; the cost is memory only, bounded by one session. Fork behaviour.
- **Suggested fix:** Optional: also `Remove(__instance.thingIDNumber)` in the existing `Blueprint.DeSpawn` and `Frame.Destroy` prefixes in `DestroyedRestore/BlueprintRemoval.cs`. Otherwise accept and leave as is.

### R3 — Upstream's Windows debug launch profile is vendored
- **Severity:** minor
- **Location:** `LocalReplaceStuff/Source/Properties/launchSettings.json`
- **Problem:** The file is upstream's local IDE launch profile (`C:\RIMWorld_Debug\RimWorld1.6\RimWorldWin64.exe`, a 1.6 path). `dotnet build` of a library never reads it. It is dead configuration in a public repo. The plan's layout rule (Task 1 step 4) lists only root-level exclusions, so this is a plan gap, not a developer error.
- **Failure:** None at run time. A reader of the public repo sees a path that does not exist on either machine.
- **Suggested fix:** Delete the file (and the now-empty `Properties/` folder), or record the keep decision in `port-notes.md`.

### R4 — README "Both C# mods" is stale
- **Severity:** minor
- **Location:** `README.md:207`
- **Problem:** The Building section opens with "Both C# mods build with the .NET SDK". The same section now lists four C# mods (`LocalCSharpFixes`, `LocalEarlyFixes`, `LocalVisualExceptions`, `LocalReplaceStuff`). The sentence predates this diff, but the diff edits the paragraph below it.
- **Failure:** A reader counts two C# mods and misses the other two.
- **Suggested fix:** "The C# mods build with the .NET SDK on either Windows or Linux:".

### R5 — README states the constructor runs once per loaded game
- **Severity:** minor
- **Location:** `README.md` (Cache fix paragraph: "RimWorld runs that constructor once for each new or loaded game")
- **Problem:** On a load, `new Game()` constructs `TickScheduler` through `FillComponents()`, then `ExposeSmallComponents()` deep-loads `components` and constructs it again. `Clear()` is idempotent, so the fix holds, but the sentence is not exact.
- **Failure:** None at run time.
- **Suggested fix:** "RimWorld constructs the game components before any map or thing loads, for a new game and for a loaded game."

## Questions for the Architect
- R1: keep the fork's cache semantics for despawned old things, or add the one-line `Spawned` test now, before the player's in-game test?
- R2: is the per-session growth acceptable, or should the two existing `DeSpawn`/`Destroy` prefixes also remove the entry?
- R3: confirm the decision on `Source/Properties/launchSettings.json`; the plan did not cover files under `Source/`.
