using System;
using HarmonyLib;
using Verse;

namespace LocalCSharpFixes
{
    // Rimefeller: a MinifiedThing ticks the thing inside it (ThingOwner.DoTick -> ThingWithComps.Tick),
    // so an uninstalled pump/tank/refinery still runs its comp ticks -- but its comps were never
    // through PostSpawnSetup, so MapComp, storage, powerComp and friends are all null. CompFuelPump
    // .CompTick reads base.pipeNet, whose getter calls MapComp.RegenGrids() unguarded, and every
    // 60-tick interval throws "Exception ticking MinifiedThing... at Rimefeller.CompPipe.get_pipeNet"
    // (observed 2026-08-31). CompSiphon (storage), CompRefinery/CompCrudeCracker (powerComp) and
    // CompStorageTank (pipeNet) have the same shape, so guard the whole CompPipe family rather than
    // just the pump the log happened to catch.
    // A comp of an unspawned building has nothing to do anyway: skip CompTick until it's installed,
    // at which point PostSpawnSetup fills those fields in. Patches every CompTick DECLARED by a
    // CompPipe subclass, so an inherited tick is only patched once, on the type that defines it.
    public static class RimefellerFixes
    {
        public static void Apply(Harmony h)
        {
            try
            {
                var pipe = AccessTools.TypeByName("Rimefeller.CompPipe");
                if (pipe == null)
                {
                    Log.Message("[LocalCSharpFixes] skip Rimefeller minified-tick fix (mod not present)");
                    return;
                }
                var prefix = new HarmonyMethod(typeof(RimefellerFixes), nameof(SkipTickWhileUnspawned));
                int patched = 0;
                foreach (var t in pipe.Assembly.GetTypes())
                {
                    if (!pipe.IsAssignableFrom(t)) continue;
                    var m = AccessTools.DeclaredMethod(t, "CompTick");
                    if (m == null) continue;
                    h.Patch(m, prefix: prefix);
                    patched++;
                }
                if (patched == 0)
                    Log.Warning("[LocalCSharpFixes] skip Rimefeller minified-tick fix (no CompTick found; mod layout changed?)");
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] failed patching Rimefeller comp ticks: {e.Message}");
            }
        }

        public static bool SkipTickWhileUnspawned(ThingComp __instance)
            => __instance?.parent != null && __instance.parent.Spawned;
    }
}
