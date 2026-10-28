using System.IO;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Diagnostics for online weapon pickups (non-hosts struggling to grab big weapons). Logs, to
    // LogOutput.log: every time your own player touches a weapon (with the values that decide
    // whether a pickup request is sent), every request, how the host answers, and the pickups that
    // come back. Enable with [Debug] PickupLog.
    [HarmonyPatch]
    internal static class PickupLog
    {
        private static readonly AccessTools.FieldRef<BodyPart, Controller> ParentController =
            AccessTools.FieldRefAccess<BodyPart, Controller>("mParentController");
        private static readonly AccessTools.FieldRef<MultiplayerManager, System.Collections.Generic.Dictionary<ushort, WeaponPickUp>> SpawnedWeapons =
            AccessTools.FieldRefAccess<MultiplayerManager, System.Collections.Generic.Dictionary<ushort, WeaponPickUp>>("mSpawnedWeapons");

        private static bool Active => Plugin.PickupLog.Value && MatchmakingHandler.IsNetworkMatch;

        private static string Role => MultiplayerManager.IsServer ? "host" : "client";

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BodyPart), "OnCollisionEnter")]
        private static void TouchPrefix(BodyPart __instance, Collision collision)
        {
            if (!Active || collision.rigidbody == null)
            {
                return;
            }
            WeaponPickUp weapon = collision.rigidbody.GetComponent<WeaponPickUp>();
            Controller owner = ParentController(__instance);
            if (weapon == null || owner == null || !owner.HasControl)
            {
                return;
            }
            Fighting fighting = owner.GetComponent<Fighting>();
            Rigidbody rig = collision.rigidbody;
            Plugin.Log.LogInfo($"[Pickup/{Role}] touch {weapon.name} spawnId={weapon.NetworkSpawnIndex} part={__instance.name} "
                + $"counter={weapon.counter:F2}(>0.3) cantPickUpFor={weapon.cantBePickledUpFor:F2}(<0) deadFor={weapon.CurrentDeadTime - Time.time:F2}(<0) "
                + $"holding={(fighting != null && fighting.weapon != null ? fighting.weapon.name : "none")} autoPickup={OptionsHolder.pickup} "
                + $"kinematic={rig.isKinematic} speed={rig.velocity.magnitude:F1} pos={rig.position.y:F2},{rig.position.z:F2}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.RequestWeaponPickUp))]
        private static void RequestPrefix(MultiplayerManager __instance, ushort weaponSpawnID, byte Index)
        {
            if (!Active)
            {
                return;
            }
            SpawnedWeapons(__instance).TryGetValue(weaponSpawnID, out WeaponPickUp weapon);
            Plugin.Log.LogInfo($"[Pickup/{Role}] request spawnId={weaponSpawnID} player={Index} known={(weapon != null ? weapon.name : "NO")}");
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.OnPlayerRequestingWeaponPickUp))]
        private static void HostAnswerPrefix(MultiplayerManager __instance, byte[] data)
        {
            if (!Active)
            {
                return;
            }
            Read(data, out byte player, out ushort id);
            bool inList = SpawnedWeapons(__instance).TryGetValue(id, out WeaponPickUp weapon);
            string result = !inList ? "REJECTED (unknown id)" : weapon == null ? "REJECTED (host's copy is gone)" : "accepted";
            Plugin.Log.LogInfo($"[Pickup/{Role}] got request from player={player} spawnId={id}: {result}"
                + (weapon != null ? $" hostPos={weapon.transform.position.y:F2},{weapon.transform.position.z:F2}" : string.Empty));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.OnWeaponWasPickedUp))]
        private static void PickedUpPrefix(MultiplayerManager __instance, byte[] data)
        {
            if (!Active)
            {
                return;
            }
            Read(data, out byte player, out ushort id);
            SpawnedWeapons(__instance).TryGetValue(id, out WeaponPickUp weapon);
            Plugin.Log.LogInfo($"[Pickup/{Role}] picked up: player={player} spawnId={id} weapon={(weapon != null ? weapon.name : "NOT FOUND")}");
        }

        private static float lastNearbyLog;

        // Every 0.5 s: guns within 3 units of your own player, and how close your body gets to them.
        internal static void LogNearby()
        {
            if (!Active || Time.realtimeSinceStartup - lastNearbyLog < 0.5f || ControllerHandler.Instance == null)
            {
                return;
            }
            lastNearbyLog = Time.realtimeSinceStartup;
            foreach (Controller player in ControllerHandler.Instance.players)
            {
                if (player == null || !player.HasControl)
                {
                    continue;
                }
                Hip hip = player.GetComponentInChildren<Hip>();
                if (hip == null)
                {
                    continue;
                }
                Collider[] body = player.GetComponentsInChildren<Collider>();
                foreach (WeaponPickUp weapon in Object.FindObjectsOfType<WeaponPickUp>())
                {
                    if (Vector3.Distance(weapon.transform.position, hip.transform.position) > 3f)
                    {
                        continue;
                    }
                    Collider[] gun = weapon.GetComponentsInChildren<Collider>(true);
                    int enabled = 0;
                    string layers = string.Empty;
                    float closest = float.MaxValue;
                    foreach (Collider g in gun)
                    {
                        if (g.enabled)
                        {
                            enabled++;
                        }
                        layers += g.gameObject.layer + (g.isTrigger ? "t " : " ");
                        foreach (Collider b in body)
                        {
                            closest = Mathf.Min(closest, Vector3.Distance(b.bounds.ClosestPoint(g.bounds.center), g.bounds.ClosestPoint(b.bounds.center)));
                        }
                    }
                    Rigidbody rig = weapon.GetComponent<Rigidbody>();
                    Plugin.Log.LogInfo($"[Pickup/{Role}] near {weapon.name} spawnId={weapon.NetworkSpawnIndex} bodyGap={closest:F2} "
                        + $"colliders={enabled}/{gun.Length} layers={layers}myLayer={hip.gameObject.layer} "
                        + $"kinematic={(rig != null && rig.isKinematic)} pos={weapon.transform.position.y:F2},{weapon.transform.position.z:F2} "
                        + $"counter={weapon.counter:F2} cantPickUpFor={weapon.cantBePickledUpFor:F2}");
                }
            }
        }

        private static void Read(byte[] data, out byte player, out ushort id)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
            {
                player = reader.ReadByte();
                id = reader.ReadUInt16();
            }
        }
    }
}
