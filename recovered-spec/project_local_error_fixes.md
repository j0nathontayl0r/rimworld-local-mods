---
name: project-local-error-fixes
description: "Local Error Fixes mod location, purpose, and all XML patches created to suppress workshop mod errors"
metadata: 
  node_type: memory
  type: project
  originSessionId: 7092a17f-b1f4-4321-b5a3-166ffa26e5be
---

## Local Error Fixes Mod

A hand-crafted local mod created to suppress startup errors from workshop mods without modifying the workshop files directly.

**Location:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\LocalPatches\`  
**packageId:** `local.errorfixes`  
**Load order:** Must load LAST in the mod list.

**Why:** Workshop mods sometimes have broken cross-references or outdated defs that generate log errors. Rather than editing workshop files (which get overwritten on update), we create XML patches here.

**How to apply:** When triaging new errors, add new `FixN_Description.xml` files in the `Patches/` subfolder using `PatchOperationSequence` with `<success>Always</success>` so failures are silent.

---

## Existing Patches (as of 2026-05-13)

### Fix1_MercenarySniper.xml
Fixes `Mercenary_Sniper_Acidifier` PawnKindDef: removes and re-adds `weaponMoney` with value `450~600`.

### Fix4_RimCitiesTurret.xml
Removes the `Turret_M240B` entry from `GenStepDef[City_Emplacements]/genStep/options` — CE defines this turret but it isn't resolvable as a cross-reference target from RimCities (mod 1775170117).

### Fix5_HediffOnlyOnce.xml
Removes the `onlyOnce` element from `HediffGiverSetDef[MechanoidHediffGivers]`.

### Fix6_VEEAuroraTarget.xml
`VEE_Aurora` IncidentDef (from VanillaLandmarksExpanded local mod) had `<targetTags><li>World</li></targetTags>`, but `IncidentWorker_FrequentAurora.CanFireNowSub` immediately casts `parms.target` to `Map` (offset 0x00001), causing `InvalidCastException` every storyteller tick. Patch removes `World` tag and adds `Map_PlayerHome` to match what the worker actually expects.

### NoOp.xml
Empty placeholder patch.

---

## C# Source Fixes (compiled directly into mod DLLs)

### LocalCSharpFixes mod
A separate local Harmony patch mod for bugs where source isn't available or can't be compiled against 1.6.  
**Location:** `C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\LocalCSharpFixes\`  
**packageId:** `local.csharpfixes` — load LAST.  
**Build command:** `dotnet build Source\LocalCSharpFixes.csproj -c Release /p:FrameworkPathOverride="C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed"`  
**Rule:** Only troubleshoot/fix 1.6 source. If a mod only has pre-1.6 source, use this Harmony patch approach instead of recompiling.

### RimDark 40k Framework — ArtificialPartsAffinity per-frame stat spam — FIXED (2026-07-26)
**Mod:** RimDark 40k - Framework (3267464767), `Core40k.ArtificialPartsAffinity`
**Bug:** Postfix on `PawnCapacityUtility.CalculatePartEfficiency` calls `GetStatValue(BEWH_ArtificialPartsAffinityFactor, cacheStaleAfterTicks: -1)` — a full uncached stat evaluation per body part per capacity recalc. DPA measured ~2.3ms avg/frame, 36ms spikes. For pawns without the 40k affinity the factor is 1 and it's all wasted work.
**Fix:** `GrimWorld40k_Fixes.cs` — unpatches their postfix and installs an equivalent one that reads the stat through vanilla's 120-tick stat cache and early-outs at factor==1. Cosmetic difference: no duplicate capacity-impactor row at factor 1. Resolves the mod's type by name; skips cleanly if the mod is absent.

### VSIE — GetChairFor out-of-bounds (2026-05-14)
**Mod:** Vanilla Social Interactions Expanded (2439736083) — source only up to 1.4, not recompilable against 1.6.  
**Bug:** `GatheringWorker_MealTogether.GetChairFor` called `ClosestThingReachable(food.Position, ...)` without checking `food.Spawned` first. Food found in pawn inventory is not spawned, so `food.Position = IntVec3.Invalid (-1000,-1000,-1000)`, triggering the out-of-bounds log error every meal-together tick.  
**Fix:** Harmony PREFIX in `LocalCSharpFixes` — if `!food.Spawned`, return `null` immediately.

### Trader Ships — wrong ship type from dev console (2026-05-14)
**Mod:** Trader Ships (2046222331)  
**Bug:** `IncidentWorkerTraderShip.TryExecuteWorker` called `MakeTraderShip(map)` ignoring `parms.traderKind`, so the dev console "Add trade ship of kind" always spawned a random type.  
**Fix:** Added `TraderKindDef forcedKind = null` parameter to `MakeTraderShip`; `TryExecuteWorker` now passes `parms.traderKind` through. Source edited and recompiled in-place.  
**Build:** `dotnet build` with `/p:FrameworkPathOverride=RimWorldWin64_Data\Managed`. Required adding `netstandard.dll` as explicit reference in `TraderShips.csproj`.  
**Caveat:** Steam update to Trader Ships will overwrite the DLL — reapply the source edit and rebuild if that happens.

---

## Runtime Errors & Workarounds (not XML-patchable)

### Reservation conflicts — benign, self-resolving (2026-05-13)
Two classes of reservation spam, both harmless:
- **VSIE armchair conflict**: `GatheringWorker_MealTogether.GetChairFor` picks a chair without checking if it's already reserved by a pawn doing a normal eat job. Job fails, pawn retries. Not patchable.
- **Dye stack conflict**: Two pawns target the same dye pile for `RecolorApparel`; reservation is maxPawns=1 so one loses. Vanilla RimWorld behaviour in `JobDriver_RecolorApparel.TryMakePreToilReservations`. Not patchable.

### GW Angels of Death vehicles — NonCE ammo cross-ref errors — FIXED (2026-05-17)
**Mod:** GrimWorld 40,000 - Angels of Death (3119816857)  
**Bug:** Vehicle turret defs reference `*NonCE` ammo ThingDefs (e.g. `GW_HeavyBolterNonCE`) that only exist without CE. When CE is active those defs are absent → ~30 turrets log "ammunition is non-null but no defs available." 
**Fix:** `Fix9_GW_VehiclesCEAmmo.xml` — `PatchOperationSequence` inside `PatchOperationFindMod(CE)`. Removes `<chargePerAmmoCount>` and `<genericAmmo>` first (while `<ammunition>` is still present for the xpath predicate), then removes `<ammunition>` last. All three fields targeted by predicate `[ammunition/thingDefs/li[contains(text(),'NonCE')]]`.

### Nano Repair Tech + Polyamory Beds — missing Ogre stats in 1.6 — FIXED (2026-05-17)
**Mods:** Nano Repair Tech (1447372624) + Polyamory Beds (3276496684)  
**Bug:** `Ogre_NanoApparelRate` and `Ogre_NanoWeaponsRate` StatDefs only exist in Nano Repair Tech's 1.2–1.4 folders, not 1.6. Polyamory Beds' NanoTech compat patch creates beds that reference these stats → cross-ref errors + "defines stat base more than once" (null keys collide).  
**Fix:** `Fix10_OgreNanoStats.xml` — re-defines the two missing stats (display-only, copied from 1.4) under `PatchOperationFindMod(Nano Repair Tech)` using conditional check to avoid duplicates.

### Gene category duplicate displayPriorityInXenotype — FIXED (2026-05-16)
**Mods:** Altered Carbon (2196278117) `AC_SleeveQuality` and GrimWorld 40k (3119805903) `GW_PowerArmorBypass` both use `displayPriorityInXenotype = 505` → startup config error about duplicate display order.  
**Fix:** `Fix8_GeneDisplayPriority.xml` in LocalPatches — patches `GW_PowerArmorBypass` to 506. Cosmetic only.
**Updated 2026-07-26:** GW_PowerArmorBypass is a `GeneCategoryDef` in current GrimWorld 40k — the original `GeneDef` xpath silently matched nothing (`success=Always`), so the error had returned. Xpath corrected.

### Vanilla — StorageSettings.CopyFrom null guard for uninitialised frames — FIXED (2026-05-17)
**Bug:** `Frame.CompleteConstruction` calls `StorageSettings.CopyFrom(frame.storageSettings)`. If the frame never initialised its `StorageSettings` (e.g. `RefineryLoadingBay` and similar buildings), `other` is null → NRE, job driver crashes, building can never be completed by a mechanoid (functional bug, not just log noise).  
**Fix:** Harmony PREFIX on `StorageSettings.CopyFrom` in `Vanilla_Fixes.cs` — returns false (skip copy) when `other` is null. Building completes with default storage settings.

### Vanilla — Pawn_FlightTracker.Notify_JobStarted NRE for animals on pre-1.6 saves — FIXED (2026-05-16)
**Bug:** `Pawn_FlightTracker.Notify_JobStarted` (new in RimWorld 1.6) NREs at offset 0x00009 when animals (chickens, etc.) loaded from pre-1.6 saves start a new job. The flight tracker exists on all pawns but its internal state isn't initialised for non-flying animals from old saves.  
**Fix:** Harmony Finalizer on `Notify_JobStarted` in `Vanilla_Fixes.cs` — suppresses the NRE silently. No gameplay effect for animals that can't fly.

### Vanilla — RecolorApparel dye reservation conflict — FIXED (2026-05-16)
**Bug:** `JobDriver_RecolorApparel.TryMakePreToilReservations` calls `Reserve()` on the dye pile. When another pawn holds a physical interaction reservation on that dye (actively picking it up), `Reserve()` logs "Could not reserve" then abandons the job anyway. `CanReserve()` does NOT check physical interaction reservers, so a simple CanReserve pre-check prefix doesn't help.  
**Fix:** Harmony PREFIX on `ReservationManager.LogCouldNotReserveError` in `Vanilla_Fixes.cs` — skips the log call entirely when `job.def.defName == "RecolorApparel"`. Covers both regular reservation conflicts and physical interaction reservation conflicts. Previous approach (patching `Reserve` with `ref bool errorOnFailed`) was unreliable for value-type parameters in Harmony.

### Vehicle Framework — PawnMassUsageInVehicle null pawn in caravan dialog — FIXED (2026-05-18)
**Mod:** Vehicle Framework (SmashPhil)  
**Bug:** VF's transpiler on `CollectionsMassCalculator.MassUsage` calls `PawnMassUsageInVehicle(massUsage, pawn)`. If a transferable entry has a null pawn (GC'd/invalid world pawn), NRE at 0x00000 spams every frame `Dialog_FormCaravan` is open.  
**Fix:** Harmony PREFIX in `VehicleFramework_Fixes.cs` — returns `massUsage` unchanged when `pawn == null`, same as VF skipping it.

### VFEPirates — WarcasketUtility NullRef during Animated Melee duel animations — FIXED (2026-05-16)
**Mod:** Vanilla Factions Expanded - Pirates (2723801948)  
**Bug:** `WarcasketUtility.IsWearingWarcasket(Pawn pawn)` throws NRE at offset 0x00001 when `pawn` is null or `pawn.apparel` is null (non-humanlike pawns or mid-animation draw parms from Animated Melee). VFEPirates' `HeadgearVisible` postfix calls this without null-checking. AM catches the NRE and logs "[MeleeAnim] Rendering exception when doing animation Duel Bow" every frame for the duel's duration.  
**Fix:** Harmony PREFIX on `IsWearingWarcasket` in `VFEPirates_Fixes.cs` — returns false immediately if `pawn == null || pawn.apparel == null`.

### Pick Up And Haul — HaulToInventory reservation conflict with construction delivery — FIXED (2026-05-16)
**Mod:** Pick Up And Haul (unknown workshop ID)  
**Bug:** `JobDriver_HaulToInventory.TryMakePreToilReservations` calls `Reserve()` directly without a prior `CanReserve` check. When a colonist holds a `HaulToContainer` reservation for a blueprint delivery, every competing Mech_Lifter assigned the same item by PUAH logs "Could not reserve" before quietly abandoning the job.  
**Fix:** Harmony PREFIX on `TryMakePreToilReservations` in `PickUpAndHaul_Fixes.cs` — checks `CanReserve(TargetIndex.B)` first; returns false silently if the item is already reserved. Functional outcome is identical (job abandoned) but without the error log.

### VFEEmpire stale dict keys & VTE banksByFaction null keys on load — FIXED (2026-05-16/17)
**Mods:** Vanilla Factions Expanded - Empire (2938820380) and Vanilla Trading Expanded (2785616901)  
**Bug:** Both mods hold dicts keyed by game objects that are never cleaned when removed during play. Stale IDs persist in the save and fail cross-ref on next load → "Null key while loading dictionary".  
**Dicts cleaned at save time (`SaveData_Fixes.cs`):**
- VFEEmpire `artCreator` (Dictionary<ThingWithComps, Pawn>) — strips destroyed art pieces
- VFEEmpire `lastRoyalGossipTick` (Dictionary<Pawn, int>) — strips discarded/destroyed pawns
- Core40k `cachedDecoratives` (Dictionary<Pawn, CachedDecoratives>) — strips discarded/destroyed pawns
- VTE `banksByFaction` (Dictionary<Faction, Bank>) — strips factions no longer in FactionManager
**Note:** Existing saves already have the bad data — errors appear one final time on next load, then gone once the cleaned save is written.

### Vanilla — Fire double-destroy when pawn GC'd — FIXED (2026-05-15)
**Bug:** Vanilla RimWorld issue. When the World Pawn GC discards a pawn, `CompAttachBase.PostDestroy` tries to destroy all attached `AttachableThing`s (e.g. fire). If the fire already went out and was destroyed, `CompAttachBase` holds a stale reference and calls `Destroy` again, logging "Tried to destroy already-destroyed thing Fire...".  
**Fix:** Harmony PREFIX on `Thing.Destroy` in `Vanilla_Fixes.cs` — if the thing is already destroyed AND is an `AttachableThing`, skip silently. Other double-destroy calls still surface normally.

### BEWH Apothecary Tools — no CE support (MeleeDamageAverage error) — FIXED (2026-05-17)
**Mod:** Black Eagles Warhammer Heresy (3505068525) — `BEWH_Narthecium`, `BEWH_Reductor`  
**Bug:** Same as AstartesPowerSword — vanilla Tool entries, CE's `StatWorker_MeleeDamageAverage` needs `ToolCE`. Triggered by Simple Sidearms evaluating all carried weapons.  
**Fix:** Added `ToolCE` entries to `Fix7_BEWH_PowerSwordCE.xml` in LocalPatches. Narthecium: gauntlet (Blunt AP 0.2), drill (Sharp AP 0.4). Reductor: handle (Blunt AP 0.05), drill (Sharp AP 0.2).

### BEWH Power Sword — no CE support (MeleeDamageAverage error) — FIXED (2026-05-15)
**Mod:** Black Eagles Warhammer Heresy (3505068525) — `BEWH_AstartesPowerSword`  
**Bug:** Weapon uses vanilla `Tool` entries. CE replaces `MeleeDamageAverage`'s stat worker with `StatWorker_MeleeDamageAverage` which requires `ToolCE` entries with `armorPenetrationSharp`/`Blunt`. Animated Melee reads this stat when generating right-click execution options, causing an error log every time the sword is equipped.  
**Fix:** `Fix7_BEWH_PowerSwordCE.xml` in LocalPatches — wrapped in `PatchOperationFindMod` for CE; replaces the tools block with `ToolCE` equivalents: handle (Blunt, AP 0.1), edge (Cut, AP sharp 0.5 — reflects the powered energy blade's original vanilla AP of 1.2).

### Reinforced Mechanoids — TrooperStorage NullReferenceException on explosion — FIXED (2026-05-15)
**Mod:** Reinforced Mechanoid 2 Continued (3541404610)  
**Bug:** `TrooperStorage.PreApplyDamage` → `ReleaseTroopers` → `CompPawnProducer.CreateOrAddToAssaultLord` throws `NullReferenceException` when an explosion hits a trooper storage building in an edge-case context (e.g. `y=0` cell, null map/faction at release time). Spams once per explosion tick.  
**Fix:** Harmony Finalizer on `TrooperStorage.PreApplyDamage` in `ReinforcedMechanoids_Fixes.cs` — suppresses the NRE silently. Stored troopers fail to emerge in the broken context, but no error spam.

### Alpha Animals — black truffles spawned forbidden — FIXED (2026-05-15)
**Mod:** Alpha Animals (1541721856)  
**Bug:** Pig-dig job driver spawns `AA_BlackTruffles` forbidden (mimicking the mining-drop pattern), requiring manual allow before hauling.  
**Fix:** Harmony PREFIX in `LocalCSharpFixes` (`AlphaAnimals_Fixes.cs`) on `ForbidUtility.SetForbidden` — skips the call when `value == true` and `t.def.defName == "AA_BlackTruffles"`. Side effect: truffles also cannot be manually forbidden by the player (acceptable for a fungible food item).

### Traders Have Money — Horder silver incorrectly lowered — FIXED (2026-05-15)
**Mod:** Traders Have Money (2898006725)  
**Bug:** `THMStartup` (C# static constructor) iterates all `TraderKindDef`s at startup and sets every orbital trader's silver to `orbitalMin~orbitalMax` (default 2000~4000). This overwrites `Orbital_Horder`'s intentional 10000~100000 silver range defined in its XML.  
**Fix:** `TradersHaveMoney_Fixes.cs` in LocalCSharpFixes — a `[StaticConstructorOnStartup]` that runs after THMStartup (LocalCSharpFixes loads last) and restores `Orbital_Horder`'s silver to 10000~100000.  
**Note:** "Traders Have Money" (2898006725) is a separate mod from "Traders have more Silver 1.6" (3513786356). The latter still needs to load last in RimSort to work correctly for vanilla orbital traders.

### Smart Farming FinalizeInit tile out-of-bounds — FIXED (2026-05-15)
**Mod:** Smart Farming (3220129183)  
**Bug:** `MapComponent_SmartFarming.FinalizeInit` calls `WorldGrid.DistanceFromEquatorNormalized(map.Tile)` without guarding against `Tile == -1` (which occurs for pocket maps / maps without a world tile). `UpdateCommonCache` already had the guard but `FinalizeInit` didn't.  
**Fix:** Harmony PREFIX in `LocalCSharpFixes` (`SmartFarming_Patches.cs`) — if `map.Tile < 0`, return false to skip FinalizeInit for that map entirely.

### VSIE MealTogether GetChairFor out-of-bounds — FIXED (2026-05-14)
Fixed via Harmony PREFIX in `LocalCSharpFixes`. See C# Source Fixes section above.

### DrawSilhouettes NullReferenceException (2026-05-13)
`Exception drawing dynamic things: NullReferenceException in Verse.DynamicDrawManager.DrawSilhouettes` — spams dev console every frame. Root cause unknown (likely a pawn or thing with a null graphic/material in the silhouette rendering pipeline).  
**Workaround:** Draft all pawns, then undraft them. This refreshes draw state and clears the spam. Not reproducible reliably; no XML fix possible.

---

### GW4KArmor Custodes torso mask textures renamed in 1.6 — FIXED (2026-05-17)
**Mod:** GW4KArmor / Custodes (3320940377)  
**Bug:** C# code (`MaskTextureStorage.GetTexture`) requests `CGuard_torso_mask1_{south,north,east}` but the 1.6 Textures folder only has `CGuard_torso_Male_mask1_*` (added `_Male_` to the filename). Error fires when opening the armor painter window for CGuard armor.  
**Fix:** Copied the three `.dds` files from the mod's 1.6 folder to `LocalPatches\Textures\Things\CustodesArmors\Custodes\` without the `_Male_` prefix.

### ReBuildDoorsAndCorners — missing glass door textures — FIXED (2026-05-15)
**Mod:** ReBuildDoorsAndCorners (3262718980)  
**Bug:** The 1.6 def renames textures (strips `RB_` prefix: e.g. `GlassAutodoor_VanillaMenuIcon` instead of `RB_GlassAutodoor_VanillaMenuIcon`) but the 1.6 `Textures/` folder was never updated — all glass door textures are missing in 1.6.  
**Fix:** Copied all affected textures from the mod's 1.5 folder into `LocalPatches\Textures\Things\Building\RB_Door\` with the `RB_` prefix stripped. Covers: `GlassAutodoor_MenuIcon`, `GlassAutodoor_VanillaMenuIcon`, `GlassAutodoor\*_Mover*`, and the `ReinforcedGlassAutodoor` equivalents (10 texture assets × 2 formats = 30 files).

## Known Errors in errors.txt (not yet patched, as of 2026-05-13)

These are save-game cross-reference errors (pawns stored in saves that no longer exist as defs) — XML patches cannot fix save data, so these are benign on new games:

- `Corpse_Orbital_Mech_Scyther` — missing ThingDef, triggers via DubsMintMenus bill filters
- `Corpse_Overwatch_Ward_drone`, `Corpse_Overwatch_missile_drone`, `Corpse_Overwatch_Guard_drone`, `Corpse_Overwatch_heavy_drone` — same pattern, from Overwatch Mechanoid mod (3310358738)
- `WaterTrough` — unresolved cross-reference (wanter=things)
- **GrimWorld 40,000** (`Building_TrapMassExplosive:SpringChance`) — assembly error: `Pawn.get_IsNonMutantAnimal()` method not found. This is a C# compatibility issue (mod compiled against an older RimWorld API); requires a mod update from the author or a Harmony patch — not fixable via XML.

---

## Other Local Mods in RimWorld\Mods\

| Folder | Notes |
|--------|-------|
| `LocalPatches` | This mod (Local Error Fixes) |
| `AV_Mechspots` | Local mod, added 2026-01-18 |
| `VanillaLandmarksExpanded` | Local copy, added 2026-01-22 |
| `Fishery` | Local mod |
| `PerformanceFish` | Local mod |
| `1201382956`, `2574315206`, `2854006492`, `3509486825` | Workshop IDs installed to local Mods folder |

---

## Load Order Notes

- **Traders have more Silver (3513786356)** must load *last* among workshop mods (just before `local.errorfixes` and `local.csharpfixes`). It uses full `<Defs>` redefinitions (not patches) for `Orbital_BulkGoods`, `Orbital_CombatSupplier`, `Orbital_Exotic`, etc. Any mod loading after it that redefines those same defNames will silently override the silver boost.
