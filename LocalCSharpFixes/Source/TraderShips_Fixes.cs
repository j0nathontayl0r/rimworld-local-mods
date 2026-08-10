using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Trader Ships (2046222331): IncidentWorkerTraderShip.TryExecuteWorker ignores parms.traderKind
    // — MakeTraderShip always rolls a weighted-random TraderKindDef before handing it to
    // CompShip.GenerateInternalTradeShip — so the dev action "Call trade ship of kind" spawns a
    // random ship regardless of the selected kind. Rebuild of the pre-wipe fix, which was an
    // in-place edit of the mod's DLL and died with the Windows wipe (see recovered-spec); done as a
    // Harmony relay this time so workshop updates can't erase it.
    //
    // Relay: stash parms.traderKind on TryExecuteWorker entry, consume it inside
    // GenerateInternalTradeShip (replacing the rolled kind), and always clear it in a finalizer so
    // a failed incident can't leak a forced kind into a later natural one. The mod's quest-path
    // callers of MakeTraderShip never pass through TryExecuteWorker, and natural incidents carry a
    // null traderKind — both behave exactly as stock.
    //
    // Signatures verified against the installed DLL (strings) + upstream source:
    //   protected override bool TraderShips.IncidentWorkerTraderShip.TryExecuteWorker(IncidentParms)
    //   void TraderShips.CompShip.GenerateInternalTradeShip(Map, TraderKindDef)
    public static class TraderShipsFixes
    {
        private static TraderKindDef forcedKind;

        public static void Apply(Harmony h)
        {
            try
            {
                var worker = AccessTools.TypeByName("TraderShips.IncidentWorkerTraderShip");
                var compShip = AccessTools.TypeByName("TraderShips.CompShip");
                if (worker == null || compShip == null)
                {
                    Log.Message("[LocalCSharpFixes] skip TraderShips kind fix (Trader Ships not present)");
                    return;
                }
                var tryExec = AccessTools.Method(worker, "TryExecuteWorker");
                var generate = AccessTools.Method(compShip, "GenerateInternalTradeShip");
                if (tryExec == null || generate == null)
                {
                    Log.Warning("[LocalCSharpFixes] skip TraderShips kind fix (method not found; mod layout changed?)");
                    return;
                }

                h.Patch(tryExec,
                        prefix: new HarmonyMethod(typeof(TraderShipsFixes), nameof(StashForcedKind)),
                        finalizer: new HarmonyMethod(typeof(TraderShipsFixes), nameof(ClearForcedKind)));
                h.Patch(generate,
                        prefix: new HarmonyMethod(typeof(TraderShipsFixes), nameof(ForceKind)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] TraderShips kind fix failed to apply: {e.Message}");
            }
        }

        public static void StashForcedKind(IncidentParms __0)
            => forcedKind = __0?.traderKind;

        public static Exception ClearForcedKind(Exception __exception)
        {
            forcedKind = null;
            return __exception;
        }

        public static void ForceKind(ref TraderKindDef __1)
        {
            if (forcedKind != null)
            {
                __1 = forcedKind;
                forcedKind = null;
            }
        }
    }
}
