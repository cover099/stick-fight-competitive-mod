using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Online, a non-host only picks up a gun when their body touches their own copy of it, which
    // follows the host's copy with a delay. For the minigun, flamethrower and RPG that copy can slide
    // away (see SyncedObjectFix), so non-hosts could barely grab them. Fix: the host also grants the
    // pickup when it sees another player's body touch the real (host) copy of a gun, using the same
    // rules as a normal pickup. Only for players with empty hands: the host can't see their
    // auto-pickup setting, so swapping guns still goes through their own touch.
    [HarmonyPatch]
    internal static class HostPickupFix
    {
        private static readonly AccessTools.FieldRef<BodyPart, Controller> ParentController =
            AccessTools.FieldRefAccess<BodyPart, Controller>("mParentController");
        private static readonly AccessTools.FieldRef<BodyPart, CharacterInformation> Info =
            AccessTools.FieldRefAccess<BodyPart, CharacterInformation>("info");
        private static readonly AccessTools.FieldRef<MultiplayerManager, System.Collections.Generic.Dictionary<ushort, WeaponPickUp>> SpawnedWeapons =
            AccessTools.FieldRefAccess<MultiplayerManager, System.Collections.Generic.Dictionary<ushort, WeaponPickUp>>("mSpawnedWeapons");

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BodyPart), "OnCollisionEnter")]
        private static void TouchPostfix(BodyPart __instance, Collision collision)
        {
            if (!Plugin.FixHostPickup.Value || !MatchmakingHandler.IsNetworkMatch || !MultiplayerManager.IsServer || collision.rigidbody == null)
            {
                return;
            }
            WeaponPickUp weapon = collision.rigidbody.GetComponent<WeaponPickUp>();
            Controller player = ParentController(__instance);
            if (weapon == null || player == null || player.HasControl || player.canFly
                || collision.transform.root.GetComponent<Controller>() != null || (Info(__instance) != null && Info(__instance).isDead))
            {
                return;
            }
            Fighting fighting = player.GetComponent<Fighting>();
            NetworkPlayer networkPlayer = player.GetComponent<NetworkPlayer>();
            if (fighting == null || networkPlayer == null || (fighting.weapon != null && weapon.sendTheMovementAbility == -1))
            {
                return;
            }
            if (weapon.counter > 0.3f && weapon.cantBePickledUpFor < 0f && Time.time > weapon.CurrentDeadTime)
            {
                GameManager.Instance.mMultiplayerManager.RequestWeaponPickUp(weapon.NetworkSpawnIndex, (byte)networkPlayer.NetworkSpawnID);
                weapon.DelayBy(0.5f);
            }
        }

        // Vanilla bug: the host hands out a gun it already gave away (its list keeps the destroyed
        // gun) if a second request for it arrives, re-arming that player with a fresh copy.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.RequestWeaponPickUp))]
        private static bool AlreadyTakenPrefix(MultiplayerManager __instance, ushort weaponSpawnID)
        {
            if (!Plugin.FixHostPickup.Value || !MultiplayerManager.IsServer)
            {
                return true;
            }
            return !(SpawnedWeapons(__instance).TryGetValue(weaponSpawnID, out WeaponPickUp weapon) && weapon == null);
        }
    }
}
