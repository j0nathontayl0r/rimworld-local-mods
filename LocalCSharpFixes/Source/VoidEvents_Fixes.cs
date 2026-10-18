using System;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // [RH2] Faction: V.O.I.D. (2883208829, VoidEvents.dll, 1.6 build): Pawn_JobTracker_JobTrackerTick_Patch
    // is a prefix on Pawn_JobTracker.JobTrackerTick that returns false for every pawn that isn't
    // spawned. A carried pawn isn't spawned, but vanilla ticks it through the carrier's held things,
    // and JobTrackerTick is where vanilla resets jobsGivenThisTick each tick. With the prefix, a
    // carried baby's counter never resets: each toil of the carrier's walking baby play starts a
    // BabyPlay job on the baby, the baby goes back to Carried when the toil ends, and after five of
    // these pairs, over many ticks, vanilla logs "Guo started 10 jobs in one tick" and gives the
    // baby an error-recovery Wait job (observed 2026-10-18, repeatedly). The carried pawn's
    // DriverTick is skipped too. Let JobTrackerTick run for a pawn held in a Pawn_CarryTracker; every
    // other unspawned pawn keeps V.O.I.D.'s behaviour. Signature verified against the installed DLL:
    //   public static bool VoidEvents.Pawn_JobTracker_JobTrackerTick_Patch.Prefix(Pawn_JobTracker __instance)
    public static class VoidEventsFixes
    {
        private static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> trackerPawn =
            AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        public static void Apply(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("VoidEvents.Pawn_JobTracker_JobTrackerTick_Patch");
                var m = t == null ? null : AccessTools.Method(t, "Prefix", new[] { typeof(Pawn_JobTracker) });
                if (m == null)
                {
                    Log.Message("[LocalCSharpFixes] skip V.O.I.D. carried-pawn job tick fix (target not present)");
                    return;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(VoidEventsFixes), nameof(VoidPrefix_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] V.O.I.D. carried-pawn job tick fix failed: {e.Message}");
            }
        }

        public static bool VoidPrefix_Prefix(Pawn_JobTracker __instance, ref bool __result)
        {
            if (__instance != null && trackerPawn(__instance)?.ParentHolder is Pawn_CarryTracker)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
