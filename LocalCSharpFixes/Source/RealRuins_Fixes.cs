using System;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // RealRuins rebuilds blueprint pawns from raw donor-save XML (PawnRestoreUtility). Its
    // ValidateAndFixHediffNode checks that the hediff's BodyPartDef exists locally, but the actual
    // part is resolved by Scribe_BodyParts from BodyDef name + index; when the donor ran a race mod
    // we don't have (seen: BodyDef "Gor"), the part resolves to null and RestorePawnHealth still
    // adds the hediff (null part is legal for whole-body hediffs). It also deserialises hediffs via
    // ScribeExtractor under LoadingVars, keeping the donor save's <loadID> values. Result, observed
    // 2026-08-24: injuries/missing-parts with null parts spamming "Hediff_Injury has null part
    // after loading" on every subsequent save load, and donor loadIDs colliding with local hediffs
    // ("Cannot register ... id already used") at save/load. Sanitise each restored pawn: drop
    // part-less injuries (untendable dead weight) and re-ID every hediff. Safe here: nothing
    // references the fresh hediffs by ID yet (the combat-log link is an object WeakReference).
    // Signatures verified against the installed 1.6 DLL:
    //   Pawn RealRuins.PawnRestoreUtility.MakePawnWithRawXml(string xml)   (internal, returns null on failure)
    public static class RealRuinsFixes
    {
        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("RealRuins.PawnRestoreUtility");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip RealRuins pawn-restore fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "MakePawnWithRawXml");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip RealRuins pawn-restore fix (MakePawnWithRawXml not found; mod layout changed?)");
                return;
            }
            h.Patch(m, postfix: new HarmonyMethod(typeof(RealRuinsFixes), nameof(SanitizeRestoredPawn)));
        }

        public static void SanitizeRestoredPawn(Pawn __result)
        {
            try
            {
                var hediffs = __result?.health?.hediffSet?.hediffs;
                if (hediffs == null) return;
                var removed = 0;
                for (var i = hediffs.Count - 1; i >= 0; i--)
                {
                    var hd = hediffs[i];
                    if ((hd is Hediff_Injury || hd is Hediff_MissingPart) && hd.Part == null)
                    {
                        __result.health.RemoveHediff(hd);
                        removed++;
                        continue;
                    }
                    hd.loadID = Find.UniqueIDsManager.GetNextHediffID();
                }
                if (removed > 0)
                    Log.Message($"[LocalCSharpFixes] RealRuins: dropped {removed} part-less injur{(removed == 1 ? "y" : "ies")} from restored pawn {__result.LabelShort}");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] RealRuins SanitizeRestoredPawn failed: {e.Message}");
            }
        }
    }
}
