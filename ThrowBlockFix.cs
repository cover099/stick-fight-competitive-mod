using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Online, a thrown weapon only deals damage on the thrower's game. If you stop a throw by
    // shooting it, your bullet only reaches the thrower's game after a network delay, when the throw
    // has already flown on, so there it still hits you: you die to a throw you blocked on your
    // screen. (Blocking a throw with another throw mostly works because both weapons exist on every
    // game.) Fix: the target's own game decides whether a throw hit them, so whatever stopped it on
    // their screen counts. Both players must run the mod (it's built for modded 1v1s).
    [HarmonyPatch(typeof(WeaponPickUp), "OnCollisionEnter")]
    internal static class ThrowBlockFix
    {
        private static readonly AccessTools.FieldRef<WeaponPickUp, bool> CanDealDamage =
            AccessTools.FieldRefAccess<WeaponPickUp, bool>("canDealDamage");
        private static readonly AccessTools.FieldRef<WeaponPickUp, bool> IsThrown =
            AccessTools.FieldRefAccess<WeaponPickUp, bool>("isThrown");
        private static readonly AccessTools.FieldRef<WeaponPickUp, Controller> Damager =
            AccessTools.FieldRefAccess<WeaponPickUp, Controller>("damager");
        private static readonly System.Action<WeaponPickUp> EnableCollidersForThrower =
            AccessTools.MethodDelegate<System.Action<WeaponPickUp>>(AccessTools.Method(typeof(WeaponPickUp), "EnableCollidersForThrower"));

        private static bool Prefix(WeaponPickUp __instance, Collision collision)
        {
            if (!Plugin.FixThrowBlock.Value || !MatchmakingHandler.IsNetworkMatch || !collision.rigidbody
                || collision.relativeVelocity.magnitude <= 20f || !CanDealDamage(__instance))
            {
                return true;
            }
            BodyPart part = collision.rigidbody.GetComponent<BodyPart>();
            Controller thrower = Damager(__instance);
            Controller target = part != null ? part.transform.root.GetComponent<Controller>() : null;
            if (part == null || thrower == null || target == null || target == thrower)
            {
                return true; // not a hit on another player: vanilla
            }
            bool targetDecides;
            if (thrower.HasControl && !target.HasControl)
            {
                targetDecides = false; // our throw, their game decides: no damage here
            }
            else if (target.HasControl && !thrower.HasControl)
            {
                targetDecides = true; // their throw hit us on our screen: we apply it
            }
            else
            {
                return true; // vanilla rule
            }

            if (targetDecides)
            {
                Vector3 direction = -collision.relativeVelocity;
                part.TakeDamageWithParticle(55f, __instance.transform.position, direction, thrower, DamageType.LocalDamage);
                Rigidbody torso = target.GetComponentInChildren<Torso>().GetComponent<Rigidbody>();
                if (part.isGun)
                {
                    torso.GetComponent<BodyPart>().TakeDamageWithParticle(55f, __instance.transform.position, direction, thrower, DamageType.LocalDamage);
                }
                // Same knockback the thrower's game would send; it's our body, so apply it directly.
                collision.rigidbody.AddForce(-collision.relativeVelocity.normalized * 50f, ForceMode.VelocityChange);
                torso.AddForce(-collision.relativeVelocity.normalized * 80f, ForceMode.VelocityChange);
            }
            // Rest of vanilla OnCollisionEnter for a damaging hit.
            CanDealDamage(__instance) = false;
            EnableCollidersForThrower(__instance);
            IsThrown(__instance) = false;
            ScreenshakeHandler.Instance.AddShake(0.1f * -collision.relativeVelocity / 20f);
            return false;
        }
    }
}
