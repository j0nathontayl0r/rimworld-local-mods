# Plan: Local Replace Stuff

Spec: `specs/local-replace-stuff.md`

## Amendment (conductor, 2026-10-17) — overrides the base choice below

The user chose to build the port now, knowing that Workshop "Replace Stuff - Continued" ships a native
1.7 build. These facts were verified by the conductor:
- Workshop `3526354009` (updated 2026-10-17 16:37, packageId `Memegoddess.ReplaceStuff`, `LICENSE.txt`
  © Alex Tearse-Doyle, MIT) has 52 `.cs` files under `Source/` and `Source/Replace_Stuff.csproj` targeting
  `net10.0`. It is newer than MemeGoddess's GitHub `master` (master stops at 1.6; the only branches are
  `master` and `feat/vfe-tribals-replacing`). The user has it active in the game now.

Changes to this plan:
1. **Base = the Workshop 1.7 tree**, not "fork + upstream master". Copy
   `/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3526354009` into a scratchpad `base/`
   folder (read-only input: never edit the Workshop folder) and commit it there as a local git repo so
   that the diff in Verification has a baseline. The "Vendor the Workshop 1.7 `Source/` directly"
   alternative is no longer rejected; it is the chosen base.
2. **Task 1 steps 1–3 become:**
   1. Copy the Workshop tree to `<scratch>/base/`, `git init`, commit as `Jonathon Taylor
      <github@jdtay.com>`. Record the Workshop id, the folder mtime and `sha256sum` of
      `1.7/Assemblies/Replace_Stuff.dll` in `port-notes.md` (no commit hash exists).
   2. Fetch the fork diff: `gh api repos/Hexnet111/RimWorld-ReplaceStuff-Performance-Patch/compare/MemeGoddess:master...Hexnet111:master`
      (record the fork head SHA). Port every `.cs` hunk onto the base **by hand**. Skip a hunk when
      the 1.7 base already supersedes it (for example, check which `CanPlaceBlueprintAt*` overload the
      1.7 base targets before taking the fork's `CanPlaceBlueprintAt_NewTemp` retarget; Task 4's
      checklist decides). Ignore DLL, csproj and workflow hunks. Record each hunk as ported / adapted /
      skipped with a reason.
   3. Find the `thingReplacementCache` memo and any other static per-game cache the ported hunks add.
   Steps 4–9 stay, applied to the base tree (the Workshop copy has the same `1.1`–`1.7` layout).
   Ported fork hunks carry `// LOCAL PORT (Hexnet111 performance fork): <what>` and 1.7/cache fixes
   carry `// LOCAL 1.7 PORT: <reason>`, so the base diff check accepts both markers.
3. **README placeholders** keep their names but change meaning: `{{FORK_COMMIT}}` = the Hexnet111 head
   SHA ported; `{{UPSTREAM_COMMIT}}` = "Workshop 3526354009, updated 2026-10-17" plus the DLL sha256;
   `{{UPSTREAM_COMMITS}}` = the ported / adapted / skipped hunk list; `{{MERGE_NOTES}}` = adaptations.
4. **LICENSE** keeps the Workshop `LICENSE.txt` notice (Alex Tearse-Doyle) and the fork's notice if it
   differs, then appends the port line.
5. **User steps** (README): turn off "Replace Stuff - Continued" in RimSort before turning on the port;
   `incompatibleWith` makes RimWorld warn if both are on.
6. **Verification base comparison** uses `<scratch>/base` (or re-copies the Workshop folder if the
   Workshop item has not updated since the recorded mtime).

## Approach

Vendor the Hexnet111 fork into `LocalReplaceStuff/`, like `LocalVisualExceptions` vendors Visual
Exceptions. Merge the 3 upstream commits that the fork lacks in a scratch clone first, so the vendored
tree is "fork + upstream master". That merged tree is the **base**. Every later change to the base
carries the `LOCAL 1.7 PORT` marker. The port keeps the assembly name `Replace_Stuff`, every namespace
and every public type name. The mod gets its own identity (`jdtay.replacestuff`), its own `LICENSE`
with every original notice, and a csproj modelled on `LocalVisualExceptions/Source/VisualExceptions.csproj`
(target `net10.0`, install-root `Assembly-CSharp.dll` + `UnityStubs.dll`, Harmony's 1.7
`0Harmony.dll`, `Krafs.Publicizer`).

One fact changes the size of the work. The Workshop copy of "Replace Stuff - Continued"
(`/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3526354009`) already ships a
`1.7/Assemblies/Replace_Stuff.dll`, lists 1.7 in `supportedVersions`, and bundles a `Source/` tree whose
csproj targets `net10.0`. So upstream master very probably already compiles on .NET 10, and the merge
brings most of the 1.7 work with it. The spec's goals still stand: the player wants the fork's
performance changes, the per-game cache fix, reviewed source and a Workshop-safe identity. The Workshop
`Source/` folder is a read-only **reference** for what upstream's live 1.7 code looks like. It is not the
base. I record this in the README retirement condition (see Task 2).

Decisions the spec left to the Architect:
- **Harmony ID stays `Uuugggg.rimworld.Replace_Stuff.main`.** It labels patches, not the mod. Other
  mods' `HarmonyBefore`/`HarmonyAfter` attributes keyed on it keep working. The two Replace Stuff
  mods are mutually exclusive, so no collision occurs. A `LOCAL 1.7 PORT` comment records this.
- **Cache fix = keep the fork's memoisation, clear it per game.** The clear runs in the constructor of
  the mod's `GameComponent` (`TickScheduler(Game)` in upstream). `Game` constructs its components
  before any map or thing loads, for a new game and for a loaded game. No tick code is added.
- **Layout = root + `1.7/` only, with an explicit `LoadFolders.xml`.** Upstream's 1.7 entry lists `/`
  and `1.1`…`1.7`; RimWorld resolves same relative paths to the highest folder. The port reproduces
  that resolved set with two folders (rule in Task 1, step 4). `News/` and the HugsLib `DefInjected`
  folders go, because no HugsLib is active and RimWorld never reads `News/`.
- **`Debugger.Break()` in `NewThingReplacement.CanReplace` becomes a `Verse.Log.Warning`.** On
  CoreCLR/Linux an unattached `Debugger.Break()` raises SIGTRAP and can kill the game. This is a 1.7
  safety change, marked.
- **Windows machine.** I cannot confirm its game version from here (`modlists/` holds only a Linux
  snapshot). Procedure: create the junction when `sync-and-play.sh` prints it; do not activate the mod
  on 1.6 (RimWorld marks it unsupported); when that machine runs 1.7, pull and activate in RimSort.
  README records this.
- **Research Reinvented** declares `loadBefore Uuugggg.ReplaceStuff`. The port adds
  `petetimessix.researchreinvented` to its own `loadAfter`. The effect of a load-order hint is the
  load order; the copy costs nothing and keeps the order RR requests.

Alternatives rejected:
- Vendor the Workshop 1.7 `Source/` directly: it lacks the fork's 11 commits and has no commit hash to
  cite.
- Flatten everything into the root with `Assemblies/`: the spec names `1.7/Assemblies/`.
- Remove the memoisation: it discards the fork's main change; a per-game clear satisfies criteria 12
  and 26 with one line.
- Change the Harmony ID: breaks other mods' patch-order attributes for no gain.

## Affected files

| Path | Change | Reason |
| --- | --- | --- |
| `LocalReplaceStuff/About/About.xml` | new | Own identity, credits, incompatibilities (criterion 2) |
| `LocalReplaceStuff/LICENSE` | new | Fork's MIT text + every notice + port notice (criterion 3) |
| `LocalReplaceStuff/LoadFolders.xml` | new | `<v1.7>` → `/` and `1.7` (criterion 7) |
| `LocalReplaceStuff/Source/Replace_Stuff.csproj` | new (replaces fork's) | net10.0 build against install-root DLLs (criteria 5, 6) |
| `LocalReplaceStuff/Source/**/*.cs` | vendored + marked edits | Base source, 1.7 fixes, cache fix (criteria 8–15) |
| `LocalReplaceStuff/Defs/`, `Patches/`, `Languages/`, `Textures/` | vendored | Shared XML and art |
| `LocalReplaceStuff/1.7/Defs/`, `1.7/Patches/` | vendored | Version-specific compat defs and patches |
| `LocalReplaceStuff/1.7/Assemblies/Replace_Stuff.dll` | built, committed | What the game loads (criterion 1) |
| `scripts/sync-and-play.sh` | edit | Add `LocalReplaceStuff` to the link list (criterion 17) |
| `README.md` | edit | Mod section, Deployment, Building, compat table (criterion 18) |
| `CLAUDE.md` | edit | Three rules (criterion 19) |

Not changed: `KNOWN-INCOMPATIBILITIES.md` (nothing new broke), `modlists/` (follows activation), the
game's `Mods/` folder and `ModsConfig.xml` (player-owned).

## Interfaces / contracts

**Mod identity (`About/About.xml`)**
```xml
<name>Replace Stuff (1.7 Port)</name>
<author>Uuugggg (alextd), MemeGoddess, Hexnet111; 1.7 port: Jonathon Taylor</author>
<packageId>jdtay.replacestuff</packageId>
<supportedVersions><li>1.7</li></supportedVersions>
<url>https://github.com/j0nathontayl0r/rimworld-local-mods</url>
<modDependencies> …brrainz.harmony block copied from LocalVisualExceptions… </modDependencies>
<loadAfter><li>brrainz.harmony</li><li>petetimessix.researchreinvented</li></loadAfter>
<incompatibleWith>
  <li>Memegoddess.ReplaceStuff</li><li>Uuugggg.ReplaceStuff</li>
  <li>solaris.furniturebase</li><li>gloomy.furniture.continued</li>
</incompatibleWith>
<modIconPath IgnoreIfNoMatchingField="True">TDReplaceStuffIcon</modIconPath>
```
`description` names: 1.7 port; base = fork commit `<hash>` merged with upstream `<hash>`;
source URLs `https://github.com/Hexnet111/RimWorld-ReplaceStuff-Performance-Patch`,
`https://github.com/MemeGoddess/RimWorld-ReplaceStuff`, `https://github.com/alextd/RimWorld-ReplaceStuff`.

**Load folders (`LoadFolders.xml`)**
```xml
<loadFolders>
  <v1.7>
    <li>/</li>
    <li>1.7</li>
  </v1.7>
</loadFolders>
```
Root holds `About/`, `Defs/`, `Patches/`, `Languages/`, `Textures/`, `LICENSE`, `Source/`. `1.7/` holds
`Assemblies/`, `Defs/`, `Patches/` and any file that only a version folder supplied (Task 1, step 4).
No `Assemblies/` at the root.

**Build (`Source/Replace_Stuff.csproj`)** — copy of `VisualExceptions.csproj` with:
`AssemblyName`/`RootNamespace` = `Replace_Stuff`, `Version` = `1.7.0.0`, no steamworks reference,
`OutputPath` = `$(MSBuildProjectDirectory)/../1.7/Assemblies`, `Publicize Include="Assembly-CSharp"`,
no `DEBUG` constant. If the fork ships `Properties/AssemblyInfo.cs`, keep it and set
`GenerateAssemblyInfo=false`. Build command:
```
dotnet build /home/jt/src/rimworld-local-mods/LocalReplaceStuff/Source/Replace_Stuff.csproj
```

**Cache fix hook**
```csharp
// in the static class that owns the fork's IsNewThingReplacement memo
public static void ResetPerGameCache();   // clears every per-game dictionary; LOCAL 1.7 PORT
// in TickScheduler(Game game) (or a new GameComponent with only this ctor if none exists)
NewThingReplacement.ResetPerGameCache();  // LOCAL 1.7 PORT: thing IDs repeat across saves
```

**Sync script** — the link loop in `scripts/sync-and-play.sh` line 139 becomes
`for m in LocalPatches LocalCSharpFixes LocalEarlyFixes LocalVisualExceptions LocalReplaceStuff; do`.

**README placeholders** (Task 2 writes them, Task 5 fills them, Python replaces them):
`{{FORK_COMMIT}}`, `{{UPSTREAM_COMMIT}}`, `{{UPSTREAM_COMMITS}}`, `{{MERGE_NOTES}}`,
`{{PORT_CHANGES}}`, `{{COMPAT_TABLE}}`.

**Hand-off files in the session scratchpad**
(`/tmp/claude-1000/-home-jt-src-rimworld-local-mods/187e4e9f-3aea-47be-8709-a6a45efc1ded/scratchpad/replace-stuff/`;
if your scratchpad path differs, return the real path in your result text):
- `target-checklist.md` (Task 4 → Task 1): one row per Harmony target and reflection-bound member:
  `file:line | target | 1.7 status (ok / renamed to … / missing / param-name mismatch …) | action`.
- `compat-table.md` (Task 3 → Task 5): the README table, format in Task 3.
- `port-notes.md` (Task 1 → Task 5): hashes, upstream commit subjects, merge resolutions, dropped
  files, each marked change with file and reason.

## Data / migration notes

- No save-format change. Generated defs (`ReplaceFrame_*`) and class names match the Workshop mod,
  so a save keeps loading when the player swaps between the two mods.
- Mod settings file name follows the mod folder, so settings do not carry over from the Workshop mod
  (two checkboxes and a bridge preference). Record in README.
- `.gitignore` already excludes `**/obj/`, `**/bin/` and `*.dds` (the game writes `.dds` next to
  every `.png`).
- NuGet: `Krafs.Publicizer` restores from the local cache (`LocalVisualExceptions` already uses it).
- Clone upstream repos over HTTPS. Outbound SSH is sometimes blocked on this LAN.

## Risks & mitigations

- **One bad patch class aborts the whole `PatchAll` on Harmony 3.** Mitigation: Task 4 checks every
  target, parameter name and parameter type against the 1.7 `Assembly-CSharp.dll` before the build;
  Task 1 fixes each mismatch; the player's first launch (criterion 20) is the final check.
- **Ambiguous targets.** `GenConstruct.CanPlaceBlueprintAt` (used by name in `InteractionSpot.cs` and
  `CanReplaceAnyStuff.cs`) and `Designator_Dropdown` `MethodType.Constructor` have no argument list.
  Mitigation: Task 4 lists overloads; Task 1 adds argument types only where 1.7 has more than one.
- **Compiler-generated target.** `DeliverUnderRock.cs:146` finds a local function by name
  `IsEdificeOverNonEdifice` inside `GenConstruct`. Mitigation: Task 4 confirms it exists in 1.7 IL.
- **Reflection bound at type-initialiser time.** `RemoveThingFromStatWorkerCache` (`FieldRefAccess`
  with exact types `Dictionary<Thing, StatCacheEntry>`, `ConcurrentDictionary<Thing, float>`),
  `ReplaceFrame.GetBuildingResourcesLeaveCalculator`, `GenReplace.GiveShortHash` and
  `takenHashesPerDeftype`. A mismatch throws at first use, not at startup. Mitigation: Task 4 lists them
  with their 1.7 types.
- **UnityStubs gaps.** `Settings.cs` uses `GUIUtility.GUIToScreenPoint`, `Event.current`,
  `EventType.Repaint`; drawing code uses `Material`, `Graphic`, `Color`. Mitigation: the build reports a
  missing member; Task 1 applies the smallest marked change.
- **Merge conflicts.** Likely in csproj, `About.xml`, `LoadFolders.xml`, DLLs. Mitigation: the port
  replaces those files anyway; for `.cs` conflicts take upstream's hunk when it is a 1.7 change or one
  of the 3 named fixes, keep the fork's hunk when it is a performance change, combine by hand when both
  touch one line, and record every resolution in `port-notes.md` → README.
- **The fork is not a Git fork of upstream.** Mitigation: cherry-pick the 3 commits instead of merging;
  record the hashes the same way.
- **Duplicate `Autodoor` build command.** Upstream loads both `1.5/Patches/ReplaceStuff15Patches.xml`
  and `…16Patches.xml` on 1.7. The port ships only the `1.7/Patches` file. Accepted: the 1.5 file is a
  superset guard for a `<building>` node vanilla always has.
- **`Assets.zip` content unknown.** Mitigation: Task 1 runs `unzip -l`; if it holds only source art or
  files that also exist unpacked, drop it; otherwise unpack what a def references and record it.
- **Game running during development.** The repo folder is not linked into `Mods/` until the player
  creates the link, so builds cannot touch the live game. Developers never create the link, never edit
  `Mods/` or `ModsConfig.xml`.
- **`grep` is ugrep in this shell.** It rewrites files badly. Use `/usr/bin/grep` for searches and
  Python for in-place edits.
- **Criterion 15 (no new tick code).** The base already has `TickScheduler.GameComponentTick` and a
  `Building_Cooler.TickRare` transpiler. Accepted: they are base code. The port adds none; Task 1
  proves it with a diff.

## Task breakdown

Tasks 1–4 are independent. Task 5 runs after all four. Developers do not commit or push; the
conductor commits as `Jonathon Taylor <github@jdtay.com>`.

1. **Vendor, merge, port, build** — Owns every file under `LocalReplaceStuff/`. Writes
   `port-notes.md` to the scratchpad. Steps:
   1. In a new empty scratchpad folder, `git clone https://github.com/Hexnet111/RimWorld-ReplaceStuff-Performance-Patch fork`
      (branch `master`); `git remote add upstream https://github.com/MemeGoddess/RimWorld-ReplaceStuff`;
      `git fetch upstream`. Record `git rev-parse master` and `upstream/master`. Record
      `git log --oneline master..upstream/master` (expect 3 commits: "Basic Dropdowns fix (#13)",
      "Marked Gloomy as incompatible due to too many bug reports", and one more) and
      `git log --oneline upstream/master..master` (expect 11).
   2. `git -c user.name="Jonathon Taylor" -c user.email=github@jdtay.com merge --no-ff upstream/master`.
      Resolve conflicts with the rules in Risks. Save the merged tree as the base:
      `git archive HEAD | tar -x -C ../base`. Record every resolution.
   3. Read the fork's 11 commits (`git log -p upstream/master..master`, skip DLL hunks). Find the
      `IsNewThingReplacement` memoisation and every other static per-game cache the fork added. Note
      file and line.
   4. Copy the base into `/home/jt/src/rimworld-local-mods/LocalReplaceStuff/` with this layout rule:
      root files and folders stay at the root, except `*.sln`, `.editorconfig`, `News/`, `Assets.zip`,
      `About/Preview.png`, `About/PublishedFileId.txt`, any `Assemblies/` and any `*.dll`;
      `1.7/` keeps its `Defs/`, `Patches/`, `Textures/` (drop a `1.7/` file that is byte-identical to
      the root file at the same relative path); from `1.1`–`1.6`, copy into `1.7/` every file whose
      relative path exists in neither the root nor `1.7/`, except `Assemblies/`, `Assets.zip`,
      `Patches/ReplaceStuff15Patches.xml`, `News/`, `Textures/TDReplaceStuffIcon.png`; delete every
      `Languages/*/DefInjected/HugsLib.UpdateFeatureDef/` folder; then delete `1.1`–`1.6`. Decide
      `Assets.zip` per Risks. List every dropped file in `port-notes.md`.
   5. Write `About/About.xml`, `LICENSE` (fork's text verbatim, then append
      `Copyright (c) 2026 Jonathon Taylor (RimWorld 1.7 port)`), `LoadFolders.xml` and
      `Source/Replace_Stuff.csproj` per Interfaces. Delete the fork's csproj and any `packages.config`.
   6. Apply 1.7 changes with the smallest edit, each with a `// LOCAL 1.7 PORT: <reason>` comment (or
      `<!-- LOCAL 1.7 PORT: … -->` in XML). Use `target-checklist.md` from Task 4 if present; verify
      yourself any patch the checklist lacks (the fork's commits can add patches). Replace
      `Debugger.Break()` with `Verse.Log.Warning("[Replace Stuff] HasMatchingReplacementTag threw: " + ex)`.
      Add a marked comment at the `new Harmony("Uuugggg.rimworld.Replace_Stuff.main")` site in the Mod
      constructor that records why the ID stays. Add a marked comment or `<!-- -->` next to every
      remaining `Memegoddess.ReplaceStuff` / `Uuugggg.ReplaceStuff` string that is not an
      `incompatibleWith` entry.
   7. Cache fix: add `ResetPerGameCache()` on the memo's owner and call it from the `GameComponent`
      constructor (Interfaces). If the base has no `GameComponent`, add
      `GameComponent_ReplaceStuffCache(Game)` with only that constructor.
   8. `dotnet build …/Replace_Stuff.csproj`. Fix errors with marked minimal edits. Repeat until exit 0.
   9. Verify (all must pass):
      - `ls LocalReplaceStuff/1.7/Assemblies` → only `Replace_Stuff.dll`.
      - `find LocalReplaceStuff \( -name '*.dll' -o -name '*.pdb' -o -name '*.deps.json' \) -not -path '*/obj/*'` → one line.
      - `find LocalReplaceStuff -maxdepth 1 -type d -regex '.*/1\.[1-6]'` → empty.
      - `diff -ru <scratch>/base/Source LocalReplaceStuff/Source` → every hunk sits within 3 lines of
        a `LOCAL 1.7 PORT` marker; no new `Tick`, `TickRare`, `TickLong`, `GameComponentTick`,
        `MapComponentTick` method appears in the `+` lines.
      - `/usr/bin/grep -rn -e Memegoddess.ReplaceStuff -e Uuugggg.ReplaceStuff LocalReplaceStuff` →
        only `incompatibleWith` lines or lines with a reason comment.
      - `/usr/bin/grep -rn "AssemblyName\|namespace \|public class\|public static class" …` shows the
        names the base had (spot-check `Replace_Stuff.ReplaceFrame`, `Replace_Stuff.Designator_ReplaceStuff`,
        `Replace_Stuff.NewThing.NewThingReplacement`, `Replace_Stuff.InterchangeableItems`).
      - `ls LocalReplaceStuff/About/About.xml LocalReplaceStuff/LICENSE LocalReplaceStuff/LoadFolders.xml`.
   Done when: build exits 0, the six checks pass, and `port-notes.md` holds hashes, the 3 upstream
   commit subjects, merge resolutions, dropped files, and one line per marked change.

2. **Repo wiring** — Owns `scripts/sync-and-play.sh`, `README.md`, `CLAUDE.md`. Do not touch
   `LocalReplaceStuff/`.
   - `sync-and-play.sh`: add `LocalReplaceStuff` to the `for m in …` list (line 139). No other change.
   - `README.md`: change "Three hand-made local mods" to "Five local mods" (count the sections);
     add a `### \`LocalReplaceStuff\` — packageId \`jdtay.replacestuff\` (RimWorld 1.7 only)` section
     after `LocalVisualExceptions` with: what it is; credits with links (Uuugggg/alextd
     `https://github.com/alextd/RimWorld-ReplaceStuff`, MemeGoddess
     `https://github.com/MemeGoddess/RimWorld-ReplaceStuff`, Hexnet111
     `https://github.com/Hexnet111/RimWorld-ReplaceStuff-Performance-Patch`); base = fork commit
     `{{FORK_COMMIT}}` merged with upstream `{{UPSTREAM_COMMIT}}`; merged upstream commits
     `{{UPSTREAM_COMMITS}}`; `{{MERGE_NOTES}}`; the cache fix (fork memo keyed on thing IDs, IDs repeat
     across saves, cleared in the `GameComponent` constructor each game); `{{PORT_CHANGES}}`; why the
     packageId differs and that `incompatibleWith` blocks both Workshop copies; the Harmony ID stays;
     settings do not carry over; the Windows procedure (Approach); the retirement condition: "Retire
     when Workshop `3526354009` carries the fork's performance changes and the per-game cache clear,
     or when Dubs Performance Analyzer shows the fork's changes give no benefit; the Workshop build
     already supports 1.7"; a `#### Compatibility patches keyed on the old packageIds` subsection
     holding `{{COMPAT_TABLE}}`; and "Mod Error Checker" style errors seen at first launch go here
     (criterion 20) as an empty "Known log lines" list.
     Deployment block: add `Mods/LocalReplaceStuff -> .../rimworld-local-mods/LocalReplaceStuff`.
     Building section: add `dotnet build LocalReplaceStuff/Source/Replace_Stuff.csproj` next to the
     Visual Exceptions sentence and say it is 1.7-only, uses `Krafs.Publicizer`, and keeps upstream's
     assembly name.
   - `CLAUDE.md`, under "Local mods and links", add three bullets: (a) `LocalReplaceStuff` uses its own
     packageId `jdtay.replacestuff` and declares `Memegoddess.ReplaceStuff` and `Uuugggg.ReplaceStuff`
     incompatible; activate exactly one Replace Stuff; (b) after any change to the active mod list,
     re-run the compatibility-patch scan for the old packageIds and update the README table;
     (c) local changes in `LocalReplaceStuff` carry the `LOCAL 1.7 PORT` marker and `LICENSE` keeps
     every original notice.
   Done when: `/usr/bin/grep -c LocalReplaceStuff scripts/sync-and-play.sh` ≥ 1; `/usr/bin/grep -c
   '{{' README.md` equals 6; the three CLAUDE.md bullets exist; `bash -n scripts/sync-and-play.sh`
   exits 0. Prose in ASD-STE100 style, British English.

3. **Compatibility-patch survey** — Owns no repo file. Writes `compat-table.md` and the scan script
   to the scratchpad (script in a different folder from any downloaded data).
   - Read the active list from
     `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Config/ModsConfig.xml` (read-only;
     fall back to `modlists/ModsConfig_linux_2026-10-17.xml`). Map each packageId to its folder under
     `/mnt/games/SteamLibrary/steamapps/workshop/content/294100/*/About/About.xml`
     (case-insensitive) or `Mods/`.
   - For each active mod, with Python (`python3 -I`), search text files (`.xml`, `.json`, `.txt`,
     `.cs`) for `memegoddess.replacestuff` and `uuugggg.replacestuff` (case-insensitive), and every
     `.dll` for the byte strings `Replace_Stuff`, `ReplaceStuff`, `Memegoddess.ReplaceStuff`,
     `Uuugggg.ReplaceStuff`, `Uuugggg.rimworld.Replace_Stuff.main`, each in UTF-8 and UTF-16LE.
     Report the DLL's folder so the reader sees which game version loads it.
   - For each hit, read the referencing code (`~/.dotnet/tools/ilspycmd` on the DLL, or the XML) and
     write one row: `| Mod (packageId, workshop id) | File | What the reference does | Keyed on |
     Decision |`. `Keyed on` is `packageId`, `mod name`, or `type name`. Decisions: type-name references
     keep working (criterion 10); packageId references get "port supplies it: <how>" or "accepted
     loss: <visible effect>"; name-keyed references note the name the code expects.
   - Seed rows (verify, do not assume): Research Reinvented `loadBefore Uuugggg.ReplaceStuff`
     (`2868392160/About/About.xml:22`) → port adds `petetimessix.researchreinvented` to `loadAfter`;
     Use This Instead `3396308787/replacements.json` (old→new Workshop mapping, no effect on the port);
     VFE Architect `ModLister.HasActiveModWithName("Replace Stuff")` in its 1.3 source (check the 1.6
     DLL; name-keyed; "Replace Stuff - Continued" already fails it, so no new loss); Combat Extended
     `ModPatches/*` comments "Replace Stuff Thickness" (plain words, not a reference; exclude).
   - Add a row with "no reference found" for each mod the spec names in criterion 27: Smarter
     Construction, Fix Styled Blueprints, Vehicle Framework, `[BTD] Gravship Blueprints`, Bigger
     Gravships, `[sbz] Gravship Storage`, Complementary Odyssey.
   - If a hit needs a `loadAfter`/`loadBefore` entry or an XML patch in the port, write it under a
     `## Required About.xml changes` heading in `compat-table.md`.
   Done when: `compat-table.md` holds the table, the required-changes section (possibly "none beyond
   Research Reinvented"), and the exact script invocation; the script read nothing outside the mod
   folders and wrote nothing outside the scratchpad.

4. **1.7 target survey** — Owns no repo file. Writes `target-checklist.md` to the scratchpad. Input:
   the reference source at `/mnt/games/SteamLibrary/steamapps/workshop/content/294100/3526354009/Source`
   (read-only; it mirrors upstream's current code) and the 1.7
   `/mnt/games/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll` via `~/.dotnet/tools/ilspycmd`.
   - Enumerate every `[HarmonyPatch(...)]`, `TargetMethod`/`TargetMethods`, `harmony.Patch(...)`
     site and every `AccessTools.Method/Field/Property/FieldRefAccess/MethodDelegate/TypeByName`
     call. Start from `/usr/bin/grep -rn -E "HarmonyPatch\(|TargetMethod|harmony\.Patch\(|AccessTools\."`.
   - For each Harmony target: confirm the type and member exist in 1.7; list overloads; confirm each
     patch-method parameter name and type matches the original (Harmony 3 rejects mismatches;
     `__instance`, `__result`, `___field` need the exact field type); confirm `MethodType.Getter`
     targets are properties. Flag ambiguity (`CanPlaceBlueprintAt`, `Designator_Dropdown` ctor,
     `GenSpawn.Spawn`, `GenSpawn.SpawningWipes`, `GenConstruct.CanConstruct`, `Mineable.TrySpawnYield`,
     `GenConstruct.GetWallAttachedTo`).
   - For each transpiler (criterion 14): dump the 1.7 IL of the target (`ilspycmd -il -t <Type>`) and
     confirm the searched pattern exists. Named ones: `GenConstruct.CanPlaceBlueprintAt_NewTemp`
     (`GridsUtility.Fogged(IntVec3, Map)` call followed by `brfalse`), `PlaceWorker_Conduit.AllowsPlacing`
     (`ldloc.0` loop, `entityDefToBuild` load, `thingToIgnore` as arg 5), `Blueprint.TryReplaceWithSolidThing`
     (`FirstBlockingThing`), `GenConstruct.CanPlaceBlueprintOver` (`EdificeUtility.IsEdifice`) and its
     local function `IsEdificeOverNonEdifice`, `GenConstruct.CanBuildOnTerrain`
     (`GetAffordances` + `List.Contains`), `ThingUtility.CheckAutoRebuildOnDestroyed`,
     `Frame.CompleteConstruction` (`GenSpawn.Spawn` overload), `JobGiver_GetRest.TryGiveJob`
     (`RestUtility.FindBedFor(Pawn)`), `GenSpawn.Refund` (`Minifiable` getter),
     `GenConstruct.BlocksConstruction` (`Fillage` getter), `ThingDef.CanInteractThroughCorners`,
     `Mineable.TrySpawnYield`, `GhostUtility.GhostGraphicFor`, `Graphic_Appearances.Init`,
     `GenConstruct.HandleBlockingThingJob`, `Designator_Build.CanDesignateCell`/`CanPlaceBlueprintAt`
     (`Rot4.op_Equality`), `PlaceWorker_Cooler/Vent.DrawGhost` and `Building_Cooler.TickRare`
     (`IntVec3.North`), `PlaceWorker_PreventInteractionSpotOverlap.AllowsPlacing`,
     `GenConstruct.CanConstruct` (`FirstBlockingThing`).
   - For each reflection-bound member list the 1.7 declaration: `StatDef.workerInt`,
     `StatWorker.temporaryStatCache`, `StatWorker.immutableStatCache`,
     `GenLeaving.GetBuildingResourcesLeaveCalculator`, `ShortHashGiver.GiveShortHash`,
     `ShortHashGiver.takenHashesPerDeftype`, `GraphicDatabase.Get` overloads, `GraphicData.CopyFrom`,
     `Graphic.data`, `ThingDef.mineable`, `ThingDef.entityDefToBuild`, `ModLister.AnyModActiveNoSuffix`,
     `GenConstruct.HasMatchingReplacementTag`, `ReservationManager` methods with a `LocalTargetInfo`
     parameter (for `ReserveSharing`, which patches the generic one with `JobDriver_TakeToBed`).
   - Note any `UnityEngine` member the source uses that `UnityStubs.dll` lacks
     (`ilspycmd -l` on `UnityStubs.dll`; check `GUIUtility.GUIToScreenPoint`, `Event.current`,
     `EventType.Repaint`, `ReorderableWidget` is Verse).
   Done when: `target-checklist.md` has one row per site with status and a concrete action
   ("none", "add argument types `[typeof(...)]`", "rename parameter `x` → `y`", "target renamed to …",
   "pattern missing: …"), plus a short "fork-only patches not covered" note telling Task 1 to check
   anything the fork added.

5. **Integrate** (after Tasks 1–4) — Owns `README.md` and `LocalReplaceStuff/About/About.xml`.
   - With Python, replace the six README placeholders from `port-notes.md` and `compat-table.md`.
   - Apply `## Required About.xml changes` from `compat-table.md` to `About.xml`, with an XML
     comment `<!-- LOCAL 1.7 PORT: copied from <mod>'s loadBefore/loadAfter -->` per entry.
   - Run the Verification plan commands below. No rebuild is needed unless a `.cs` file changed.
   Done when: `/usr/bin/grep -c '{{' README.md` is 0, `About.xml` parses
   (`python3 -I -c "import xml.etree.ElementTree as E,sys;E.parse(sys.argv[1])" …/About.xml`), and
   every verification command passes.

## Verification plan

Offline, by the Developer (Task 5) and QA:
```
cd /home/jt/src/rimworld-local-mods
dotnet build LocalReplaceStuff/Source/Replace_Stuff.csproj              # exit 0, 0 errors
ls LocalReplaceStuff/1.7/Assemblies                                      # Replace_Stuff.dll only
find LocalReplaceStuff \( -name '*.dll' -o -name '*.pdb' -o -name '*.deps.json' \) -not -path '*/obj/*'
find LocalReplaceStuff -maxdepth 1 -type d -regex '.*/1\.[1-6]'          # empty
cat LocalReplaceStuff/LoadFolders.xml                                    # <v1.7> with / and 1.7 only
/usr/bin/grep -n -e packageId -e supportedVersions -e incompatibleWith -e loadAfter -A4 LocalReplaceStuff/About/About.xml
/usr/bin/grep -rn "LOCAL 1.7 PORT" LocalReplaceStuff --include='*.cs' --include='*.xml' --include='*.csproj'
/usr/bin/grep -rn -e Memegoddess.ReplaceStuff -e Uuugggg.ReplaceStuff LocalReplaceStuff
/usr/bin/grep -rn -E "void (Tick|TickRare|TickLong|GameComponentTick|MapComponentTick)\(" LocalReplaceStuff/Source
/usr/bin/grep -n "ResetPerGameCache" -r LocalReplaceStuff/Source          # definition + call in a GameComponent ctor
/usr/bin/grep -n "for m in" scripts/sync-and-play.sh                      # includes LocalReplaceStuff
bash -n scripts/sync-and-play.sh
/usr/bin/grep -c '{{' README.md                                           # 0
/usr/bin/grep -n "LocalReplaceStuff" README.md CLAUDE.md
```
Base comparison (QA re-creates the base from the recorded hashes if the scratchpad is gone):
`git clone` the fork, `git merge` the recorded upstream commit, `git archive` → `diff -ru base/Source
LocalReplaceStuff/Source`; every hunk sits next to a marker; no tick method is added.

Criterion 17 needs a clean tree and network: after the conductor commits,
`./scripts/sync-and-play.sh --no-pull --launcher none` prints
`WARN: LocalReplaceStuff is MISSING from Mods/ - recreate with: ln -s '…/LocalReplaceStuff' '…/Mods/LocalReplaceStuff'`.
Before the commit, verify by reading the loop list.

Criterion 13/14: QA re-runs three rows of `target-checklist.md` at random with `ilspycmd` against
`/mnt/games/SteamLibrary/steamapps/common/RimWorld/Assembly-CSharp.dll`.

Player, in game (criteria 20–27, after RimSort activation and the `ln -s` the script prints): start
RimWorld 1.7; check `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log` for
`Harmony`, `Replace_Stuff`, `TypeInitializationException`; enable Workshop `3526354009` too and confirm
the incompatibility warning; wood wall → stone wall blueprint; floor, door, furniture replacement;
cooler and vent on a wall; bridge then building over water; load save A then save B with no restart
and repeat one replacement; one replacement on a gravship substructure with Smarter Construction,
Fix Styled Blueprints and Vehicle Framework active. Any log line from the mod goes into the README
"Known log lines" list with an explanation.

## Questions for PO

None. Two facts the PO should know, recorded above and not blocking: the Workshop mod already ships a
1.7 build (the port's value is the fork's changes, the cache fix, reviewed source and the own identity;
the README retirement condition reflects this); and the Windows game version is unconfirmed from this
machine (README documents the junction-but-do-not-activate procedure).
