using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LocalCSharpFixes
{
    // Shared save-time scrub for a recurring mod bug class: mod trackers holding Pawns or world
    // objects that the save can no longer reach. The stale reference scribes as a bare load ID and
    // the next load logs either "Null key while loading dictionary of <K> and <V>" (dictionary keys)
    // or "Could not resolve reference to object with loadID Thing_X ... curParent=<tracker>"
    // (everything else). Harmless -- the entry is dropped -- but red, recurring, and it grows as
    // more pawns are orphaned. Known offenders, each observed here 2026-08-25/29:
    //   VanillaTraitsExpanded.TraitsManager      - madSurgeonsWithLastHarvestedTick,
    //                                              wanderLustersWithLastMapExitedTick, snobs,
    //                                              cowards, bigBoned, ...
    //   SimpleLeadership.FactionLeadershipData   - settlementLeaders (Dictionary<Settlement, Pawn>:
    //                                              destroyed settlement keys AND discarded pawn values)
    //   CombatExtended.LoadoutManager            - assignmentLoadouts (Dictionary<Pawn, Loadout>)
    //   AlteredCarbon.AlteredCarbonManager       - pawnsWithStacks / emptySleeves / deadPawns
    //                                              (HashSet<Pawn>), and StackGroupData.originalPawn /
    //                                              copiedPawns / deadPawns reached through
    //                                              stacksRelationships (54 of the 132 unresolved
    //                                              references on one load, 2026-08-29)
    //
    // Structures walked (reflection over instance fields, so a mod update that adds or renames a
    // tracker stays covered): dictionary keys AND values, lists, sets and any other collection
    // exposing a one-argument Remove, scalar Pawn/WorldObject fields, and -- one level deeper --
    // plain data objects belonging to the same mod assembly. Things, maps, factions, world objects
    // and defs are never recursed into: that would walk the whole game graph, and their own contents
    // are the game's business.
    //
    // Staleness: a Pawn is stale when it is Discarded, or when the save simply can't reach it --
    // not spawned, no ParentHolder, not in WorldPawns. The reachability half matters because the
    // observed leaks are pawns that are NOT destroyed (Altered Carbon already strips Destroyed ones
    // itself, and Discarded implies Destroyed) but are orphaned: alive objects nothing deep-saves.
    // The one false-positive shape is a pawn deep-saved through a mod's own scalar field, which is
    // reachable for the scribe but looks orphaned here (in Altered Carbon:
    // Building_SleeveGestator.sleeveForGrowing, held only between StartGrowth and StartGrowing,
    // SleevePreset.sleeve, NeuralStack.casterPawn). Worst case we drop a tracker entry for a pawn
    // that would have loaded -- never a pawn -- and every drop is logged once so it is visible.
    // World objects leave the deep-saved world list the moment they're destroyed, so for them
    // Destroyed remains the criterion.
    //
    // Signatures verified against the installed 1.6 DLLs:
    //   VanillaTraitsExpanded.TraitsManager.ExposeData()      (GameComponent override; public Dictionary/List fields)
    //   SimpleLeadership.FactionLeadershipData.ExposeData()   (plain IExposable method)
    //   CombatExtended.LoadoutManager.ExposeData()            (GameComponent override)
    //   AlteredCarbon.AlteredCarbonManager.ExposeData()       (GameComponent override; HashSet<Pawn> fields,
    //                                                          Dictionary<int, StackGroupData> stacksRelationships)
    //   Verse.Thing.Discarded (mapIndexOrState == -3) / RimWorld.Planet.WorldObject.Destroyed
    public static class StaleSaveRefFixes
    {
        private const int MaxDepth = 4;

        private static readonly (string type, string label)[] Targets =
        {
            ("VanillaTraitsExpanded.TraitsManager", "Vanilla Traits Expanded tracker"),
            ("SimpleLeadership.FactionLeadershipData", "Simple Leadership settlement-leaders"),
            ("CombatExtended.LoadoutManager", "Combat Extended loadout-assignment"),
            ("AlteredCarbon.AlteredCarbonManager", "Altered Carbon stack/sleeve tracker"),
        };

        private static readonly HashSet<string> reported = new HashSet<string>();

        public static void Apply(Harmony h)
        {
            foreach (var (typeName, label) in Targets)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null)
                {
                    Log.Message($"[LocalCSharpFixes] skip {label} fix (mod not present)");
                    continue;
                }
                var m = AccessTools.Method(t, "ExposeData");
                if (m == null)
                {
                    Log.Warning($"[LocalCSharpFixes] skip {label} fix (ExposeData not found; mod layout changed?)");
                    continue;
                }
                h.Patch(m, prefix: new HarmonyMethod(typeof(StaleSaveRefFixes), nameof(DropStaleRefsBeforeSave)));
            }
        }

        public static void DropStaleRefsBeforeSave(object __instance)
        {
            try
            {
                if (Scribe.mode != LoadSaveMode.Saving || __instance == null) return;
                Scrub(__instance, __instance.GetType().Assembly, __instance.GetType().Name, 0, new HashSet<object>(ReferenceComparer.Instance));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] stale-reference cleanup failed for {__instance?.GetType().Name}: {e.Message}");
            }
        }

        private static void Scrub(object holder, Assembly modAssembly, string root, int depth, HashSet<object> seen)
        {
            if (holder == null || depth > MaxDepth || !seen.Add(holder)) return;
            foreach (var field in holder.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try { value = field.GetValue(holder); }
                catch { continue; }
                if (value == null) continue;
                if (value is Pawn || value is WorldObject)
                {
                    if (!IsStale(value)) continue;
                    Report(root, value);
                    try { field.SetValue(holder, null); }
                    catch { }
                    continue;
                }
                ScrubValue(value, modAssembly, root, depth, seen);
            }
        }

        private static void ScrubValue(object value, Assembly modAssembly, string root, int depth, HashSet<object> seen)
        {
            switch (value)
            {
                case string _:
                case Array _:
                    return;
                case IDictionary dict:
                    ScrubDictionary(dict, modAssembly, root, depth, seen);
                    return;
                case IList list:
                    ScrubList(list, modAssembly, root, depth, seen);
                    return;
                case IEnumerable set when RemoveMethodOf(set) != null:
                    ScrubSet(set, modAssembly, root, depth, seen);
                    return;
                default:
                    if (CanRecurse(value, modAssembly))
                        Scrub(value, modAssembly, root, depth + 1, seen);
                    return;
            }
        }

        private static void ScrubDictionary(IDictionary dict, Assembly modAssembly, string root, int depth, HashSet<object> seen)
        {
            var stale = new List<object>();
            var nested = new List<object>();
            foreach (DictionaryEntry entry in dict)
            {
                if (IsStale(entry.Key) || IsStale(entry.Value))
                {
                    Report(root, IsStale(entry.Key) ? entry.Key : entry.Value);
                    stale.Add(entry.Key);
                }
                else if (CanRecurse(entry.Value, modAssembly))
                    nested.Add(entry.Value);
            }
            foreach (var key in stale)
                dict.Remove(key);
            foreach (var value in nested)
                Scrub(value, modAssembly, root, depth + 1, seen);
        }

        private static void ScrubList(IList list, Assembly modAssembly, string root, int depth, HashSet<object> seen)
        {
            var nested = new List<object>();
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];
                if (IsStale(item))
                {
                    Report(root, item);
                    list.RemoveAt(i);
                }
                else if (CanRecurse(item, modAssembly))
                    nested.Add(item);
            }
            foreach (var item in nested)
                Scrub(item, modAssembly, root, depth + 1, seen);
        }

        // HashSet<T> and friends: no non-generic mutation interface, so the one-argument Remove is
        // found by reflection and invoked after enumeration finishes.
        private static void ScrubSet(IEnumerable set, Assembly modAssembly, string root, int depth, HashSet<object> seen)
        {
            var stale = new List<object>();
            var nested = new List<object>();
            foreach (var item in set)
            {
                if (IsStale(item))
                    stale.Add(item);
                else if (CanRecurse(item, modAssembly))
                    nested.Add(item);
            }
            if (stale.Count > 0)
            {
                var remove = RemoveMethodOf(set);
                foreach (var item in stale)
                {
                    Report(root, item);
                    try { remove.Invoke(set, new[] { item }); }
                    catch { }
                }
            }
            foreach (var item in nested)
                Scrub(item, modAssembly, root, depth + 1, seen);
        }

        private static MethodInfo RemoveMethodOf(object collection)
        {
            foreach (var m in collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (m.Name == "Remove" && m.GetParameters().Length == 1 && m.ReturnType != typeof(void))
                    return m;
            return null;
        }

        // Only the mod's own plain data objects (e.g. AlteredCarbon.StackGroupData). Recursing into
        // Things, maps, factions, world objects or defs would walk the whole game graph.
        private static bool CanRecurse(object value, Assembly modAssembly)
        {
            if (value == null || value.GetType().Assembly != modAssembly) return false;
            return !(value is Thing) && !(value is Map) && !(value is Faction) && !(value is WorldObject) && !(value is Def);
        }

        private static bool IsStale(object o)
        {
            switch (o)
            {
                case Pawn p: return IsStalePawn(p);
                case WorldObject wo: return wo.Destroyed;
                default: return false;
            }
        }

        // Discarded pawns always scribe as a dangling reference. Beyond those, a pawn the save can't
        // reach at all -- not on a map, held by nothing, absent from WorldPawns -- writes an ID that
        // resolves to nothing on the next load.
        private static bool IsStalePawn(Pawn p)
        {
            if (p == null) return false;
            if (p.Discarded) return true;
            if (p.Spawned || p.ParentHolder != null) return false;
            var worldPawns = Find.WorldPawns;
            if (worldPawns == null) return false; // can't tell -> keep
            return !worldPawns.Contains(p);
        }

        private static void Report(string root, object stale)
        {
            var key = (stale is Pawn p ? p.ThingID : stale?.GetHashCode().ToString()) + "@" + root;
            if (reported.Add(key))
                Log.Message($"[LocalCSharpFixes] dropping stale reference to {stale} from {root} before saving.");
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
