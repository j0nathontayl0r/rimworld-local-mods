Fix15: missing ReGrowth 2 world-map hill textures
=================================================

ReGrowth 2 (ReGrowth.BOTR.Core, workshop 2260097569) has a WorldDrawLayer_Beautification that
asks MaterialPool.MatFrom for per-biome hill textures at
    WorldMaterials/BiomesKit/<Biome>/Hills/<Feature>[_<SnowLevel>]
but its own Textures/ tree is missing some rungs of that ladder, so world generation spams
    "Could not load Texture2D at 'WorldMaterials/BiomesKit/...' in any active mod or in base resources."
Observed 392 errors during a single new-world generation (381 of them the Grasslands one).

Stack: ReGrowthCore.WorldDrawLayer_Beautification+<Regenerate>d__2.MoveNext
    -> Verse.MaterialPool.MatFrom -> Verse.ContentFinder`1<Texture2D>.Get -> Log.Error
reached via RimWorld.Planet.WorldRenderer.RegenerateAllLayersNow from Page_CreateWorldParams.

The snow ladder ReGrowth uses is: (none) < _SemiSnowy < _Snowy < _VerySnowy < _FullySnowy.
Each biome only ships some rungs. Nothing in the entire load order ships a file named
Mountains_Snowy* for Grasslands - this is a gap in ReGrowth's own art, not a mod conflict.

LocalPatches loads last, and RimWorld's ContentFinder resolves textures by path across all mods,
so dropping files here SUPPLIES the missing paths. Nothing existing is overridden - every file
below is a path ReGrowth requests but does not provide.

  Biome       Requested (missing)          Substituted with (same biome)
  ----------  ---------------------------  ------------------------------
  Grasslands  Mountains_Snowy.png          Mountains_SemiSnowy.png    (381 errors)
  Grasslands  Impassable_Snowy.png         Impassable_SemiSnowy.png   (8 errors)
  IceSheet    SmallHills.png               SmallSnowpiles.png         (2 errors)
  Scarlands   Mountains_FullySnowy.png     Mountains_VerySnowy.png    (1 error)

COSMETIC CAVEAT: these are copies of the nearest available art, not new artwork. A "Snowy"
mountain will render with the "SemiSnowy" sprite, so those tiles look slightly less snowy than
intended. IceSheet SmallHills reuses SmallSnowpiles, which is that biome's own equivalent and is
the closest of the four. This trades a small visual inaccuracy for removing ~392 world-gen errors
and the missing materials that came with them.

If ReGrowth ever ships the real textures, DELETE this whole folder - otherwise these copies will
keep winning over the upstream art, since LocalPatches loads last.

Note: DefLoadCache's fingerprint hashes only Defs/, Patches/ and Assemblies/ (see its
src/Fingerprint/ModlistFingerprint.cs), so adding textures here does NOT invalidate the def cache.
