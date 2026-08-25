using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Capture Expansion: JobDriver_RestrainPrisonerToBed walks the warden to
    // GotoThing(bed, PathEndMode.InteractionCell). Vanilla beds have no interaction cell, so the
    // pather falls back to the first standable cell adjacent to the bed's rect
    // (ThingUtility.InteractionCellWhenAt) -- which, depending on walls around the bed, can border
    // the FOOT of the bed while the prisoner lies at the head. The next toil,
    // WaitWith(prisoner, 180, PathEndMode.Touch), fail-fasts via FailOnCannotTouch unless the
    // warden is 8-way adjacent to the prisoner's cell, so the job dies the instant it starts, and
    // WorkGiver_Warden_RestrainPrisonerToBed -- which checks none of this geometry -- re-issues it
    // in the same tick: "Pierce started 10 jobs in one tick. newJob=CE_RestrainPrisonerToBed"
    // (observed 2026-08-25, prisoner already lying in the target hospital bed). Predict the wait
    // toil's own check from the exact cell the driver will walk to and suppress the job when it
    // cannot succeed; the warden does other work instead, and restraining resumes if the bed is
    // repositioned.
    // Only the prisoner-already-in-bed fast path is gated: the carry path ends in the same toils,
    // but a failure there tucks the prisoner into bed first, so its retry takes the fast path and
    // is caught here on the next issue.
    // Signatures verified against the installed 1.6 DLLs:
    //   Job CaptureExpansion.WorkGiver_Warden_RestrainPrisonerToBed.JobOnThing(Pawn pawn, Thing t, bool forced)
    //   static bool Verse.ReachabilityImmediate.CanReachImmediate(IntVec3, LocalTargetInfo, Map, PathEndMode, Pawn)
    //   static Building_Bed RimWorld.RestUtility.CurrentBed(Pawn p)
    public static class CaptureExpansionFixes
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("CaptureExpansion.WorkGiver_Warden_RestrainPrisonerToBed");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip Capture Expansion restrain fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "JobOnThing");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Capture Expansion restrain fix (JobOnThing not found; mod layout changed?)");
                return;
            }
            h.Patch(m, postfix: new HarmonyMethod(typeof(CaptureExpansionFixes), nameof(SuppressUntouchableRestrain)));
        }

        public static void SuppressUntouchableRestrain(Pawn pawn, Thing t, ref Job __result)
        {
            try
            {
                if (__result == null || !(t is Pawn prisoner))
                    return;
                var bed = __result.targetB.Thing as Building_Bed;
                if (bed == null || !bed.Spawned || RestUtility.CurrentBed(prisoner) != bed)
                    return;
                // bed.InteractionCell is the fallback cell the driver's goto resolves to; the wait
                // toil then requires touch-adjacency to the prisoner from wherever the warden stands.
                if (!ReachabilityImmediate.CanReachImmediate(bed.InteractionCell, prisoner, bed.Map, PathEndMode.Touch, pawn))
                    __result = null;
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Capture Expansion SuppressUntouchableRestrain failed: {e.Message}");
            }
        }
    }
}
