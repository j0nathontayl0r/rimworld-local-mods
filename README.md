# RimWorld Local Fix Mods

Two hand-made local mods that suppress errors / fix small runtime bugs from a large RimWorld 1.6
modlist, without editing the workshop mods themselves. Both **must load last** in the mod order.

## Mods

### `LocalPatches` — packageId `local.errorfixes`
XML `PatchOperation` mod. Each `Patches/FixN_*.xml` is self-contained and wrapped so failures are
silent (`success=Always` / `PatchOperationFindMod`). Also ships replacement textures for a couple of
mods whose 1.6 texture paths broke (`Textures/`).

### `LocalCSharpFixes` — packageId `local.csharpfixes`
Harmony patch mod. Requires the **Harmony** mod. Suppresses NREs / log spam and fixes small runtime
bugs where an XML patch can't reach. Third-party targets are resolved by name at startup and skipped
(with a log line) if a mod is absent, so a missing target never breaks startup.

**Build (Linux, from `LocalCSharpFixes/Source/`):**
```
dotnet build
```
Targets `net472` via `FrameworkPathOverride` pointed at RimWorld's bundled `Managed/` folder; the DLL
is emitted straight into `LocalCSharpFixes/Assemblies/`. Update the `RimManaged` / `HarmonyDll` paths
in the `.csproj` if the library location changes.

## Deployment
Developed here on a backed-up drive and symlinked into the RimWorld `Mods/` folder:
```
Mods/LocalPatches      -> .../rimworld-local-mods/LocalPatches
Mods/LocalCSharpFixes  -> .../rimworld-local-mods/LocalCSharpFixes
```

## `recovered-spec/`
The original design/triage notes (recovered from a backup) documenting what each patch fixes and why.
