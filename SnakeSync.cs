using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace CompetitiveMod
{
    // Online snakes (snake gun): the host picks each snake's target and sends it out; every game
    // simulates the snake's movement itself. Vanilla problems:
    //  - the shooter's game gives its snake a sync ID but never registers it, so on a non-host
    //    shooter's screen the host's target messages find no snake and it never chases anyone;
    //    the shooter's next snake also reuses the same ID and other games delete the earlier one;
    //  - target messages are unreliable, so a lost one leaves a snake chasing the wrong player;
    //  - snake positions are never synced, and every game drops a snake's target after 1 s on its
    //    own clock, so snakes drift apart and chase different players on different screens.
    // Fix: give snakes unique per-player IDs and register them on the shooter; resend targets
    // reliably; only the host decides when a snake drops its target; the host sends snake positions
    // 10 times a second and other games pull their snake towards them.
    internal static class SnakeSync
    {
        private const int Channel = 44; // game uses 0-11, FallingRockSync 42, ShotSync 43
        private const byte TargetMagic = 0xF9;
        private const byte StateMagic = 0xFA;
        private const float StateInterval = 0.1f;
        private const ushort IdBase = 60000; // vanilla allocates from the bottom up
        private const ushort IdsPerPlayer = 1000;

        internal static readonly HashSet<SnakeAI> Snakes = new HashSet<SnakeAI>();
        private static float lastStateSend;

        internal static bool Active => Plugin.FixSnakeSync.Value && MatchmakingHandler.IsNetworkMatch;

        private static MultiplayerManager Manager => GameManager.Instance != null ? GameManager.Instance.mMultiplayerManager : null;

        internal static ushort AllocateId(Controller shooter)
        {
            MultiplayerManager manager = Manager;
            NetworkPlayer networkPlayer = shooter != null ? shooter.GetComponent<NetworkPlayer>() : null;
            if (manager == null || networkPlayer == null)
            {
                return ushort.MaxValue;
            }
            int start = IdBase + (networkPlayer.NetworkSpawnID % 5) * IdsPerPlayer;
            for (int i = 0; i < IdsPerPlayer; i++)
            {
                ushort id = (ushort)(start + i);
                if (manager.GetSyncedObject(id) == null)
                {
                    return id;
                }
            }
            return ushort.MaxValue;
        }

        private static void SendToOthers(byte[] data, EP2PSend mode)
        {
            MultiplayerManager manager = Manager;
            if (manager == null)
            {
                return;
            }
            CSteamID self = SteamUser.GetSteamID();
            foreach (ConnectedClientData client in manager.ConnectedClients)
            {
                if (client != null && client.ClientID.IsValid() && client.ClientID != self)
                {
                    SteamNetworking.SendP2PPacket(client.ClientID, data, (uint)data.Length, mode, Channel);
                }
            }
        }

        internal static void SendTarget(ushort index, byte target)
        {
            SendToOthers(new byte[] { TargetMagic, (byte)index, (byte)(index >> 8), target }, EP2PSend.k_EP2PSendReliable);
        }

        // Host: periodic snake positions.
        internal static void Tick()
        {
            if (!Active || !MultiplayerManager.IsServer || Time.realtimeSinceStartup - lastStateSend < StateInterval)
            {
                return;
            }
            lastStateSend = Time.realtimeSinceStartup;
            Snakes.RemoveWhere(s => s == null);
            foreach (SnakeAI snake in Snakes)
            {
                NetworkSyncableObject sync = snake.GetComponent<NetworkSyncableObject>();
                if (sync == null || sync.Index == ushort.MaxValue || snake.mainRig == null)
                {
                    continue;
                }
                byte[] data;
                using (MemoryStream stream = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(StateMagic);
                        writer.Write(sync.Index);
                        Vector3 p = snake.mainRig.position;
                        Vector3 v = snake.mainRig.velocity;
                        writer.Write(p.y);
                        writer.Write(p.z);
                        writer.Write(v.y);
                        writer.Write(v.z);
                    }
                    data = stream.ToArray();
                }
                SendToOthers(data, EP2PSend.k_EP2PSendUnreliableNoDelay);
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
                if (!SteamNetworking.ReadP2PPacket(data, size, out uint read, out CSteamID _, Channel) || read < 4 || MultiplayerManager.IsServer)
                {
                    continue;
                }
                ushort index = (ushort)(data[1] | data[2] << 8);
                SnakeAI snake = FindSnake(index);
                if (snake == null)
                {
                    continue;
                }
                if (data[0] == TargetMagic)
                {
                    byte target = data[3];
                    List<Controller> players = Manager.PlayerControllers;
                    snake.NetworkForceNewTarget(target < players.Count ? players[target] : null);
                }
                else if (data[0] == StateMagic && read >= 19)
                {
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 3, (int)read - 3)))
                    {
                        Vector3 position = new Vector3(0f, reader.ReadSingle(), reader.ReadSingle());
                        Vector3 velocity = new Vector3(0f, reader.ReadSingle(), reader.ReadSingle());
                        Correct(snake, position, velocity);
                    }
                }
            }
        }

        private static SnakeAI FindSnake(ushort index)
        {
            MultiplayerManager manager = Manager;
            NetworkSyncableObject sync = manager != null ? manager.GetSyncedObject(index) : null;
            return sync != null ? sync.GetComponent<SnakeAI>() : null;
        }

        // Pull the whole snake towards the host's position and match its velocity.
        private static void Correct(SnakeAI snake, Vector3 hostPosition, Vector3 hostVelocity)
        {
            if (snake.mainRig == null)
            {
                return;
            }
            Vector3 local = snake.mainRig.position;
            hostPosition.x = local.x;
            Vector3 delta = hostPosition - local;
            // Far off (spawned late, big hit): snap. Otherwise close part of the gap each update.
            Vector3 shift = delta.magnitude > 3f ? delta : delta * 0.5f;
            HashSet<Rigidbody> moved = new HashSet<Rigidbody>();
            foreach (Rigidbody rig in snake.GetComponentsInChildren<Rigidbody>())
            {
                rig.position += shift;
                moved.Add(rig);
            }
            if (!moved.Contains(snake.mainRig))
            {
                snake.mainRig.position += shift;
            }
            hostVelocity.x = snake.mainRig.velocity.x;
            snake.mainRig.velocity = hostVelocity;
        }
    }

    // Shooter: give a snake a sync ID from this player's own range; vanilla then registers it
    // (it only does that when it's handed an ID).
    [HarmonyPatch(typeof(Weapon), "ActuallyShoot")]
    internal static class SnakeIdPatch
    {
        private static void Prefix(Weapon __instance, bool networkForce, ref ushort syncIndex, Controller ___controller)
        {
            if (!SnakeSync.Active || networkForce || syncIndex != ushort.MaxValue || __instance.projectile == null || __instance.projectile.GetComponent<SnakeAI>() == null)
            {
                return;
            }
            syncIndex = SnakeSync.AllocateId(___controller);
        }
    }

    [HarmonyPatch(typeof(SnakeAI))]
    internal static class SnakeAIPatch
    {
        private static readonly AccessTools.FieldRef<SnakeAI, float> SnakeCounter =
            AccessTools.FieldRefAccess<SnakeAI, float>("snakeCounter");

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void StartPostfix(SnakeAI __instance)
        {
            SnakeSync.Snakes.Add(__instance);
        }

        // Non-hosts: only the host decides when a snake drops its target (it tells everyone).
        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        private static void UpdatePrefix(SnakeAI __instance)
        {
            if (SnakeSync.Active && !MultiplayerManager.IsServer && __instance.GetComponent<NetworkSyncableObject>() != null)
            {
                SnakeCounter(__instance) = 0f;
            }
        }
    }

    // Host: resend every snake target the game sends, reliably.
    [HarmonyPatch(typeof(NetworkSyncableObject), nameof(NetworkSyncableObject.NewSnakeTarget))]
    internal static class SnakeTargetPatch
    {
        private static void Prefix(float ___mLastSnakeSendTime, out bool __state)
        {
            // Same throttle check vanilla uses to decide whether to send.
            __state = Time.time - ___mLastSnakeSendTime >= 0.5f;
        }

        private static void Postfix(NetworkSyncableObject __instance, byte targetPlayerIndex, bool __state)
        {
            if (__state && SnakeSync.Active && MultiplayerManager.IsServer && __instance.Index != ushort.MaxValue)
            {
                SnakeSync.SendTarget(__instance.Index, targetPlayerIndex);
            }
        }
    }
}
