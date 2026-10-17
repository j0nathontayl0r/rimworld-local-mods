using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Replace_Stuff.NewThing;
using UnityEngine;
using Verse;

namespace Replace_Stuff
{
	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.GetWallAttachedTo), [typeof(IntVec3), typeof(Rot4), typeof(Map)])]
	public static class NoWallAttachment
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			// In the loop: foreach (Thing thing in c.GetThingList(map))
			// insert:
			// if(thing is ReplaceFrame) continue;

			// (This is probably redundant because the wall under the replace frame will probably always be checked first and returned.)


			// The first br should branch to the entry point for the loop, keep that label to continue to
			Label continueLabel = (Label)instructions.First(ci => ci.opcode == OpCodes.Br_S).operand;

			FieldInfo defInfo = AccessTools.Field(typeof(Thing), nameof(Thing.def));

			List<CodeInstruction> insts = instructions.ToList();
			for (int i = 0; i < insts.Count; i++)
			{
				CodeInstruction inst = insts[i];

				yield return inst;

				// Before we get the thing.def, use the thing:
				if (i + 1 < insts.Count && insts[i + 1].LoadsField(defInfo))
				{
					// stack has: Thing thing from the list
					yield return new CodeInstruction(OpCodes.Isinst, typeof(ReplaceFrame));// thing == typeof(ReplaceFrame)
					yield return new CodeInstruction(OpCodes.Brtrue_S, continueLabel);// if(thing == typeof(ReplaceFrame)) continue;


					// Call ldlocal for Thing again to replace what was there (with, maybe, no labels...)
					yield return new CodeInstruction(inst.opcode, inst.operand);
				}
			}
		}
	}


	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.GetAttachedBuildings))]
	public static class NoAttachedBuildings
	{
		private static List<Thing> emptyList = [];

		public static bool Prefix(Thing thing, ref List<Thing> __result)
		{
			if (thing is ReplaceFrame)
			{
				__result = emptyList;
				return false;
			}
			return true;
		}
	}

	[HarmonyPatch(typeof(Placeworker_AttachedToWall), nameof(Placeworker_AttachedToWall.AllowsPlacing))]
	public static class AllowAttachedWallReplacement
	{
		public static void Postfix(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, ref AcceptanceReport __result)
		{
			if (__result.Accepted || checkingDef is not ThingDef newDef || map == null)
				return;

			List<Thing> thingList = loc.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing oldThing = thingList[i];
				if (oldThing.Rotation != rot)
					continue;

				ThingDef oldBuiltDef = GenConstruct.BuiltDefOf(oldThing.def) as ThingDef;
				if (oldBuiltDef?.building?.isAttachment != true)
					continue;

				if (newDef.CanReplace(oldThing.def))
				{
					__result = AcceptanceReport.WasAccepted;
					return;
				}
			}
		}
	}

	[HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
	public static class AttachmentsCanWipeReplacementTargets
	{
		public static void Postfix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
		{
			if (__result || newEntDef is not ThingDef newDef || oldEntDef is not ThingDef oldDef)
				return;

			if ((!newDef.building?.isAttachment ?? true) || (!oldDef.building?.isAttachment ?? true))
				return;

			if (newDef.CanReplace(oldDef))
				__result = true;
		}
	}
}

