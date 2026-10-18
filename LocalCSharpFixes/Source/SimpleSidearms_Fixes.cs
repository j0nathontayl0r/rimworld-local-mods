using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Simple Sidearms: with "auto-switch to tools" on, ToilMaker_MakeToil_Patches.CheckForTools adds
    // a pre-init action to every toil that calls
    // WeaponAssingment.equipBestWeaponFromInventoryByStatModifiers for the toil's skill stats. When
    // that swaps weapons, equipSpecificWeapon ends the current job if it is a Hunt
    // (EndCurrentJob(..., startNewJob: true)). JobGiver_Work at once gives the same Hunt, CE's
    // JobDriver_Hunt reaches its next toil in the same tick, the tool check swaps again, and the
    // hunter hits vanilla's "started 10 jobs in one tick" breaker (observed 2026-10-18: 45 times, one
    // hunter carrying a melee primary, a plant-speed katana and seven looted guns). A tool swap
    // never helps a hunt, so skip the tool check while the pawn's job is Hunt. Signature verified
    // against the installed 1.6 DLL:
    //   static bool PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment
    //        .equipBestWeaponFromInventoryByStatModifiers(Pawn pawn, List<StatDef> stats)
    public static class SimpleSidearmsFixes
    {
        public static void Apply(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.WeaponAssingment");
                var m = t == null ? null : AccessTools.Method(t, "equipBestWeaponFromInventoryByStatModifiers");
                if (m == null)
                {
                    Log.Message("[LocalCSharpFixes] skip Simple Sidearms hunt tool-swap fix (target not present)");
                    return;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(SimpleSidearmsFixes), nameof(ToolSwap_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Simple Sidearms hunt tool-swap fix failed: {e.Message}");
            }
        }

        public static bool ToolSwap_Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn?.CurJobDef != JobDefOf.Hunt)
                return true;
            __result = false;
            return false;
        }
    }
}
