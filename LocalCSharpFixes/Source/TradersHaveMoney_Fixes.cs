using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Traders Have Money (2898006725): its THMStartup static constructor iterates all TraderKindDefs at
    // startup and overwrites every orbital trader's silver to orbitalMin~orbitalMax (default 2000~4000),
    // clobbering Orbital_Horder's intentional 10000~100000 range. Because LocalCSharpFixes loads LAST,
    // this [StaticConstructorOnStartup] runs AFTER THMStartup and restores the Horder's silver.
    // NOTE: distinct from "Traders have more Silver" (3513786356), which must still load last in RimSort
    // for the vanilla orbital traders.
    [StaticConstructorOnStartup]
    internal static class TradersHaveMoney_Fixes
    {
        static TradersHaveMoney_Fixes()
        {
            try
            {
                var horder = DefDatabase<TraderKindDef>.GetNamedSilentFail("Orbital_Horder");
                if (horder?.stockGenerators == null) return;
                var countRange = AccessTools.Field(typeof(StockGenerator), "countRange");
                if (countRange == null) return;

                int fixedCount = 0;
                foreach (var sg in horder.stockGenerators)
                {
                    if (sg != null && sg.HandlesThingDef(ThingDefOf.Silver))
                    {
                        countRange.SetValue(sg, new IntRange(10000, 100000));
                        fixedCount++;
                    }
                }
                if (fixedCount > 0)
                    Log.Message($"[LocalCSharpFixes] restored Orbital_Horder silver (10000~100000) on {fixedCount} generator(s).");
            }
            catch (Exception e)
            {
                Log.Warning("[LocalCSharpFixes] Orbital_Horder silver restore failed: " + e.Message);
            }
        }
    }
}
