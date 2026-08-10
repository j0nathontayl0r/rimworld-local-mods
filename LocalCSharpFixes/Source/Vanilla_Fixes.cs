using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Signatures verified against RimWorldLinux_Data/Managed/Assembly-CSharp.dll (1.6) via MetadataLoadContext:
    //   RimWorld.StorageSettings.CopyFrom(StorageSettings other)
    //   Verse.AI.ReservationManager.LogCouldNotReserveError(Pawn, Job, LocalTargetInfo, int, int, ReservationLayerDef)
    //   RimWorld.Pawn_FlightTracker.Notify_JobStarted(Job job)
    //   static RimWorld.ForbidUtility.SetForbidden(Thing t, bool value, bool warnOnFail)
    //   Verse.Thing.Destroy(DestroyMode mode)
    //   Verse.PathFinderMapData.Notify_Reservation(ReservationManager.Reservation)

    // Vanilla: when the World Pawn GC discards a pawn, CompAttachBase.PostDestroy tries to destroy its
    // attached AttachableThings (e.g. Fire). If the fire already went out and was destroyed, the stale
    // reference calls Destroy again -> "Tried to destroy already-destroyed thing Fire...". Skip the
    // redundant destroy for already-destroyed AttachableThings; leave all other cases alone.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    internal static class Thing_Destroy_DoubleDestroyGuard
    {
        private static bool Prefix(Thing __instance)
        {
            if (__instance != null && __instance.Destroyed && __instance is AttachableThing)
                return false;
            return true;
        }
    }

    // Vanilla: Frame.CompleteConstruction calls StorageSettings.CopyFrom(frame.storageSettings). Buildings
    // that never initialised storageSettings (e.g. RefineryLoadingBay) pass null -> NRE, the job driver
    // crashes and a mechanoid can never complete the building. Skip the copy when other is null; the
    // building completes with default storage settings.
    [HarmonyPatch(typeof(StorageSettings), "CopyFrom")]
    internal static class StorageSettings_CopyFrom_NullGuard
    {
        private static bool Prefix(StorageSettings other)
        {
            return other != null; // false = skip original when there's nothing to copy
        }
    }

    // Vanilla (1.6): Pawn_FlightTracker.Notify_JobStarted (new in 1.6) NREs when animals loaded from a
    // pre-1.6 save start a job -- the tracker exists on every pawn but its state isn't initialised for
    // non-flying animals from old saves. Swallow the exception; no gameplay effect for animals that
    // can't fly.
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    internal static class PawnFlightTracker_NotifyJobStarted_Suppress
    {
        private static Exception Finalizer(Exception __exception)
        {
            return null; // swallow any NRE from the original
        }
    }

    // Vanilla: JobDriver_RecolorApparel dye reservation conflicts spam "Could not reserve" through
    // ReservationManager.LogCouldNotReserveError. CanReserve doesn't see physical-interaction reservers,
    // so a CanReserve pre-check upstream doesn't help -- suppress the log itself, but ONLY for
    // RecolorApparel jobs (covers both normal and physical-interaction reservation conflicts).
    [HarmonyPatch(typeof(ReservationManager), "LogCouldNotReserveError")]
    internal static class ReservationManager_LogCouldNotReserveError_SkipRecolor
    {
        private static bool Prefix(Job job)
        {
            if (job?.def != null && job.def.defName == "RecolorApparel")
                return false; // skip the log call
            return true;
        }
    }

    // Alpha Animals: the pig-dig job spawns AA_BlackTruffles forbidden (mimicking the mining-drop
    // pattern), forcing a manual allow before hauling. Patch the vanilla ForbidUtility.SetForbidden to
    // not forbid truffles. Side effect: truffles also can't be manually forbidden -- acceptable for a
    // fungible food item.
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.SetForbidden))]
    internal static class ForbidUtility_SetForbidden_AllowTruffles
    {
        private static bool Prefix(Thing t, bool value)
        {
            if (value && t?.def != null && t.def.defName == "AA_BlackTruffles")
                return false; // skip forbidding
            return true;
        }
    }

    // Vanilla (1.6): PathFinderMapData.Notify_Reservation NREs when a reservation is released for an
    // already-destroyed thing (no map to update), preceded by vanilla's own "Releasing destroyed
    // thing ..." warning. Trigger in this colony: MendAndRecycle finishing a mend bill on stackable
    // CE ordnance (smoke grenades), which merges/destroys the mended stack before the Release toil
    // runs. The pathfinder has nothing to update for a mapless thing; swallow the NRE so the job
    // ends cleanly instead of crashing the driver.
    [HarmonyPatch(typeof(PathFinderMapData), "Notify_Reservation")]
    internal static class PathFinderMapData_NotifyReservation_Suppress
    {
        private static Exception Finalizer(Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }
    }
}
