using System;
using System.Collections.Concurrent;
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

            ApplyGearOffsetFix(h);
        }

        // Core40k.GetOffsetFromGearPatch postfixes StatWorker.StatOffsetFromGear -- every gear
        // stat query on every pawn (DPA 2026-08-26: 3489 calls/frame, ~5.6ms avg) -- and does two
        // linear TryGetComp scans per call, though only RimDark's own customizable gear carries
        // CompGraphicParent/CompMultiColor. Patch THEIR postfix (Harmony detours the method
        // itself, so the generated stat-worker replacement hits our prefix) with a per-ThingDef
        // cache of "can this def have either comp at all"; plain gear skips both scans. Defs with
        // the comps run the original postfix unchanged. Binding is by the target's own parameter
        // name (gear) -- its other params (__result/stat) aren't needed.
        // Signatures verified against the installed 1.6 DLL:
        //   static void Core40k.GetOffsetFromGearPatch.Postfix(ref float __result, Thing gear, StatDef stat)
        private static Type graphicParentType;
        private static Type multiColorType;
        private static readonly ConcurrentDictionary<ThingDef, bool> gearOffsetDefs = new ConcurrentDictionary<ThingDef, bool>();

        private static void ApplyGearOffsetFix(Harmony h)
        {
            var gearPatch = AccessTools.TypeByName("Core40k.GetOffsetFromGearPatch");
            if (gearPatch == null) { Log.Message("[LocalCSharpFixes] skip Core40k.GetOffsetFromGearPatch (type not present)"); return; }
            var postfix = AccessTools.Method(gearPatch, "Postfix");
            graphicParentType = AccessTools.TypeByName("Core40k.CompGraphicParent");
            multiColorType = AccessTools.TypeByName("Core40k.CompMultiColor");
            if (postfix == null || graphicParentType == null || multiColorType == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Core40k.GetOffsetFromGearPatch (member not found; mod layout changed?)");
                return;
            }
            h.Patch(postfix, prefix: new HarmonyMethod(typeof(GrimWorld40kFixes), nameof(SkipGearOffsetForPlainGear)));
        }

        public static bool SkipGearOffsetForPlainGear(Thing gear)
        {
            var def = gear?.def;
            if (def == null)
                return true; // let the original handle its own null check
            return gearOffsetDefs.GetOrAdd(def, DefCanHaveGearOffsets);
        }

        private static bool DefCanHaveGearOffsets(ThingDef def)
        {
            var comps = def.comps;
            if (comps == null)
                return false;
            for (var i = 0; i < comps.Count; i++)
            {
                var compClass = comps[i]?.compClass;
                if (compClass != null && (graphicParentType.IsAssignableFrom(compClass) || multiColorType.IsAssignableFrom(compClass)))
                    return true;
            }
            return false;
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
