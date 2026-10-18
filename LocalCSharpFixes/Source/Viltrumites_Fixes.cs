using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Viltrumites in Rimworld (3708277667): ViltrumiteEmpireIdeologyHelper.ApplyImperialIdeology
    // rewrites the Viltrumite faction's primary ideo (name, adjective, member name, leader title,
    // locked description, icon, deities, roles and 22+ issue precepts) on faction generation,
    // in a FactionIdeosTracker.ChooseOrGenerateIdeo postfix and again in every Map.FinalizeInit
    // postfix. In classic mode every faction, the player's included, shares one ideo, so the mod
    // turns the colony's own ideo into "The Great Viltrumite Empire" with its precepts (observed
    // 2026-10-18: Raiding_Respected, IdeoDiversity_Abhorrent and so on on the shared Ideo_22).
    //
    // 1. Skip ApplyImperialIdeology and the main-thread icon when the ideo is not the Viltrumites'
    //    own: classic mode, or another faction holds the same ideo.
    // 2. On load in classic mode, restore an ideo that still carries the Viltrumite name or
    //    adjective once: remove the non-classic precepts of the issues the mod replaced, add back
    //    the missing classic precepts of those issues and the specialist roles, unlock and rename
    //    the roles, and reset the name (culture label, as IdeoGenerator.GenerateClassicIdeo does),
    //    texts, deities and icon. The old name is not saved anywhere, so it can't come back.
    // Signatures verified against the shipped source (1.6/Source/ViltrumiteEmpireIdeologyPatch.cs):
    //   public static void ViltrumiteEmpireIdeologyHelper.ApplyImperialIdeology(Ideo, FactionDef)
    //   private static void ViltrumiteEmpireIdeologyMainThreadPatch.ApplyMainThreadIcon(Ideo)
    public static class ViltrumitesFixes
    {
        private static readonly string[] ReplacedIssues =
        {
            "Raiding", "Research", "IdeoDiversity", "Execution", "OrganUse", "Cannibalism", "Biosculpting",
            "Blindness", "Corpses", "FungusEating", "InsectMeat", "MarriageName", "NutrientPasteEating",
            "Scarification", "Skullspike", "Slavery", "Lovin", "SpouseCount_Male", "SpouseCount_Female",
            "VME_Leader", "VME_Death", "VME_Scars",
        };

        private static readonly string[] RemovedRoles = { "IdeoRole_ShootingSpecialist", "IdeoRole_MeleeSpecialist" };

        public static void Apply(Harmony h)
        {
            try
            {
                var helper = AccessTools.TypeByName("Viltrumites.ViltrumiteEmpireIdeologyHelper");
                var apply = helper == null ? null : AccessTools.Method(helper, "ApplyImperialIdeology", new[] { typeof(Ideo), typeof(FactionDef) });
                if (apply == null)
                {
                    Log.Message("[LocalCSharpFixes] skip Viltrumites shared-ideo fix (target not present)");
                    return;
                }
                h.Patch(apply, prefix: new HarmonyMethod(typeof(ViltrumitesFixes), nameof(ApplyImperialIdeology_Prefix)));

                var icon = AccessTools.Method(AccessTools.TypeByName("Viltrumites.ViltrumiteEmpireIdeologyMainThreadPatch"), "ApplyMainThreadIcon", new[] { typeof(Ideo) });
                if (icon != null)
                    h.Patch(icon, prefix: new HarmonyMethod(typeof(ViltrumitesFixes), nameof(ApplyMainThreadIcon_Prefix)));

                h.Patch(AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)),
                    postfix: new HarmonyMethod(typeof(ViltrumitesFixes), nameof(Game_FinalizeInit_Postfix)));
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Viltrumites shared-ideo fix failed: {e.Message}");
            }
        }

        public static bool ApplyImperialIdeology_Prefix(Ideo ideo) => IsOwnIdeo(ideo);

        public static bool ApplyMainThreadIcon_Prefix(Ideo ideo) => IsOwnIdeo(ideo);

        private static bool IsOwnIdeo(Ideo ideo)
        {
            if (ideo == null)
                return true;
            var world = Find.World;
            if (world?.ideoManager != null && world.ideoManager.classicMode)
                return false;
            var viltrum = DefDatabase<FactionDef>.GetNamedSilentFail("ImperioViltrumita");
            var factions = world?.factionManager?.AllFactionsListForReading;
            return factions == null || !factions.Any(f => f.def != viltrum && f.ideos != null && f.ideos.Has(ideo));
        }

        public static void Game_FinalizeInit_Postfix()
        {
            try
            {
                var manager = Find.IdeoManager;
                if (manager == null || !manager.classicMode)
                    return;
                string name = "ViltrumiteIdeo.Name".TryTranslate(out var n) ? n.ToString() : null;
                string adjective = "ViltrumiteIdeo.Adjective".TryTranslate(out var a) ? a.ToString() : null;
                foreach (Ideo ideo in manager.IdeosListForReading)
                {
                    if ((name != null && ideo.name == name) || (adjective != null && ideo.adjective == adjective))
                        Restore(ideo);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Viltrumites classic ideo restore failed: {e}");
            }
        }

        private static void Restore(Ideo ideo)
        {
            string oldName = ideo.name;
            var issues = new HashSet<string>(ReplacedIssues);
            foreach (Precept p in ideo.PreceptsListForReading.ToList())
            {
                if (p?.def != null && !p.def.classic && p.def.issue != null && issues.Contains(p.def.issue.defName))
                    ideo.RemovePrecept(p);
            }
            ideo.memes.RemoveAll(m => m.defName == "VME_BloodCourt");
            foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefsListForReading)
            {
                if (!def.classic || ideo.PreceptsListForReading.Any(p => p.def == def))
                    continue;
                if ((def.issue != null && issues.Contains(def.issue.defName)) || RemovedRoles.Contains(def.defName))
                    ideo.AddPrecept(PreceptMaker.MakePrecept(def), init: true, null, def.ritualPatternBase);
            }
            foreach (Precept_Role role in ideo.PreceptsListForReading.OfType<Precept_Role>())
            {
                if (!role.nameLocked)
                    continue;
                role.nameLocked = false;
                role.RegenerateName();
            }

            ideo.name = UniqueCultureName(ideo);
            ideo.adjective = null;
            ideo.memberName = FactionDefOf.PlayerColony.basicMemberKind.label;
            ideo.leaderTitleMale = null;
            ideo.leaderTitleFemale = null;
            Traverse.Create(ideo).Field("overriddenWorshipRoomLabel").SetValue(null);
            ideo.descriptionLocked = false;
            ideo.RegenerateDescription(force: true);
            (ideo.foundation as IdeoFoundation_Deity)?.GenerateDeities();
            ideo.SetIcon(IdeoFoundation.GetRandomIconDef(ideo), IdeoFoundation.GetRandomColorDef(ideo), clearPrimaryFactionColor: true);
            ideo.RecachePrecepts();
            ideo.RecachePossibleBuildings();
            ideo.RecachePossibleRoles();
            Log.Message($"[LocalCSharpFixes] restored classic ideo '{oldName}' -> '{ideo.name}' (Viltrumites rewrite removed)");
        }

        private static string UniqueCultureName(Ideo self)
        {
            string baseName = self.culture?.LabelCap.ToString() ?? "Ideoligion";
            for (int i = 1; ; i++)
            {
                string candidate = i == 1 ? baseName : baseName + " " + i;
                if (!Find.IdeoManager.IdeosListForReading.Any(o => o != self && o.name == candidate))
                    return candidate;
            }
        }
    }
}
