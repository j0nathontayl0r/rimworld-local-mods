# Known modlist incompatibilities & gotchas

Running notes on mod combinations that broke, so they aren't re-tried later.
Dates are when the issue was diagnosed on the live modlist.

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

## Research Reinvented: Stepping Stones — removed as buggy (2026-08-09)

Removed from the list ("buggy as"). While present it also logged seven
`RR.PatchOperationAddOrReplace … recipeMaker` failures on CE-replaced weapons
(molotov, EMP/smoke/toxbomb launchers, flamebow) — a CE compat gap, cosmetic.
