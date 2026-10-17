using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using Replace_Stuff.NewThing;

namespace Replace_Stuff.Replace
{
	[StaticConstructorOnStartup]
	static class ReserveSharing
	{
		//When you reserve a repalce frame, actually reserve the thing it's replacing
		static ReserveSharing()
		{
			HarmonyMethod prefix = new HarmonyMethod(typeof(ReserveSharing), nameof(ReserveSharing.Prefix));
			Harmony harmony = new Harmony("Uuugggg.rimworld.Replace_Stuff.main");

			//HERE WE GO
			foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ReservationManager)))
			{
				if (method.GetParameters().Any(t => t.ParameterType == typeof(LocalTargetInfo)))
				{
					if (method.IsGenericMethod)
						harmony.Patch(method.MakeGenericMethod(new Type[] { typeof(JobDriver_TakeToBed) }), prefix, null);//JobDriver_TakeToBed seems to be the only one used;todo: foreach jobdriver subset
					else if (method.Name == nameof(ReservationManager.Reserve))
						harmony.Patch(method, new HarmonyMethod(typeof(ReserveSharing), nameof(ReserveSharing.ReservePrefix)), null);
					else
						harmony.Patch(method, prefix, null);
				}
			}
		}

		// LOCAL 1.7 PORT: a replacement frame's reservation goes to the old thing, which another pawn can hold
		// for a different job (e.g. refuelling a torch lamp that is being replaced). Vanilla then logs
		// "Could not reserve", but the job continues. Don't log an error for a redirected reservation.
		public static void ReservePrefix(ref LocalTargetInfo target, ref bool errorOnFailed)
		{
			LocalTargetInfo original = target;
			Prefix(ref target);
			if (target != original)
				errorOnFailed = false;
		}

		//public bool CanReserve(Pawn claimant, LocalTargetInfo target, int maxPawns = 1, int stackCount = -1, ReservationLayerDef layer = null, bool ignoreOtherReservations = false)
		public static void Prefix(ref LocalTargetInfo target)//Luckily each parameter is named 'target' or this wouldn't work
		{
			if (target.Thing is ReplaceFrame replaceFrame)
				target = replaceFrame.oldThing;
			else if (target.Thing is Frame frame && frame.IsNewThingReplacement(out Thing oldThing))
				target = oldThing;
		}
	}
}
