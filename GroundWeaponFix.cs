using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Some maps start with guns placed on them (Lava 3/5/6/7, Laser 7/8/9, Western 3/5/6/9/10,
    // Winter 15, Halloween 2/5/7/8/9/10/11, Xmas 12). Online, each game registers those guns by
    // position as the map loads, and the host sends everyone the network IDs for them as soon as
    // *its* map has loaded. If the other player's map is still loading when that message arrives
    // (likely the first time a map is played), their guns aren't registered yet, the IDs are thrown
    // away, and the guns never work properly on that player's game (pickups fail or grab the wrong
    // gun). Fix: keep IDs for guns that haven't loaded yet and apply them when the gun registers.
    [HarmonyPatch(typeof(MultiplayerManager))]
    internal static class GroundWeaponFix
    {
        private struct PendingWeapon
        {
            public Vector2 Position;
            public ushort WeaponSpawnId;
            public ushort SyncIndex;
        }

        private const float MatchDistance = 0.05f;

        private static readonly List<PendingWeapon> Pending = new List<PendingWeapon>();

        private static readonly AccessTools.FieldRef<MultiplayerManager, Dictionary<Vector2, WeaponPickUp>> PreSpawned =
            AccessTools.FieldRefAccess<MultiplayerManager, Dictionary<Vector2, WeaponPickUp>>("mTempPreSpawnedWeapons");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MultiplayerManager.OnGroundWeaponsInit))]
        private static bool OnGroundWeaponsInitPrefix(MultiplayerManager __instance, byte[] data)
        {
            if (!Plugin.FixGroundWeapons.Value)
            {
                return true;
            }
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
            {
                ushort count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    PendingWeapon weapon = new PendingWeapon
                    {
                        Position = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        WeaponSpawnId = reader.ReadUInt16(),
                        SyncIndex = reader.ReadUInt16(),
                    };
                    WeaponPickUp loaded = Find(__instance, weapon.Position);
                    if (loaded != null)
                    {
                        Init(__instance, loaded, weapon);
                    }
                    else
                    {
                        Pending.Add(weapon);
                    }
                }
            }
            if (Pending.Count > 0)
            {
                Plugin.Log.LogInfo($"{Pending.Count} placed gun(s) not loaded yet; registering them when they load.");
            }
            return false;
        }

        // A placed gun just loaded: apply its IDs if the host's list already came in.
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MultiplayerManager.AddPreSpawnedWeapon))]
        private static void AddPreSpawnedWeaponPostfix(MultiplayerManager __instance, Vector2 pos, WeaponPickUp weapon)
        {
            if (!Plugin.FixGroundWeapons.Value || Pending.Count == 0)
            {
                return;
            }
            int index = Pending.FindIndex(p => Vector2.Distance(p.Position, pos) <= MatchDistance);
            if (index >= 0)
            {
                PendingWeapon pending = Pending[index];
                Pending.RemoveAt(index);
                Init(__instance, weapon, pending);
            }
        }

        // Map change: IDs for the old map no longer apply.
        [HarmonyPostfix]
        [HarmonyPatch("ClearWeapons")]
        private static void ClearWeaponsPostfix()
        {
            Pending.Clear();
        }

        private static WeaponPickUp Find(MultiplayerManager manager, Vector2 position)
        {
            if (PreSpawned(manager).TryGetValue(position, out WeaponPickUp exact) && exact != null)
            {
                return exact;
            }
            foreach (KeyValuePair<Vector2, WeaponPickUp> entry in PreSpawned(manager))
            {
                if (entry.Value != null && Vector2.Distance(entry.Key, position) <= MatchDistance)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        // What vanilla OnGroundWeaponsInit does for each gun it finds.
        private static void Init(MultiplayerManager manager, WeaponPickUp weapon, PendingWeapon ids)
        {
            Object.Destroy(weapon.transform.GetComponent<NetworkSyncableObject>());
            manager.OnWeaponSpawned(weapon, ids.WeaponSpawnId, ids.SyncIndex);
        }
    }
}
