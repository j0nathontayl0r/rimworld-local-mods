using System;
using System.Collections.Generic;
using System.Linq;
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
    //   static ?     Xenomorphtype.XMTUtility.Witness*(IntVec3 positionHeld, Map mapHeld, float, float, float, KnowledgeProfileDef)  [8-method family]
    //   static float Vehicles.Patch_CaravanHandling.PawnMassUsageInVehicle(float massUsage, Pawn pawn)
    //   static ?     Xenomorphtype.XMTUtility.TrySpawnPawnFromTarget(Pawn, Thing)
    //   bool         CSL_Blackboard.ITab_CSL_BlackboardSchedule.IsTeachingDisabledForPawn(Pawn) and (Pawn, string&, bool)
    //   static bool  CSL_SchoolTable.ITab_CSL_SchoolTableSchedule.IsLearningDisabledForPawn(Pawn) and (Pawn, string&, bool)
    //   bool         CombatExtended.JobDriver_Hunt.TryMakePreToilReservations(bool errorOnFailed)
    //   void         ColonyManagerRedux.Building_AIManager.Tick()   (protected override)
    //   void         CombatExtended.CompMechAmmo.TryMakeAmmoJob(bool forced = false)
    //   static IEnumerable<PawnKindDef> ColonyManagerRedux.Managers.Utilities_Hunting.GetMapPawnKindDefs(Map? map, bool animalsOnly = true)
    //   Job          CombatExtended.AI.CompTend.TryGiveTacticalJob()   (override; TendSelf job targeting SelPawn)
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

        // Combat Extended: CompTend.TryGiveTacticalJob hands a non-player pawn a TendSelf job aimed at
        // itself without checking reservations, and CompTacticalManager.TryGiveTacticalJobs starts it
        // with preToilReservationsCanFail: false, so vanilla computes errorOnFailed = true. When one of
        // our doctors has already reserved that pawn (DoctorTendEmergency on a downed enemy or a
        // prisoner), JobDriver_TendPatient's reservation fails and every attempt costs two log lines --
        // the red "Could not reserve" plus vanilla's "TryMakePreToilReservations() returned false for a
        // non-queued job" -- repeating on CE's 600-tick cooldown for as long as the tend lasts.
        // Drop the job when the pawn can't reserve itself; the doctor's tend is the better outcome
        // anyway. CE sets lastTendJobAt before returning, so its own cooldown still throttles retries.
        public static void CE_TendSelfJob_Postfix(ref Job __result)
        {
            if (__result?.def == null || __result.def.defName != "TendSelf") return;
            var patient = __result.targetA.Thing as Pawn;
            if (patient == null || !patient.Spawned || patient.Map == null) return;
            if (!patient.CanReserve(patient))
                __result = null;
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

        // XMT: the whole XMTUtility.Witness* family (WitnessLarva, WitnessHorror, WitnessAcid,
        // WitnessOvomorph + *Horror variants, WitnessPsychicHorror) shares one signature and one
        // bug: mapHeld goes straight into GetFirstPawn/GetThingList with no null check, so any
        // trigger on an off-map pawn throws. Seen live twice - a larval attachment removed inside
        // a DropPodIncoming ("Exception ticking DropPodIncoming...") and a building-morphing
        // hediff tended on a WORLD pawn ("Exception tending to a world pawn..."). No map means
        // nobody can witness; skip. Registered across the family by the mapHeld parameter.
        public static bool XMT_Witness_Prefix(Map mapHeld)
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

        // Children, school and learning (Dylan.CSL): the blackboard schedule tab's
        // IsTeachingDisabledForPawn and the school table tab's IsLearningDisabledForPawn (same
        // shape, same bug - both registered onto this prefix) call Pawn_WorkSettings.WorkIsActive
        // without checking the settings are initialized. The Children mod's area-restriction hook
        // routes EVERY pawn's job start through them (Area_School cache), so each visitor/
        // trade-ship pawn spams "<name> did not have work settings initialized" (seen: Kia, Poopy,
        // Darklight; Máté via the school table). A pawn with no work settings can't teach or
        // learn: report disabled and skip. Binds by index + __args so one prefix covers both
        // overloads ((Pawn) and (Pawn, string&, bool)); the priorities field null-check mirrors
        // vanilla's ConfirmInitializedDebug condition.
        private static readonly System.Reflection.FieldInfo workPriorities =
            AccessTools.Field(typeof(Pawn_WorkSettings), "priorities");

        public static bool CSL_TeachingDisabled_Prefix(Pawn __0, object[] __args, ref bool __result)
        {
            var ws = __0?.workSettings;
            if (ws != null && (workPriorities == null || workPriorities.GetValue(ws) != null)) return true;
            if (__args.Length > 1) __args[1] = "";
            __result = true;
            return false;
        }

        // Combat Extended: JobDriver_Hunt.TryMakePreToilReservations hardcodes errorOnFailed: true
        // in its Reserve call instead of forwarding the parameter (vanilla forwards it). StartJob
        // passes errorOnFailed = !fromQueue, so a queued hunt losing the reservation race to
        // another hunter -- a perfectly normal outcome vanilla fails silently -- logs a red
        // "Could not reserve ... for job Hunt" (observed 2026-08-25: queued hunter Pusca vs active
        // hunter Shen on one designated animal). Replace the one-line body with the flag forwarded;
        // behaviour is otherwise identical.
        public static bool CE_HuntReservation_Prefix(JobDriver __instance, bool errorOnFailed, ref bool __result)
        {
            __result = __instance.pawn.Reserve(__instance.job.GetTarget(TargetIndex.A), __instance.job, 1, -1, null, errorOnFailed);
            return false;
        }

        // Colony Manager Redux: Building_AIManager.Tick assumes it's spawned -- it flips Powered
        // from the power comp, whose setter drives CompGlower.UpdateLit(Map), and later calls
        // Manager.For(Map). While the building is minified and carried (a pawn hauling or a raider
        // stealing it), containers still tick it and Map is null, so every tick NREs ("Exception
        // ticking <carrier> ... at Verse.CompGlower.UpdateLit"; observed 2026-08-26 via Daag
        // carrying one). An unspawned manager has nothing to manage: skip the tick entirely.
        public static bool CMR_AIManagerTick_Prefix(Thing __instance) => __instance.Spawned;

        // Combat Extended: CompMechAmmo.CompTickInterval -> TryMakeAmmoJob gates on IsWorkable(),
        // which checks faction and equipped weapon but never Spawned. A player mech ticking as a
        // WORLD pawn (in a caravan or otherwise off-map) then calls MTAAmmoUtility.FindBestAmmo ->
        // GenClosest.ClosestThingReachable with pawn.Map == null -> NRE ("Exception ticking world
        // pawn IM_Mech_Bombardier..."; observed 2026-08-26; vanilla then suppresses that pawn's
        // errors). An unspawned mech can't run a take-ammo job anyway: skip. Also inert for the
        // forced gizmo path, which only exists for spawned mechs.
        public static bool CE_MechAmmoJob_Prefix(ThingComp __instance) => __instance.parent?.Spawned ?? false;

        // Colony Manager Redux: Utilities_Hunting.GetMapPawnKindDefs guards a null map but then
        // reads map.Biome, which indexes the world grid by the map's tile -- and throws
        // ArgumentOutOfRangeException for a map with no valid tile. ReGrowth's SavedCamp
        // constructs such a transient map during every game save, so each save logged "Could not
        // create ManagerJob instance for CM_HuntingManager" (observed 2026-08-26). Same failure
        // class as the SmartFarming pocket-map guard above: no tile means no biome and no wild
        // animals -- return the empty set.
        public static bool CMR_MapPawnKinds_Prefix(Map map, ref IEnumerable<PawnKindDef> __result)
        {
            if (map == null || map.Tile.Valid)
                return true;
            __result = Enumerable.Empty<PawnKindDef>();
            return false;
        }
    }
}
