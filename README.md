# RimWorld Local Fix Mods

Three hand-made local mods that suppress errors / fix small runtime bugs from a large RimWorld 1.6
modlist, without editing the workshop mods themselves. `LocalPatches` and `LocalCSharpFixes` **must
load last** in the mod order; `LocalEarlyFixes` is the exception and must load **first** (see below).

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

## Building

Both C# mods build with the .NET SDK on either Windows or Linux:
```
dotnet build LocalCSharpFixes/Source/LocalCSharpFixes.csproj
dotnet build LocalEarlyFixes/Source/LocalEarlyFixes.csproj
```
They target `net472` without the targeting pack, via `FrameworkPathOverride` pointed at RimWorld's
bundled `Managed/` folder, and emit straight into each mod's `Assemblies/`. The `SteamRoot` /
`RimDataDir` properties are selected per-OS — update them in the `.csproj` if either install moves.

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
```
On Windows use junctions (`mklink /J`) rather than symlinks — they need no elevation and RimWorld
follows them transparently. Never link into `steamapps/workshop/content/294100/`: Steam owns that
tree and rewrites it on sync or file-verification.

## `recovered-spec/`
The original design/triage notes (recovered from a backup) documenting what each patch fixes and why.

## Known incompatibilities
Mod combinations that broke and shouldn't be re-tried (e.g. Coventech Mechsuit
Framework × VFE Pirates) are logged in
[`KNOWN-INCOMPATIBILITIES.md`](KNOWN-INCOMPATIBILITIES.md).
