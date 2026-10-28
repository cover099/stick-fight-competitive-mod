using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Non-hosts don't simulate map objects synced by the host (moving platforms, crates); they get
    // ~5 position updates a second and slide towards each one. Vanilla sets the slide speed to
    // distance / time since the previous update arrived, and keeps sliding past the target until the
    // next update. Two updates arriving close together (any network hiccup, or the same frame) give
    // an enormous or infinite speed, so the object flies off screen; once it's below y = -50 the
    // game stops syncing it for the rest of the round (the platform "despawns" on Factory 1/3/4).
    // Fix: clamp the time between updates to a sane range and never slide past the target.
    [HarmonyPatch(typeof(NetworkSyncableObject))]
    internal static class SyncedObjectFix
    {
        private const float DefaultInterval = 0.2f; // host sends 5/s
        private const float MinInterval = 0.05f;
        private const float MaxInterval = 0.5f;

        private static readonly AccessTools.FieldRef<NetworkSyncableObject, Vector3> EndPos =
            AccessTools.FieldRefAccess<NetworkSyncableObject, Vector3>("m_EndPos");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, Vector3> DistanceToTravel =
            AccessTools.FieldRefAccess<NetworkSyncableObject, Vector3>("m_DistanceToTravel");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, float> PositionSpeed =
            AccessTools.FieldRefAccess<NetworkSyncableObject, float>("m_PositionSpeed");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, float> AngleToTravel =
            AccessTools.FieldRefAccess<NetworkSyncableObject, float>("m_AngleToTravel");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, float> RotationSpeed =
            AccessTools.FieldRefAccess<NetworkSyncableObject, float>("m_RotationSpeed");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, Vector3> TargetAngle =
            AccessTools.FieldRefAccess<NetworkSyncableObject, Vector3>("m_TargetAngle");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, bool> IsSnake =
            AccessTools.FieldRefAccess<NetworkSyncableObject, bool>("mIsSnake");
        private static readonly AccessTools.FieldRef<NetworkSyncableObject, bool> DontSyncPos =
            AccessTools.FieldRefAccess<NetworkSyncableObject, bool>("mDontSyncPos");

        // Instance ID -> real time the last position update arrived.
        private static readonly Dictionary<int, float> LastUpdate = new Dictionary<int, float>();

        // Dropped guns: the host's last reported position and its velocity between reports.
        private struct GunTrack
        {
            public Vector3 End;
            public Vector3 Velocity;
            public float Time;
        }

        private static readonly Dictionary<int, GunTrack> Guns = new Dictionary<int, GunTrack>();
        private const float MaxPrediction = 0.25f;
        private const float SnapDistance = 3f;
        private const float CorrectionRate = 8f;

        [HarmonyPostfix]
        [HarmonyPatch("SyncObjectState")]
        private static void SyncObjectStatePostfix(NetworkSyncableObject __instance)
        {
            if (!Plugin.FixSyncedObjects.Value || IsSnake(__instance) || DontSyncPos(__instance))
            {
                return;
            }
            int id = __instance.GetInstanceID();
            float now = Time.realtimeSinceStartup;
            float interval = LastUpdate.TryGetValue(id, out float last) ? Mathf.Clamp(now - last, MinInterval, MaxInterval) : DefaultInterval;
            if (LastUpdate.Count > 4096)
            {
                LastUpdate.Clear(); // drop entries of objects from previous maps
            }
            bool hadUpdate = LastUpdate.ContainsKey(id);
            LastUpdate[id] = now;
            PositionSpeed(__instance) = DistanceToTravel(__instance).magnitude / interval;
            RotationSpeed(__instance) = AngleToTravel(__instance) / interval;

            if (__instance.GetComponent<WeaponPickUp>() != null)
            {
                Vector3 end = EndPos(__instance);
                Vector3 velocity = Vector3.zero;
                bool knownVelocity = hadUpdate && Guns.ContainsKey(id);
                if (knownVelocity)
                {
                    velocity = (end - Guns[id].End) / interval;
                    velocity.x = 0f;
                }
                if (Guns.Count > 4096)
                {
                    Guns.Clear();
                }
                Guns[id] = new GunTrack { End = end, Velocity = velocity, Time = now };
                // Let local physics carry the gun the way the host's is moving until the next report.
                Rigidbody rig = __instance.GetComponent<Rigidbody>();
                if (knownVelocity && rig != null && !rig.isKinematic && Time.timeScale > 0.5f && IsFinite(velocity))
                {
                    // Reports are timed in real seconds, physics in game seconds.
                    rig.velocity = new Vector3(rig.velocity.x, velocity.y / Time.timeScale, velocity.z / Time.timeScale);
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("LerpLocalDummy")]
        private static bool LerpLocalDummyPrefix(NetworkSyncableObject __instance)
        {
            if (!Plugin.FixSyncedObjects.Value)
            {
                return true;
            }
            Transform t = __instance.transform;
            Vector3 end = EndPos(__instance);
            float positionSpeed = PositionSpeed(__instance);
            if (!IsFinite(end) || float.IsNaN(positionSpeed) || float.IsInfinity(positionSpeed))
            {
                return false;
            }
            float dt = Time.unscaledDeltaTime;
            if (__instance.GetComponent<WeaponPickUp>() != null)
            {
                FollowGun(__instance, t, dt);
                return false;
            }
            // Updates arrive in real time, so slide in real time; stop at the target.
            Vector3 target = new Vector3(t.position.x, end.y, end.z);
            t.position = Vector3.MoveTowards(t.position, target, positionSpeed * dt);

            Vector3 targetUp = TargetAngle(__instance);
            float rotationSpeed = RotationSpeed(__instance);
            if (targetUp != Vector3.zero && IsFinite(targetUp) && !float.IsNaN(rotationSpeed) && !float.IsInfinity(rotationSpeed))
            {
                Vector3 up = Vector3.RotateTowards(t.up, targetUp, rotationSpeed * Mathf.Deg2Rad * dt, 0f);
                t.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.right, up), up);
            }
            return false;
        }

        // Dropped guns fall and bounce under their own physics (velocity set from the host's motion
        // on each report). Here, gently pull them towards where the host's gun should be by now, and
        // match its tilt. Vanilla forced the rotation from the host's up vector alone, which could
        // flip a gun round; with an off-centre collider (minigun, flamethrower, RPG) that shoves it
        // into the ground and it slides away from a player trying to pick it up. Keep the facing
        // closest to the current one instead.
        private static void FollowGun(NetworkSyncableObject sync, Transform t, float dt)
        {
            if (!Guns.TryGetValue(sync.GetInstanceID(), out GunTrack gun) || !IsFinite(gun.End))
            {
                return;
            }
            float ahead = Mathf.Min(Time.realtimeSinceStartup - gun.Time, MaxPrediction);
            Vector3 predicted = gun.End + gun.Velocity * ahead;
            predicted.x = t.position.x;
            Vector3 error = predicted - t.position;
            t.position = error.magnitude > SnapDistance ? predicted : t.position + error * (1f - Mathf.Exp(-CorrectionRate * dt));

            Vector3 targetUp = TargetAngle(sync);
            if (targetUp.sqrMagnitude < 0.01f || !IsFinite(targetUp))
            {
                return;
            }
            Vector3 forward = Vector3.Cross(Vector3.right, targetUp);
            if (Vector3.Dot(forward, t.forward) < 0f)
            {
                forward = -forward;
            }
            Quaternion targetRotation = Quaternion.LookRotation(forward, targetUp);
            t.rotation = Quaternion.Slerp(t.rotation, targetRotation, 1f - Mathf.Exp(-CorrectionRate * dt));
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }
    }
}
