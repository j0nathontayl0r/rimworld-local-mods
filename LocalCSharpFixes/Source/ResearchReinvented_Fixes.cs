using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Research Reinvented: WorkGiver_ResearcherRR.HasJobOnThing offers RR_Research jobs on ANY
    // research bench (project researchable there, not forbidden, reservable) but never checks the
    // bench's faction, while JobDriver_ResearchRR carries a global FailOn(!bench.CanUseNow()) where
    // CanUseNow() additionally requires Spawned + powered + Faction == OfPlayer. On a non-player
    // bench (seen: unclaimed SimpleResearchBench spawned by a RealRuins site blueprint) the job dies
    // the tick it starts, JobGiver_Work re-offers it immediately, and the pawn hits vanilla's
    // "started 10 jobs in one tick" breaker forever (observed 2026-08-24, ~12k failed jobs in one
    // site visit). Align the giver with the driver: veto HasJobOnThing when the driver's CanUseNow()
    // conditions can't hold. Signatures verified against the installed 1.6 DLL:
    //   bool PeteTimesSix.ResearchReinvented.Rimworld.WorkGivers.WorkGiver_ResearcherRR
    //        .HasJobOnThing(Pawn pawn, Thing t, bool forced)
    //   SmallExtensions.CanUseNow(bench): Spawned && (no CompPowerTrader || PowerOn)
    //        && (no CompForbiddable || !Forbidden) && Faction == Faction.OfPlayer
    // (Forbidden is already covered by the workgiver's own IsForbidden check.)
    public static class ResearchReinventedFixes
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("PeteTimesSix.ResearchReinvented.Rimworld.WorkGivers.WorkGiver_ResearcherRR");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip Research Reinvented bench fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "HasJobOnThing");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Research Reinvented bench fix (HasJobOnThing not found; mod layout changed?)");
                return;
            }
            h.Patch(m, postfix: new HarmonyMethod(typeof(ResearchReinventedFixes), nameof(BenchUsable_Postfix)));
        }

        public static void BenchUsable_Postfix(Thing t, ref bool __result)
        {
            try
            {
                if (!__result || t == null) return;
                if (!t.Spawned || t.Faction != Faction.OfPlayer)
                {
                    __result = false;
                    return;
                }
                var power = t.TryGetComp<CompPowerTrader>();
                if (power != null && !power.PowerOn) __result = false;
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] RR BenchUsable_Postfix failed: {e.Message}");
            }
        }
    }
}
