using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Vanilla pushes the ragdoll from Update (once per rendered frame, force * Time.deltaTime).
    // Forces only reach the physics engine on the next 60 Hz step, so below 60 FPS one step gets
    // all of it and the others get none, and spring forces (weapon hold, balance, turning) act on
    // stale positions. This driver runs that same code on every physics step instead, which is
    // exactly what vanilla does at a steady 60 FPS. Inside FixedUpdate, Time.deltaTime returns
    // the fixed step, so the original method bodies scale correctly without changes.
    internal class FixedStepDriver : MonoBehaviour
    {
        // While true, the gated originals below are allowed to run.
        internal static bool Driving;

        private static readonly Action<Controller> ApplyMovementState =
            AccessTools.MethodDelegate<Action<Controller>>(AccessTools.Method(typeof(Controller), "ApplyMovementState"));
        private static readonly Action<Rotation> RotationUpdate =
            AccessTools.MethodDelegate<Action<Rotation>>(AccessTools.Method(typeof(Rotation), "Update"));
        private static readonly Action<Balance> BalanceUpdate =
            AccessTools.MethodDelegate<Action<Balance>>(AccessTools.Method(typeof(Balance), "Update"));
        private static readonly Action<Fighting, Rigidbody, Vector3, float> HoldWeaponPosition =
            AccessTools.MethodDelegate<Action<Fighting, Rigidbody, Vector3, float>>(AccessTools.Method(typeof(Fighting), "HoldWeaponPosition"));
        private static readonly Action<Fighting, Rigidbody, Vector3, float> HoldWeaponRotation =
            AccessTools.MethodDelegate<Action<Fighting, Rigidbody, Vector3, float>>(AccessTools.Method(typeof(Fighting), "HoldWeaponRotation"));

        private static readonly AccessTools.FieldRef<Controller, bool> HasControl = AccessTools.FieldRefAccess<Controller, bool>("mHasControl");
        private static readonly AccessTools.FieldRef<Fighting, CharacterInformation> FightingInfo = AccessTools.FieldRefAccess<Fighting, CharacterInformation>("info");
        private static readonly AccessTools.FieldRef<Fighting, Transform> AimPosition = AccessTools.FieldRefAccess<Fighting, Transform>("aimPosition");
        private static readonly AccessTools.FieldRef<Fighting, Rigidbody> WeaponBody = AccessTools.FieldRefAccess<Fighting, Rigidbody>("weaponBody");
        private static readonly AccessTools.FieldRef<Fighting, Rigidbody> LeftHand = AccessTools.FieldRefAccess<Fighting, Rigidbody>("leftHand");
        private static readonly AccessTools.FieldRef<Fighting, Rigidbody> RightHand = AccessTools.FieldRefAccess<Fighting, Rigidbody>("rightHand");
        private static readonly AccessTools.FieldRef<Fighting, BlockHandler> FightingBlock = AccessTools.FieldRefAccess<Fighting, BlockHandler>("blockHandler");
        private static readonly AccessTools.FieldRef<Fighting, GrabHandler> FightingGrab = AccessTools.FieldRefAccess<Fighting, GrabHandler>("grabHandler");

        private Controller controller;
        private CharacterInformation info;
        private Rotation rotation;
        private Balance balance;
        private Fighting fighting;

        private void Start()
        {
            controller = GetComponent<Controller>();
            info = GetComponent<CharacterInformation>();
            rotation = GetComponent<Rotation>();
            balance = GetComponent<Balance>();
            fighting = GetComponent<Fighting>();
        }

        private void FixedUpdate()
        {
            Driving = true;
            try
            {
                if (Plugin.FixRemoteMovement.Value || Plugin.FixedStepMovement.Value)
                {
                    DriveMovement();
                }
                if (Plugin.FixedStepTurning.Value && rotation != null && rotation.enabled)
                {
                    RotationUpdate(rotation);
                }
                if (Plugin.FixedStepBalance.Value && balance != null && balance.enabled)
                {
                    BalanceUpdate(balance);
                }
                if (Plugin.FixedStepWeaponHold.Value && fighting != null && fighting.enabled)
                {
                    DriveWeaponHold();
                }
            }
            finally
            {
                Driving = false;
            }
        }

        // Mirrors the movement part of Controller.Update.
        private void DriveMovement()
        {
            if (controller.inactive || PauseManager.isPaused || info.isDead || (!GameManager.inFight && !GameManager.stillInMenu))
            {
                return;
            }
            if (!HasControl(controller))
            {
                if (Plugin.FixRemoteMovement.Value)
                {
                    ApplyMovementState(controller);
                }
                return;
            }
            if (!Plugin.FixedStepMovement.Value || controller.isAI || ChatManager.isTyping || controller.canFly || controller.PlayerActions == null)
            {
                return;
            }
            float x = controller.PlayerActions.Movement.X;
            if (x < -0.05f)
            {
                controller.Left();
            }
            if (x > 0.05f)
            {
                controller.Right();
            }
        }

        // Mirrors the weapon/hand-holding part of Fighting.Update.
        private void DriveWeaponHold()
        {
            CharacterInformation fInfo = FightingInfo(fighting);
            if (fInfo == null || fInfo.sinceFallen < 0f)
            {
                return;
            }
            Transform aim = AimPosition(fighting);
            if (!fighting.weapon)
            {
                if (FightingGrab(fighting).isGrabbing)
                {
                    HoldWeaponPosition(fighting, LeftHand(fighting), aim.position, 50000f);
                    HoldWeaponPosition(fighting, RightHand(fighting), aim.position, 50000f);
                }
                else if (FightingBlock(fighting).isBlocking)
                {
                    HoldWeaponPosition(fighting, LeftHand(fighting), aim.position - aim.forward * 0.2f, 50000f);
                    HoldWeaponPosition(fighting, RightHand(fighting), aim.position - aim.forward * 0.2f, 50000f);
                }
            }
            else
            {
                HoldWeaponPosition(fighting, WeaponBody(fighting), aim.position + aim.forward * 0.05f, fighting.weapon.positionForce);
                HoldWeaponRotation(fighting, WeaponBody(fighting), aim.forward, fighting.weapon.rotationForce);
            }
        }
    }

    [HarmonyPatch(typeof(Controller), "Start")]
    internal static class AttachDriverPatch
    {
        private static void Postfix(Controller __instance)
        {
            if (!__instance.inactive && __instance.GetComponent<FixedStepDriver>() == null)
            {
                __instance.gameObject.AddComponent<FixedStepDriver>();
            }
        }
    }

    // Remote players: vanilla re-applies the last network packet's movement flags every rendered
    // frame (wall-jump sideways kicks stack per frame), so how they move on your screen depends on
    // your FPS. The driver applies them once per physics step instead.
    [HarmonyPatch(typeof(Controller), "ApplyMovementState")]
    internal static class RemoteMovementPatch
    {
        private static bool Prefix()
        {
            return !Plugin.FixRemoteMovement.Value || FixedStepDriver.Driving;
        }
    }

    // Local player: swap the per-frame Left()/Right() calls in Controller.Update for ones that are
    // skipped when the driver is doing movement on the physics step. Network flags and facing
    // direction bookkeeping in Update are left untouched.
    [HarmonyPatch(typeof(Controller), "Update")]
    internal static class LocalMovementPatch
    {
        private static readonly MethodInfo Left = AccessTools.Method(typeof(Controller), nameof(Controller.Left));
        private static readonly MethodInfo Right = AccessTools.Method(typeof(Controller), nameof(Controller.Right));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo method)
                {
                    if (method == Left)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(LocalMovementPatch), nameof(FrameLeft));
                        replaced++;
                    }
                    else if (method == Right)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(LocalMovementPatch), nameof(FrameRight));
                        replaced++;
                    }
                }
                yield return instruction;
            }
            if (replaced != 2)
            {
                Plugin.Log.LogWarning($"Controller.Update: expected 2 Left/Right calls, replaced {replaced}");
            }
        }

        private static readonly AccessTools.FieldRef<Controller, Rotation> ControllerRotation = AccessTools.FieldRefAccess<Controller, Rotation>("rotation");
        private static readonly AccessTools.FieldRef<Controller, CharacterInformation> ControllerInfo = AccessTools.FieldRefAccess<Controller, CharacterInformation>("info");

        // Left()/Right() both push the body (moved to the physics step) and set per-frame state:
        // facing direction and paceState, which Update just reset to 0. StepController.Update only
        // alternates legs while paceState != 0, so that state must still be set every frame or the
        // character limps instead of walking.
        private static void FrameLeft(Controller controller)
        {
            if (!Plugin.FixedStepMovement.Value)
            {
                controller.Left();
                return;
            }
            ControllerRotation(controller).lookingRight = false;
            ControllerInfo(controller).paceState = 1;
        }

        private static void FrameRight(Controller controller)
        {
            if (!Plugin.FixedStepMovement.Value)
            {
                controller.Right();
                return;
            }
            ControllerRotation(controller).lookingRight = true;
            ControllerInfo(controller).paceState = 1;
        }
    }

    [HarmonyPatch]
    internal static class PerFramePhysicsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Rotation), "Update");
            yield return AccessTools.Method(typeof(Balance), "Update");
            yield return AccessTools.Method(typeof(Fighting), "HoldWeaponPosition");
            yield return AccessTools.Method(typeof(Fighting), "HoldWeaponRotation");
        }

        // Only objects that have a driver are moved to the physics step; anything else keeps vanilla behaviour.
        private static bool Prefix(Component __instance, MethodBase __originalMethod)
        {
            return !Enabled(__originalMethod) || FixedStepDriver.Driving || __instance.GetComponent<FixedStepDriver>() == null;
        }

        private static bool Enabled(MethodBase method)
        {
            if (method.DeclaringType == typeof(Rotation))
            {
                return Plugin.FixedStepTurning.Value;
            }
            if (method.DeclaringType == typeof(Balance))
            {
                return Plugin.FixedStepBalance.Value;
            }
            return Plugin.FixedStepWeaponHold.Value;
        }
    }
}
