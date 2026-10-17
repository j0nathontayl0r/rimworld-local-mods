# Local Replace Stuff

## Problem
The player runs RimWorld 1.7 (build 4981, Waylith engine, .NET 10). "Replace Stuff" lets a player put a wall, floor, door or furniture blueprint over an existing one. Colonists then replace the old thing in place, with no deconstruct step. It also puts coolers and vents into walls and builds bridges over water. No release supports 1.7. Upstream (MemeGoddess, Workshop "Replace Stuff - Continued") and the Hexnet111 performance fork both target 1.6 and ship prebuilt net472 DLLs. The fork's replacement cache also has a bug: it keeps entries across games, and thing IDs repeat across saves. Thus a second save loaded without a restart can get stale results. The player wants Replace Stuff on 1.7, built from source that the player reviewed, not from a downloaded DLL. The player can publish the mod on the Steam Workshop later, so the mod must not need rework for that step.

## Goal
The player can use Replace Stuff in RimWorld 1.7 from a local mod that this repo builds from reviewed source, with an identity that can go on the Workshop later without change.

## Non-goals
- No 1.6 build. The mod is 1.7-only, like `LocalVisualExceptions`.
- No new Replace Stuff features. No change to gameplay behaviour, except the cache fix and 1.7 compatibility.
- No performance claims. The player profiles the mod with Dubs Performance Analyzer after install.
- No activation of the mod in the game's mod list. The player does this in RimSort.
- No Workshop upload and no Workshop page.
- No fixes to other mods' Replace Stuff compatibility code.

## User stories
- As the player, I want to place a replacement blueprint over an existing wall, floor, door or furniture item, so that colonists replace it without a deconstruct step.
- As the player, I want to place coolers and vents in walls and build bridges over water on 1.7, so that I keep the Replace Stuff features I used on 1.6.
- As the player, I want the mod built from source in this public repo, so that I know what code runs in my game.
- As the player, I want to load a second save without a restart and get correct replacement behaviour, so that stale data from the previous game has no effect.
- As the maintainer, I want the mod linked and documented like the other local mods, so that both machines get it through `scripts/sync-and-play.sh`.
- As the maintainer, I want a unique identity, correct credits and the MIT licence from the start, so that a later Workshop release needs no rename or metadata rework.

## Acceptance criteria

### Verifiable offline (by reading the repo or by running a build)

1. Given the repo, when you list `LocalReplaceStuff/`, then it contains `About/About.xml`, `LICENSE`, `Source/` with a `.csproj`, and `1.7/Assemblies/` with the built DLL.
2. Given `LocalReplaceStuff/About/About.xml`, when you read it, then:
   - `packageId` is `jdtay.replacestuff` (see Assumptions). No other mod uses this packageId.
   - `name` is distinct from "Replace Stuff" and "Replace Stuff - Continued" (for example "Replace Stuff (1.7 Port)").
   - `supportedVersions` lists only `1.7`.
   - `url` points to this repo's GitHub page.
   - `modDependencies` and `loadAfter` list `brrainz.harmony`.
   - `incompatibleWith` lists `Memegoddess.ReplaceStuff` and `Uuugggg.ReplaceStuff`.
   - `author` and `description` credit Uuugggg (alextd), MemeGoddess and Hexnet111, and identify the build as a 1.7 port.
   - `description` names the base fork commit and gives the source URLs of upstream and of the fork.
3. Given `LocalReplaceStuff/LICENSE`, when you read it, then it holds the MIT licence text with every copyright notice from the base fork. The port adds its own notice and removes none.
4. Given the repo, when you search `LocalReplaceStuff/`, then it contains no DLL from upstream or from the fork. There are no `1.1`–`1.6` assembly folders. The only DLL is the one that the local build writes into `1.7/Assemblies/`.
5. Given a clean checkout on the Linux 1.7 install, when you run `dotnet build` on the `LocalReplaceStuff` project, then the build exits with code 0 and no errors. The build writes the mod DLL into `LocalReplaceStuff/1.7/Assemblies/`.
6. Given `LocalReplaceStuff/1.7/Assemblies/` after a build, when you list it, then it holds only the mod DLL. It holds no `0Harmony.dll`, `mscorlib.dll`, `Assembly-CSharp.dll` or other copied reference.
7. Given the mod's load-folder configuration, when RimWorld 1.7 reads it, then 1.7 loads exactly one copy of the mod DLL and the shared XML folders (Defs, Patches, Languages, Textures) once.
8. Given the source, when you compare it with the base fork, then these upstream commits are present:
   - "Basic Dropdowns fix (#13)".
   - "Marked Gloomy as incompatible due to too many bug reports". `About.xml` shows the Gloomy incompatibility.
   - The third commit that the fork lacks.
9. Given the source, when you search for the `LOCAL 1.7 PORT` marker, then every change from the merged base marks itself with it. This includes the cache fix, each 1.7 API change and each packageId reference.
10. Given the source, when you compare it with the base fork, then the assembly name, namespaces and public type names are unchanged. Other mods that find Replace Stuff types by name still find them.
11. Given the source and XML, when you search for `Memegoddess.ReplaceStuff` and `Uuugggg.ReplaceStuff`, then each remaining match is either an `incompatibleWith` entry or has a comment that gives its reason. No code path that identifies this mod uses the old packageIds.
12. Given the source, when you read the memoisation for `IsNewThingReplacement`, then a code path clears it each time a game starts or loads. No entry from an earlier game can reach a lookup in the next game.
13. Given each Harmony patch in the source (attribute or manual `Patch` call), when you check its target against the 1.7 `Assembly-CSharp.dll`, then the target type and method exist with the patched parameter types. Each patch method's parameter names and types match its target, as Harmony 3.x requires.
14. Given each transpiler in the source, when you inspect the IL of its 1.7 target method, then every instruction pattern that the transpiler searches for exists. This includes the fogged-blueprint transpiler on `GenConstruct.CanPlaceBlueprintAt_NewTemp`, or on its 1.7 equivalent if the name changed.
15. Given a diff of the port against the base fork, when you read it, then the port adds no code that runs every tick. This includes `Tick`, `TickRare`, `TickLong`, map-component and game-component tick methods.
16. Given the active mod list and the Workshop content folder, when you search the active mods for `Memegoddess.ReplaceStuff` and `Uuugggg.ReplaceStuff`, then README lists each match: the mod, what the match does, and one of these decisions:
    - The port supplies the same compatibility itself, with a reason.
    - The player accepts its loss, with the visible effect.
17. Given `scripts/sync-and-play.sh`, when you run it with `--no-pull` and the link is absent, then it warns that `LocalReplaceStuff` is missing from `Mods/` and prints the link command.
18. Given `README.md`, when you read it, then:
    - A `LocalReplaceStuff` section names the packageId, the base fork commit, the merged upstream commits, the cache fix and the retirement condition.
    - The section credits Uuugggg (alextd), MemeGoddess and Hexnet111 with links to their repos.
    - The section contains the compatibility table from criterion 16.
    - The Deployment block lists `Mods/LocalReplaceStuff`.
    - The Building section gives the build command.
19. Given `CLAUDE.md`, when you read it, then it states:
    - `LocalReplaceStuff` uses its own packageId and declares the Workshop copies incompatible. The player activates only one Replace Stuff.
    - A change to the active mods needs a new check for compatibility patches keyed on the old packageIds.
    - Local changes carry the `LOCAL 1.7 PORT` marker, and `LICENSE` keeps every original notice.

### Needs the player's in-game test (after RimSort activation)

20. Given the mod is active after Harmony, when RimWorld 1.7 starts, then `Player.log` shows no Harmony error and no exception from Replace Stuff. If an error appears, README lists it with an explanation.
21. Given the mod and Workshop "Replace Stuff - Continued" are both enabled in the mod list, when RimWorld or RimSort checks the list, then it reports the two mods as incompatible.
22. Given a colony with a wood wall, when the player places a stone wall blueprint on it, then a replacement frame appears without a deconstruct designation. When construction completes, the stone wall stands in that cell and no deconstruct designation remains.
23. Given existing floors, doors and furniture, when the player places a replacement of each type over them, then colonists replace each one in place.
24. Given a wall, when the player places a cooler or vent blueprint on it, then colonists build the cooler or vent in the wall cell.
25. Given shallow or deep water, when the player places a bridge and then a building blueprint on it, then colonists build the bridge first, then the building.
26. Given the player loads save A, then loads save B without a restart, when the player places replacement blueprints in save B, then they behave the same as after a fresh start. The log shows no Replace Stuff errors.
27. Given Smarter Construction, Fix Styled Blueprints, Vehicle Framework and the gravship mods are active, when the player places and completes replacement blueprints (including on a gravship substructure), then the log shows no errors from Replace Stuff or from those mods.

## Out of scope / deferred
- A 1.6 build: the Linux install is 1.7-only and cannot compile net472 against 1.6.
- Performance tuning beyond the fork's changes: it waits for the player's Dubs Performance Analyzer results.
- An update of the `modlists/` snapshot: it follows the player's RimSort activation, under the existing CLAUDE.md rule.
- Workshop publication: the upload, the Workshop page text, a final `Preview.png` and mod icon, a `PublishedFileId.txt`, and a release folder without `Source/` or the local link. The player decides on this later. Until then, `About/Preview.png` can be a placeholder or absent.
- Contact with the upstream maintainers: recommended before any Workshop release (MemeGoddess for "Replace Stuff - Continued", Hexnet111 for the fork), but MIT does not require it and it does not block this work.
- Sending the cache fix upstream to Hexnet111 or MemeGoddess: optional, after in-game verification.
- Removal of this mod when an official 1.7 build ships: README records the retirement condition, but the removal is separate work.

## Open questions
- If the merge of the 3 upstream commits into the fork conflicts, the Architect decides the resolution and records it in README.
- The Architect must find each 1.7 API change that breaks the build or a patch target. The Developer fixes each one with the smallest change.
- Some XML in `Defs/` or `Patches/` can refer to defs that 1.7 removed. The Architect decides if the port needs a 1.7-specific XML change.
- The source can compare a packageId with its own (for example a `ModLister` lookup or a Harmony ID). The Architect finds each such reference and decides which ones change to `jdtay.replacestuff` (criterion 11).
- The Windows machine can run 1.6. There, the local mod does not support the game version, and `scripts/sync-and-play.sh` still expects its junction. The Architect confirms the Windows game version and documents the procedure for that machine.

## Assumptions
- **packageId: `jdtay.replacestuff`, not `Memegoddess.ReplaceStuff`.** Reason: a later Workshop release needs a unique packageId that does not collide with "Replace Stuff - Continued". A change after release breaks saves and mod lists, so the choice happens now. `jdtay` matches the public repo author's domain.
  - Cost: other mods' `IfModActive`, `loadAfter`, `loadBefore` and `incompatibleWith` entries keyed on `Memegoddess.ReplaceStuff` or `Uuugggg.ReplaceStuff` do not fire for this mod.
  - Mitigation: criterion 16 lists each such entry in the active mods. The port copies only the entries with a visible effect into its own `About.xml` load order or its own XML patches. Compatibility code that finds Replace Stuff types by name still works, because criterion 10 keeps the type names.
  - `incompatibleWith` on both old packageIds stops the player from activating two Replace Stuff copies.
- The `LocalVisualExceptions` shared-packageId precedent does not apply, because that mod has no publication plan.
- The base is the Hexnet111 fork, branch `master`, at the commit current when work starts. README records the hash.
- The build follows `LocalVisualExceptions/Source/VisualExceptions.csproj`: target `net10.0`, reference the install-root `Assembly-CSharp.dll`, `UnityStubs.dll` and Harmony's 1.7 `0Harmony.dll`.
- The mod folder name is `LocalReplaceStuff`, and the DLL keeps upstream's assembly name.
- The built DLL is committed, like the other local mods.
- The cache fix keeps the fork's memoisation and clears it per game. The Architect can remove the memoisation instead, if the result satisfies criterion 12 and criterion 26.
