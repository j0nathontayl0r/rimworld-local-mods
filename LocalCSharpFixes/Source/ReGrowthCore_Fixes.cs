using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // ReGrowth: Core: GenStep_RockChunks_GrowLowRockFormationFrom_Patch.TrySpawnThingAt clears a
    // boulder's cells with
    //     List<Thing> list = cell.GetThingList(map);
    //     for (int i = list.Count - 1; i >= 0; i--) list[i].DeSpawn();
    // over the live thing grid list. Despawning one thing can remove more than one entry from that
    // list, so the next index runs past the end: ArgumentOutOfRangeException out of the postfix, which
    // aborts the whole GenStep_RockChunks and drops the map's remaining rock-chunk formations and
    // boulders (observed 2026-10-18, new colony map; ReGrowth ships a 1.6 DLL only). Iterate a copy of
    // the list instead, and skip a thing that an earlier DeSpawn already removed. Only the first
    // GetThingList call (the clearing loop) and its DeSpawn call change; the filth loop is untouched.
    // IL verified against the installed 1.6 DLL:
    //   IL_0108 call GridsUtility::GetThingList(IntVec3, Map)
    //   IL_0126 callvirt Entity::DeSpawn(DestroyMode)
    public static class ReGrowthCoreFixes
    {
        private static readonly MethodInfo getThingList =
            AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetThingList), new[] { typeof(IntVec3), typeof(Map) });
        private static readonly MethodInfo deSpawn = AccessTools.Method(typeof(Entity), nameof(Entity.DeSpawn));

        public static void Apply(Harmony h)
        {
            try
            {
                var t = AccessTools.TypeByName("ReGrowthCore.GenStep_RockChunks_GrowLowRockFormationFrom_Patch");
                var m = t == null ? null : AccessTools.Method(t, "TrySpawnThingAt");
                if (m == null)
                {
                    Log.Message("[LocalCSharpFixes] skip ReGrowth rock-chunk despawn fix (target not present)");
                    return;
                }
                h.Patch(m, transpiler: new HarmonyMethod(typeof(ReGrowthCoreFixes), nameof(TrySpawnThingAt_Transpiler)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] ReGrowth rock-chunk despawn fix failed: {e.Message}");
            }
        }

        public static IEnumerable<CodeInstruction> TrySpawnThingAt_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int listIdx = codes.FindIndex(c => c.Calls(getThingList));
            int despawnIdx = listIdx < 0 ? -1 : codes.FindIndex(listIdx, c => c.Calls(deSpawn));
            if (listIdx < 0 || despawnIdx < 0)
            {
                Log.Warning("[LocalCSharpFixes] ReGrowth rock-chunk despawn fix: IL changed, left unpatched.");
                return codes;
            }
            // Keep each instruction object so labels and blocks stay attached.
            codes[listIdx].opcode = System.Reflection.Emit.OpCodes.Call;
            codes[listIdx].operand = AccessTools.Method(typeof(ReGrowthCoreFixes), nameof(CopyThingList));
            codes[despawnIdx].opcode = System.Reflection.Emit.OpCodes.Call;
            codes[despawnIdx].operand = AccessTools.Method(typeof(ReGrowthCoreFixes), nameof(DeSpawnIfSpawned));
            return codes;
        }

        public static List<Thing> CopyThingList(IntVec3 cell, Map map) => new List<Thing>(cell.GetThingList(map));

        public static void DeSpawnIfSpawned(Entity entity, DestroyMode mode)
        {
            if (entity is Thing thing && !thing.Spawned)
                return;
            entity.DeSpawn(mode);
        }
    }
}
