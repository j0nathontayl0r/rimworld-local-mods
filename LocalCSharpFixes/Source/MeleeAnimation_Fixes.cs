using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Melee Animation: JobDriver_GrapplePawn.Ensnare -> GrappleFlyer.MakeGrappleFlyer returns null
    // when the victim already stands on the grapple destination cell (its first guard:
    // victim.Position == targetPos), which Ensnare then reports as a red "[MeleeAnim] Failed:
    // failed to spawn flyer." and ends the job Errored (observed 2026-08-26). It's a plain race --
    // the rope reaches the victim just as they step onto the destination -- and the mod already
    // has a graceful path for a missing flyer: TickEnsnared with Flyer == null ends the job
    // Succeeded and triggers the follow-up execution animation. So when the victim is already at
    // the destination (or gone), mark the driver ensnared and let that path finish the job next
    // tick instead of spawning a flyer for a zero-length flight.
    // Signatures verified against the installed 1.6 DLL (zAnimationMod.dll):
    //   void AM.Grappling.JobDriver_GrapplePawn.Ensnare()   (private; public bool HasEnsnared field)
    //   static bool AM.Grappling.GrabUtility.EndGrabAttempt(Pawn pawn)
    //   TargetDestination = job.targetA.Cell, GrappledPawn = job.targetB.Pawn
    public static class MeleeAnimationFixes
    {
        private static FieldInfo hasEnsnared;
        private static MethodInfo endGrabAttempt;

        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("AM.Grappling.JobDriver_GrapplePawn");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip Melee Animation grapple fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "Ensnare");
            hasEnsnared = AccessTools.Field(t, "HasEnsnared");
            var grabUtility = AccessTools.TypeByName("AM.Grappling.GrabUtility");
            endGrabAttempt = grabUtility == null ? null : AccessTools.Method(grabUtility, "EndGrabAttempt");
            if (m == null || hasEnsnared == null || endGrabAttempt == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Melee Animation grapple fix (member not found; mod layout changed?)");
                return;
            }
            h.Patch(m, prefix: new HarmonyMethod(typeof(MeleeAnimationFixes), nameof(EnsnareWithoutFlight)));
        }

        public static bool EnsnareWithoutFlight(JobDriver __instance)
        {
            try
            {
                var job = __instance.job;
                var victim = job?.targetB.Pawn;
                if (victim != null && victim.Spawned && victim.Position != job.targetA.Cell)
                    return true; // normal grapple: a real flight is needed
                hasEnsnared.SetValue(__instance, true);
                endGrabAttempt.Invoke(null, new object[] { victim });
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Melee Animation EnsnareWithoutFlight failed: {e.Message}");
                return true;
            }
        }
    }
}
