using System;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // RealRuins rebuilds blueprint pawns from raw donor-save XML (PawnRestoreUtility). Its
    // ValidateAndFixHediffNode checks that the hediff's BodyPartDef exists locally, but the actual
    // part is resolved by Scribe_BodyParts from BodyDef name + index; when the donor ran a race mod
    // we don't have (seen: BodyDef "Gor"), the part resolves to null and RestorePawnHealth still
    // adds the hediff (null part is legal for whole-body hediffs). It also deserialises hediffs via
    // ScribeExtractor under LoadingVars, keeping the donor save's <loadID> values. Result, observed
    // 2026-08-24: injuries/missing-parts with null parts spamming "Hediff_Injury has null part
    // after loading" on every subsequent save load, and donor loadIDs colliding with local hediffs
    // ("Cannot register ... id already used") at save/load. Sanitise each restored pawn: drop
    // part-less injuries (untendable dead weight) and re-ID every hediff. Safe here: nothing
    // references the fresh hediffs by ID yet (the combat-log link is an object WeakReference).
    // Second RealRuins scribe bug, observed 2026-08-25: BakedLogEntry (the battle-log lines baked
    // into blueprints) is constructed during blueprint restore while a Scribe operation is active,
    // and the vanilla LogEntry ctor only assigns a fresh logID when Scribe.mode == Inactive -- so
    // every baked entry keeps logID 0 (all 108 in the current save). GetUniqueLoadID() is
    // "LogEntry_{ticksAbs}_{logID}", and RealRuins date-shifts ticksAbs, so two entries baked with
    // the same shift collide ("Cannot register RealRuins.BakedLogEntry ... Id already used") on
    // every load. Two patches, mirroring the VTE company-id fix:
    //   - 3-arg ctor postfix: assign a fresh logID whenever the base ctor skipped it.
    //   - ExposeData postfix (PostLoadInit): re-ID zero-ID entries already baked into a save. Safe
    //     post cross-ref: references hold live objects by then. The duplicate-registration error
    //     fires once more when loading an old save (registration precedes PostLoadInit), then the
    //     next save is clean.
    // Signatures verified against the installed 1.6 DLLs:
    //   Pawn RealRuins.PawnRestoreUtility.MakePawnWithRawXml(string xml)   (internal, returns null on failure)
    //   RealRuins.BakedLogEntry..ctor(string logString, Pawn pawn, long dateShift)
    //   Verse.LogEntry.logID   (protected int; ctor guard: if (Scribe.mode == LoadSaveMode.Inactive) logID = GetNextLogID())
    // 1.7 follow-up, observed 2026-10-15: on a brand-new game AssignFreshLogId itself drew
    // "Getting next unique ID during LoadingVars before UniqueIDsManager was loaded. Assigning a
    // random value." (11x per map). UniqueIDsManager.GetNextID warns and returns Rand.Int whenever
    // a scribe load runs before the manager was loaded from a save -- which on a new game is
    // always, since it never is. While a map is being generated in such a game the in-memory
    // counter IS authoritative, so take the next logID straight from nextLogID; everywhere else
    // keep going through GetNextLogID so a real save load still gets vanilla's behaviour.
    // Members verified against the installed 1.7 Assembly-CSharp:
    //   private int RimWorld.UniqueIDsManager.nextLogID / private bool wasLoaded
    //   public static Map Verse.MapGenerator.mapBeingGenerated
    public static class RealRuinsFixes
    {
        private static AccessTools.FieldRef<LogEntry, int> logIdField;
        private static readonly AccessTools.FieldRef<RimWorld.UniqueIDsManager, int> nextLogIdField = TryFieldRef<int>("nextLogID");
        private static readonly AccessTools.FieldRef<RimWorld.UniqueIDsManager, bool> wasLoadedField = TryFieldRef<bool>("wasLoaded");

        private static AccessTools.FieldRef<RimWorld.UniqueIDsManager, T> TryFieldRef<T>(string name)
        {
            try { return AccessTools.FieldRefAccess<RimWorld.UniqueIDsManager, T>(name); }
            catch { return null; }
        }

        private static int NextLogId()
        {
            var ids = Find.UniqueIDsManager;
            if (Scribe.mode == LoadSaveMode.LoadingVars && MapGenerator.mapBeingGenerated != null
                && nextLogIdField != null && wasLoadedField != null && !wasLoadedField(ids))
            {
                int id = nextLogIdField(ids);
                nextLogIdField(ids) = id == int.MaxValue - 1 ? 0 : id + 1;
                return id;
            }
            return ids.GetNextLogID();
        }

        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("RealRuins.PawnRestoreUtility");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip RealRuins pawn-restore fix (mod not present)");
                return;
            }
            var m = AccessTools.Method(t, "MakePawnWithRawXml");
            if (m == null)
            {
                Log.Warning("[LocalCSharpFixes] skip RealRuins pawn-restore fix (MakePawnWithRawXml not found; mod layout changed?)");
                return;
            }
            h.Patch(m, postfix: new HarmonyMethod(typeof(RealRuinsFixes), nameof(SanitizeRestoredPawn)));

            var tLog = AccessTools.TypeByName("RealRuins.BakedLogEntry");
            var ctor = tLog == null ? null : AccessTools.Constructor(tLog, new[] { typeof(string), typeof(Pawn), typeof(long) });
            var expose = tLog == null ? null : AccessTools.Method(tLog, "ExposeData");
            try { logIdField = AccessTools.FieldRefAccess<LogEntry, int>("logID"); } catch { logIdField = null; }
            if (ctor == null || expose == null || logIdField == null)
            {
                Log.Warning("[LocalCSharpFixes] skip RealRuins baked-log-id fix (member not found; mod layout changed?)");
                return;
            }
            h.Patch(ctor, postfix: new HarmonyMethod(typeof(RealRuinsFixes), nameof(AssignFreshLogId)));
            h.Patch(expose, postfix: new HarmonyMethod(typeof(RealRuinsFixes), nameof(HealZeroLogId)));
        }

        // The base LogEntry ctor only assigns an id while no Scribe operation is running; RealRuins
        // bakes entries mid-scribe, so cover the gap. When the base ctor already assigned one,
        // skip -- don't burn ids.
        public static void AssignFreshLogId(LogEntry __instance)
        {
            try
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                    logIdField(__instance) = NextLogId();
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] RealRuins AssignFreshLogId failed: {e.Message}");
            }
        }

        public static void HealZeroLogId(LogEntry __instance)
        {
            try
            {
                if (Scribe.mode == LoadSaveMode.PostLoadInit && logIdField(__instance) == 0)
                    logIdField(__instance) = NextLogId();
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] RealRuins HealZeroLogId failed: {e.Message}");
            }
        }

        public static void SanitizeRestoredPawn(Pawn __result)
        {
            try
            {
                var hediffs = __result?.health?.hediffSet?.hediffs;
                if (hediffs == null) return;
                var removed = 0;
                for (var i = hediffs.Count - 1; i >= 0; i--)
                {
                    var hd = hediffs[i];
                    if ((hd is Hediff_Injury || hd is Hediff_MissingPart) && hd.Part == null)
                    {
                        __result.health.RemoveHediff(hd);
                        removed++;
                        continue;
                    }
                    hd.loadID = Find.UniqueIDsManager.GetNextHediffID();
                }
                if (removed > 0)
                    Log.Message($"[LocalCSharpFixes] RealRuins: dropped {removed} part-less injur{(removed == 1 ? "y" : "ies")} from restored pawn {__result.LabelShort}");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] RealRuins SanitizeRestoredPawn failed: {e.Message}");
            }
        }
    }
}
