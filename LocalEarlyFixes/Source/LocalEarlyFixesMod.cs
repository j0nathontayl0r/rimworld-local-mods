using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;      // Building_Trap, MassUtility, StatDefOf, StatExtension
using UnityEngine;   // Mathf
using Verse;

namespace LocalEarlyFixes
{
    // A Mod subclass, NOT [StaticConstructorOnStartup]. Mod constructors run during
    // LoadedModManager.CreateModClasses(), which is before LoadModXML/defs; static constructors run
    // long afterwards. The whole point of this mod is to get in before other mods' Mod constructors,
    // so the load order position (top of the list, after the DLCs) is load-bearing.
    public class LocalEarlyFixesMod : Mod
    {
        public LocalEarlyFixesMod(ModContentPack content) : base(content)
        {
            GauntletCctorFix.Apply();
            MineExtensionsSpringChanceFix.Apply(new Harmony("local.earlyfixes"));
        }
    }

    // VFEPirates.PawnsArrivalModeWorker_Gauntlet (VFE Pirates, 2723801948) declares:
    //
    //     private static readonly int PawnsPerShip = 8;
    //     private static readonly Dictionary<ThingDef, ThingDef> shipAssoc = new()
    //     {
    //         { VFEP_DefOf.VFEP_Ship_Black,  VFEP_DefOf.VFEP_CrashedShip_Black  },
    //         { VFEP_DefOf.VFEP_Ship_Orange, VFEP_DefOf.VFEP_CrashedShip_Orange },
    //         { VFEP_DefOf.VFEP_Ship_Green,  VFEP_DefOf.VFEP_CrashedShip_Green  },
    //         { VFEP_DefOf.VFEP_Ship_Red,    VFEP_DefOf.VFEP_CrashedShip_Red    }
    //     };
    //
    // Static field initialisers compile into the type initialiser, which fires the first time
    // anything touches the type. Coventech Mechsuit Framework (universalflight.dll, load order 71)
    // Harmony-patches Arrive() from its Mod constructor - during CreateModClasses, before defs load -
    // so every VFEP_DefOf field is still null. Dictionary.Add(null, x) throws
    // ArgumentNullException("key"). The type initialiser fails, and the CLR caches that failure for
    // the process lifetime, so the type is unusable from then on. Consequences seen in Player.log:
    //   - "Error patching VFEPirates.PawnsArrivalModeWorker_Gauntlet - Void Arrive(...)"
    //   - "Error while instantiating a mod of type universalflight.universalflightMod"
    //     (the exception propagates out of PatchAll and aborts that mod entirely - it does nothing)
    // Both carry the same [Ref] stack marker: one root cause, two symptoms.
    //
    // The defs themselves are fine - all eight exist in VFE Pirates' own Defs. This is purely an
    // ordering bug, so it cannot be fixed with an XML patch.
    //
    // Fix: skip the throwing initialiser body, then assign both fields ourselves once defs exist.
    public static class GauntletCctorFix
    {
        internal const string TypeName = "VFEPirates.PawnsArrivalModeWorker_Gauntlet";
        internal static bool Skipped;

        private static readonly string[][] ShipPairs =
        {
            new[] { "VFEP_Ship_Black",  "VFEP_CrashedShip_Black"  },
            new[] { "VFEP_Ship_Orange", "VFEP_CrashedShip_Orange" },
            new[] { "VFEP_Ship_Green",  "VFEP_CrashedShip_Green"  },
            new[] { "VFEP_Ship_Red",    "VFEP_CrashedShip_Red"    },
        };

        // ATTEMPT 1 (failed, kept as a warning): Harmony-patch the .cctor with a skip prefix.
        // h.Patch() has to prepare the original method, and preparing a type initialiser RUNS it -
        // so the patch call itself triggered the very exception it was meant to prevent:
        //   "[LocalEarlyFixes] Gauntlet fix failed to apply: The type initializer for
        //    'VFEPirates.PawnsArrivalModeWorker_Gauntlet' threw an exception."
        // Anything that touches that type before defs load is unusable as a fix site.
        //
        // ATTEMPT 2 (this one): never touch the Gauntlet type at all. The initialiser only throws
        // because Dictionary.Add rejects a null KEY, so give it non-null keys. VFEP_DefOf is a plain
        // [DefOf] static class with no type initialiser of its own, so poking its fields runs nothing.
        // Install placeholder ThingDefs into the eight fields the initialiser reads; when Mechsuit
        // Framework touches the type at load order 72 the dictionary builds from placeholders and the
        // type initialises cleanly. DefOfHelper.RebindAllDefOfs later overwrites these fields with the
        // real defs, and Populate() below replaces shipAssoc with the correct contents.
        public static void Apply()
        {
            try
            {
                var defOfType = AccessTools.TypeByName("VFEPirates.VFEP_DefOf");
                if (defOfType == null) { Log.Message("[LocalEarlyFixes] skip Gauntlet fix (VFE Pirates not present)"); return; }

                var installed = 0;
                foreach (var pair in ShipPairs)
                {
                    foreach (var fieldName in pair)
                    {
                        var f = AccessTools.Field(defOfType, fieldName);
                        if (f == null) { Log.Warning("[LocalEarlyFixes] VFEP_DefOf." + fieldName + " not found"); continue; }
                        if (f.GetValue(null) != null) continue;   // already bound - leave it alone

                        // defName set so these are identifiable if one ever leaks into a log.
                        var placeholder = new ThingDef();
                        placeholder.defName = "LocalEarlyFixes_Placeholder_" + fieldName;
                        f.SetValue(null, placeholder);
                        installed++;
                    }
                }

                Skipped = installed > 0;
                Log.Message("[LocalEarlyFixes] installed " + installed + " placeholder def(s) so the Gauntlet initialiser cannot throw.");
            }
            catch (Exception e)
            {
                Log.Warning("[LocalEarlyFixes] Gauntlet fix failed to apply: " + e.Message);
            }
        }

        // Called once defs are loaded. The initialiser has by now run successfully, so PawnsPerShip
        // is already 8 - but shipAssoc is full of the placeholder keys from Apply(), so it has to be
        // rebuilt from the real defs. PawnsPerShip is reasserted defensively; Arrive() divides by it.
        internal static void Populate()
        {
            var t = AccessTools.TypeByName(TypeName);
            if (t == null) return;

            var dict = new Dictionary<ThingDef, ThingDef>();
            foreach (var pair in ShipPairs)
            {
                var key   = DefDatabase<ThingDef>.GetNamedSilentFail(pair[0]);
                var value = DefDatabase<ThingDef>.GetNamedSilentFail(pair[1]);
                // Skipping null keys is the actual bug fix: the original would throw here.
                if (key == null || value == null)
                {
                    Log.Warning("[LocalEarlyFixes] Gauntlet: skipping pair " + pair[0] + " -> " + pair[1] + " (def missing)");
                    continue;
                }
                dict[key] = value;
            }

            var assocField = AccessTools.Field(t, "shipAssoc");
            var perShip    = AccessTools.Field(t, "PawnsPerShip");
            if (assocField == null || perShip == null)
            {
                Log.Warning("[LocalEarlyFixes] Gauntlet: field layout changed, cannot populate.");
                return;
            }

            assocField.SetValue(null, dict);
            perShip.SetValue(null, 8);
            Log.Message("[LocalEarlyFixes] Gauntlet populated with " + dict.Count + " ship pair(s).");
        }
    }

    // GrimWorld 40,000 - Core Imperialis (grimworld.core) ships mine_extensions.Building_TrapMassExplosive,
    // which its own bundled source declares as a verbatim copy of RimWorld 1.5's
    // RimWorld.Building_TrapExplosive (see 3018671708/Source/.../Building_TrapMassExplosive.cs, header
    // comment "Assembly-CSharp, Version=1.5.9214.33606"). Its SpringChance calls Pawn.IsNonMutantAnimal,
    // which 1.6 removed - Verse.Pawn now exposes IsAnimal and IsMutant separately (verified against
    // Assembly-CSharp metadata) -> "System.MissingMethodException: Method not found:
    // bool Verse.Pawn.get_IsNonMutantAnimal()".
    //
    // The prefix below reimplements SpringChance exactly as the mod's own source has it, substituting
    // (IsAnimal && !IsMutant) for IsNonMutantAnimal, and returns false so the broken body never runs.
    //
    // CONFIRMED NOT TO WORK - kept as documentation, and because it costs nothing but a caught throw.
    // Tested 2026-08-02: h.Patch fails with the very error it is trying to fix -
    //   "[LocalEarlyFixes] SpringChance fix could not be applied:
    //    Method not found: bool Verse.Pawn.get_IsNonMutantAnimal()"
    // Harmony has to read the original method body to build its replacement, and reading resolves the
    // dangling IsNonMutantAnimal token; the MissingMethodException stack traces go through
    // RuntimeModule.ResolveMethodToken, which is exactly that step. This is not fixable from Harmony at
    // all - prefix, postfix and transpiler all require reading the body first, so no variation helps.
    // The remaining routes are a Prepatcher IL rewrite (Prepatcher is active at load order 0, and
    // DefLoadCache uses the same FreePatch mechanism, so it is known to work in this setup) or an
    // upstream mod update. The failure is logged at info level, not warning: it is the expected outcome
    // now, and this whole exercise was about reducing log noise.
    public static class MineExtensionsSpringChanceFix
    {
        public static void Apply(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("mine_extensions.Building_TrapMassExplosive");
                if (t == null) { Log.Message("[LocalEarlyFixes] skip SpringChance fix (Mine-Extensions not present)"); return; }

                var m = AccessTools.Method(t, "SpringChance");
                if (m == null) { Log.Warning("[LocalEarlyFixes] skip SpringChance fix (method not found)"); return; }

                h.Patch(m, prefix: new HarmonyMethod(typeof(MineExtensionsSpringChanceFix), "SpringChance_Prefix"));
                Log.Message("[LocalEarlyFixes] SpringChance patched (IsNonMutantAnimal -> IsAnimal && !IsMutant).");
            }
            catch (Exception e)
            {
                // Info, not warning - see the class comment; this failure is expected and understood.
                Log.Message("[LocalEarlyFixes] SpringChance fix not applied (expected, see source): " + e.Message);
            }
        }

        // Typed as Building_Trap (the mod's base class) so we never reference the mod's assembly.
        // reqMass and KnowsOfTrap are reached reflectively for the same reason.
        public static bool SpringChance_Prefix(Building_Trap __instance, Pawn p, ref float __result)
        {
            try
            {
                var reqMass = 300f;   // the mod's declared default
                var f = AccessTools.Field(__instance.GetType(), "reqMass");
                if (f != null) reqMass = (float)f.GetValue(__instance);

                if ((MassUtility.GearAndInventoryMass(p) + p.GetStatValue(StatDefOf.Mass)) < reqMass) { __result = 0f; return false; }
                if (p.kindDef.immuneToTraps) { __result = 0f; return false; }

                var num = 1f;
                var knows = Traverse.Create(__instance).Method("KnowsOfTrap", new object[] { p }).GetValue<bool>();
                if (knows)
                {
                    if (p.IsAnimal && !p.IsMutant)      // was: p.IsNonMutantAnimal
                    {
                        num = 0.2f;
                        num *= __instance.def.building.trapPeacefulWildAnimalsSpringChanceFactor;
                    }
                    else
                    {
                        num = 0.25f;
                    }
                }

                num *= __instance.GetStatValue(StatDefOf.TrapSpringChance) * p.GetStatValue(StatDefOf.PawnTrapSpringChance);
                __result = Mathf.Clamp01(num);
                return false;
            }
            catch (Exception e)
            {
                // Deliberately do NOT fall through to the original - it is the broken method. A trap that
                // never springs is a smaller failure than an exception every time one is evaluated.
                Log.Warning("[LocalEarlyFixes] SpringChance prefix failed, trap disabled this call: " + e.Message);
                __result = 0f;
                return false;
            }
        }
    }

    // Runs after defs are loaded, which is exactly what the original initialiser needed.
    [StaticConstructorOnStartup]
    public static class GauntletLatePopulate
    {
        static GauntletLatePopulate()
        {
            if (!GauntletCctorFix.Skipped) return;
            try { GauntletCctorFix.Populate(); }
            catch (Exception e) { Log.Warning("[LocalEarlyFixes] Gauntlet populate failed: " + e.Message); }
        }
    }
}
