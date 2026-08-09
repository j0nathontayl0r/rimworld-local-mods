using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // VanillaTradingExpanded assigns new Company loadIDs as companies.Count
    // (TradingManager: new Company(GetFaction(tradeKind), tradeKind, companies.Count)). Once any
    // company has ever been removed, Count falls below the max ID in use, so every newly generated
    // company collides with an existing one -> duplicate "Company_N" loadIDs -> two
    // DebugLoadIDsSavingErrorsChecker warnings (with stack extraction) on every save, and ambiguous
    // cross-refs on load. Signatures verified against the installed 1.6 DLL and upstream source:
    //   VanillaTradingExpanded.Company..ctor(Faction, TraderKindDef, int)   (sets this.loadID = id)
    //   VanillaTradingExpanded.TradingManager.companies                     (List<Company>)
    //   VanillaTradingExpanded.Company.loadID                               (int)
    // Two patches:
    //   - Company ctor postfix: if the freshly assigned loadID is already taken, move it to max+1.
    //   - TradingManager.ExposeData postfix (PostLoadInit): re-ID collisions already baked into a
    //     save, so damaged saves self-heal on load. Safe post cross-ref: refs hold live objects by
    //     then, not ID strings.
    public static class VTECompanyFixes
    {
        private static Type companyType;
        private static Type managerType;
        private static FieldInfo loadIdField;
        private static FieldInfo companiesField;

        public static void Apply(Harmony h)
        {
            companyType = AccessTools.TypeByName("VanillaTradingExpanded.Company");
            managerType = AccessTools.TypeByName("VanillaTradingExpanded.TradingManager");
            if (companyType == null || managerType == null)
            {
                Log.Message("[LocalCSharpFixes] skip VTE company-id fix (VanillaTradingExpanded not present)");
                return;
            }
            loadIdField = AccessTools.Field(companyType, "loadID");
            companiesField = AccessTools.Field(managerType, "companies");
            var ctor = AccessTools.Constructor(companyType, new[] { typeof(Faction), typeof(TraderKindDef), typeof(int) });
            var expose = AccessTools.Method(managerType, "ExposeData");
            if (loadIdField == null || companiesField == null || ctor == null || expose == null)
            {
                Log.Warning("[LocalCSharpFixes] skip VTE company-id fix (member not found; mod layout changed?)");
                return;
            }

            h.Patch(ctor, postfix: new HarmonyMethod(typeof(VTECompanyFixes), nameof(EnsureUniqueId)));
            h.Patch(expose, postfix: new HarmonyMethod(typeof(VTECompanyFixes), nameof(HealLoadedIds)));
        }

        // New companies: the ctor runs before TradingManager adds the company to its list, so any
        // list entry holding our ID is a genuine collision.
        public static void EnsureUniqueId(object __instance)
        {
            try
            {
                var list = CompaniesOf(CurrentManager());
                if (list == null) return;
                var mine = (int)loadIdField.GetValue(__instance);
                var max = -1;
                var clash = false;
                foreach (var c in list)
                {
                    if (ReferenceEquals(c, __instance)) continue;
                    var id = (int)loadIdField.GetValue(c);
                    if (id == mine) clash = true;
                    if (id > max) max = id;
                }
                if (clash)
                {
                    loadIdField.SetValue(__instance, max + 1);
                    Log.Message($"[LocalCSharpFixes] VTE company id collision avoided: {mine} -> {max + 1}");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] VTE EnsureUniqueId failed: {e.Message}");
            }
        }

        // Loaded saves: re-ID any duplicates that are already baked in.
        public static void HealLoadedIds(GameComponent __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            try
            {
                var list = CompaniesOf(__instance);
                if (list == null) return;
                var seen = new HashSet<int>();
                var max = -1;
                var dupes = new List<object>();
                foreach (var c in list)
                {
                    var id = (int)loadIdField.GetValue(c);
                    if (!seen.Add(id)) dupes.Add(c);
                    if (id > max) max = id;
                }
                foreach (var c in dupes) loadIdField.SetValue(c, ++max);
                if (dupes.Count > 0)
                    Log.Message($"[LocalCSharpFixes] VTE: re-IDed {dupes.Count} duplicate compan{(dupes.Count == 1 ? "y" : "ies")} on load");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] VTE HealLoadedIds failed: {e.Message}");
            }
        }

        private static object CurrentManager()
        {
            var game = Current.Game;
            if (game == null) return null;
            foreach (var c in game.components)
            {
                if (managerType.IsInstanceOfType(c)) return c;
            }
            return null;
        }

        private static IEnumerable CompaniesOf(object manager)
            => manager == null ? null : companiesField.GetValue(manager) as IEnumerable;
    }
}
