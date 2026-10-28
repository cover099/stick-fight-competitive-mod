using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Quickdraw: shoot the moment you pick up a gun and it stays glitched (no recoil, straight
    // shots, no cooldown) for a couple of seconds. The cause is Fighting.GrabWeapon, which runs
    // every frame until your hand reaches the gun: it snaps the gun to your aim point, pulls the
    // hand towards it, and only latches the hand on once they're within 0.3. Shooting before that
    // lets the recoil throw the gun away from the hand on the next physics step, so the latch keeps
    // failing while you shoot.
    //
    // At 60 FPS every frame has one physics step, so every check comes right after the recoil has
    // moved the gun. At higher FPS most frames have no physics step: the check runs right after the
    // gun was snapped back next to the hand, the latch succeeds, and the glitch ends at once.
    // Fix: run GrabWeapon once per physics step (right after it), exactly as it runs at 60 FPS.
    [HarmonyPatch(typeof(Fighting), "SetWeapon")]
    internal static class QuickdrawFix
    {
        private static readonly MethodInfo VanillaGrab = AccessTools.Method(typeof(Fighting), "GrabWeapon");
        private static readonly System.Func<Fighting, IEnumerator> VanillaGrabCall =
            AccessTools.MethodDelegate<System.Func<Fighting, IEnumerator>>(VanillaGrab);
        private static readonly System.Func<Fighting, Rigidbody, Rigidbody, ConfigurableJoint> AttachHand =
            AccessTools.MethodDelegate<System.Func<Fighting, Rigidbody, Rigidbody, ConfigurableJoint>>(AccessTools.Method(typeof(Fighting), "AttachHand"));
        private static readonly AccessTools.FieldRef<Fighting, Rigidbody> RightHand =
            AccessTools.FieldRefAccess<Fighting, Rigidbody>("rightHand");
        private static readonly AccessTools.FieldRef<Fighting, Rigidbody> WeaponBody =
            AccessTools.FieldRefAccess<Fighting, Rigidbody>("weaponBody");
        private static readonly AccessTools.FieldRef<Fighting, Transform> AimPosition =
            AccessTools.FieldRefAccess<Fighting, Transform>("aimPosition");
        private static readonly AccessTools.FieldRef<Fighting, ConfigurableJoint> Joint1 =
            AccessTools.FieldRefAccess<Fighting, ConfigurableJoint>("joint1");

        private static readonly WaitForFixedUpdate AfterPhysicsStep = new WaitForFixedUpdate();

        // Swap `this.GrabWeapon()` in SetWeapon's StartCoroutine call for Grab(this).
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(typeof(QuickdrawFix), nameof(Grab));
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(VanillaGrab))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    replaced = true;
                    continue;
                }
                yield return instruction;
            }
            if (!replaced)
            {
                Plugin.Log.LogWarning("Quickdraw fix: GrabWeapon call not found in Fighting.SetWeapon; not applied.");
            }
        }

        private static IEnumerator Grab(Fighting fighting)
        {
            return Plugin.FixQuickdraw.Value ? FixedStepGrab(fighting) : VanillaGrabCall(fighting);
        }

        // Fighting.GrabWeapon, stepping once per physics step instead of once per frame.
        private static IEnumerator FixedStepGrab(Fighting fighting)
        {
            Rigidbody hand = RightHand(fighting);
            Transform aim = AimPosition(fighting);
            while (Vector3.Distance(hand.position, WeaponBody(fighting).position) > 0.3f && (bool)fighting.weapon)
            {
                fighting.weapon.transform.position = aim.position;
                fighting.weapon.transform.rotation = aim.rotation;
                hand.AddForce((WeaponBody(fighting).position - hand.transform.position) * 50000f * Time.fixedDeltaTime, ForceMode.Acceleration);
                yield return AfterPhysicsStep;
            }
            if ((bool)fighting.weapon)
            {
                Transform grip = fighting.weapon.transform.GetChild(0);
                hand.transform.position = grip.position;
                hand.transform.rotation = grip.rotation;
                Joint1(fighting) = AttachHand(fighting, hand, WeaponBody(fighting));
            }
        }
    }
}
