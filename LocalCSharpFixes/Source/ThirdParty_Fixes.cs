using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Patch bodies for third-party mod methods. We can't reference those mods' assemblies at compile
    // time, so these are registered manually by name in LocalCSharpFixesMod (which logs + skips if a
    // target is absent). Signatures verified against each mod's DLL via MetadataLoadContext:
    //   static Thing VanillaSocialInteractionsExpanded.GatheringWorker_MealTogether.GetChairFor(Pawn pawn, Thing food, Thing firstChair)
    //   static bool  VFEPirates.WarcasketUtility.IsWearingWarcasket(Pawn pawn)
    //   void         SmartFarming.MapComponent_SmartFarming.FinalizeInit()
    //   void         ReinforcedMechanoids.TrooperStorage.PreApplyDamage(ref DamageInfo, ref bool)
    //   bool         PickUpAndHaul.JobDriver_HaulToInventory.TryMakePreToilReservations(bool errorOnFailed)
    internal static class ThirdPartyPatches
    {
        // VSIE: GetChairFor calls ClosestThingReachable(food.Position, ...) without checking food.Spawned.
        // Food in a pawn's inventory isn't spawned -> food.Position == IntVec3.Invalid (-1000,-1000,-1000)
        // -> out-of-bounds log spam every meal-together tick. Return null immediately when food isn't spawned.
        public static bool VSIE_GetChairFor_Prefix(Thing food, ref Thing __result)
        {
            if (food != null && !food.Spawned) { __result = null; return false; }
            return true;
        }

        // VFEPirates: IsWearingWarcasket(pawn) NREs when pawn or pawn.apparel is null (non-humanlike pawns
        // or mid-animation draw parms from Animated Melee's duel bow). Return false for those cases.
        public static bool VFEP_IsWearingWarcasket_Prefix(Pawn pawn, ref bool __result)
        {
            if (pawn == null || pawn.apparel == null) { __result = false; return false; }
            return true;
        }

        // Smart Farming: FinalizeInit() calls WorldGrid.DistanceFromEquatorNormalized(map.Tile) without
        // guarding tile validity (pocket maps have no world tile). Skip FinalizeInit for tile-less maps.
        // 1.6: Map.Tile is a PlanetTile struct exposing .Valid.
        public static bool SmartFarming_FinalizeInit_Prefix(MapComponent __instance)
        {
            var map = __instance != null ? __instance.map : null;
            if (map != null && !map.Tile.Valid) return false;
            return true;
        }

        // Reinforced Mechanoids 2: TrooperStorage.PreApplyDamage -> ReleaseTroopers ->
        // CompPawnProducer.CreateOrAddToAssaultLord NREs in edge cases (y=0 cell, null map/faction on
        // release) when an explosion hits the building. Suppress the NRE; troopers just fail to emerge
        // in that broken context.
        public static Exception RM_PreApplyDamage_Finalizer(Exception __exception) => null;

        // Pick Up And Haul: JobDriver_HaulToInventory.TryMakePreToilReservations(bool) calls Reserve()
        // with no CanReserve pre-check, spamming "Could not reserve" when a colonist already holds a
        // HaulToContainer reservation on the same item. Pre-check CanReserve(B) and silently fail the job
        // (same outcome, no log). Uses object + Traverse so we don't couple to PUAH's field layout;
        // GetActor() is the public accessor for the driver's pawn.
        public static bool PUAH_TryMakePreToilReservations_Prefix(object __instance, ref bool __result)
        {
            try
            {
                var pawn = Traverse.Create(__instance).Method("GetActor").GetValue<Pawn>();
                var job = Traverse.Create(__instance).Field("job").GetValue<Job>();
                if (pawn != null && job != null)
                {
                    var tgt = job.GetTarget(TargetIndex.B);
                    if (tgt.IsValid && !pawn.CanReserve(tgt)) { __result = false; return false; }
                }
            }
            catch { }
            return true;
        }
    }
}
