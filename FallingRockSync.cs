using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace CompetitiveMod
{
    // Crumbling blocks (Castle 6/7, a few other Castle maps, many Xmas maps) are FallingRock: when something touches one, it shakes
    // and drops. Nothing about them is networked, so each game decides on its own which blocks were
    // touched. Remote players are always slightly off on your screen, so the games drop different
    // blocks and the maps desync. Fix: whenever a block starts falling on any game, tell everyone
    // else to drop the same block. A block falls for everyone if anyone touched it.
    // Needs every player in the lobby to run the mod.
    internal static class FallingRockSync
    {
        private const int Channel = 42; // game uses 0-11
        private const byte Magic = 0xF7;

        // Rock -> key that's the same on every machine: scene + position relative to the map root.
        private static readonly Dictionary<FallingRock, string> Keys = new Dictionary<FallingRock, string>();
        private static readonly Dictionary<string, FallingRock> Rocks = new Dictionary<string, FallingRock>();
        private static bool receiving;

        private static readonly AccessTools.FieldRef<FallingRock, bool> Done =
            AccessTools.FieldRefAccess<FallingRock, bool>("done");

        internal static void Register(FallingRock rock)
        {
            Transform root = rock.transform.root;
            Vector3 p = root.InverseTransformPoint(rock.transform.position);
            // A block can carry several FallingRock components; their order on the object is fixed.
            int component = System.Array.IndexOf(rock.GetComponents<FallingRock>(), rock);
            string key = rock.gameObject.scene.name + ":" + rock.name + ":" + Mathf.RoundToInt(p.x * 100f) + ":" + Mathf.RoundToInt(p.y * 100f) + ":" + Mathf.RoundToInt(p.z * 100f) + ":" + component;
            if (Rocks.TryGetValue(key, out FallingRock existing) && existing != null && existing != rock)
            {
                Plugin.Log.LogWarning($"Two falling blocks share the sync key {key}; only one will sync.");
                return;
            }
            Keys[rock] = key;
            Rocks[key] = rock;
        }

        internal static bool Active => Plugin.FixFallingBlocks.Value && MatchmakingHandler.IsNetworkMatch;

        internal static void OnLocalFall(FallingRock rock)
        {
            if (receiving || !Active || !Keys.TryGetValue(rock, out string key))
            {
                return;
            }
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(key);
                }
                data = stream.ToArray();
            }
            MultiplayerManager manager = GameManager.Instance != null ? GameManager.Instance.mMultiplayerManager : null;
            if (manager == null)
            {
                return;
            }
            CSteamID self = SteamUser.GetSteamID();
            foreach (ConnectedClientData client in manager.ConnectedClients)
            {
                if (client != null && client.ClientID.IsValid() && client.ClientID != self)
                {
                    SteamNetworking.SendP2PPacket(client.ClientID, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, Channel);
                }
            }
        }

        internal static void Poll()
        {
            if (!Active)
            {
                return;
            }
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, Channel))
            {
                byte[] data = new byte[size];
                if (!SteamNetworking.ReadP2PPacket(data, size, out uint read, out CSteamID _, Channel) || read < 2 || data[0] != Magic)
                {
                    continue;
                }
                string key;
                using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 1, (int)read - 1)))
                {
                    key = reader.ReadString();
                }
                if (Rocks.TryGetValue(key, out FallingRock rock) && rock != null && !Done(rock))
                {
                    receiving = true;
                    try
                    {
                        rock.Go();
                    }
                    finally
                    {
                        receiving = false;
                    }
                }
            }
        }

        internal static void Cleanup()
        {
            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, FallingRock> entry in Rocks)
            {
                if (entry.Value == null)
                {
                    dead.Add(entry.Key);
                }
            }
            foreach (string key in dead)
            {
                Rocks.Remove(key);
            }
            List<FallingRock> deadRocks = new List<FallingRock>();
            foreach (FallingRock rock in Keys.Keys)
            {
                if (rock == null)
                {
                    deadRocks.Add(rock);
                }
            }
            foreach (FallingRock rock in deadRocks)
            {
                Keys.Remove(rock);
            }
        }
    }

    [HarmonyPatch(typeof(FallingRock))]
    internal static class FallingRockPatch
    {
        private static readonly AccessTools.FieldRef<FallingRock, bool> Done =
            AccessTools.FieldRefAccess<FallingRock, bool>("done");

        // Start runs while the block is still where the map placed it.
        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void StartPostfix(FallingRock __instance)
        {
            FallingRockSync.Register(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(FallingRock.Go))]
        private static void GoPrefix(FallingRock __instance, out bool __state)
        {
            __state = Done(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(FallingRock.Go))]
        private static void GoPostfix(FallingRock __instance, bool __state)
        {
            if (!__state && Done(__instance))
            {
                FallingRockSync.OnLocalFall(__instance);
            }
        }
    }

    // Drop blocks of the old map from the lookup when a new map starts.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReviveAllPlayers))]
    internal static class FallingRockCleanupPatch
    {
        private static void Postfix(bool newMap)
        {
            if (newMap)
            {
                FallingRockSync.Cleanup();
            }
        }
    }
}
