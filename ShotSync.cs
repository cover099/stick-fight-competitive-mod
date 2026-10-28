using System.IO;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace CompetitiveMod
{
    // Online, every gun shot (bullets, grenades, ...) is replayed on the other games from "projectile
    // packages" carried in the shooter's position updates. Those have three problems:
    //  - position updates are sent unreliably, so a lost packet loses its shots;
    //  - the last shot of a gun is fired, then the gun is dropped at once; the drop message usually
    //    arrives first, so the other games have no gun to fire it from and skip it (grenade launcher:
    //    the 5th grenade);
    //  - packages are only sent if the update goes out while the attack flag is set, otherwise they
    //    sit queued until the player next attacks and then fire late.
    // The shooter's game still deals the damage, so you get hit by shots you never saw.
    //
    // Fix: the shooter sends each shot right away as a reliable message (with the gun it came from);
    // receivers fire it even if the gun is already gone, and ignore the vanilla packages. Both
    // players must run the mod (it's built for modded 1v1s).
    internal static class ShotSync
    {
        private const int Channel = 43; // game uses 0-11, FallingRockSync 42
        private const byte Magic = 0xF8;

        private static readonly AccessTools.FieldRef<Fighting, Weapons> WeaponsRef =
            AccessTools.FieldRefAccess<Fighting, Weapons>("weapons");

        internal static bool Active => Plugin.FixShotSync.Value && MatchmakingHandler.IsNetworkMatch;

        internal static void Send(Fighting fighting, Vector3 position, Vector3 rotation, ushort syncIndex)
        {
            NetworkPlayer networkPlayer = fighting.GetComponent<NetworkPlayer>();
            MultiplayerManager manager = GameManager.Instance != null ? GameManager.Instance.mMultiplayerManager : null;
            if (networkPlayer == null || manager == null)
            {
                return;
            }
            // Same quantisation as the vanilla package.
            ShortVector2 pos = new ShortVector2(position);
            ByteVector2 vec = new ByteVector2(rotation);
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(networkPlayer.NetworkSpawnID);
                    writer.Write(fighting.CurrentWeaponIndex);
                    writer.Write(pos.X);
                    writer.Write(pos.Y);
                    writer.Write(vec.X);
                    writer.Write(vec.Y);
                    writer.Write(syncIndex);
                }
                data = stream.ToArray();
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
                if (!SteamNetworking.ReadP2PPacket(data, size, out uint read, out CSteamID _, Channel) || read < 12 || data[0] != Magic)
                {
                    continue;
                }
                ushort player;
                byte weaponIndex;
                ShortVector2 pos;
                ByteVector2 vec;
                ushort syncIndex;
                using (BinaryReader reader = new BinaryReader(new MemoryStream(data, 1, (int)read - 1)))
                {
                    player = reader.ReadUInt16();
                    weaponIndex = reader.ReadByte();
                    pos = new ShortVector2(reader.ReadInt16(), reader.ReadInt16());
                    vec = new ByteVector2(reader.ReadSByte(), reader.ReadSByte());
                    syncIndex = reader.ReadUInt16();
                }
                Controller shooter = FindPlayer(player);
                if (shooter == null || shooter.HasControl)
                {
                    continue;
                }
                Fire(shooter, weaponIndex, pos.ToVector3(), vec.ToVector3(), syncIndex);
            }
        }

        private static Controller FindPlayer(ushort spawnId)
        {
            MultiplayerManager manager = GameManager.Instance != null ? GameManager.Instance.mMultiplayerManager : null;
            if (manager == null || manager.PlayerControllers == null || spawnId >= manager.PlayerControllers.Count)
            {
                return null;
            }
            return manager.PlayerControllers[spawnId];
        }

        private static void Fire(Controller shooter, byte weaponIndex, Vector3 position, Vector3 vector, ushort syncIndex)
        {
            Fighting fighting = shooter.GetComponent<Fighting>();
            if (fighting == null || weaponIndex == 0)
            {
                return;
            }
            if (fighting.weapon != null && fighting.CurrentWeaponIndex == weaponIndex)
            {
                fighting.weapon.Shoot(null, networkForce: true, vector, position, syncIndex);
                return;
            }
            // The gun is already gone here (dropped after its last shot): spawn the projectile from
            // the gun's template, set up the way Weapon.ActuallyShoot does.
            Weapons weapons = WeaponsRef(fighting);
            if (weapons == null || weaponIndex > weapons.transform.childCount)
            {
                return;
            }
            Transform gun = weapons.transform.GetChild(weaponIndex - 1);
            Weapon template = gun.GetComponent<Weapon>();
            if (template == null || template.projectile == null)
            {
                return;
            }
            vector.x = 0f;
            if (vector == Vector3.zero)
            {
                return;
            }
            GameObject projectile = Object.Instantiate(template.projectile, position, Quaternion.LookRotation(vector));
            if (projectile.GetComponent<SnakeAI>() != null && syncIndex != ushort.MaxValue)
            {
                NetworkSyncableObject syncable = projectile.GetComponent<NetworkSyncableObject>();
                if (syncable != null)
                {
                    GameManager.Instance.mMultiplayerManager.AddSyncableObject(syncIndex, syncable);
                    syncable.InitNetworkIndex(syncIndex, syncRotation: false);
                }
            }
            projectile.transform.position = new Vector3(0f, projectile.transform.position.y, projectile.transform.position.z);
            foreach (RayCastForward ray in projectile.GetComponentsInChildren<RayCastForward>())
            {
                if (!ray.dontChangeMask)
                {
                    ray.mask = ~(1 << shooter.playerID + 8);
                }
            }
            foreach (ProjectileCollision collision in projectile.GetComponentsInChildren<ProjectileCollision>())
            {
                collision.damager = shooter;
            }
            foreach (SpawnObjectAfterDelay spawner in projectile.GetComponentsInChildren<SpawnObjectAfterDelay>())
            {
                spawner.damager = shooter;
            }
            foreach (Collider collider in projectile.GetComponentsInChildren<Collider>())
            {
                collider.gameObject.layer = gun.gameObject.layer;
            }
            foreach (DamagerHolder holder in projectile.GetComponentsInChildren<DamagerHolder>())
            {
                holder.damager = shooter;
            }
            projectile.AddComponent<TeamHolder>().team = shooter.playerID;
        }

    }

    // Shooter: every shot that becomes a projectile package is also sent reliably, right away.
    [HarmonyPatch(typeof(Fighting), nameof(Fighting.AddNewProjectilePackage))]
    internal static class ShotSyncSendPatch
    {
        private static void Postfix(Fighting __instance, Vector3 position, Vector3 rotation, ushort syncindex, Controller ___controller)
        {
            if (ShotSync.Active && ___controller != null && ___controller.HasControl)
            {
                ShotSync.Send(__instance, position, rotation, syncindex);
            }
        }
    }

    // Receiver: shots come from the reliable message; ignore the vanilla packages (still apply the
    // weapon switch they carry; with no packages vanilla would treat it as a melee swing).
    [HarmonyPatch(typeof(Fighting), nameof(Fighting.FirePackages))]
    internal static class ShotSyncReceivePatch
    {
        private static bool Prefix(Fighting __instance, ProjectilePackageStruct[] packages, byte weaponType)
        {
            if (!ShotSync.Active || packages == null || packages.Length == 0)
            {
                return true;
            }
            __instance.NetworkPickUpWeapon(weaponType);
            return false;
        }
    }
}
