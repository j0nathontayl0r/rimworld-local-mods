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
            TryPrefix(h, "Xenomorphtype.XMTUtility", "WitnessLarva",
                      nameof(ThirdPartyPatches.XMT_WitnessLarva_Prefix));

            GrimWorld40kFixes.Apply(h);
            VACLogSpamFixes.Apply(h);
            VTECompanyFixes.Apply(h);
            TraderShipsFixes.Apply(h);

            Log.Message("[LocalCSharpFixes] Harmony patches applied.");
        }

        private static void TryPrefix(Harmony h, string type, string method, string patchMethod)
            => TryPatch(h, type, method, patchMethod, isFinalizer: false);

        private static void TryFinalizer(Harmony h, string type, string method, string patchMethod)
            => TryPatch(h, type, method, patchMethod, isFinalizer: true);

        private static void TryPatch(Harmony h, string typeName, string methodName, string patchMethod, bool isFinalizer)
        {
            try
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) { Log.Message($"[LocalCSharpFixes] skip {typeName}.{methodName} (type not present)"); return; }
                var m = AccessTools.Method(t, methodName);
                if (m == null) { Log.Warning($"[LocalCSharpFixes] skip {typeName}.{methodName} (method not found)"); return; }
                var hm = new HarmonyMethod(typeof(ThirdPartyPatches), patchMethod);
                if (isFinalizer) h.Patch(m, finalizer: hm);
                else h.Patch(m, prefix: hm);
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching {typeName}.{methodName}: {e.Message}");
            }
        }
    }
}
