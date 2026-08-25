using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // ISEKAI RPG LEVELING's Patch_EggLayXP prefixes+postfixes CompEggLayer.CompTick -- every
    // egg-laying animal, every tick -- and reads the private eggProgress field through
    // string-based Traverse reflection on BOTH ends. With a 92-bird flock at max speed that's
    // ~593 calls/frame and the single biggest frame cost in DPA (2026-08-26: ~7.5ms avg/frame,
    // 106ms max). The patch's job is just "grant 5 XP when an egg is laid" (progress crossed
    // from >=1 back below 1).
    //
    // Replacement: unpatch their pair and install the same detection with a compiled FieldRef
    // read (nanoseconds vs microseconds). The XP grant itself happens once per egg laid, so its
    // reflection Invoke is irrelevant. Semantics preserved exactly, including the
    // player-faction-only and not-dead checks.
    // Signatures verified against the installed DLLs:
    //   static void IsekaiLeveling.Patches.Patch_EggLayXP.Prefix(CompEggLayer __instance, out float __state)
    //   static void IsekaiLeveling.Patches.Patch_EggLayXP.Postfix(CompEggLayer __instance, float __state)
    //   void IsekaiLeveling.MobRanking.MobRankComponent.GainXP(int amount, string source = null)
    //   RimWorld.CompEggLayer.eggProgress   (private float)
    public static class IsekaiLevelingFixes
    {
        private static AccessTools.FieldRef<CompEggLayer, float> eggProgress;
        private static Type mobRankType;
        private static MethodInfo gainXP;

        public static void Apply(Harmony h)
        {
            var patchType = AccessTools.TypeByName("IsekaiLeveling.Patches.Patch_EggLayXP");
            if (patchType == null)
            {
                Log.Message("[LocalCSharpFixes] skip Isekai egg-XP fix (mod not present)");
                return;
            }
            mobRankType = AccessTools.TypeByName("IsekaiLeveling.MobRanking.MobRankComponent");
            gainXP = mobRankType == null ? null : AccessTools.Method(mobRankType, "GainXP");
            var original = AccessTools.Method(typeof(CompEggLayer), "CompTick");
            var theirPrefix = AccessTools.Method(patchType, "Prefix");
            var theirPostfix = AccessTools.Method(patchType, "Postfix");
            try { eggProgress = AccessTools.FieldRefAccess<CompEggLayer, float>("eggProgress"); }
            catch { eggProgress = null; }
            if (gainXP == null || original == null || theirPrefix == null || theirPostfix == null || eggProgress == null)
            {
                Log.Warning("[LocalCSharpFixes] skip Isekai egg-XP fix (member not found; mod layout changed?)");
                return;
            }
            h.Unpatch(original, theirPrefix);
            h.Unpatch(original, theirPostfix);
            h.Patch(original,
                    prefix: new HarmonyMethod(typeof(IsekaiLevelingFixes), nameof(TrackEggProgress)),
                    postfix: new HarmonyMethod(typeof(IsekaiLevelingFixes), nameof(GrantEggXP)));
        }

        public static void TrackEggProgress(CompEggLayer __instance, out float __state)
        {
            __state = eggProgress(__instance);
        }

        public static void GrantEggXP(CompEggLayer __instance, float __state)
        {
            try
            {
                if (__state < 1f || eggProgress(__instance) >= 1f)
                    return; // no egg laid this tick
                if (!(__instance.parent is Pawn pawn) || pawn.Dead || pawn.Faction == null || !pawn.Faction.IsPlayer)
                    return;
                var comps = pawn.AllComps;
                for (var i = 0; i < comps.Count; i++)
                {
                    if (mobRankType.IsInstanceOfType(comps[i]))
                    {
                        gainXP.Invoke(comps[i], new object[] { 5, "EggLaying" });
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Isekai GrantEggXP failed: {e.Message}");
            }
        }
    }
}
