using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Vanilla Factions Expanded - Empire: WorldComponent_Hierarchy.RefreshPawns filters with
    //     where p.royalty.GetCurrentTitleInFaction(empire).def.seniority > 0
    // and GetCurrentTitleInFaction returns NULL for a pawn holding no title in that faction, so the
    // .def dereference NREs (observed 2026-08-29). The pawns come from EmpireUtility's static cache
    // (colonistsWithTitle), which is only rebuilt by Notify_ColonistsChanged / Notify_TitlesChanged --
    // driven off a fixed patch set (Pawn_RoyaltyTracker.SetTitle/UpdateRoyalTitle/ReduceTitle,
    // Pawn.SetFaction, Pawn.Kill, Pawn_GuestTracker.SetGuestStatus). Any other route that strips a
    // colonist's Empire title, or a swapped Empire Faction instance, leaves a stale entry behind and
    // every later refresh throws. Vanilla isn't at fault: HasAnyTitleIn (used to fill the cache) and
    // GetCurrentTitleInFaction share the same title.faction == faction test.
    // RefreshPawns runs on load and once per in-game day, and the throw lands after CleanupTitleHolders
    // and FillTitles but before the colonists are appended and sorted -- so each failed refresh leaves
    // TitleHolders holding NPC nobles only, and FillTitles' quota maths stops counting titled
    // colonists. Filtering the cache read fixes RefreshPawns and the mod's twelve other callers
    // (hierarchy tab, honour workers, quest reward gating, main button) in one place, and matches what
    // the method claims to return. Patching the read side rather than the compiler-generated lambda
    // (<>c__DisplayClass12_0.<RefreshPawns>b__0) also survives a mod update renumbering it.
    // Signatures verified against the installed 1.6 DLL:
    //   static IEnumerable<Pawn> VFEEmpire.EmpireUtility.AllColonistsWithTitle()   (returns the cache list)
    //   RoyalTitle RimWorld.Pawn_RoyaltyTracker.GetCurrentTitleInFaction(Faction)  (null when untitled there)
    public static class VFEEmpireFixes
    {
        private static readonly HashSet<string> reported = new HashSet<string>();

        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("VFEEmpire.EmpireUtility");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip VFE Empire title-holder fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "AllColonistsWithTitle");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip VFE Empire title-holder fix (AllColonistsWithTitle not found; mod layout changed?)");
                return;
            }
            h.Patch(m, postfix: new HarmonyMethod(typeof(VFEEmpireFixes), nameof(DropColonistsWithoutEmpireTitle)));
        }

        public static void DropColonistsWithoutEmpireTitle(ref IEnumerable<Pawn> __result)
        {
            try
            {
                if (__result == null)
                    return;
                var empire = Faction.OfEmpire;
                if (empire == null)
                    return; // GetCurrentTitleInFaction(null) logs an error of its own
                var anyStale = false;
                foreach (var p in __result)
                    if (!HasEmpireTitle(p, empire)) { anyStale = true; break; }
                if (!anyStale)
                    return; // healthy case allocates nothing
                var kept = new List<Pawn>();
                foreach (var p in __result)
                {
                    if (HasEmpireTitle(p, empire))
                        kept.Add(p);
                    else if (p != null && reported.Add(p.ThingID))
                        Log.Message($"[LocalCSharpFixes] {p.LabelShortCap} is cached as an Empire title holder but holds no Empire title; excluding it from VFE Empire's hierarchy.");
                }
                __result = kept;
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] VFE Empire title-holder filter failed: {e.Message}");
            }
        }

        private static bool HasEmpireTitle(Pawn p, Faction empire)
        {
            return p?.royalty != null && p.royalty.GetCurrentTitleInFaction(empire)?.def != null;
        }
    }
}
