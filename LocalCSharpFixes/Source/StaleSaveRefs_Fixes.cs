using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace LocalCSharpFixes
{
    // Shared save-time scrub for a recurring mod bug class: collections keyed on Pawns or world
    // objects saved with LookMode.Reference, where the mod's cleanup hook misses a key that gets
    // destroyed and discarded. The stale reference scribes as "null" and every later load logs
    // "Null key while loading dictionary of <K> and <V>" (harmless -- the pair is dropped -- but
    // red, and recurring as new stale keys accumulate). Known offenders, each observed here
    // 2026-08-25/26:
    //   VanillaTraitsExpanded.TraitsManager      - madSurgeonsWithLastHarvestedTick,
    //                                              wanderLustersWithLastMapExitedTick, ...
    //                                              (RemoveDestroyedPawn misses discard-only paths)
    //   SimpleLeadership.FactionLeadershipData   - settlementLeaders (Dictionary<Settlement, Pawn>;
    //                                              destroyed settlements never removed)
    //   CombatExtended.LoadoutManager            - assignmentLoadouts (Dictionary<Pawn, Loadout>;
    //                                              PurgeLoadoutRolls doesn't cover discarded pawns)
    // Strip stale keys from every reference-holding collection just before each manager saves.
    // Collections are found by reflection over the instance fields, so a mod update that adds or
    // renames a tracker stays covered without a code change.
    // Signatures verified against the installed 1.6 DLLs:
    //   VanillaTraitsExpanded.TraitsManager.ExposeData()      (GameComponent override; public Dictionary/List fields)
    //   SimpleLeadership.FactionLeadershipData.ExposeData()   (plain IExposable method)
    //   CombatExtended.LoadoutManager.ExposeData()            (GameComponent override)
    //   Verse.Thing.Discarded / RimWorld.Planet.WorldObject.Destroyed
    public static class StaleSaveRefFixes
    {
        private static readonly (string type, string label)[] Targets =
        {
            ("VanillaTraitsExpanded.TraitsManager", "Vanilla Traits Expanded tracker"),
            ("SimpleLeadership.FactionLeadershipData", "Simple Leadership settlement-leaders"),
            ("CombatExtended.LoadoutManager", "Combat Extended loadout-assignment"),
        };

        public static void Apply(Harmony h)
        {
            foreach (var (typeName, label) in Targets)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null)
                {
                    Log.Message($"[LocalCSharpFixes] skip {label} fix (mod not present)");
                    continue;
                }
                var m = AccessTools.Method(t, "ExposeData");
                if (m == null)
                {
                    Log.Warning($"[LocalCSharpFixes] skip {label} fix (ExposeData not found; mod layout changed?)");
                    continue;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(StaleSaveRefFixes), nameof(DropStaleRefsBeforeSave)));
            }
        }

        // A destroyed pawn can still be scribed (kept world pawns); only a Discarded one saves as a
        // null reference, so that's the pawn criterion. World objects leave the deep-saved world
        // list the moment they're destroyed, so for them Destroyed is the criterion.
        private static bool IsStale(object key)
        {
            switch (key)
            {
                case Pawn p: return p.Discarded;
                case WorldObject wo: return wo.Destroyed;
                default: return false;
            }
        }

        public static void DropStaleRefsBeforeSave(object __instance)
        {
            try
            {
                if (Scribe.mode != LoadSaveMode.Saving) return;
                foreach (var field in __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var value = field.GetValue(__instance);
                    switch (value)
                    {
                        case IDictionary dict:
                        {
                            var stale = new List<object>();
                            foreach (var key in dict.Keys)
                                if (IsStale(key))
                                    stale.Add(key);
                            foreach (var key in stale)
                                dict.Remove(key);
                            break;
                        }
                        case IList list when !(value is Array):
                        {
                            for (var i = list.Count - 1; i >= 0; i--)
                                if (IsStale(list[i]))
                                    list.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] stale-reference cleanup failed for {__instance?.GetType().Name}: {e.Message}");
            }
        }
    }
}
