using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Between maps, GameManager.MovePlayer freezes each player's ragdoll (kinematic) and slides the
    // whole thing to the new spawn point, so whatever pose they had (legs out, upside down) and their
    // velocity carry into the next round. Record each player's standing pose at spawn, snap back to
    // it when the transition starts, and zero all velocity when the round starts.
    internal class StandingPose : MonoBehaviour
    {
        private struct Part
        {
            public Rigidbody Rig;
            public Vector3 OffsetFromHip;
            public Quaternion Rotation;
        }

        private readonly List<Part> parts = new List<Part>();
        private Rigidbody hip;

        // Controller.Start runs before the player's first physics step, so the ragdoll is still
        // in the prefab's standing pose.
        internal void Capture(Controller controller)
        {
            Hip hipComponent = controller.GetComponentInChildren<Hip>();
            hip = hipComponent != null ? hipComponent.GetComponent<Rigidbody>() : null;
            if (hip == null)
            {
                return;
            }
            parts.Clear();
            foreach (Rigidbody rig in controller.GetComponentsInChildren<Rigidbody>())
            {
                parts.Add(new Part
                {
                    Rig = rig,
                    OffsetFromHip = rig.transform.position - hip.transform.position,
                    Rotation = rig.transform.rotation,
                });
            }
        }

        // Upright, standing pose around the hip's current position.
        internal void Restore()
        {
            if (hip == null)
            {
                return;
            }
            Vector3 hipPosition = hip.transform.position;
            foreach (Part part in parts)
            {
                if (part.Rig == null)
                {
                    continue;
                }
                part.Rig.transform.position = hipPosition + part.OffsetFromHip;
                part.Rig.transform.rotation = part.Rotation;
            }
            ClearVelocity();
        }

        internal void ClearVelocity()
        {
            foreach (Part part in parts)
            {
                if (part.Rig != null && !part.Rig.isKinematic)
                {
                    part.Rig.velocity = Vector3.zero;
                    part.Rig.angularVelocity = Vector3.zero;
                }
            }
        }
    }

    [HarmonyPatch(typeof(Controller), "Start")]
    internal static class CapturePosePatch
    {
        private static void Postfix(Controller __instance, bool ___inactive)
        {
            if (___inactive)
            {
                return;
            }
            StandingPose pose = __instance.GetComponent<StandingPose>() ?? __instance.gameObject.AddComponent<StandingPose>();
            pose.Capture(__instance);
        }
    }

    // SetCollision(false) is only called by MovePlayer, right after it freezes the ragdoll for the
    // trip to the new map, so the player travels and arrives in the standing pose.
    [HarmonyPatch(typeof(Controller), nameof(Controller.SetCollision))]
    internal static class ResetPoseOnMapChangePatch
    {
        private static void Postfix(Controller __instance, bool enabled)
        {
            if (!Plugin.FixPoseCarryOver.Value || enabled)
            {
                return;
            }
            StandingPose pose = __instance.GetComponent<StandingPose>();
            if (pose != null)
            {
                pose.Restore();
            }
        }
    }

    // The ragdoll is unfrozen just before ReviveAllPlayers; start the round from rest.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReviveAllPlayers))]
    internal static class ClearVelocityOnNewMapPatch
    {
        private static void Postfix(bool newMap)
        {
            if (!Plugin.FixPoseCarryOver.Value || !newMap || ControllerHandler.Instance == null)
            {
                return;
            }
            foreach (Controller player in ControllerHandler.Instance.players)
            {
                StandingPose pose = player != null ? player.GetComponent<StandingPose>() : null;
                if (pose != null)
                {
                    pose.ClearVelocity();
                }
            }
        }
    }
}
