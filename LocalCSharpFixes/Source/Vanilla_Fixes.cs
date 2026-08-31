using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LocalCSharpFixes
{
    // Signatures verified against RimWorldLinux_Data/Managed/Assembly-CSharp.dll (1.6) via MetadataLoadContext:
    //   RimWorld.StorageSettings.CopyFrom(StorageSettings other)
    //   Verse.AI.ReservationManager.LogCouldNotReserveError(Pawn, Job, LocalTargetInfo, int, int, ReservationLayerDef)
    //   RimWorld.Pawn_FlightTracker.Notify_JobStarted(Job job)
    //   static RimWorld.ForbidUtility.SetForbidden(Thing t, bool value, bool warnOnFail)
    //   Verse.Thing.Destroy(DestroyMode mode)
    //   Verse.PathFinderMapData.Notify_Reservation(ReservationManager.Reservation)

    // Vanilla: when the World Pawn GC discards a pawn, CompAttachBase.PostDestroy tries to destroy its
    // attached AttachableThings (e.g. Fire). If the fire already went out and was destroyed, the stale
    // reference calls Destroy again -> "Tried to destroy already-destroyed thing Fire...". Skip the
    // redundant destroy for already-destroyed AttachableThings; leave all other cases alone.
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    internal static class Thing_Destroy_DoubleDestroyGuard
    {
        private static bool Prefix(Thing __instance)
        {
            if (__instance != null && __instance.Destroyed && __instance is AttachableThing)
                return false;
            return true;
        }
    }

    // Vanilla: Frame.CompleteConstruction calls StorageSettings.CopyFrom(frame.storageSettings). Buildings
    // that never initialised storageSettings (e.g. RefineryLoadingBay) pass null -> NRE, the job driver
    // crashes and a mechanoid can never complete the building. Skip the copy when other is null; the
    // building completes with default storage settings.
    [HarmonyPatch(typeof(StorageSettings), "CopyFrom")]
    internal static class StorageSettings_CopyFrom_NullGuard
    {
        private static bool Prefix(StorageSettings other)
        {
            return other != null; // false = skip original when there's nothing to copy
        }
    }

    // Vanilla (1.6): Pawn_FlightTracker.Notify_JobStarted (new in 1.6) NREs when animals loaded from a
    // pre-1.6 save start a job -- the tracker exists on every pawn but its state isn't initialised for
    // non-flying animals from old saves. Swallow the exception; no gameplay effect for animals that
    // can't fly.
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    internal static class PawnFlightTracker_NotifyJobStarted_Suppress
    {
        private static Exception Finalizer(Exception __exception)
        {
            return null; // swallow any NRE from the original
        }
    }

    // Vanilla: JobDriver_RecolorApparel dye reservation conflicts spam "Could not reserve" through
    // ReservationManager.LogCouldNotReserveError. CanReserve doesn't see physical-interaction reservers,
    // so a CanReserve pre-check upstream doesn't help -- suppress the log itself, but ONLY for
    // RecolorApparel jobs (covers both normal and physical-interaction reservation conflicts).
    [HarmonyPatch(typeof(ReservationManager), "LogCouldNotReserveError")]
    internal static class ReservationManager_LogCouldNotReserveError_SkipRecolor
    {
        private static bool Prefix(Job job)
        {
            if (job?.def != null && job.def.defName == "RecolorApparel")
                return false; // skip the log call
            return true;
        }
    }

    // Alpha Animals: the pig-dig job spawns AA_BlackTruffles forbidden (mimicking the mining-drop
    // pattern), forcing a manual allow before hauling. Patch the vanilla ForbidUtility.SetForbidden to
    // not forbid truffles. Side effect: truffles also can't be manually forbidden -- acceptable for a
    // fungible food item.
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.SetForbidden))]
    internal static class ForbidUtility_SetForbidden_AllowTruffles
    {
        private static bool Prefix(Thing t, bool value)
        {
            if (value && t?.def != null && t.def.defName == "AA_BlackTruffles")
                return false; // skip forbidding
            return true;
        }
    }

    // Vanilla (Biotech): Bill_ResurrectMech.CreateProducts dereferences
    // Gestator.ResurrectingMechCorpse unguarded. If the corpse leaves the gestator's container
    // mid-cycle (cause unpinned; observed 2026-08-26 on a large mech gestator), the forming cycle
    // still completes: BillTick sets state = Formed, Notify_FormingCompleted NREs, and the
    // gestator is left bricked -- a Formed bill with neither corpse nor mech inside that the
    // mechanitor can never collect. Two patches:
    //   - Notify_FormingCompleted prefix: corpse gone at completion -> reset the bill to
    //     Gathering (vanilla's own Reset) so it collects a new corpse, instead of throwing.
    //   - BillTick prefix: heal a bill already bricked in a save -- Formed with an empty
    //     container -> same reset. Healthy Formed bills hold the resurrected mech, so the check
    //     never touches them.
    // Signatures verified against the installed 1.6 DLL:
    //   void RimWorld.Building_MechGestator.Notify_FormingCompleted()   (activeBill.CreateProducts() first)
    //   Corpse RimWorld.Building_MechGestator.ResurrectingMechCorpse / Pawn GestatingMech   (container scans)
    //   void RimWorld.Bill_Autonomous.Reset()   (ingredients.Clear(); state = Gathering; Bill_Mech adds cycle/boundPawn reset)
    //   Building_MechGestator RimWorld.Bill_Mech.Gestator => (Building_MechGestator)billStack.billGiver
    [HarmonyPatch(typeof(Building_MechGestator), nameof(Building_MechGestator.Notify_FormingCompleted))]
    internal static class MechGestator_FormingCompleted_MissingCorpseGuard
    {
        private static bool Prefix(Building_MechGestator __instance)
        {
            if (!(__instance.ActiveBill is Bill_ResurrectMech bill) || __instance.ResurrectingMechCorpse != null)
                return true;
            Log.Warning($"[LocalCSharpFixes] {__instance.LabelCap} finished a resurrection cycle with no corpse inside; resetting the bill to gather a new one.");
            bill.Reset();
            return false;
        }
    }

    [HarmonyPatch(typeof(Bill_Mech), nameof(Bill_Mech.BillTick))]
    internal static class BillMech_BillTick_HealBrickedResurrection
    {
        private static void Prefix(Bill_Mech __instance)
        {
            if (__instance.State != FormingState.Formed || !(__instance is Bill_ResurrectMech))
                return;
            var gestator = __instance.Gestator;
            if (gestator == null || gestator.GestatingMech != null || gestator.ResurrectingMechCorpse != null)
                return;
            Log.Warning($"[LocalCSharpFixes] {gestator.LabelCap} had a formed resurrection bill with an empty container; resetting the bill to gather a new corpse.");
            __instance.Reset();
        }
    }

    // Vanilla: Pawn_EquipmentTracker.TryDropEquipment funnels into ThingOwner.TryDrop, which logs
    // "container tried to drop X which it didn't contain" and returns false when the weapon
    // already left the tracker. With many mods swapping equipment mid-job (this modlist has 9
    // patches on JobDriver_DropEquipment's drop toil alone), a drop job losing that race is
    // routine (observed 2026-08-26: AC_Gun_Autorevolver already gone when the toil fired).
    // Return the same false without reaching the error log. The invalid-cell error path and all
    // genuine drops are untouched.
    // Signatures verified against the installed 1.6 DLL:
    //   bool Verse.Pawn_EquipmentTracker.TryDropEquipment(ThingWithComps eq, out ThingWithComps resultingEq, IntVec3 pos, bool forbid = true)
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.TryDropEquipment))]
    internal static class PawnEquipmentTracker_TryDropEquipment_NotContainedGuard
    {
        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq, ref ThingWithComps resultingEq, ref bool __result)
        {
            if (eq != null && __instance.Contains(eq))
                return true;
            resultingEq = null;
            __result = false;
            return false;
        }
    }

    // Vanilla: JobGiver_AIDefendEscortee.GetDefendee casts duty.focus to Thing. A caravan animal
    // can transiently hold an escort-family duty whose focus is a bare cell (observed 2026-08-26:
    // Giddy-Up mount Doedicurus in a trade caravan, focus near the chill spot -- Giddy-Up's own
    // suppression prefix for these job givers only guards while the mount is reserved, and the
    // rider's dismount clears that). Result: two red errors per job search ("Casted
    // LocalTargetInfo to Thing but it had cell ..." + "JobGiver_AIDefendEscortee has null
    // defendee"), then vanilla returns null and the think tree moves on anyway. Make the decline
    // quiet: same null result, no logs. Only the Escortee subclass reads focus this way, so other
    // JobGiver_AIDefendPawn subclasses are untouched.
    // Signatures verified against the installed 1.6 DLL:
    //   protected override Job RimWorld.JobGiver_AIDefendPawn.TryGiveJob(Pawn pawn)
    //   protected override Pawn RimWorld.JobGiver_AIDefendEscortee.GetDefendee(Pawn pawn)   ((Thing)pawn.mindState.duty.focus)
    [HarmonyPatch(typeof(JobGiver_AIDefendPawn), "TryGiveJob")]
    internal static class JobGiverAIDefendPawn_TryGiveJob_CellFocusGuard
    {
        private static bool Prefix(JobGiver_AIDefendPawn __instance, Pawn pawn, ref Job __result)
        {
            if (__instance is JobGiver_AIDefendEscortee && !(pawn?.mindState?.duty?.focus.HasThing ?? false))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    // Vanilla (Odyssey): CompUniqueWeapon.PostExposeData scribes its ColorDef with
    // Scribe_Defs.Look(ref color, "color") -- the same node name CompColorable saves its RGBA Color
    // under. On a thing carrying both comps (seen 2026-08-25: Star Wars - The Force unique
    // lightsaber "The Lightsaber of Legend"), both comps read the FIRST <color> node on load and
    // CompColorable throws FormatException parsing the defName "UniqueWeapon_Purple" as a Color --
    // one red error per such weapon on every save load. Fall back in ParseHelper.ParseColor: a
    // colour literal always contains '(' ("RGBA(...)" or "(r,g,b)"), a bare ColorDef defName never
    // does, so resolve the string via DefDatabase and return that def's colour. Strings matching no
    // ColorDef fall through to the original (and its usual error). During def XML loading the
    // database is empty, so the lookup misses and def parsing is unaffected.
    // Signatures verified against the installed 1.6 DLL:
    //   static Color Verse.ParseHelper.ParseColor(string str)
    //   RimWorld.ColorDef.color   (public Color field)
    [HarmonyPatch(typeof(ParseHelper), nameof(ParseHelper.ParseColor))]
    internal static class ParseHelper_ParseColor_ColorDefFallback
    {
        private static bool Prefix(string str, ref Color __result)
        {
            if (string.IsNullOrEmpty(str) || str.IndexOf('(') >= 0)
                return true;
            var def = DefDatabase<ColorDef>.GetNamedSilentFail(str);
            if (def == null)
                return true;
            __result = def.color;
            return false; // skip the original; it could only throw
        }
    }

    // Vanilla (1.6): PathFinderMapData.Notify_Reservation NREs when a reservation is released for an
    // already-destroyed thing (no map to update), preceded by vanilla's own "Releasing destroyed
    // thing ..." warning. Trigger in this colony: MendAndRecycle finishing a mend bill on stackable
    // CE ordnance (smoke grenades), which merges/destroys the mended stack before the Release toil
    // runs. The pathfinder has nothing to update for a mapless thing; swallow the NRE so the job
    // ends cleanly instead of crashing the driver.
    [HarmonyPatch(typeof(PathFinderMapData), "Notify_Reservation")]
    internal static class PathFinderMapData_NotifyReservation_Suppress
    {
        private static Exception Finalizer(Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }
    }

    // Vanilla: Bill.GetUniqueLoadID() is "Bill_" + recipe.defName + "_" + loadID, so a bill whose
    // recipe reference failed to resolve NREs the moment CrossRefHandler registers it ("Exception
    // registering RimWorld.Bill_ProductionWithUft ... in loaded object directory"). Null recipes are
    // routine after a content mod is dropped: both BillStack.ExposeData and Dubs Mint Menus'
    // FBenchTemplate.ExposeData prune them -- but only in PostLoadInit, after cross-ref resolution
    // has already thrown (observed 2026-08-29: a saved Mint Menus bench template still referencing
    // Make_Seti_SpiderChair from a deactivated "Bicycles and more!"). Hand those bills a stable
    // placeholder ID so registration succeeds and the existing pruning happens quietly. loadID is
    // private, hence the FieldRef; if it ever disappears we fall back to the object hash, which is
    // still unique per bill for the directory's purposes.
    // Signature verified against the installed 1.6 DLL:
    //   public string RimWorld.Bill.GetUniqueLoadID()   (non-virtual, subclasses don't override)
    [HarmonyPatch(typeof(Bill), nameof(Bill.GetUniqueLoadID))]
    internal static class Bill_GetUniqueLoadID_NullRecipeGuard
    {
        private static readonly AccessTools.FieldRef<Bill, int> loadIDRef = MakeLoadIDRef();

        private static AccessTools.FieldRef<Bill, int> MakeLoadIDRef()
        {
            try { return AccessTools.FieldRefAccess<Bill, int>("loadID"); }
            catch { return null; }
        }

        private static bool Prefix(Bill __instance, ref string __result)
        {
            if (__instance == null || __instance.recipe != null)
                return true;
            __result = "Bill_NullRecipe_" + (loadIDRef != null ? loadIDRef(__instance) : __instance.GetHashCode());
            return false; // skip the original; it could only NRE
        }
    }

    // Vanilla (Biotech): JobDriver_BreastfeedCarryToMom's last toil hands the carried baby over with
    //     pawn.carryTracker.innerContainer.TryTransferToContainer(pawn.carryTracker.CarriedThing, Mom.carryTracker.innerContainer)
    // and ThingOwner.TryTransferToContainer reads item.stackCount immediately, so a hauler who lost
    // the baby somewhere along the walk NREs instead of failing the job. The driver's own guards
    // (FailOnDestroyedNullOrForbidden on both pawns, CanMomBreastfeedBaby) never recheck the carry
    // tracker after the goto toil. Trigger here (2026-08-29, twice): a birth handled twice over --
    // the log shows the same newborn processed twice and vanilla's "Tried to add the same relation
    // twice: Parent, pawn=Katina, otherPawn=Sandra" one line earlier -- so the second pass took the
    // baby out of the hauler's arms. End the job the way the toil's own transfer-failed branch
    // would, instead of throwing into TryStartErrorRecoverJob.
    // The toil body is a compiler-generated lambda; it's found by name rather than by its b__10_0
    // ordinal so a RimWorld update renumbering it downgrades to a skip, not a silent miss.
    // Signatures verified against the installed 1.6 DLL:
    //   void JobDriver_BreastfeedCarryToMom.<StartBreastfeedJobOnMom>b__10_0()   (instance, no args;
    //       inherited by JobDriver_BreastfeedCarryToDownedMom, which is covered by the same patch)
    //   public void Verse.AI.JobDriver.EndJobWith(JobCondition)   /   public Pawn JobDriver.pawn
    internal static class BreastfeedCarryToMomFixes
    {
        public static void Apply(Harmony h)
        {
            MethodInfo toilBody = null;
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(global::JobDriver_BreastfeedCarryToMom)))
                if (m.Name.StartsWith("<StartBreastfeedJobOnMom>") && m.GetParameters().Length == 0)
                {
                    toilBody = m;
                    break;
                }
            if (toilBody == null)
            {
                Log.Warning("[LocalCSharpFixes] skip breastfeed carry fix (StartBreastfeedJobOnMom toil body not found; RimWorld changed?)");
                return;
            }
            h.Patch(toilBody, prefix: new HarmonyMethod(typeof(BreastfeedCarryToMomFixes), nameof(EndJobWhenBabyNoLongerCarried)));
        }

        public static bool EndJobWhenBabyNoLongerCarried(JobDriver __instance)
        {
            if (__instance?.pawn?.carryTracker?.CarriedThing != null)
                return true;
            __instance?.EndJobWith(JobCondition.Incompletable);
            return false; // skip the original; it could only NRE
        }
    }

    // Vanilla: QuestManager.QuestManagerTick parks a null in activeQuests[j] when a quest goes
    // historical mid-loop and only compacts the list after the whole loop, so for the rest of that
    // tick ActiveQuestsListForReading has holes. Vanilla's own IsReservedByAnyQuest null-checks for
    // exactly this; QuestUtility.GetWorkDisabledQuestPart doesn't, and NREs on quests[i].State.
    // Stock RimWorld rarely reaches it, but Combat Readiness Check prefixes
    // StorytellerUtility.DefaultThreatPointsNow and walks every colonist through WorkTagIsDisabled ->
    // CombinedDisabledWorkTags -> here, so any quest part computing threat points during the same
    // tick a quest ends hits the hole (observed 2026-08-29 from QuestPart_SubquestGenerator_Gravcores;
    // Quest.QuestTick catches per part, so the subquest generation is just skipped for that tick).
    // Same null-safe walk, skipping the holes, plus a guard on a null pawns list. Also covers the
    // three other vanilla callers (CharacterCardUtility and two in Pawn), which would hit it from
    // the UI. The static buffer mirrors vanilla's own tmpQuestWorkDisabled, including its
    // single-buffer re-entrancy behaviour.
    // Signatures verified against the installed 1.6 DLL:
    //   static List<QuestPart_WorkDisabled> RimWorld.QuestUtility.GetWorkDisabledQuestPart(Pawn p)
    //   List<Pawn> RimWorld.QuestPart_WorkDisabled.pawns   /   QuestState RimWorld.Quest.State
    [HarmonyPatch(typeof(QuestUtility), nameof(QuestUtility.GetWorkDisabledQuestPart))]
    internal static class QuestUtility_GetWorkDisabledQuestPart_NullQuestGuard
    {
        private static readonly List<QuestPart_WorkDisabled> buffer = new List<QuestPart_WorkDisabled>();

        private static bool Prefix(Pawn p, ref List<QuestPart_WorkDisabled> __result)
        {
            var quests = Find.QuestManager?.ActiveQuestsListForReading;
            if (quests == null)
                return true; // no quest manager yet; let the original deal with it
            buffer.Clear();
            for (var i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (quest == null || quest.State != QuestState.Ongoing)
                    continue;
                var parts = quest.PartsListForReading;
                for (var j = 0; j < parts.Count; j++)
                    if (parts[j] is QuestPart_WorkDisabled disabled && disabled.pawns != null && disabled.pawns.Contains(p))
                        buffer.Add(disabled);
            }
            __result = buffer;
            return false;
        }
    }

    // Vanilla (Ideology): three ritual behaviour workers dereference an unfilled prisoner role in
    // PostCleanup -- "Error in LordJob.PostCleanup(): NullReferenceException" (observed 2026-08-29
    // on the conversion ritual). LordJob_Ritual.Notify_PawnLost calls assignments.RemoveParticipant
    // for every loss condition (killed, incapped, left the map, faction change, pulled into another
    // lord), so PawnWithRole returns null and `pawn.IsPrisonerOfColony` throws. Ludeon fixed exactly
    // this in RitualBehaviorWorker_Execution -- `if (pawn != null && pawn.IsPrisonerOfColony)` --
    // and left the other three workers unguarded. All four also pass the warden role straight into
    // TryTakePrisonerToBed, which calls warden.CanReserveAndReach, so a moralist/leader who left
    // mid-ritual is a second hole that even the fixed worker doesn't cover.
    // Take over the method with the fixed Execution logic: nothing to do when the prisoner role is
    // empty, escort only when there is still a warden, and apply the escape delay either way, so a
    // missing warden costs the escort rather than the whole cleanup. Lord.Cleanup catches the throw
    // today, so only that escort and delay are lost -- but they're lost on every affected ritual.
    // Signatures verified against the installed 1.6 DLL:
    //   void RimWorld.RitualBehaviorWorker_{Conversion,Trial,PrisonerSacrifice}.PostCleanup(LordJob_Ritual)
    //   Pawn RimWorld.LordJob_Ritual.PawnWithRole(string)   (assignments.FirstAssignedPawn, nullable)
    //   static void RimWorld.WorkGiver_Warden_TakeToBed.TryTakePrisonerToBed(Pawn prisoner, Pawn warden)
    //   Role ids as passed by each worker: conversion moralist/convertee, trial leader/convict,
    //   prisoner sacrifice moralist/prisoner
    internal static class RitualPrisonerCleanup
    {
        public static bool TakePrisonerToBed(LordJob_Ritual ritual, string wardenRole, string prisonerRole)
        {
            var prisoner = ritual?.PawnWithRole(prisonerRole);
            if (prisoner == null || !prisoner.IsPrisonerOfColony)
                return false; // role vacated mid-ritual; the original would only NRE
            var warden = ritual.PawnWithRole(wardenRole);
            if (warden != null)
                WorkGiver_Warden_TakeToBed.TryTakePrisonerToBed(prisoner, warden);
            prisoner.guest?.WaitInsteadOfEscapingFor(1250);
            return false;
        }
    }

    [HarmonyPatch(typeof(RitualBehaviorWorker_Conversion), nameof(RitualBehaviorWorker_Conversion.PostCleanup))]
    internal static class RitualConversion_PostCleanup_MissingRoleGuard
    {
        private static bool Prefix(LordJob_Ritual ritual) => RitualPrisonerCleanup.TakePrisonerToBed(ritual, "moralist", "convertee");
    }

    [HarmonyPatch(typeof(RitualBehaviorWorker_Trial), nameof(RitualBehaviorWorker_Trial.PostCleanup))]
    internal static class RitualTrial_PostCleanup_MissingRoleGuard
    {
        private static bool Prefix(LordJob_Ritual ritual) => RitualPrisonerCleanup.TakePrisonerToBed(ritual, "leader", "convict");
    }

    [HarmonyPatch(typeof(RitualBehaviorWorker_PrisonerSacrifice), nameof(RitualBehaviorWorker_PrisonerSacrifice.PostCleanup))]
    internal static class RitualPrisonerSacrifice_PostCleanup_MissingRoleGuard
    {
        private static bool Prefix(LordJob_Ritual ritual) => RitualPrisonerCleanup.TakePrisonerToBed(ritual, "moralist", "prisoner");
    }

    // Vanilla: a pawn already lying in a bed whose reservation slots are all claimed can't start its
    // LayDown job -- red "Could not reserve <bed> ... for maxPawns 4", then the yellow
    // "TryMakePreToilReservations() returned false for a non-queued job", and the pawn is left in the
    // bed with an errored job that the think tree re-issues (observed 2026-08-31: four caged prisoners
    // in one Capture Expansion CE_CageLarge, 4 sleeping slots, FIVE reservations on it). Beds can be
    // over-subscribed because ReservationManager.Reserve lets a playerForced job (here: ordered
    // captures into a full cage) reserve past maxPawns and interrupt the existing reservers -- whose
    // LayDown jobs then restart and find no slot left, even though they are physically in the bed.
    // When the pawn already occupies the bed the reservation is bookkeeping, not a claim on a free
    // slot: take one quietly if there is one, and lie down either way. Pawns not yet in the bed still
    // go through vanilla, so a genuine race for a free slot is still resolved by reservation.
    [HarmonyPatch(typeof(JobDriver_LayDown), nameof(JobDriver_LayDown.TryMakePreToilReservations))]
    internal static class JobDriverLayDown_AlreadyInBed_ReservationGuard
    {
        private static bool Prefix(JobDriver_LayDown __instance, ref bool __result)
        {
            var pawn = __instance.pawn;
            var bed = __instance.Bed;
            if (pawn == null || bed == null || !bed.Spawned || bed.Map != pawn.Map)
                return true;
            if (!bed.OccupiedRect().Contains(pawn.Position))
                return true; // not in the bed yet -- vanilla's reservation race still applies
            if (pawn.CanReserve(bed, bed.SleepingSlotsCount, 0))
                return true;
            pawn.Reserve(bed, __instance.job, bed.SleepingSlotsCount, 0, null, errorOnFailed: false);
            __result = true;
            return false;
        }
    }

    // The Dead Man's Switch: its analyzable items (artificial muscle, wetware computer, T/R component,
    // ...) set completedLetter to "By studying the {PARENT_label}, {STUDIER_labelShort} has acquired
    // the insight ...". PARENT/STUDIER are Anomaly's CompStudiable symbols; the comp here is Biotech's
    // CompAnalyzableUnlockResearch, and CompAnalyzable.GetFormattedLetterString only supplies PAWN plus
    // the comp's ExtraNamedArg (RESEARCH). So finishing an analysis logs a red "Could not resolve
    // symbol" per unresolved symbol and shows the raw braces in the letter. Fixing the mod's XML isn't
    // enough -- its English DefInjected carries the same text and is applied after patches -- so add
    // STUDIER (the studier) and PARENT (the studied thing) as aliases here. Vanilla's own analyzable
    // letters use PAWN and never contain these symbols, so nothing else changes behaviour.
    [HarmonyPatch(typeof(CompAnalyzable), "GetFormattedLetterString")]
    internal static class CompAnalyzable_GetFormattedLetterString_StudySymbols
    {
        private static bool Prefix(CompAnalyzable __instance, string text, Pawn pawn, ref string __result)
        {
            if (string.IsNullOrEmpty(text) || (!text.Contains("{PARENT") && !text.Contains("{STUDIER")))
                return true;
            var args = new List<NamedArgument>();
            if (pawn != null) { args.Add(pawn.Named("PAWN")); args.Add(pawn.Named("STUDIER")); }
            if (__instance?.parent != null) args.Add(__instance.parent.Named("PARENT"));
            var extra = __instance?.ExtraNamedArg;
            if (extra.HasValue) args.Add(extra.Value);
            __result = text.Formatted(args).Resolve();
            return false;
        }
    }
}
