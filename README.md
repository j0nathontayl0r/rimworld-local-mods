# RimWorld Local Fix Mods

Three hand-made local mods that suppress errors / fix small runtime bugs from a large RimWorld 1.6
modlist, without editing the workshop mods themselves. `LocalPatches` and `LocalCSharpFixes` **must
load last** in the mod order; `LocalEarlyFixes` is the exception and must load **first** (see below).

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

## Building

Both C# mods build with the .NET SDK on either Windows or Linux:
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
same way from its own `Source/`.

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

## `recovered-spec/`
The original design/triage notes (recovered from a backup) documenting what each patch fixes and why.

## Known incompatibilities
Mod combinations that broke and shouldn't be re-tried (e.g. Coventech Mechsuit
Framework × VFE Pirates) are logged in
[`KNOWN-INCOMPATIBILITIES.md`](KNOWN-INCOMPATIBILITIES.md).
