using System;
using System.Collections.Concurrent;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Vanilla (1.7): GenTypes.IsSealedWithCache wraps a plain Dictionary in one global lock:
    //     lock (isSealedCache) { if (isSealedCache.TryGetValue(type, out var v)) return v; ... }
    // ThingWithComps.GetComp<T> calls it on every miss for things with 3+ comps, so it runs
    // constantly, from the main thread AND the parallel pawn-render workers (ParallelPreDraw etc.),
    // and they queue on that lock. Profiled 2026-10-15: Monitor.Enter_Slowpath was 17% of the main
    // thread, ~70% of it under IsSealedWithCache, reached from CE, VEF, Core40k, stat workers and
    // vanilla comps alike; dotnet-counters showed ~400 contentions/s without a profiler attached.
    // Type.IsSealed never changes for a loaded type, so the lock buys nothing: answer from a
    // lock-free ConcurrentDictionary instead. Vanilla also clears its cache on reload; there's
    // nothing to invalidate here, since the answer per Type is permanent.
    // Resolved by name, not [HarmonyPatch], because the 1.6 build compiles these same sources and
    // this member may not exist there.
    // Signature verified against the installed 1.7 Assembly-CSharp:
    //   public static bool Verse.GenTypes.IsSealedWithCache(this Type type)
    public static class Vanilla17Fixes
    {
        private static readonly ConcurrentDictionary<Type, bool> sealedCache = new ConcurrentDictionary<Type, bool>();

        public static void Apply(Harmony h)
        {
            try
            {
                var m = AccessTools.Method(typeof(GenTypes), "IsSealedWithCache", new[] { typeof(Type) });
                if (m == null)
                {
                    Log.Message("[LocalCSharpFixes] skip lock-free IsSealedWithCache (method not present; pre-1.7?)");
                    return;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(Vanilla17Fixes), nameof(IsSealedWithCache_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching GenTypes.IsSealedWithCache: {e.Message}");
            }

            try
            {
                var m = AccessTools.Method(typeof(JobDriver_SocialRelax), nameof(JobDriver.TryMakePreToilReservations));
                h.Patch(m, prefix: new HarmonyMethod(typeof(Vanilla17Fixes), nameof(SocialRelaxReservations_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching JobDriver_SocialRelax.TryMakePreToilReservations: {e.Message}");
            }
        }

        // Vanilla (1.7): JobDriver_SocialRelax.TryMakePreToilReservations(errorOnFailed) calls
        // pawn.ReserveSittableOrSpot(cell, pawn.CurJob) without passing errorOnFailed, so it always
        // logs "Could not reserve" when the seat is taken. A queued job starts with errorOnFailed =
        // false and is allowed to fail here (seen 2026-10-17: Loreen's queued SocialRelax after Stomp
        // took the chair). Work out the same seat cell as vanilla; if it can't be reserved, fail
        // quietly. A player-forced job keeps vanilla's path, because Reserve lets it take the seat.
        public static bool SocialRelaxReservations_Prefix(JobDriver_SocialRelax __instance, bool errorOnFailed, ref bool __result)
        {
            Job job = __instance.job;
            Pawn pawn = __instance.pawn;
            if (errorOnFailed || job == null || job.playerForced || pawn == null)
                return true;
            Thing chair = job.GetTarget(TargetIndex.B).Thing;
            if (chair == null || !Toils_Ingest.TryFindFreeSittingSpotOnThing(chair, pawn, out var cell))
                cell = job.GetTarget(TargetIndex.B).Cell;
            if (pawn.Map == null || cell.Impassable(pawn.Map))
                return true;
            // Same target choice as ReservationUtility.ReserveSittableOrSpot.
            Building edifice = cell.GetEdifice(pawn.Map);
            LocalTargetInfo target = edifice == null || edifice.def.building.multiSittable ? cell : edifice;
            if (pawn.CanReserve(target))
                return true;
            __result = false;
            return false;
        }

        public static bool IsSealedWithCache_Prefix(Type type, ref bool __result)
        {
            if (type == null)
                return true; // let vanilla throw as it always did
            if (!sealedCache.TryGetValue(type, out __result))
            {
                __result = type.IsSealed;
                sealedCache.TryAdd(type, __result);
            }
            return false;
        }
    }
}
