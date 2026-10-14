using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // Instruments for Extinction (ATMnou.exomech): Patch_MiracleBox_CellTemperature prefixes
    // GenTemperature.GetTemperatureForCell, and its ShouldFreeze(map, c) walks EVERY artificial
    // building on the map with an `is Building_MiracleBox` check on EVERY call. That method is one of
    // the hottest in the game (water freezing alone asks for temperature cell by cell each tick), so
    // the cost is cells-queried x buildings. Profiled 2026-10-15 on 1.7: ShouldFreeze was 52% of the
    // main thread, 25% of it in the type checks alone, and the main thread was what capped both TPS
    // and FPS.
    // Same answer, cached: once per tick per map, collect the cells covered by Miracle Boxes whose
    // freezer is active (found through ThingsOfDef on the box defs, not a scan of all buildings), and
    // answer each query with one set lookup. The snapshot is replaced whole, never mutated, so the
    // parallel pawn-render jobs that also read cell temperatures only ever see a finished set.
    // Freezer toggles take effect from the next tick instead of the same tick, which nothing can see.
    // Members verified against ExoMech.dll (workshop 3789316911, 2026-10-15):
    //   static bool ExoMech.Patch_MiracleBox_CellTemperature.ShouldFreeze(Map map, IntVec3 c)
    //   class ExoMech.Building_MiracleBox : Building
    //   class ExoMech.CompMiracleBoxFreezer : ThingComp { public bool FreezingActive { get; } }
    public static class ExoMechFixes
    {
        private sealed class Snapshot
        {
            public readonly int Tick;
            public readonly HashSet<IntVec3> Cells;
            public Snapshot(int tick, HashSet<IntVec3> cells) { Tick = tick; Cells = cells; }
        }

        private sealed class Holder { public volatile Snapshot Current; }

        private static readonly ConditionalWeakTable<Map, Holder> cache = new ConditionalWeakTable<Map, Holder>();
        private static Type boxType;
        private static Type freezerType;
        private static MethodInfo freezingActive;
        private static List<ThingDef> boxDefs;

        public static void Apply(Harmony h)
        {
            try
            {
                var target = AccessTools.Method("ExoMech.Patch_MiracleBox_CellTemperature:ShouldFreeze");
                boxType = AccessTools.TypeByName("ExoMech.Building_MiracleBox");
                freezerType = AccessTools.TypeByName("ExoMech.CompMiracleBoxFreezer");
                freezingActive = freezerType == null ? null : AccessTools.PropertyGetter(freezerType, "FreezingActive");
                if (target == null || boxType == null || freezingActive == null)
                {
                    Log.Message("[LocalCSharpFixes] skip ExoMech Miracle Box temperature cache (mod not present or layout changed)");
                    return;
                }
                h.Patch(target, prefix: new HarmonyMethod(typeof(ExoMechFixes), nameof(ShouldFreeze_Prefix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching ExoMech ShouldFreeze: {e.Message}");
            }
        }

        public static bool ShouldFreeze_Prefix(Map map, IntVec3 c, ref bool __result)
        {
            var tickManager = Find.TickManager;
            if (map == null || tickManager == null)
            {
                __result = false;
                return false;
            }
            int tick = tickManager.TicksGame;
            var holder = cache.GetOrCreateValue(map);
            var snap = holder.Current;
            if (snap == null || snap.Tick != tick)
            {
                snap = new Snapshot(tick, CollectFrozenCells(map));
                holder.Current = snap;
            }
            __result = snap.Cells.Count != 0 && snap.Cells.Contains(c);
            return false;
        }

        private static HashSet<IntVec3> CollectFrozenCells(Map map)
        {
            var cells = new HashSet<IntVec3>();
            boxDefs ??= DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.thingClass != null && boxType.IsAssignableFrom(d.thingClass)).ToList();
            foreach (var def in boxDefs)
            {
                foreach (var thing in map.listerThings.ThingsOfDef(def))
                {
                    if (!(thing is ThingWithComps box)) continue;
                    var comp = box.AllComps.FirstOrDefault(cp => freezerType.IsInstanceOfType(cp));
                    if (comp == null || !(bool)freezingActive.Invoke(comp, null)) continue;
                    foreach (var cell in box.OccupiedRect())
                        cells.Add(cell);
                }
            }
            return cells;
        }
    }
}
