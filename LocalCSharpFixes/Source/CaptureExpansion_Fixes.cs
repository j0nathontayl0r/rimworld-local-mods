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

            var driver = AccessTools.TypeByName("CaptureExpansion.JobDriver_RestrainPrisonerToBed");
            var reservations = driver == null ? null : AccessTools.Method(driver, "TryMakePreToilReservations");
            if (reservations == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Capture Expansion restrain-release fix (TryMakePreToilReservations not found; mod layout changed?)");
                return;
            }
            h.Patch(reservations, postfix: new HarmonyMethod(typeof(CaptureExpansionFixes), nameof(ReleaseBedOnFastPath)));
        }

        // Second restrain bug, exposed once the geometry gate above lets the job run (observed
        // 2026-08-26): the driver's prisoner-already-in-bed fast path (JumpIf to
        // goToInteractionCell) jumps over its own Toils_Reserve.Release(B) toil, so the warden
        // holds the bed reservation from TryMakePreToilReservations all the way through WaitWith.
        // WaitWith's initAction ForceWaits the prisoner, and vanilla 1.6 ForceWait starts a
        // LayDown job for a pawn in bed (PawnUtility.ForceWait: if (pawn.InBed()) def = LayDown*),
        // which then can't reserve the prisoner's own bed -> red "Could not reserve ... for job
        // LayDown" against the warden's stale claim, once per restrain. Mirror what the mod's
        // carry path does at the Release toil: when the prisoner already lies in the target bed,
        // drop the warden's bed reservation right after it's made. The prisoner reservation
        // (target A) stays, so no second warden can take the same restrain job.
        public static void ReleaseBedOnFastPath(JobDriver __instance, bool __result)
        {
            try
            {
                if (!__result)
                    return;
                var job = __instance.job;
                var prisoner = job?.targetA.Thing as Pawn;
                var bed = job?.targetB.Thing as Building_Bed;
                if (prisoner == null || bed == null || RestUtility.CurrentBed(prisoner) != bed)
                    return;
                __instance.pawn.Map?.reservationManager.Release(bed, __instance.pawn, job);
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Capture Expansion ReleaseBedOnFastPath failed: {e.Message}");
            }
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
