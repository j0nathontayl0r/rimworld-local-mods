using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Replace_Stuff.NewThing;
using Verse;

namespace Replace_Stuff
{
	//public override AcceptanceReport CanDesignateCell(IntVec3 c)
	[HarmonyPatch(typeof(Designator_Build), "CanDesignateCell")]
	static class DesignatorContext
	{
		public static bool designating;

		public static void Prefix(Designator_Build __instance)
		{
			designating = true;
		}
		public static void Postfix()
		{
			designating = false;
		}
	}


	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanReplace))]
	public static class CanReplaceAnyStuff
	{
		// Normal stuff replacement only here.
		// public static bool CanReplace(BuildableDef placing, BuildableDef existing, ThingDef placingStuff = null, ThingDef existingStuff = null)
		public static void Postfix(ref bool __result,  BuildableDef placing, BuildableDef existing, ThingDef placingStuff = null, ThingDef existingStuff = null)
		{
			// Only care to find new cases of true
			if (__result) return;

			if (placing is not ThingDef) return;

			if (!placing.MadeFromStuff) return;

			ThingDef placingDef = placing as ThingDef;
			ThingDef existingDef = existing as ThingDef;
			if (placingDef == null || existingDef == null)
				return;
			BuildableDef placingBuiltDef = placingDef.entityDefToBuild ?? placingDef;
			BuildableDef existingBuiltDef = existingDef.entityDefToBuild ?? existingDef;

			// Whether or not this an existing thing, blueprint or frame,
			// if it's the same builtDef and different stuff, so it can be replaced.
			__result = 
				placingBuiltDef == existingBuiltDef && 
				placingBuiltDef.MadeFromStuff && 
				placingStuff != existingStuff;
		}
	}

	// LOCAL 1.7 PORT: in 1.7 CanPlaceBlueprintAt only calls CanPlaceBlueprintAt_NewTemp, so the patch targets
	// _NewTemp. There the test is "thing.Rotation != rot" then continue (op_Inequality, not op_Equality).
	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp))]
	public static class CanPlaceBlueprintRotDoesntMatter
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo RotationEquals = AccessTools.Method(typeof(Rot4), "op_Equality");
			MethodInfo OrRotDoesntMatter = AccessTools.Method(typeof(CanPlaceBlueprintRotDoesntMatter), nameof(OrRotDoesntMatter));
			// LOCAL 1.7 PORT: the same rule for the op_Inequality form.
			MethodInfo RotationNotEquals = AccessTools.Method(typeof(Rot4), "op_Inequality");
			MethodInfo AndRotMatters = AccessTools.Method(typeof(CanPlaceBlueprintRotDoesntMatter), nameof(AndRotMatters));

			foreach (CodeInstruction i in instructions)
			{
				yield return i;
				if (i.Calls(RotationEquals))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//BuildableDef entDef
					yield return new CodeInstruction(OpCodes.Call, OrRotDoesntMatter);
				}
				else if (i.Calls(RotationNotEquals)) // LOCAL 1.7 PORT
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);//BuildableDef entDef
					yield return new CodeInstruction(OpCodes.Call, AndRotMatters);
				}
			}
		}

		public static bool OrRotDoesntMatter(bool result, BuildableDef entDef)
		{
			return result || PlacingRotationDoesntMatter(entDef);
		}

		// LOCAL 1.7 PORT: "rotation differs" counts only when the rotation matters (the negation of OrRotDoesntMatter).
		public static bool AndRotMatters(bool result, BuildableDef entDef)
		{
			return result && !PlacingRotationDoesntMatter(entDef);
		}

		public static bool PlacingRotationDoesntMatter(BuildableDef entDef)
		{
			return entDef is ThingDef def &&
				(!def.rotatable ||
				typeof(Building_Door).IsAssignableFrom(def.thingClass));
		}
	}
}