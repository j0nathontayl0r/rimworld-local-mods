# Known modlist incompatibilities & gotchas

Running notes on mod combinations that broke, so they aren't re-tried later.
Dates are when the issue was diagnosed on the live modlist.

## GrimWorld Mine-Extensions `SpringChance` — UNFIXABLE from Harmony (2026-08-15)

`GrimWorld 40,000 - Core Imperialis` (`grimworld.core`, workshop 3018671708)
bundles `mine_extensions.Building_TrapMassExplosive`, which its own shipped
source declares as a verbatim copy of RimWorld **1.5**'s
`RimWorld.Building_TrapExplosive` (see the header comment in
`3018671708/Source/.../Building_TrapMassExplosive.cs`, "Assembly-CSharp,
Version=1.5.9214.33606"). Its `SpringChance` calls `Pawn.IsNonMutantAnimal`,
which 1.6 removed — 1.6 exposes `IsAnimal` and `IsMutant` separately. Result:

```
Error in GrimWorld 40,000 - Core Imperialis, assembly name: Mine-Extensions,
method: Building_TrapMassExplosive:SpringChance, exception:
System.MissingMethodException: Method not found: bool Verse.Pawn.get_IsNonMutantAnimal()
```

**Cannot be patched with Harmony — do not try again.** Harmony must *read* the
original method body to build any replacement, and reading resolves the dangling
`IsNonMutantAnimal` token, throwing the very exception the patch exists to
prevent. The stack proves it: the failure is inside
`HarmonyLib.MethodBodyReader.ReadOperand` → `RuntimeModule.ResolveMethodToken`,
i.e. during instruction reading, before any prefix/postfix/transpiler could run.
Prefix, postfix and transpiler all require that same read, so no variation helps.
**Update 2026-10-17: it doesn't need fixing.** The class is dead code. Its only
user is the abstract `TrapATBase` def, and no def in any installed mod inherits
from it, so no such trap can be built and `SpringChance` never runs. The failed
attempt (`LocalEarlyFixes.MineExtensionsSpringChanceFix`) was removed; see git
history for it. The Prepatcher IL-rewrite route is also gone, because Prepatcher
doesn't run on 1.7 (.NET 10). Revisit only if a mod starts inheriting
`TrapATBase`: then a replacement class in LocalCSharpFixes plus an XML
`thingClass` redirect would work, since nothing would need to read the broken body.

Note the error is often reported by **Mod Error Checker**
(`Taranchuk.ModErrorChecker`) rather than by our fix mods — it sweeps every
assembly with `PatchProcessor.GetOriginalInstructions`, hitting the same
unreadable body. That is the tool working correctly, not a second bug.

## Coventech Mechsuit Framework × VFE Pirates — REMOVED, do not re-add (2026-08-09)

`Coventech Mechsuit Framework` (workshop 3636334743, packageId
`shunthewitch.mechsuitframework`) bundles `universalflight.dll`, whose `Mod`
constructor runs `Harmony.PatchAll()`. Patching
`VFEPirates.PawnsArrivalModeWorker_Gauntlet.Arrive` forces that type's static
constructor to run **before defs are loaded**, which throws
`ArgumentNullException` (null dictionary key) — the framework then fails to
instantiate at all, and the poisoned Gauntlet type risks breaking
warcasket-gauntlet raid arrivals in play.

- Not fixable locally: the crash happens during `CreateModClasses`, before any
  fix mod (even `LocalEarlyFixes`) could reliably intervene, and the damage is
  in the *other* mod's type initialiser.
- Not fixable upstream: the Coventech author has stated they will no longer
  patch around VFE Pirates breakage (see workshop comments on 3636334743).
- Decision 2026-08-09: framework removed. Nothing in the list depended on it.
  If Coventech mechsuit content (e.g. Milira Kitsune Mechsuit) is ever wanted,
  this conflict must be re-evaluated first.

## DMS Tri-Tech requires XML Extensions (2026-08-09)

`The Dead Man's Switch - Tri-Tech` (`dms.tritech`, workshop 3622884272) ships
settings-driven patches using `XmlExtensions.UseSetting`. Without **XML
Extensions** (imranfish, workshop 2574315206) active, every launch logs ~17
`Could not find type named XmlExtensions.UseSetting` / "doesn't correspond to
any field" errors and Tri-Tech's tuning patches (beam damage, lance
warmup/cooldown) silently don't apply. Keep XML Extensions subscribed and
loaded early (after Harmony/Core/frameworks, before the DMS mods).

## DMS Tri-Tech must load after MobileDragoon (2026-08-09)

Tri-Tech's Mobile Dragoon compat defs inherit (`ParentName`) from abstracts
defined in `The Dead Man's Switch - MobileDragoon` (workshop 3377130226).
With Tri-Tech ordered before MobileDragoon, the parents aren't found and the
Dragoon lance items (`DMS_Module_WeaponLance`, `DMS_Dragoon_lance`,
`DMS_Apparel_WeaponLance`) load broken: null `thingClass`, no graphics, no
apparel layers, plus a cascade of `Config error` lines. Keep Tri-Tech after
all DMS modules in the load order.

## Defaults mod holds stale def references after removing content mods

The `Defaults` mod (workshop 3285178686) bakes ThingDefs into its saved
default stockpile filters (`Config/Mod_3285178686_DefaultsMod.xml`). Removing
a content mod leaves stale refs that spam `Could not load reference to
Verse.ThingDef named …` on the next launch (23× per def — once per zone-type
default). Harmless and **self-cleaning**: the mod re-saves its settings during
the session, and the following launch is quiet. Seen 2026-08-09 with a removed
cyberpunk apparel mod (~2,810 lines) and again with `RR_Weapon_Torch` after
removing Research Reinvented: Stepping Stones.

## VTE duplicate company loadIDs — patched locally (2026-08-09)

Vanilla Trading Expanded assigns new stock-market company IDs as
`companies.Count`, so once any company has been removed, new spawns collide
with existing IDs → `DebugLoadIDsSavingErrorsChecker` warnings on every
(auto)save. Present in VTE 1.4–1.6; worth reporting upstream. Fixed durably by
`LocalCSharpFixes` (`VTE_Fixes.cs`): ctor postfix reassigns colliding IDs to
max+1, and a `TradingManager.ExposeData` postfix re-IDs duplicates already
baked into a save at load. One-off save surgery alone does NOT stick — the
game regenerates colliding companies within minutes.

## Research Reinvented: Stepping Stones — removed as buggy (2026-08-09)

Removed from the list ("buggy as"). While present it also logged seven
`RR.PatchOperationAddOrReplace … recipeMaker` failures on CE-replaced weapons
(molotov, EMP/smoke/toxbomb launchers, flamebow) — a CE compat gap, cosmetic.
