using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace LocalCSharpFixes
{
    // Vanilla Expanded Framework: its Notify_ApparelRemoved postfix drops a heavy weapon the pawn
    // can no longer hold (HeavyWeapon apparel link, e.g. a VFE-Pirates warcasket's weapon) at
    // pawn.Position. During pawn GENERATION the pawn is unspawned and Position is invalid, so any
    // ideo-precept apparel swap that removes the warcasket logs "X tried to drop ... at invalid
    // cell" (observed 2026-08-26: trade-ship mercenary stock generating a warcasketed relative,
    // Nytro). For an off-map pawn the drop is meaningless anyway: replicate the check and move
    // the weapon into the pawn's inventory instead. Spawned pawns keep VEF's original drop
    // behaviour untouched.
    // Their Postfix's parameter is named __instance (a reserved Harmony name), so ours binds it
    // by index (__0).
    // Signatures verified against the installed 1.6 DLLs:
    //   static void VEF.Weapons.VanillaExpandedFramework_Pawn_ApparelTracker_Notify_ApparelRemoved_Patch.Postfix(Pawn_ApparelTracker __instance)
    //   static bool VEF.Weapons.VanillaExpandedFramework_EquipmentUtility_CanEquip_Patch.CanEquip(Thing thing, Pawn pawn, HeavyWeapon options)
    //   VEF.Weapons.HeavyWeapon.isHeavy   (public bool field on the DefModExtension)
    //   bool Verse.Pawn_EquipmentTracker.TryTransferEquipmentToContainer(ThingWithComps eq, ThingOwner container)
    public static class VEFHeavyWeaponFixes
    {
        private static Type heavyWeaponType;
        private static FieldInfo isHeavyField;
        private static MethodInfo canEquip;

        public static void Apply(Harmony h)
        {
            var patchType = AccessTools.TypeByName("VEF.Weapons.VanillaExpandedFramework_Pawn_ApparelTracker_Notify_ApparelRemoved_Patch");
            if (patchType == null)
            {
                Log.Message("[LocalCSharpFixes] skip VEF heavy-weapon drop fix (mod not present)");
                return;
            }
            var postfix = AccessTools.Method(patchType, "Postfix");
            heavyWeaponType = AccessTools.TypeByName("VEF.Weapons.HeavyWeapon");
            isHeavyField = heavyWeaponType == null ? null : AccessTools.Field(heavyWeaponType, "isHeavy");
            var canEquipType = AccessTools.TypeByName("VEF.Weapons.VanillaExpandedFramework_EquipmentUtility_CanEquip_Patch");
            canEquip = canEquipType == null ? null : AccessTools.Method(canEquipType, "CanEquip");
            if (postfix == null || isHeavyField == null || canEquip == null)
            {
                Log.Warning("[LocalCSharpFixes] skip VEF heavy-weapon drop fix (member not found; mod layout changed?)");
                return;
            }
            h.Patch(postfix, prefix: new HarmonyMethod(typeof(VEFHeavyWeaponFixes), nameof(StashHeavyWeaponWhenUnspawned)));
        }

        public static bool StashHeavyWeaponWhenUnspawned(Pawn_ApparelTracker __0)
        {
            try
            {
                var pawn = __0?.pawn;
                if (pawn == null || pawn.Spawned)
                    return true; // on-map: VEF's drop is valid
                var primary = pawn.equipment?.Primary;
                if (primary == null)
                    return false; // original would no-op
                DefModExtension ext = null;
                var extensions = primary.def.modExtensions;
                if (extensions != null)
                    for (var i = 0; i < extensions.Count; i++)
                        if (heavyWeaponType.IsInstanceOfType(extensions[i])) { ext = extensions[i]; break; }
                if (ext == null || !(bool)isHeavyField.GetValue(ext))
                    return false;
                if ((bool)canEquip.Invoke(null, new object[] { primary, pawn, ext }))
                    return false; // still equippable; original would no-op
                if (pawn.inventory?.innerContainer != null)
                    pawn.equipment.TryTransferEquipmentToContainer(primary, pawn.inventory.innerContainer);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[LocalCSharpFixes] VEF StashHeavyWeaponWhenUnspawned failed: {e.Message}");
                return true;
            }
        }
    }
}
