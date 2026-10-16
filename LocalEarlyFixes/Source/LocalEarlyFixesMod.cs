using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;      // Faction
using RimWorld.Planet; // GlobalTargetInfo
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
            var h = new Harmony("local.earlyfixes");
            EnlistPatchAllFix.Apply(h);
            HarGraphicsHookRaceFix.Apply(h);
        }
    }

    // Humanoid Alien Races: each AlienPartGenerator adds itself to the static HashSet graphicsQueue
    // from ThingDef_AlienRace.ResolveReferences - on the def-loading thread - and subscribes
    // LoadGraphicsHook to Application.onBeforeRender, which runs on the main thread every frame and
    // enumerates that set. Faster Game Loading releases the hook early (once all mod content is
    // loaded, before defs finish resolving), so the two overlap and the enumeration throws
    // "Collection was modified; enumeration operation may not execute" (once, at startup; observed
    // 2026-10-17). It is self-healing: the throw happens before graphicsQueue.Clear() and before
    // the hook unsubscribes, so the next frame re-runs it over the full queue. A real fix would mean
    // changing load timing for every race, so instead a finalizer turns just this exception, from
    // just this method, into a warning. Swallowing it keeps the original's state - queue intact,
    // hook still subscribed - so the retry is unchanged. Must be applied before loading starts,
    // hence here and not in LocalCSharpFixes.
    // Signature verified against the installed 1.7 DLL (839005762/1.7):
    //   public static void AlienRace.AlienPartGenerator.LoadGraphicsHook()
    public static class HarGraphicsHookRaceFix
    {
        public static void Apply(Harmony h)
        {
            try
            {
                var hook = AccessTools.Method("AlienRace.AlienPartGenerator:LoadGraphicsHook");
                if (hook == null) { Log.Message("[LocalEarlyFixes] skip HAR graphics-hook fix (Humanoid Alien Races not present)"); return; }
                h.Patch(hook, finalizer: new HarmonyMethod(typeof(HarGraphicsHookRaceFix), nameof(Finalizer)));
            }
            catch (Exception e)
            {
                Log.Warning("[LocalEarlyFixes] HAR graphics-hook fix failed to apply: " + e.Message);
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            if (__exception is not InvalidOperationException) return __exception;
            Log.Warning("[LocalEarlyFixes] HAR graphics queue changed while loading (def loading still running); retrying next frame.");
            return null;
        }
    }

    // Enlist ([RH2] Faction: V.O.I.D., 2883208829, bundles Enlist.dll) patches from its Mod
    // constructor with new Harmony("ChickenPlucker.Enlist").PatchAll(). One of its classes is
    //
    //     [HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
    //     static void Postfix(Faction __instance, Faction other, int goodwillChange,
    //                         bool canSendMessage, bool canSendHostilityLetter,
    //                         string reason, GlobalTargetInfo? lookTarget)
    //
    // but the original's reason parameter has been a HistoryEventDef since 1.3. Harmony 2 bound it
    // without checking; Harmony 3 (RimWorld 1.7) validates the type and throws "Unrelated classes
    // cannot describe the same non-null object ... supplied RimWorld.HistoryEventDef, requested
    // System.String". PatchAll stops at the first throw, so every Enlist class after that one goes
    // unpatched and the exception aborts EnlistMod's constructor ("Error while instantiating a mod
    // of type Enlist.EnlistMod", observed 2026-10-17).
    //
    // Fix: prefix PatchClassProcessor.Patch() and, for that one class only, skip it and register a
    // correctly typed postfix that forwards to Enlist's own body with reason = null (the body never
    // reads it, verified by decompiling the 1.6 DLL). With the throwing class gone, PatchAll carries
    // on through the rest. Hooked on Patch() rather than Harmony.PatchAll(Assembly) because the
    // latter is a two-line method the .NET JIT may already have inlined into PatchAll(), where a
    // detour would never be reached. This must run before EnlistMod's constructor, hence here.
    public static class EnlistPatchAllFix
    {
        private const string BrokenPatchType = "Enlist.Patch_TryAffectGoodwillWith";

        private static Action<Faction, Faction, int, bool, bool, string, GlobalTargetInfo?> enlistPostfix;

        public static void Apply(Harmony h)
        {
            try
            {
                if (AccessTools.TypeByName(BrokenPatchType) == null) { Log.Message("[LocalEarlyFixes] skip Enlist fix (Enlist not present)"); return; }
                h.Patch(AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch)),
                        prefix: new HarmonyMethod(typeof(EnlistPatchAllFix), nameof(Patch_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning("[LocalEarlyFixes] Enlist fix failed to apply: " + e.Message);
            }
        }

        public static bool Patch_Prefix(Harmony ___instance, Type ___containerType, ref List<MethodInfo> __result)
        {
            if (___containerType?.FullName != BrokenPatchType) return true;

            __result = new List<MethodInfo>();
            try
            {
                enlistPostfix = AccessTools.MethodDelegate<Action<Faction, Faction, int, bool, bool, string, GlobalTargetInfo?>>(
                    AccessTools.Method(___containerType, "Postfix"));
                // Registered under Enlist's own Harmony instance, so it is attributed to Enlist.
                ___instance.Patch(AccessTools.Method(typeof(Faction), nameof(Faction.TryAffectGoodwillWith)),
                                  postfix: new HarmonyMethod(typeof(EnlistPatchAllFix), nameof(TryAffectGoodwillWith_Postfix)));
                Log.Message("[LocalEarlyFixes] Enlist goodwill postfix retyped (reason: string -> HistoryEventDef).");
            }
            catch (Exception e)
            {
                Log.Warning("[LocalEarlyFixes] Enlist goodwill postfix could not be retyped, skipped: " + e.Message);
            }
            return false;
        }

        public static void TryAffectGoodwillWith_Postfix(Faction __instance, Faction other, int goodwillChange,
                                                         bool canSendMessage, bool canSendHostilityLetter,
                                                         GlobalTargetInfo? lookTarget)
            => enlistPostfix(__instance, other, goodwillChange, canSendMessage, canSendHostilityLetter, null, lookTarget);
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
