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
    //    adjective, or that still holds what the rewrite left behind (a non-classic role, a
    //    non-classic precept of a replaced issue, two precepts of one def, an "ERR" role name):
    //    remove the non-classic precepts of the replaced issues and the non-classic roles (with
    //    replacing: true, because otherwise RemovePrecept adds a random default precept back),
    //    remove duplicates, add back the missing classic precepts of those issues, clear the
    //    deities and give a locked or broken role its label back. A classic ideo has no memes, so
    //    role names and deities can't be generated (the grammar gives "ERR", GenerateDeities
    //    throws). Only for the name match, also reset the name (culture label, as
    //    IdeoGenerator.GenerateClassicIdeo does), texts and icon. The old name is not saved
    //    anywhere, so it can't come back. A first version renamed the roles and generated deities;
    //    it left "ERR: Viltrumite" on the Moralist role (observed 2026-10-19), which this repairs.
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

        private static readonly AccessTools.FieldRef<IdeoFoundation_Deity, List<IdeoFoundation_Deity.Deity>> deities =
            AccessTools.FieldRefAccess<IdeoFoundation_Deity, List<IdeoFoundation_Deity.Deity>>("deities");
        private static readonly AccessTools.FieldRef<Precept, string> preceptName = AccessTools.FieldRefAccess<Precept, string>("name");
        private static readonly AccessTools.FieldRef<Precept, string> labelCapCache = AccessTools.FieldRefAccess<Precept, string>("labelCapCache");

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
                var issues = new HashSet<string>(ReplacedIssues);
                foreach (Ideo ideo in manager.IdeosListForReading)
                {
                    bool rewritten = (name != null && ideo.name == name) || (adjective != null && ideo.adjective == adjective);
                    if (rewritten || NeedsRepair(ideo, issues))
                        Restore(ideo, issues, rewritten);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] Viltrumites classic ideo restore failed: {e}");
            }
        }

        private static bool InReplacedIssue(Precept p, HashSet<string> issues) =>
            p?.def?.issue != null && issues.Contains(p.def.issue.defName);

        private static bool IsErrName(Precept p) => preceptName(p)?.StartsWith("ERR") == true;

        private static bool NeedsRepair(Ideo ideo, HashSet<string> issues)
        {
            var seen = new HashSet<PreceptDef>();
            foreach (Precept p in ideo.PreceptsListForReading)
            {
                if (p?.def == null)
                    continue;
                if (p is Precept_Role && (!p.def.classic || IsErrName(p)))
                    return true;
                if (InReplacedIssue(p, issues) && (!p.def.classic || !seen.Add(p.def)))
                    return true;
            }
            return false;
        }

        private static void Restore(Ideo ideo, HashSet<string> issues, bool resetTexts)
        {
            string oldName = ideo.name;
            var seen = new HashSet<PreceptDef>();
            foreach (Precept p in ideo.PreceptsListForReading.ToList())
            {
                if (p?.def == null)
                    continue;
                bool badRole = p is Precept_Role && !p.def.classic;
                bool badIssue = InReplacedIssue(p, issues) && (!p.def.classic || !seen.Add(p.def));
                if (badRole || badIssue)
                    ideo.RemovePrecept(p, replacing: true);
            }
            ideo.memes.RemoveAll(m => m.defName == "VME_BloodCourt");
            foreach (PreceptDef def in DefDatabase<PreceptDef>.AllDefsListForReading)
            {
                if (def.classic && def.issue != null && issues.Contains(def.issue.defName)
                    && !ideo.PreceptsListForReading.Any(p => p.def == def))
                    ideo.AddPrecept(PreceptMaker.MakePrecept(def), init: true, null, def.ritualPatternBase);
            }
            foreach (Precept_Role role in ideo.PreceptsListForReading.OfType<Precept_Role>())
            {
                if (!role.nameLocked && !IsErrName(role))
                    continue;
                role.nameLocked = false;
                preceptName(role) = role.def.label;
                labelCapCache(role) = null;
                role.ClearTipCache();
            }
            if (ideo.foundation is IdeoFoundation_Deity foundation)
                deities(foundation)?.Clear();

            if (resetTexts)
            {
                ideo.name = UniqueCultureName(ideo);
                ideo.adjective = null;
                ideo.memberName = FactionDefOf.PlayerColony.basicMemberKind.label;
                ideo.leaderTitleMale = null;
                ideo.leaderTitleFemale = null;
                Traverse.Create(ideo).Field("overriddenWorshipRoomLabel").SetValue(null);
                ideo.descriptionLocked = false;
                ideo.RegenerateDescription(force: true);
                ideo.SetIcon(IdeoFoundation.GetRandomIconDef(ideo), IdeoFoundation.GetRandomColorDef(ideo), clearPrimaryFactionColor: true);
            }
            ideo.RecachePrecepts();
            ideo.RecachePossibleBuildings();
            ideo.RecachePossibleRoles();
            Log.Message(resetTexts
                ? $"[LocalCSharpFixes] restored classic ideo '{oldName}' -> '{ideo.name}' (Viltrumites rewrite removed)"
                : $"[LocalCSharpFixes] repaired classic ideo '{ideo.name}' (Viltrumites leftovers removed)");
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
