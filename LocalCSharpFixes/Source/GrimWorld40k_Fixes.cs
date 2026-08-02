using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // RimDark 40k Framework (Core40k.ArtificialPartsAffinity) postfixes
    // PawnCapacityUtility.CalculatePartEfficiency and calls GetStatValue for
    // BEWH_ArtificialPartsAffinityFactor with cacheStaleAfterTicks:-1 (uncached), once per
    // body part per capacity recalculation — a full stat-pipeline evaluation thousands of
    // times per second (DPA: ~2.3ms avg/frame, 36ms spikes). For every pawn without the
    // 40k affinity the factor is 1 and the whole postfix is a no-op.
    //
    // Replacement: same semantics, but the stat is read through vanilla's stat cache
    // (120 ticks) and checked first, so non-40k pawns exit before touching hediffs.
    // Cosmetic difference: at factor==1 the original still added a duplicate capacity
    // impactor line in the health tab; we skip it.
    public static class GrimWorld40kFixes
    {
        private static StatDef affinityStat;

        public static void Apply(Harmony h)
        {
            var theirs = AccessTools.TypeByName("Core40k.ArtificialPartsAffinity");
            if (theirs == null) { Log.Message("[LocalCSharpFixes] skip Core40k.ArtificialPartsAffinity (type not present)"); return; }
            var theirPostfix = AccessTools.Method(theirs, "Postfix");
            var original = AccessTools.Method(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculatePartEfficiency));
            if (theirPostfix == null || original == null) { Log.Warning("[LocalCSharpFixes] skip ArtificialPartsAffinity (method not found)"); return; }

            h.Unpatch(original, theirPostfix);
            h.Patch(original, postfix: new HarmonyMethod(typeof(GrimWorld40kFixes), nameof(CalculatePartEfficiency_Postfix)));
        }

        private static void CalculatePartEfficiency_Postfix(ref float __result, HediffSet diffSet, BodyPartRecord part,
                                                            List<PawnCapacityUtility.CapacityImpactor> impactors)
        {
            var pawn = diffSet?.pawn;
            if (pawn == null)
                return;
            affinityStat ??= DefDatabase<StatDef>.GetNamedSilentFail("BEWH_ArtificialPartsAffinityFactor");
            if (affinityStat == null)
                return;
            var factor = pawn.GetStatValue(affinityStat, applyPostProcess: true, cacheStaleAfterTicks: 120);
            if (factor == 1f)
                return;

            Hediff hediff;
            if (diffSet.HasDirectlyAddedPartFor(part))
                hediff = diffSet.GetFirstHediffMatchingPart<Hediff_AddedPart>(part);
            else if (diffSet.IsBionicOrImplant(part.def))
                hediff = FirstImplantFor(diffSet, part.def);
            else
                return;

            impactors?.Add(new PawnCapacityUtility.CapacityImpactorHediff { hediff = hediff });
            __result *= factor;
        }

        private static Hediff FirstImplantFor(HediffSet diffSet, BodyPartDef partDef)
        {
            var hediffs = diffSet.hediffs;
            for (var i = 0; i < hediffs.Count; i++)
            {
                var hd = hediffs[i];
                if (hd.Part != null && hd.Part.def == partDef && hd.def.countsAsAddedPartOrImplant)
                    return hd;
            }
            return null;
        }
    }
}
