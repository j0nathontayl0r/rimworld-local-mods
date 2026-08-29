using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LocalCSharpFixes
{
    // ReGrowth: Expanded World Generation (Continued) builds the create-world preview on a BACKGROUND
    // thread (Page_CreateWorldParams_DoWindowContents.generateWorld): it assigns Current.CreatingWorld
    // and then runs the world gen steps against it. Find.World falls back to Current.CreatingWorld
    // whenever Current.Game.World is null -- which that thread nulls at the start of generation -- so
    // the MAIN thread renders the half-built world every frame while the worker is still adding world
    // objects to it:
    //   Root_Entry.Update -> World.WorldUpdate -> WorldRenderer.DrawWorldLayers ->
    //     WorldDrawLayer_WorldObjects.Regenerate      (iterates WorldObjects.AllWorldObjectsOnLayer)
    //   Root.OnGUI -> WorldInterface.WorldInterfaceOnGUI ->
    //     ExpandableLandmarksUtility.ExpandableLandmarksOnGUI  (iterates a static cache that
    //     WorldObjectsHolder.AddToCache clears -- from the worker thread)
    // Both throw "Collection was modified; enumeration operation may not execute", dozens per preview
    // (observed 2026-08-29). ReGrowth's own WorldDrawLayerBase.RegenerateNow guard is inert here:
    // startRefreshWorldPreview sets dirty = false immediately before starting the thread, so that
    // guard's `dirty` check is false for the whole generation window; nothing guards the 1.6 landmark
    // path at all.
    // Fix: don't touch the world from the main thread while the worker owns it, keyed off ReGrowth's
    // own generatingWorld flag (which it already uses exactly this way for Rand.EnsureStateStackEmpty).
    // The mod regenerates the draw layers and re-takes the preview snapshot in initializeWorld() once
    // the thread finishes, so the skipped frames cost nothing. The extra Page_CreateWorldParams check
    // keeps this fail-safe: if the flag ever stuck true (aborted thread), only the world-params page
    // would stop rendering, never an actual game.
    // Signatures verified against the installed 1.6 DLLs:
    //   public static bool RGExpandedWorldGeneration.Page_CreateWorldParams_DoWindowContents.generatingWorld
    //   public void RimWorld.Planet.World.WorldUpdate()
    //   public void RimWorld.WorldInterface.WorldInterfaceOnGUI()
    public static class ReGrowthWorldGenFixes
    {
        private static AccessTools.FieldRef<bool> generatingWorld;

        public static void Apply(Harmony h)
        {
            var t = AccessTools.TypeByName("RGExpandedWorldGeneration.Page_CreateWorldParams_DoWindowContents");
            if (t == null)
            {
                Log.Message("[LocalCSharpFixes] skip ReGrowth world-gen race fix (mod not present)");
                return;
            }
            var field = AccessTools.Field(t, "generatingWorld");
            if (field == null || !field.IsStatic || field.FieldType != typeof(bool))
            {
                Log.Warning("[LocalCSharpFixes] skip ReGrowth world-gen race fix (generatingWorld not found; mod layout changed?)");
                return;
            }
            try { generatingWorld = AccessTools.StaticFieldRefAccess<bool>(field); }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] skip ReGrowth world-gen race fix ({e.Message})");
                return;
            }

            var prefix = new HarmonyMethod(typeof(ReGrowthWorldGenFixes), nameof(SkipWhilePreviewGenerating));
            h.Patch(AccessTools.Method(typeof(World), nameof(World.WorldUpdate)), prefix: prefix);
            h.Patch(AccessTools.Method(typeof(WorldInterface), nameof(WorldInterface.WorldInterfaceOnGUI)), prefix: prefix);
        }

        public static bool SkipWhilePreviewGenerating()
        {
            try
            {
                if (!generatingWorld())
                    return true;
                return Find.WindowStack?.WindowOfType<Page_CreateWorldParams>() == null;
            }
            catch
            {
                return true; // never block rendering because of this guard
            }
        }
    }
}
