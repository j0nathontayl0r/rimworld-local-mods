using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // Vanilla Traits Expanded (not Vanilla Trading Expanded -- that one is VTE_Fixes.cs):
    // TraitsManager tracks pawns in Pawn-keyed dictionaries and lists saved with
    // LookMode.Reference (forcedJobs, madSurgeonsWithLastHarvestedTick, cowards, ...). Its
    // RemoveDestroyedPawn hook misses pawns destroyed and discarded outside its notify path, so
    // the stale reference scribes as "null" and every later load logs "Null key while loading
    // dictionary of Verse.Pawn and System.Int32, label=madSurgeonsWithLastHarvestedTick"
    // (observed 2026-08-25). Strip discarded pawns from every Pawn-keyed collection just before
    // the manager saves. Collections are found by reflection over the instance fields, so a mod
    // update that adds or renames a tracker stays covered without a code change.
    // Signatures verified against the installed 1.6 DLL:
    //   VanillaTraitsExpanded.TraitsManager.ExposeData()   (GameComponent override; public Dictionary/List fields)
    public static class TraitsExpandedFixes
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("VanillaTraitsExpanded.TraitsManager");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip Vanilla Traits Expanded tracker fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "ExposeData");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Vanilla Traits Expanded tracker fix (ExposeData not found; mod layout changed?)");
                return;
            }
            h.Patch(m, prefix: new HarmonyMethod(typeof(TraitsExpandedFixes), nameof(DropDiscardedPawnsBeforeSave)));
        }

        // A destroyed pawn can still be scribed (kept world pawns); only a Discarded one saves as
        // a null reference, so that's the removal criterion.
        private static bool IsStale(object key) => key is Pawn p && p.Discarded;

        public static void DropDiscardedPawnsBeforeSave(object __instance)
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
                Log.Warning($"[LocalCSharpFixes] Vanilla Traits Expanded tracker cleanup failed: {e.Message}");
            }
        }
    }
}
