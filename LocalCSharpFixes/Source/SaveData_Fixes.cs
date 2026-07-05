using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Several mods hold GameComponent dictionaries keyed by game objects that are never cleaned when
    // those objects are removed during play. Stale keys persist in the save and fail cross-ref on the
    // next load -> "Null key while loading dictionary". Prune the dead keys at save time (before the
    // components serialize), so the written save is clean.
    //
    // Declaring types verified via MetadataLoadContext:
    //   VFEEmpire.GameComponent_Empire.artCreator          (Dictionary<ThingWithComps, Pawn>)
    //   VFEEmpire.GameComponent_Empire.LastRoyalGossipTick (Dictionary<Pawn, int>)
    //   VanillaTradingExpanded.TradingManager.banksByFaction (Dictionary<Faction, Bank>)
    // (GrimWorld Core40k.cachedDecoratives was not confirmed present in the installed build, so it is
    //  intentionally omitted rather than guessed.)
    [HarmonyPatch(typeof(Game), "ExposeData")]
    internal static class Game_ExposeData_CleanStaleDicts
    {
        private static void Prefix()
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;
            var game = Current.Game;
            if (game == null) return;

            Clean(game, "VFEEmpire.GameComponent_Empire", "artCreator", KeyDeadThing);
            Clean(game, "VFEEmpire.GameComponent_Empire", "LastRoyalGossipTick", KeyDeadThing);
            Clean(game, "VanillaTradingExpanded.TradingManager", "banksByFaction", KeyGoneFaction);
        }

        // Pawn is a Thing, so a dead-Thing test covers both ThingWithComps and Pawn keys.
        private static bool KeyDeadThing(object k)
        {
            var t = k as Thing;
            return t != null && (t.Destroyed || t.Discarded);
        }

        private static bool KeyGoneFaction(object k)
        {
            var f = k as Faction;
            return f != null && (Find.FactionManager == null || !Find.FactionManager.AllFactions.Contains(f));
        }

        private static void Clean(Game game, string compTypeName, string fieldName, Func<object, bool> stale)
        {
            try
            {
                var ct = AccessTools.TypeByName(compTypeName);
                if (ct == null) return; // mod not installed
                GameComponent comp = null;
                foreach (var c in game.components)
                {
                    if (ct.IsInstanceOfType(c)) { comp = c; break; }
                }
                if (comp == null) return;
                var fi = AccessTools.Field(ct, fieldName);
                if (fi == null) return;
                var dict = fi.GetValue(comp) as IDictionary;
                if (dict == null) return;

                var kill = new List<object>();
                foreach (DictionaryEntry e in dict)
                {
                    if (stale(e.Key)) kill.Add(e.Key);
                }
                foreach (var k in kill) dict.Remove(k);
                if (kill.Count > 0)
                    Log.Message($"[LocalCSharpFixes] pruned {kill.Count} stale entr{(kill.Count == 1 ? "y" : "ies")} from {compTypeName}.{fieldName}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[LocalCSharpFixes] SaveData clean {compTypeName}.{fieldName} failed: {ex.Message}");
            }
        }
    }
}
