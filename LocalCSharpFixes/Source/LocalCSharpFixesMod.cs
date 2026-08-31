using System;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // Bootstrap. Loads LAST, so its patches sit on top of every other mod's.
    //  - PatchAll() applies the attribute-based patches (vanilla types + Game.ExposeData).
    //  - The manual block patches third-party mod methods we can't reference at compile time; each
    //    resolves its target by name and logs+skips if the mod is absent or has changed, so a missing
    //    target never breaks startup.
    [StaticConstructorOnStartup]
    public static class LocalCSharpFixesMod
    {
        static LocalCSharpFixesMod()
        {
            var h = new Harmony("local.csharpfixes");
            h.PatchAll();

            TryPrefix(h, "VanillaSocialInteractionsExpanded.GatheringWorker_MealTogether", "GetChairFor",
                      nameof(ThirdPartyPatches.VSIE_GetChairFor_Prefix));
            TryPrefix(h, "VFEPirates.WarcasketUtility", "IsWearingWarcasket",
                      nameof(ThirdPartyPatches.VFEP_IsWearingWarcasket_Prefix));
            TryPrefix(h, "SmartFarming.MapComponent_SmartFarming", "FinalizeInit",
                      nameof(ThirdPartyPatches.SmartFarming_FinalizeInit_Prefix));
            TryPrefix(h, "PickUpAndHaul.JobDriver_HaulToInventory", "TryMakePreToilReservations",
                      nameof(ThirdPartyPatches.PUAH_TryMakePreToilReservations_Prefix));
            TryFinalizer(h, "ReinforcedMechanoids.TrooperStorage", "PreApplyDamage",
                      nameof(ThirdPartyPatches.RM_PreApplyDamage_Finalizer));
            TryFinalizer(h, "Defaults.WorkPriorities.Patch_Pawn", "Postfix",
                      nameof(ThirdPartyPatches.Defaults_WorkPrioPostfix_Finalizer));
            TryFinalizer(h, "VEF.AnimalBehaviours.JobDriver_IngestWeird+<>c__DisplayClass20_0", "<FinalizeIngestAnything>b__0",
                      nameof(ThirdPartyPatches.VEF_IngestWeird_Finalizer));
            TryPrefix(h, "VAC.VAC_Settings", "PlayEventNotificationWithVolumeByDef",
                      nameof(ThirdPartyPatches.VAC_PlayEventNotification_Prefix));
            TryPrefix(h, "Xenomorphtype.XMTLifeStagePatches+Patch_LifeStageWorker_Notify_LifeStageStarted", "Postfix",
                      nameof(ThirdPartyPatches.XMT_LifeStageStarted_Prefix));
            foreach (var witness in new[] { "WitnessLarva", "WitnessLarvaHorror", "WitnessHorror", "WitnessAcid",
                                            "WitnessAcidHorror", "WitnessOvomorph", "WitnessOvomorphHorror", "WitnessPsychicHorror" })
                TryPrefixAllOverloads(h, "Xenomorphtype.XMTUtility", witness,
                          nameof(ThirdPartyPatches.XMT_Witness_Prefix), requiredParam: "mapHeld");
            TryPrefix(h, "Vehicles.Patch_CaravanHandling", "PawnMassUsageInVehicle",
                      nameof(ThirdPartyPatches.VF_PawnMassUsage_Prefix));
            TryPrefixAllOverloads(h, "Xenomorphtype.XMTUtility", "TrySpawnPawnFromTarget",
                      nameof(ThirdPartyPatches.XMT_TrySpawnPawn_Prefix), requiredParam: null);
            TryPrefixAllOverloads(h, "CSL_Blackboard.ITab_CSL_BlackboardSchedule", "IsTeachingDisabledForPawn",
                      nameof(ThirdPartyPatches.CSL_TeachingDisabled_Prefix), requiredParam: null);
            TryPrefixAllOverloads(h, "CSL_SchoolTable.ITab_CSL_SchoolTableSchedule", "IsLearningDisabledForPawn",
                      nameof(ThirdPartyPatches.CSL_TeachingDisabled_Prefix), requiredParam: null);
            TryPrefix(h, "CombatExtended.JobDriver_Hunt", "TryMakePreToilReservations",
                      nameof(ThirdPartyPatches.CE_HuntReservation_Prefix));
            TryPrefix(h, "ColonyManagerRedux.Building_AIManager", "Tick",
                      nameof(ThirdPartyPatches.CMR_AIManagerTick_Prefix));
            TryPrefix(h, "CombatExtended.CompMechAmmo", "TryMakeAmmoJob",
                      nameof(ThirdPartyPatches.CE_MechAmmoJob_Prefix));
            TryPrefix(h, "ColonyManagerRedux.Managers.Utilities_Hunting", "GetMapPawnKindDefs",
                      nameof(ThirdPartyPatches.CMR_MapPawnKinds_Prefix));
            TryPostfix(h, "CombatExtended.AI.CompTend", "TryGiveTacticalJob",
                      nameof(ThirdPartyPatches.CE_TendSelfJob_Postfix));

            GrimWorld40kFixes.Apply(h);
            VACLogSpamFixes.Apply(h);
            VTECompanyFixes.Apply(h);
            TraderShipsFixes.Apply(h);
            ResearchReinventedFixes.Apply(h);
            RealRuinsFixes.Apply(h);
            StaleSaveRefFixes.Apply(h);
            CaptureExpansionFixes.Apply(h);
            MeleeAnimationFixes.Apply(h);
            IsekaiLevelingFixes.Apply(h);
            VEFHeavyWeaponFixes.Apply(h);
            ReGrowthWorldGenFixes.Apply(h);
            VFEEmpireFixes.Apply(h);
            BreastfeedCarryToMomFixes.Apply(h);
            RimefellerFixes.Apply(h);

            Log.Message("[LocalCSharpFixes] Harmony patches applied.");
        }

        private enum PatchKind { Prefix, Postfix, Finalizer }

        private static void TryPrefix(Harmony h, string type, string method, string patchMethod)
            => TryPatch(h, type, method, patchMethod, PatchKind.Prefix);

        private static void TryPostfix(Harmony h, string type, string method, string patchMethod)
            => TryPatch(h, type, method, patchMethod, PatchKind.Postfix);

        private static void TryFinalizer(Harmony h, string type, string method, string patchMethod)
            => TryPatch(h, type, method, patchMethod, PatchKind.Finalizer);

        // For overloaded targets, where AccessTools.Method(type, name) throws AmbiguousMatchException
        // (seen: XMTUtility.WitnessLarva). Patches every overload carrying a parameter named
        // requiredParam, which is also the parameter the prefix binds by name - overloads without it
        // couldn't bind and are skipped. Pass requiredParam: null to patch every overload (use with
        // prefixes that bind by __0/__1 index, which always resolve).
        private static void TryPrefixAllOverloads(Harmony h, string typeName, string methodName, string patchMethod, string requiredParam)
        {
            try
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) { Log.Message($"[LocalCSharpFixes] skip {typeName}.{methodName} (type not present)"); return; }
                int patched = 0;
                foreach (var m in AccessTools.GetDeclaredMethods(t))
                {
                    if (m.Name != methodName) continue;
                    if (requiredParam != null)
                    {
                        bool hasParam = false;
                        foreach (var p in m.GetParameters())
                            if (p.Name == requiredParam) { hasParam = true; break; }
                        if (!hasParam) continue;
                    }
                    h.Patch(m, prefix: new HarmonyMethod(typeof(ThirdPartyPatches), patchMethod));
                    patched++;
                }
                if (patched == 0) Log.Warning($"[LocalCSharpFixes] skip {typeName}.{methodName} (no matching overload{(requiredParam == null ? "" : $" with param '{requiredParam}'")})");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching {typeName}.{methodName}: {e.Message}");
            }
        }

        private static void TryPatch(Harmony h, string typeName, string methodName, string patchMethod, PatchKind kind)
        {
            try
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) { Log.Message($"[LocalCSharpFixes] skip {typeName}.{methodName} (type not present)"); return; }
                var m = AccessTools.Method(t, methodName);
                if (m == null) { Log.Warning($"[LocalCSharpFixes] skip {typeName}.{methodName} (method not found)"); return; }
                var hm = new HarmonyMethod(typeof(ThirdPartyPatches), patchMethod);
                if (kind == PatchKind.Finalizer) h.Patch(m, finalizer: hm);
                else if (kind == PatchKind.Postfix) h.Patch(m, postfix: hm);
                else h.Patch(m, prefix: hm);
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching {typeName}.{methodName}: {e.Message}");
            }
        }
    }
}
