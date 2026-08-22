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
    //   static void  Defaults.WorkPriorities.Patch_Pawn.Postfix(Pawn __instance)
    //   void         VEF.AnimalBehaviours.JobDriver_IngestWeird+<>c__DisplayClass20_0.<FinalizeIngestAnything>b__0()
    //   static void  VAC.VAC_Settings.PlayEventNotificationWithVolumeByDef(SoundDef, string)
    //   static void  Xenomorphtype.XMTLifeStagePatches+Patch_LifeStageWorker_Notify_LifeStageStarted.Postfix(Pawn, LifeStageDef)
    //   static ?     Xenomorphtype.XMTUtility.WitnessLarva(IntVec3 positionHeld, Map mapHeld, float, float, float, KnowledgeProfileDef)
    //   static float Vehicles.Patch_CaravanHandling.PawnMassUsageInVehicle(float massUsage, Pawn pawn)
    //   static ?     Xenomorphtype.XMTUtility.TrySpawnPawnFromTarget(Pawn, Thing)
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

        // Defaults (1trickPwnyta): WorkPriorities.Patch_Pawn.Postfix (their postfix on
        // Pawn.Notify_DisabledWorkTypesChanged) calls First() on a sequence with no matching element
        // whenever a pawn without work settings — any animal — dies, throwing
        // InvalidOperationException mid Pawn.Kill ("Error while killing X during phase 6").
        // Suppress exactly that exception; anything else still surfaces.
        public static Exception Defaults_WorkPrioPostfix_Finalizer(Exception __exception)
            => __exception is InvalidOperationException ? null : __exception;

        // VEF AnimalBehaviours: JobDriver_IngestWeird's FinalizeIngestAnything toil initAction NREs
        // when the weird-food target (e.g. filth) is gone by finalize time. Suppress the NRE; the
        // job simply ends and the pawn moves on. NB the target is a compiler-generated lambda, so
        // the name is compile-specific — the log+skip registration covers VEF rebuilds.
        public static Exception VEF_IngestWeird_Finalizer(Exception __exception)
            => __exception is NullReferenceException ? null : __exception;

        // VAC: its GenSpawn.Spawn prefix fires event notification sounds during Map.FinalizeLoading,
        // which runs on the loading thread. Audio resource loading is main-thread-only, so every such
        // call logs "Tried to get a resource from a different thread" + a grain resolve failure
        // (2 errors per save load). Skip the sound when off the main thread — audio couldn't have
        // played from there anyway; in-play event sounds always fire from the main thread and are
        // unaffected. (VAC also lacks the TransportCrash1 clip entirely; if a grain error ever shows
        // up in-play on the main thread, add that exact string to the VAC log filter as well.)
        public static bool VAC_PlayEventNotification_Prefix()
            => UnityData.IsInMainThread;

        // Xenomorphtype (XMT): its postfix on LifeStageWorker.Notify_LifeStageStarted calls
        // Find.FactionManager as its first action. That hook also fires during save LOADING —
        // Pawn_NeedsTracker's constructor recalculates life stages while quest pawns are being
        // scribed in Game.ExposeSmallComponents, before the World exists — so Find.FactionManager
        // NREs, logging "Error while determining if <pawn> should have Need Chemical_Alcohol" once
        // per affected quest pawn on every save load. Skip XMT's postfix while the world isn't
        // loaded; its logic couldn't act in that state anyway, and needs are re-evaluated once the
        // world is up. (Patching a patch is fine — Harmony detours the method's native entry, same
        // as the Defaults fix above.)
        public static bool XMT_LifeStageStarted_Prefix()
            => Current.Game?.World?.factionManager != null;

        // XMT again: XMTUtility.WitnessLarva(positionHeld, mapHeld, ...) passes mapHeld straight
        // into GetFirstPawn/GetThingList without a null check. A pawn held in an unspawned context
        // (seen: inside a DropPodIncoming when its larval-attachment hediff is removed -
        // LarvaSqueeze on CompPostPostRemoved) has no map, so every such removal throws
        // "Exception ticking DropPodIncoming..." and aborts the pod's tick. No map = nobody can
        // witness; skip the call and leave the result at its default. Prefix binds mapHeld by
        // name, which the runtime stack trace confirms against XMT's actual parameter list.
        public static bool XMT_WitnessLarva_Prefix(Map mapHeld)
            => mapHeld != null;

        // Vehicle Framework: PawnMassUsageInVehicle(massUsage, pawn) - a helper its transpiler
        // splices into CollectionsMassCalculator.MassUsage - starts with pawn.InVehicle(), NREing
        // when the form-caravan dialog's transferables hold a null pawn (seen: dialog open across
        // an encounter-map transition; "Exception filling window for Dialog_FormCaravan"). A null
        // pawn is in no vehicle: pass the mass through unchanged. Inlining note: safe to detour -
        // Mono doesn't inline methods containing calls, and this one calls InVehicle().
        public static bool VF_PawnMassUsage_Prefix(float massUsage, Pawn pawn, ref float __result)
        {
            if (pawn == null) { __result = massUsage; return false; }
            return true;
        }

        // XMT, third null-map path: LarvaRelease -> XMTUtility.TrySpawnPawnFromTarget hands
        // GenSpawn.Spawn a null map when the larva host is held in a container (same drop-pod
        // scenario as the WitnessLarva guard, different exit). Vehicle Framework's spawn hook then
        // NREs ("Pawn larva could not be readjusted...") and vanilla logs "Tried to spawn ... in a
        // null map" - two red errors, larva doesn't spawn either way. Skip unless the pawn or the
        // target is actually on a map. __0/__1 index binding keeps this overload-proof.
        public static bool XMT_TrySpawnPawn_Prefix(Pawn __0, Thing __1)
            => (__0 != null && __0.Spawned) || (__1 != null && __1.Spawned);
    }
}
