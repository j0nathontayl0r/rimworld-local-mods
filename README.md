# RimWorld Local Fix Mods

Five local mods. Three are hand-made: they suppress errors / fix small runtime bugs from a large
RimWorld 1.6 modlist, without editing the workshop mods themselves. The other two,
`LocalVisualExceptions` and `LocalReplaceStuff`, are local RimWorld 1.7 builds of workshop mods.
`LocalPatches` and `LocalCSharpFixes` **must load last** in the mod order; `LocalEarlyFixes` is the
exception and must load **first** (see below).

Each mod's `About.xml` carries `loadAfter`/`loadBefore` metadata enumerating its actual patch
targets, so RimSort's auto-sort keeps the placement (an auto-sort once scattered them mid-list,
2026-08-22, silently disabling an order-dependent fix). **When a new Fix targets a new mod, add that
mod's packageId to the relevant `loadAfter` list.**

## Mods

### `LocalPatches` — packageId `local.errorfixes`
XML `PatchOperation` mod. Each `Patches/FixN_*.xml` is self-contained and wrapped so failures are
silent (`success=Always` / `PatchOperationFindMod`). Also ships replacement textures for a couple of
mods whose 1.6 texture paths broke (`Textures/`).

### `LocalCSharpFixes` — packageId `local.csharpfixes`
Harmony patch mod. Requires the **Harmony** mod. Suppresses NREs / log spam and fixes small runtime
bugs where an XML patch can't reach. Third-party targets are resolved by name at startup and skipped
(with a log line) if a mod is absent, so a missing target never breaks startup.

### `LocalEarlyFixes` — packageId `local.earlyfixes`
Companion to `LocalCSharpFixes` for patches that must be applied *before* other mods' `Mod`
constructors run. Uses a `Mod` subclass rather than `[StaticConstructorOnStartup]`, so its
constructor runs during `CreateModClasses` — which is why this one must sit at the **top** of the
load order, immediately after the official DLCs. Load order position is load-bearing.

### `LocalVisualExceptions` — packageId `brrainz.visualexceptions` (RimWorld 1.7 only)
Local 1.7 build of [Visual Exceptions](https://github.com/pardeike/VisualExceptions) (MIT, vendored
from upstream commit `aa1fc51`). The workshop build's `ExceptionInfo` type initialiser binds two
methods by reflection that no longer exist on 1.7 — Mono's private `Exception.ToString(bool, bool)`
and the Harmony mod's `ExceptionTools.ExtractHarmonyEnhancedStackTrace` (renamed in its 1.7 build) —
so it threw `TypeInitializationException` on every handled exception, and crashed the game when
that happened on the loading thread. Changes from upstream are marked `LOCAL 1.7 PORT`. It shares the
workshop mod's packageId, so activate exactly one of them: this one on 1.7, workshop `2538411704` on
1.6. Retire it once the workshop mod ships a 1.7 build.

### `LocalReplaceStuff` — packageId `jdtay.replacestuff` (RimWorld 1.7 only)
Local 1.7 build of Replace Stuff, with the changes from the Hexnet111 performance fork and a fix for
the fork's replacement cache. Replace Stuff lets you place a wall, floor, door or furniture
blueprint over an existing one. Colonists then replace the old thing in place, with no deconstruct
step. The mod also puts coolers and vents into walls and builds bridges over water. The mod uses the
MIT licence. Its `LICENSE` keeps every original copyright notice and adds one for the port.

Credits:
- Uuugggg (alextd) wrote the original mod:
  [alextd/RimWorld-ReplaceStuff](https://github.com/alextd/RimWorld-ReplaceStuff).
- MemeGoddess maintains "Replace Stuff - Continued" (workshop `3526354009`):
  [MemeGoddess/RimWorld-ReplaceStuff](https://github.com/MemeGoddess/RimWorld-ReplaceStuff).
- Hexnet111 wrote the performance fork:
  [Hexnet111/RimWorld-ReplaceStuff-Performance-Patch](https://github.com/Hexnet111/RimWorld-ReplaceStuff-Performance-Patch).

**Base.** The base is the 1.7 build of workshop `3526354009`, not a GitHub commit. That build is
newer than MemeGoddess's GitHub `master`, which stops at 1.6, so no commit hash identifies it. The
record is the workshop update date and the hash of its DLL:
workshop `3526354009`, updated 2026-10-17 16:37 +1000, `1.7/Assemblies/Replace_Stuff.dll` sha256
`fb130724eecf8d2c219067daa5043224c5083f6cd048e636cd44b8f6058720e5`. The base already holds the
three MemeGoddess commits that the fork lacks, so the port needs no merge. These commits are
`0ea5dc9` "Updated for v4850", `7951289` "Marked Gloomy as incompatible due to too many bug
reports" and `5e0c881` "Basic Dropdowns fix (#13)".

**Fork changes.** The port adds the `.cs` changes from Hexnet111 fork commit
`c153cdb10b2584d68d8e50ac73d304a2e8e85944` (head of `master`, 2026-07-02) to the base by hand. The
fork targets 1.6, so the port takes each hunk as it is, adapts it, or skips it. The port ignores
the fork's DLL, `.csproj` and workflow changes. Each ported hunk carries the marker
`LOCAL PORT (Hexnet111 performance fork)`. The fork hunks, each marked ported, adapted or skipped,
with the reason:

| File (under `Source/`) | Hunk | Result | Reason |
| --- | --- | --- | --- |
| `NewThing/NewThingFrame.cs` | `thingReplacementCache`: remembers the `IsNewThingReplacement(Thing)` result for each thing (weak references; drops destroyed things) | adapted | The main performance change. The port also drops a stale hit (see **Cache fix**) |
| `NewThing/NewThingFrame.cs` | Blank-line change | skipped | Whitespace only |
| `NewThing/DeconstructOriginal.cs` | Remove the cache entry in the `Frame.CompleteConstruction` prefix | ported | Cache upkeep |
| `NewThing/DeconstructOriginal.cs` | Remove the Deconstruct designation from `ReplaceFrame.oldThing` | skipped | The base already does this (upstream `0ea5dc9`) |
| `NewThing/DeconstructWork.cs` | Return at once for a `ReplaceFrame` in the `Frame.WorkToBuild` postfix | ported | `Virtualize_WorkToBuild` already sets the work of a `ReplaceFrame` |
| `NewThing/DeconstructWork.cs` | `Blueprint_Build __instance` parameter in the `WorkTotal` postfix | skipped | The base already has it |
| `OverMineable/BlueprintOverFogged.cs` | Retarget to `CanPlaceBlueprintAt_NewTemp` with a `CodeMatcher` | skipped | The base has the same code (upstream `0ea5dc9`) |
| `OverMineable/CanPlaceBlueprintOverMineable.cs` | Test the def type once; skip a rock whose own position already has a Mine designation | ported | Stops a second Mine designation on a rock that covers more than one cell, which 1.7 logs as "Tried to double-add designation at location" |
| `PlaceBridges/BridgelikeTerrain.cs` | `allBridgeTerrainsLookup` set for `IsBridgelike` | skipped | [BTD] Gravship Blueprints removes a terrain from the list `allBridgeTerrains` by reflection, and the set does not get that change. Also, the fork's `Contains` call binds to LINQ `Enumerable.Contains`, a linear scan, so the set gave no speed-up |
| `PlaceBridges/DesignatorBuildCostCountsBridges.cs` | One pass over the cells; a `KeyValuePair` loop in place of a LINQ tuple projection | ported | The drawn result does not change |
| `Replace/DesignatorReplaceStuff.cs` | `FindReplace`: one loop in place of `FindAll` + `ChooseReplace` | adapted | Keeps the null check from `ChooseReplace`. The fork calls `DoReplace(null)` when no thing matches, which throws |
| `Replace/InterceptBlueprint.cs` | One loop in place of `FindAll` + `ChooseReplace` | ported | The same choice: the first blueprint or frame, else the first thing |

Adaptations to the 1.7 base:

- `Replace/DesignatorReplaceStuff.cs`: the fork's `FindReplace` loop keeps the null check of the
  base. Thus a cell with no match does nothing and does not throw.
- `NewThing/NewThingFrame.cs`: the port checks a cached hit again (see **Cache fix**).
- `PlaceBridges/BridgelikeTerrain.cs`: the port does not take the fork's set. `IsBridgelike` keeps
  reading the list `allBridgeTerrains`, so the change that [BTD] Gravship Blueprints makes to that
  list also reaches `IsBridgelike`.

**Cache fix.** The fork memoises the `IsNewThingReplacement` result for each thing, keyed on the
thing ID. Thing IDs repeat across saves, so a second save that loads without a restart can get stale
results from the first save. The port clears the memo with `ResetPerGameCache()` in the constructor
of the mod's `GameComponent` (`TickScheduler`). RimWorld constructs the game components before any
map or thing loads, for a new game and for a loaded game. On a load, the constructor runs twice:
`new Game()` builds the components (`FillComponents`), then `LoadGame()` builds them again when it
loads the save's components. A second clear does no harm. The fix adds no tick code.

The port also checks a cached hit again. The fork drops an entry only when the old thing is
destroyed, and then returns `false`. The port also drops the entry when the old thing is despawned
(uninstall, minify), on another map, or no longer in the cells of the new thing (for example,
reinstalled elsewhere), and then scans the cells again. The cell test uses the same footprint as
the scan: the new thing's position, rotation and size. Thus the work total, the reservation
redirect in `ReserveSharing` and the refund mode at completion use only an old thing that still
stands in the new thing's cells. This change carries the marker `LOCAL 1.7 PORT`.

Entries for blueprints and cancelled frames stay in the memo until the next game starts. This costs
memory only, because thing IDs do not repeat inside one game.

**Other local changes.** Every other change to the base carries the marker `LOCAL 1.7 PORT`:

- `About/About.xml`: the own packageId and `incompatibleWith` (see **Identity**). `loadAfter` lists
  `petetimessix.researchreinvented`, copied from the `loadBefore` of Research Reinvented (see the
  table below).
- `Source/Replace_Stuff.csproj`: a new project modelled on `LocalVisualExceptions` (see
  [Building](#building)).
- `Source/Replace_Stuff.cs`: a comment only. It records why the Harmony ID stays.
- `Source/TickScheduler.cs` and `Source/NewThing/NewThingFrame.cs`: the cache fix above.
- `Source/NewThing/NewThingFrame.cs`: `Debugger.Break()` in the `catch` block of
  `NewThingReplacement.CanReplace` becomes `Log.Warning`. On CoreCLR/Linux, an unattached
  `Debugger.Break()` raises SIGTRAP and can stop the game.
- `Source/Other/FramesDontBlock.cs`: the transpiler on `GenConstruct.BlocksConstruction` looked for
  `blt` only, but 1.7 compiles that branch as `blt.s`. The transpiler now accepts both. Effect: the
  fillage of a frame does not make the frame block construction. Thus builders do not deconstruct a
  power-conduit frame to build a wall over it.
- `Source/Replace/CanReplaceAnyStuff.cs`: `CanPlaceBlueprintRotDoesntMatter` now patches
  `GenConstruct.CanPlaceBlueprintAt_NewTemp`, not `CanPlaceBlueprintAt`. In 1.7,
  `CanPlaceBlueprintAt` only calls `_NewTemp`, and `_NewTemp` tests `Rotation != rot`
  (`op_Inequality`, not `op_Equality`). Effect: for doors and things that do not rotate, a thing at
  a different rotation counts in the "identical thing or blueprint exists" test.
- `Source/Replace/ReserveSharing.cs`: `ReservationManager.Reserve` gets its own prefix. It redirects
  the target as before, and sets `errorOnFailed = false` when it redirects. A replacement frame's
  reservation goes to the old thing, and another pawn can hold that thing for a different job (for
  example, refuelling a torch lamp that an electric lamp replaces). Upstream then logs "Could not
  reserve", but the job continues. Every reservation that is not redirected keeps its error.
- `Source/PlaceBridges/BridgelikeTerrain.cs`: a terrain without a `blueprintDef` is not a bridge
  candidate. Odyssey's ancient heavy bridge has no `designationCategory`, so it has no blueprint.
  It also has no cost, so `FindBridgeFor` chose it as a free bridge. Then
  `PlaceBlueprintForBuild` threw a `NullReferenceException` in `ThingMaker.MakeThing` when the
  player placed a building that needed a bridge.

The workshop 1.7 DLL (sha256 `fb130724…`) has neither transpiler fix. There, both transpilers find
no match, change nothing and write nothing to the log. The fork table above gives the fork hunks
that the port skips, with the reasons.

The port also changes the folder layout. Moved and deleted files carry no marker, because no text
of theirs remains to mark. Rewritten files carry one: `LoadFolders.xml`, `About/About.xml` and
`Source/Replace_Stuff.csproj`. `LICENSE` keeps upstream's text and adds only the port notice.
`LoadFolders.xml` loads `/` and `1.7/` only. The port removes the `1.1`–`1.6` folders, every
upstream DLL, `Assets.zip` (baked `.dds` copies of the PNGs), `News/`, the HugsLib `DefInjected`
folders, `About/Preview.png`, `About/PublishedFileId.txt` and `Source/Properties/launchSettings.json`
(upstream's Windows debug launch profile, with a 1.6 path). One visible effect: the setting that
hides the over-wall coolers shows the root text, which also names the vents. The workshop mod on 1.7
shows the older `1.3/` text.

**Identity.** The packageId is `jdtay.replacestuff`, not `Memegoddess.ReplaceStuff`, so that a later
Steam Workshop release does not collide with "Replace Stuff - Continued". A packageId change after a
release breaks saves and mod lists, so the choice happens now. `incompatibleWith` lists
`Memegoddess.ReplaceStuff` and `Uuugggg.ReplaceStuff`, so RimWorld warns when this mod and either
workshop copy are both active. The cost: other mods' `loadAfter`, `loadBefore` and `IfModActive`
entries keyed on the old packageIds do not fire for this mod (see the table below). The assembly
name `Replace_Stuff`, the namespaces and the public type names do not change, so other mods that
find Replace Stuff types by name still find them.

The Harmony ID stays `Uuugggg.rimworld.Replace_Stuff.main`. It labels the patches, not the mod, so
other mods' `HarmonyBefore` and `HarmonyAfter` attributes keyed on it keep working. The two Replace
Stuff mods are never active together, so the IDs do not collide.

**Saves and settings.** The save format does not change. The generated defs (`ReplaceFrame_*`) and
the class names match the workshop mod, so a save keeps loading after a swap between the two mods.
The mod settings do not carry over, because RimWorld names the settings file after the mod folder.
After the swap, set the two cooler checkboxes and the preferred bridge order again.

**Switch from the workshop mod:**
1. Run `./scripts/sync-and-play.sh --launcher none`. When the link is missing, it prints the link
   command for `LocalReplaceStuff`.
2. Run that link command.
3. In RimSort, turn off "Replace Stuff - Continued" (workshop `3526354009`).
4. In RimSort, turn on "Replace Stuff (1.7 Port)".

If both mods are on, RimWorld warns, because of `incompatibleWith`.

**Windows machine.** The Windows machine can still run RimWorld 1.6. This repo does not record its
game version, because `modlists/` holds only a Linux snapshot. On that machine:
- Create the junction when `sync-and-play.sh` prints the command for it.
- On 1.6, do not activate the mod. RimWorld marks it as unsupported.
- When the machine runs 1.7, pull, then activate the mod in RimSort with the steps above.

**Retirement.** Retire this mod when workshop `3526354009` carries the fork's performance changes
and the per-game cache clear, or when Dubs Performance Analyzer shows that the fork's changes give
no benefit. The workshop build already supports 1.7, so 1.7 support alone is no reason to keep this
mod. To retire it, turn on the workshop mod in RimSort and turn this mod off.

#### Compatibility patches keyed on the old packageIds
Some active mods refer to `Memegoddess.ReplaceStuff` or `Uuugggg.ReplaceStuff`. The table gives each
reference, what it does and the decision for this port. References by type name keep working,
because the port keeps the type names.

Scan of the 273 active mods on the Linux machine (2026-10-17). The scan looks for
`Memegoddess.ReplaceStuff`, `Uuugggg.ReplaceStuff`, the Harmony ID, the `Replace_Stuff` namespace,
the mod name and the Replace Stuff defNames. The table does not include Replace Stuff - Continued.

| Mod (packageId, workshop id) | File | What the reference does | Keyed on | Decision |
| --- | --- | --- | --- | --- |
| Research Reinvented (`petetimessix.researchreinvented`, 2868392160) | `About/About.xml:22` | `loadBefore` `Uuugggg.ReplaceStuff`: asks RimWorld to load Research Reinvented before Replace Stuff. The old id does not match Continued, so the rule has no effect now (Continued at position 27, Research Reinvented at 228). | packageId | Port supplies it: the port's `loadAfter` lists `petetimessix.researchreinvented`. RimSort then sorts the port after Research Reinvented. |
| Research Reinvented (`petetimessix.researchreinvented`, 2868392160) | `v1.6/Assemblies/ResearchReinvented.dll` (1.7 loads `v1.6`), class `Frame_CompleteConstruction_Patches` | `[HarmonyBefore("Uuugggg.rimworld.Replace_Stuff.main")]` on its `Frame.CompleteConstruction` transpiler. Its transpiler runs before the three Replace Stuff patches on that method. | Harmony ID | Port supplies it: the port keeps the Harmony ID `Uuugggg.rimworld.Replace_Stuff.main`. |
| [BTD] Gravship Blueprints (`btd.remix.gravshipblueprints`, 3575162262) | `1.6/Assemblies/RWM_BTD_Remix_GravshipBlueprints.dll` (1.7 loads `1.6`), class `BTD_ReplaceStuffCompat` | At startup, finds `Replace_Stuff.PlaceBridges.BridgelikeTerrain` by name and removes `BTD_QuestSiteSubstructure` from the static list `allBridgeTerrains`. Thus Replace Stuff does not select this free terrain as an automatic bridge. If the type is absent, the code does nothing. | type name | Keeps working: the port keeps the type and the field `public static List<TerrainDef> allBridgeTerrains`. The automatic bridge selection (`FindBridgeFor`) reads this list. |
| Use This Instead (`mlie.usethisinstead`, 3396308787) | `replacements.json:3414–3421` | Data entry: old Workshop mod 1372003680 "Replace Stuff" (`Uuugggg.ReplaceStuff`) maps to Workshop 3526354009 "Replace Stuff - Continued" (`Memegoddess.ReplaceStuff`). The mod suggests the new mod when the old mod is installed. | packageId | Accepted loss, with no visible effect: the old mod is not installed, and the entry does not name the port. |
| Vanilla Furniture Expanded - Architect (`vanillaexpanded.vfearchitect`, 2608762624) | `1.3/Source/VFEArchitect/ArchitectMod.cs:76–85`; `1.3/Assemblies/VFEArchitect.dll` and `1.4/Assemblies/VFEArchitect.dll` (1.7 loads neither) | If `ModLister.HasActiveModWithName("Replace Stuff")` is true, it patches `Replace_Stuff.ReplaceFrame` so that replace frames count Architect materials. | mod name (expects exactly "Replace Stuff") | No new loss: 1.7 loads `/` and `1.6` (`LoadFolders.xml`), and the 1.5 and 1.6 DLLs do not contain this code. The name check also fails for "Replace Stuff - Continued". |
| Smarter Construction (`dhultgren.smarterconstruction`, 2202185773) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |
| Fix Styled Blueprints (`kathanon.fixstyledblueprints`, 2957953663) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |
| Vehicle Framework (`smashphil.vehicleframework`, 3014915404) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |
| Bigger Gravships (`redmattis.biggergravship`, 3522759531) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |
| [sbz] Gravship Storage (`sbz.gravshipstorage`, 3537905298) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |
| Complementary Odyssey (`mrhydralisk.complementaryodyssey`, 3546612303) | — | No reference found. | — | Nothing to supply. The in-game test (criterion 27) checks the interaction. |

[BTD] Gravship Blueprints is also a criterion 27 mod; its row is above. Combat Extended
(`ceteam.combatextended`, 2890901044) has four XML comments that contain the words "Replace Stuff"
("Replace Stuff Thickness", "Replace stuff category for crafting"). They are plain words, not
references.

#### Known log lines
Each `Player.log` line from this mod at the first 1.7 launch goes here, with an explanation.
- None recorded. The first in-game test is still open.

## Building

Four C# mods build with the .NET SDK. `LocalCSharpFixes` and `LocalEarlyFixes` also have a 1.6
build, which needs a RimWorld 1.6 install with its `Managed/` folder (the Linux install is 1.7-only):
```
dotnet build LocalCSharpFixes/Source/LocalCSharpFixes.csproj
dotnet build LocalEarlyFixes/Source/LocalEarlyFixes.csproj
```
They target `net472` without the targeting pack, via `FrameworkPathOverride` pointed at RimWorld's
bundled `Managed/` folder, and emit straight into each mod's `Assemblies/`. The `SteamRoot` /
`RimDataDir` properties are selected per-OS — update them in the `.csproj` if either install moves.

**RimWorld 1.7** (Waylith engine, .NET 10) needs a separate build. `LocalCSharpFixes` has one at
`Source/v1.7/LocalCSharpFixes.csproj`, which compiles the same sources for `net10.0` against the
game's install-root DLLs (`Assembly-CSharp.dll`, `UnityStubs.dll` in place of UnityEngine) and
Harmony's `1.7/Assemblies`, and writes `1.7/Assemblies/`. `LoadFolders.xml` makes 1.6 load the root
`Assemblies/` and 1.7 load only `1.7/` — listing both under `<v1.7>` would apply every patch twice.
```
dotnet build LocalCSharpFixes/Source/v1.7/LocalCSharpFixes.csproj
dotnet build LocalEarlyFixes/Source/v1.7/LocalEarlyFixes.csproj
```
`LocalEarlyFixes` has the same 1.6/1.7 split. `LocalVisualExceptions` is 1.7-only and builds the
same way from its own `Source/`. `LocalReplaceStuff` is also 1.7-only and builds the same way. Like
`LocalVisualExceptions`, it uses `Krafs.Publicizer` to reach non-public game members. It keeps
upstream's assembly name, `Replace_Stuff`:
```
dotnet build LocalReplaceStuff/Source/Replace_Stuff.csproj
```

Two non-obvious details are documented inline in the `.csproj` files: `netstandard.dll` must be
referenced explicitly (Unity type-forwards through it), and a `NoCopyLocal` target clears
`ReferenceCopyLocalPaths` so transitive resolution can't drop a stray `mscorlib.dll` into
`Assemblies/`, which RimWorld would otherwise load.

The compiled DLLs are committed — they are what RimWorld actually loads, and the game does not build
from source.

## Deployment
Developed here on a backed-up drive and linked into the RimWorld `Mods/` folder:
```
Mods/LocalPatches      -> .../rimworld-local-mods/LocalPatches
Mods/LocalCSharpFixes  -> .../rimworld-local-mods/LocalCSharpFixes
Mods/LocalEarlyFixes   -> .../rimworld-local-mods/LocalEarlyFixes
Mods/LocalVisualExceptions -> .../rimworld-local-mods/LocalVisualExceptions
Mods/LocalReplaceStuff -> .../rimworld-local-mods/LocalReplaceStuff
```
On Windows use junctions (`mklink /J`) rather than symlinks — they need no elevation and RimWorld
follows them transparently. Never link into `steamapps/workshop/content/294100/`: Steam owns that
tree and rewrites it on sync or file-verification.

## Syncing between the Linux and Windows machines

Both boxes play from this repo through those links, so a `git pull` changes what RimWorld loads the
moment it lands. Launch through the wrapper rather than pulling by hand:

```
./scripts/sync-and-play.sh
```

One script for both machines: on Windows run it from Git Bash (shipped with Git for Windows). It
detects the OS itself, picking the right "is RimWorld running" check, default `Mods/` path
(override with `RIMWORLD_MODS_DIR`) and link type: symlinks on Linux, junctions on Windows. It
used to be a Bash/PowerShell pair, and the two drifted apart.

It finds the repo from its own location (no per-machine config), then refuses to proceed on
anything ambiguous: RimWorld already running, a dirty working tree, or a diverged history. It also
**warns about unpushed commits**, which is the failure that actually bites — on 2026-08-14 the
Windows box worked for four days against a remote that was six commits ahead, because `git status`
compares against the *local* `origin/main` ref and will happily report "in sync" while badly stale.
Only `git fetch` can tell you otherwise, so the wrapper always fetches.

Useful flags: `--launcher none` to sync without playing, `--launcher rimsort [--rimsort-path PATH]`,
and `--no-pull` for a read-only report (allowed even while the game is running).

The pull is `--ff-only` by design. This repo commits its compiled DLLs, so a merge is never correct —
two machines building identical source produce different bytes, and git cannot reconcile them. Set
this once per clone so manual pulls are equally safe:

```
git config pull.ff only
```

**The DLLs in `Assemblies/` are committed deliberately** so a fresh checkout runs without a build
step. The tradeoff: always rebuild before committing C#, and always fetch before starting work, or
one machine will silently run stale code.

**Each machine owns the DLL for the game version it runs.** A build compiles against the installed
game's `Assembly-CSharp.dll`, so the 1.6 DLLs in `Assemblies/` can only be built on a 1.6 install and
the 1.7 DLLs in `1.7/Assemblies/` only on a 1.7 install. After a C# change, rebuild and commit the DLL
for your own version, and expect the other version's DLL to stay stale until that machine rebuilds it
— it still loads, it just lacks the change.

## Committing
This repo is public, so commit as `Jonathon Taylor <github@jdtay.com>`, never `jonathon@jdtay.com`.
Set it per clone with `git config user.email github@jdtay.com`; the global git config may still
have the old address. History was rewritten on 2026-10-16 to remove the old email. Any clone made
before then still carries the old commits, so reset it with `git fetch && git reset --hard
origin/main` instead of pushing from it.

## `modlists/`
Dated snapshots of the active mod list, written by `./scripts/export-modlist.py`: a raw
`ModsConfig_<os>_<date>.xml` (restore it into the game's `Config/` folder or import it into RimSort to get the
exact load order back) and an `active-mods_<os>_<date>.md` table of names, packageIds and workshop
links. Re-run the script after changing the mod list. It works on either machine (Python required;
override paths with `RIMWORLD_DIR`, `RIMWORLD_WORKSHOP_DIR`, `RIMWORLD_CONFIG_DIR`).

## `recovered-spec/`
The original design/triage notes (recovered from a backup) documenting what each patch fixes and why.

## Known incompatibilities
Mod combinations that broke and shouldn't be re-tried (e.g. Coventech Mechsuit
Framework × VFE Pirates) are logged in
[`KNOWN-INCOMPATIBILITIES.md`](KNOWN-INCOMPATIBILITIES.md).
