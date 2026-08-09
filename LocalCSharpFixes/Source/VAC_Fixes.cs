using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // Voice Acted Colonists (AlexisPopcorn.VAC.base, workshop 3306119571) ships two leftover log calls
    // that fire on extremely hot paths:
    //
    //   "TAKEDAMAGE start"
    //       Log.Message from VAC.HarmonyPatches.Thing_TakeDamage_Patch:Postfix - a debug print the
    //       author forgot to remove. Fires on EVERY damage event: plants ticking, fire, deterioration,
    //       explosions, every bullet impact. Measured 2026-08-09: 4,376 occurrences in ~25 minutes of
    //       play, 79% of all gameplay logging.
    //
    //   "Couldn't play sound because the assigned list was null!"
    //       Log.Warning from VAC.VAC_GameComponent:PlayRandomSoundFromType, on selection clicks, job
    //       starts and damage. 119 occurrences over the same window.
    //
    // Together ~81% of everything written to Player.log after a save loads.
    //
    // This is NOT just log tidiness. Verse.Log.Message calls StackTraceUtility.ExtractStackTrace
    // internally, so every one of those 4,376 calls walks the stack - paid precisely during combat.
    // A prefix returning false short-circuits before that work happens, so suppressing is cheaper than
    // letting it through, not merely quieter.
    //
    // Why not an XML patch: the string is a hardcoded literal compiled into VAC6.dll and emitted from a
    // Harmony postfix. PatchOperations only touch the merged Defs document; there is no def anywhere in
    // that call path. Checked for an escape hatch too - VAC has no settings file and its assembly holds
    // no debug/verbose toggle strings. C# is the only route.
    //
    // TRADEOFF: matching is on the exact message text, not on the caller. Verifying the call originated
    // in VAC would mean walking the stack in a prefix on Log.Message - the very cost we are removing.
    // Both strings are distinctive enough that a collision with another mod is not a real concern, but
    // if some other mod ever logs these exact strings they would be swallowed too.
    //
    // SIDE EFFECT: a prefix returning false also skips lower-priority prefixes from other mods on the
    // same method (Visual Exceptions and Dubs Analyzer both patch the Log family). That only applies to
    // these two exact strings, which are noise by definition.
    public static class VACLogSpamFixes
    {
        // Exact-match only. Deliberately not prefixes/Contains - narrow beats clever here.
        private static readonly HashSet<string> Suppressed = new HashSet<string>
        {
            "TAKEDAMAGE start",
            "Couldn't play sound because the assigned list was null!",
        };

        public static void Apply(Harmony h)
        {
            try
            {
                // Gate on VAC actually being loaded. If it is absent, Verse.Log is left completely
                // untouched rather than carrying a filter that can never match.
                var vacPatchType = AccessTools.TypeByName("VAC.HarmonyPatches.Thing_TakeDamage_Patch");
                if (vacPatchType == null)
                {
                    Log.Message("[LocalCSharpFixes] skip VAC log filter (Voice Acted Colonists not present)");
                    return;
                }

                // Compile-time-verified targets. Resolving these by name would silently no-op if Ludeon
                // ever changed the signature; binding through a delegate means that becomes a BUILD
                // failure instead, which is the failure mode we want after the Fix14 silent-rot lesson.
                var message = new Action<string>(Log.Message).Method;
                var warning = new Action<string>(Log.Warning).Method;

                var prefix = new HarmonyMethod(typeof(VACLogSpamFixes), nameof(SuppressPrefix));
                h.Patch(message, prefix: prefix);
                h.Patch(warning, prefix: prefix);

                Log.Message($"[LocalCSharpFixes] VAC log filter installed ({Suppressed.Count} message(s) suppressed).");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] VAC log filter failed to apply: {e.Message}");
            }
        }

        // __0 rather than a named parameter: index-based injection is immune to Ludeon renaming the
        // argument, which would otherwise throw at patch time and disable this whole fix.
        // Runs on every Log.Message/Log.Warning in the game, so it stays a single hash lookup.
        public static bool SuppressPrefix(string __0)
        {
            return __0 == null || !Suppressed.Contains(__0);
        }
    }
}
