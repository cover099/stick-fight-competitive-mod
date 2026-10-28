using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "competitivemod.stickfight";
        public const string Name = "CompetitiveMod";
        public const string Version = "0.25.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> MasterEnabled;
        internal static ModSetting FixNetworkSendRate;
        internal static ModSetting FixFullAutoFireRate;
        internal static ModSetting FixThrowAim;
        internal static ModSetting FixThrowWhiff;
        internal static ModSetting FixStunCarryOver;
        internal static ModSetting FixPoseCarryOver;
        internal static ModSetting FixDraws;
        internal static ModSetting FixRoundStart;
        internal static ModSetting AllowAllMapsOnline;
        internal static ModSetting FixSyncedObjects;
        internal static ModSetting FixFallingBlocks;
        internal static ModSetting FixShotSync;
        internal static ModSetting FixSnakeSync;
        internal static ConfigEntry<bool> PickupLog;
        internal static ModSetting FixQuickdraw;
        internal static ModSetting FixThrowBlock;
        internal static ModSetting FixGroundWeapons;
        internal static ModSetting FixHostPickup;
        internal static ModSetting FixLobbyWin;
        internal static ModSetting FixJoinTimeout;
        internal static ModSetting LockGameSettings;
        internal static ModSetting LockMaps;
        internal static ModSetting LockWeapons;
        internal static ModSetting FixRemoteMovement;
        internal static ModSetting FixedStepMovement;
        internal static ModSetting FixedStepTurning;
        internal static ModSetting FixedStepBalance;
        internal static ModSetting FixedStepWeaponHold;

        private void Awake()
        {
            Log = Logger;
            MasterEnabled = Config.Bind("General", "Enabled", false,
                "Master switch for the whole mod (every fix and lock below). Also set by the COMP MOD lever on the first screen and the /comp chat command; online, the host's setting applies to everyone.");
            FixNetworkSendRate = new ModSetting(Config.Bind("Fixes", "NetworkSendRate", true,
                "Send your position updates at the intended 50/s regardless of FPS (vanilla: 30/s at 60 FPS, 48/s at 144-240 FPS)."));
            FixFullAutoFireRate = new ModSetting(Config.Bind("Fixes", "FullAutoFireRate", true,
                "Full-auto weapons fire at their real cooldown instead of being rounded up to the next frame (vanilla: slower at lower FPS)."));
            FixThrowAim = new ModSetting(Config.Bind("Fixes", "ThrowAim", true,
                "Throw weapons in the direction you are aiming this frame (vanilla can use last frame's aim; worse at lower FPS)."));
            FixThrowWhiff = new ModSetting(Config.Bind("Fixes", "ThrowWhiff", true,
                "Stop thrown weapons from counting as landed before they've launched (vanilla: above 60 FPS throws often lose their damage and get blocked by your own arm, dropping in front of you)."));
            FixStunCarryOver = new ModSetting(Config.Bind("Fixes", "StunCarryOver", true,
                "Clear the stun from punching into a block when the next map starts (vanilla: it carries over and you can't attack or block at the start of the round)."));
            FixPoseCarryOver = new ModSetting(Config.Bind("Fixes", "PoseCarryOver", true,
                "Start every map standing upright and at rest (vanilla: you keep your pose from the end of the last map, e.g. upside down or legs out, plus any leftover spin)."));
            FixDraws = new ModSetting(Config.Bind("Fixes", "Draws", true,
                "If the last player alive also dies before the next map, it's a draw: nobody gets the win (vanilla: they get 2 wins and the next map breaks)."));
            FixRoundStart = new ModSetting(Config.Bind("Fixes", "RoundStart", true,
                "Online, give control the moment the countdown ends (vanilla: ~2 frames later, so 60 FPS players start ~30 ms after 400 FPS players)."));
            AllowAllMapsOnline = new ModSetting(Config.Bind("Fixes", "AllowAllMapsOnline", true,
                "Let the host pick maps the game only allows offline (Desert 2, 7, 8, Factory 5, Winter 13) when they're on in the map list."));
            FixSyncedObjects = new ModSetting(Config.Bind("Fixes", "SyncedObjects", true,
                "For non-hosts, stop host-synced map objects (moving platforms, crates) from flying off and vanishing when two updates arrive close together (vanilla: the Factory 1/3/4 platform often disappears)."));
            FixFallingBlocks = new ModSetting(Config.Bind("Fixes", "FallingBlocks", true,
                "Online, when a crumbling block (Castle 6/7) falls on anyone's game, make it fall for everyone (vanilla: each game decides on its own, so the maps desync). Every player needs the mod."));
            FixShotSync = new ModSetting(Config.Bind("Fixes", "ShotSync", true,
                "Online, send every gun shot reliably so it always shows up on other players' screens, including a gun's last shot (vanilla: grenades and bullets often go missing for the other player but still hit). Both players need it on."));
            FixSnakeSync = new ModSetting(Config.Bind("Fixes", "SnakeSync", true,
                "Online, keep snakes chasing the same player in the same place on every screen (vanilla: a non-host's snakes never chase on their own screen, extra snakes get deleted for others, and snakes drift apart). Host and players need the mod."));
            FixQuickdraw = new ModSetting(Config.Bind("Fixes", "Quickdraw", true,
                "Make quickdraws (shooting the moment you pick up a gun) work the same at any FPS as at 60 FPS (vanilla: the glitch ends almost immediately at high FPS)."));
            FixThrowBlock = new ModSetting(Config.Bind("Fixes", "ThrowBlock", true,
                "Online, the target's own game decides whether a thrown weapon hit them, so a throw you stop by shooting it can't kill you on the thrower's screen (vanilla: you 'die to nothing'). Both players need it on."));
            FixGroundWeapons = new ModSetting(Config.Bind("Fixes", "PlacedGuns", true,
                "Online, make guns placed on the map (Lava 3/5/6/7, Laser 7/8/9, Western 3/5/6/9/10, Winter 15, Halloween 2/5/7/8/9/10/11, Xmas 12) work for a player whose map finished loading after the host's."));
            FixHostPickup = new ModSetting(Config.Bind("Fixes", "HostSidePickup", true,
                "Online (host), also give a gun to another player when their body touches the host's copy of it, so non-hosts can pick up the minigun, flamethrower and RPG (vanilla: their copy slides away and they can barely grab them)."));
            FixLobbyWin = new ModSetting(Config.Bind("Fixes", "NoLobbyWin", true,
                "Don't count the lobby round (first kill in the menu lobby) as a win (vanilla: the survivor starts the match one win up)."));
            FixJoinTimeout = new ModSetting(Config.Bind("Fixes", "JoinTimeout", true,
                "When joining a lobby, wait up to 30 s for the host to accept instead of 5 s (vanilla: often fails with the 'check firewall' error unless you pause while joining)."));
            FixRemoteMovement = new ModSetting(Config.Bind("Fixes", "RemoteMovement", true,
                "Apply other online players' movement/jumps once per physics step instead of once per frame (vanilla: their wall jumps kick harder on your screen the higher your FPS)."));
            FixedStepMovement = new ModSetting(Config.Bind("Fixes", "FixedStepMovement", true,
                "Run walking (and the leg animation) on the 60 Hz physics step instead of per frame (vanilla: uneven above 60 FPS, sluggish below it)."));
            FixedStepTurning = new ModSetting(Config.Bind("Fixes", "FixedStepTurning", true,
                "Run body turning on the 60 Hz physics step instead of per frame."));
            FixedStepBalance = new ModSetting(Config.Bind("Fixes", "FixedStepBalance", true,
                "Run leg balancing on the 60 Hz physics step instead of per frame."));
            FixedStepWeaponHold = new ModSetting(Config.Bind("Fixes", "FixedStepWeaponHold", true,
                "Run weapon holding and the hand pull while blocking/grabbing on the 60 Hz physics step instead of per frame (vanilla: wobbly at low FPS)."));

            LockGameSettings = new ModSetting(Config.Bind("Lock", "GameSettings", true,
                "Lock the game settings (HP, regen, weapon spawns, maps option, bots, ping display, win display) to the competitive ruleset. Auto pickup and chat are never locked."));
            LockMaps = new ModSetting(Config.Bind("Lock", "Maps", true,
                "Lock the map selection to the competitive map list."));
            LockWeapons = new ModSetting(Config.Bind("Lock", "Weapons", true,
                "Lock the weapon selection to the competitive weapon list."));

            PickupLog = Config.Bind("Debug", "PickupLog", false,
                "Log online weapon pickups (touches, requests, host answers) to LogOutput.log, to diagnose pickup problems.");

            SettingsLock.ApplyAll();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded (Enabled={MasterEnabled.Value}, PickupLog={PickupLog.Value}, LockGameSettings={LockGameSettings.Value}, LockMaps={LockMaps.Value}, LockWeapons={LockWeapons.Value}, NetworkSendRate={FixNetworkSendRate.Value}, FullAutoFireRate={FixFullAutoFireRate.Value}, ThrowAim={FixThrowAim.Value}, ThrowWhiff={FixThrowWhiff.Value}, StunCarryOver={FixStunCarryOver.Value}, PoseCarryOver={FixPoseCarryOver.Value}, Draws={FixDraws.Value}, RoundStart={FixRoundStart.Value}, AllowAllMapsOnline={AllowAllMapsOnline.Value}, SyncedObjects={FixSyncedObjects.Value}, FallingBlocks={FixFallingBlocks.Value}, ShotSync={FixShotSync.Value}, SnakeSync={FixSnakeSync.Value}, Quickdraw={FixQuickdraw.Value}, ThrowBlock={FixThrowBlock.Value}, PlacedGuns={FixGroundWeapons.Value}, HostSidePickup={FixHostPickup.Value}, NoLobbyWin={FixLobbyWin.Value}, JoinTimeout={FixJoinTimeout.Value}, RemoteMovement={FixRemoteMovement.Value}, FixedStepMovement={FixedStepMovement.Value}, FixedStepTurning={FixedStepTurning.Value}, FixedStepBalance={FixedStepBalance.Value}, FixedStepWeaponHold={FixedStepWeaponHold.Value})");
        }

        private void Update()
        {
            CompSwitch.Tick();
            FallingRockSync.Poll();
            ShotSync.Poll();
            CompetitiveMod.PickupLog.LogNearby();
            SnakeSync.Poll();
            SnakeSync.Tick();
        }
    }

    // A fix/lock setting that only counts while the mod as a whole is switched on.
    internal sealed class ModSetting
    {
        private readonly ConfigEntry<bool> entry;

        internal ModSetting(ConfigEntry<bool> entry)
        {
            this.entry = entry;
        }

        public bool Value => CompSwitch.Active && entry.Value;
    }

    // Vanilla TickSyncPos resets the send timer to 0 after each send, discarding the overshoot,
    // so the real send interval rounds up to whole frames. Carry the remainder instead.
    [HarmonyPatch(typeof(NetworkPlayer), "ResetCurrentSendTickTime")]
    internal static class NetworkSendRatePatch
    {
        private static bool Prefix(ref float ___mCurrentSendTickCount, float ___mSendRate)
        {
            if (!Plugin.FixNetworkSendRate.Value)
            {
                return true;
            }
            // Clamp so a long hitch can't queue up a burst of back-to-back sends.
            ___mCurrentSendTickCount = Mathf.Clamp(___mCurrentSendTickCount - ___mSendRate, 0f, ___mSendRate);
            return false;
        }
    }

    // Vanilla Attack sets counter = 0 when a shot fires, so held full-auto fire waits for the
    // first frame after the cooldown and loses the overshoot every shot. Keep the overshoot.
    [HarmonyPatch(typeof(Fighting), nameof(Fighting.Attack))]
    internal static class FullAutoFireRatePatch
    {
        internal struct State
        {
            public float Counter;
            public float Cooldown;
            public bool FullAuto;
        }

        private static void Prefix(Fighting __instance, out State __state)
        {
            __state = new State
            {
                Counter = __instance.counter,
                Cooldown = __instance.weapon != null ? __instance.weapon.cd : 0f,
                FullAuto = __instance.weapon != null && __instance.fullAuto,
            };
        }

        private static void Postfix(Fighting __instance, State __state)
        {
            if (!Plugin.FixFullAutoFireRate.Value || !__state.FullAuto)
            {
                return;
            }
            bool fired = __state.Counter > __state.Cooldown && __instance.counter == 0f;
            if (!fired)
            {
                return;
            }
            // Only carry when the cooldown expired during the last frame (trigger held through it).
            // A larger overshoot means the trigger was just pulled, so the shot really happened now.
            float overshoot = __state.Counter - __state.Cooldown;
            if (overshoot < Time.deltaTime)
            {
                __instance.counter = overshoot;
            }
        }
    }

    // ThrowWeapon uses the cached aimingVector, which Fighting.Update refreshes each frame. Unity
    // doesn't guarantee Fighting.Update runs before Controller.Update (which calls Throw), so the
    // throw can use last frame's aim. Recompute it from the aim transforms, same rule as Fighting.Update.
    [HarmonyPatch(typeof(Fighting), nameof(Fighting.ThrowWeapon))]
    internal static class ThrowAimPatch
    {
        private static void Prefix(Fighting __instance, bool justDrop, ref Vector3 ___aimingVector, Transform ___aimPosition, Transform ___aimPositionHelp)
        {
            if (!Plugin.FixThrowAim.Value || justDrop || __instance.weapon == null)
            {
                return;
            }
            ___aimingVector = __instance.weapon.gradualRotationSpeed > 1000f ? ___aimPosition.forward : ___aimPositionHelp.forward;
        }
    }

    // A thrown weapon's launch velocity is only applied on the next physics step, but
    // WeaponPickUp.Update treats velocity < 10 as "landed": it stops dealing damage and starts
    // colliding with the thrower. If a frame renders before that step (most frames above 60 FPS),
    // the weapon is "landed" while still in the thrower's hand and gets stopped by their own arm.
    // Hide canDealDamage from that check until a physics step has run since the throw.
    [HarmonyPatch(typeof(WeaponPickUp))]
    internal static class ThrowWhiffPatch
    {
        private static readonly AccessTools.FieldRef<WeaponPickUp, bool> CanDealDamage =
            AccessTools.FieldRefAccess<WeaponPickUp, bool>("canDealDamage");

        // Instance ID -> Time.fixedTime when thrown, for weapons not yet through a physics step.
        private static readonly Dictionary<int, float> ThrownAt = new Dictionary<int, float>();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(WeaponPickUp.Throw))]
        private static void ThrowPostfix(WeaponPickUp __instance)
        {
            if (ThrownAt.Count > 256)
            {
                ThrownAt.Clear(); // drop entries of weapons destroyed before their first step
            }
            ThrownAt[__instance.GetInstanceID()] = Time.fixedTime;
        }

        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        private static void UpdatePrefix(WeaponPickUp __instance, out bool __state)
        {
            __state = false;
            int id = __instance.GetInstanceID();
            if (!ThrownAt.TryGetValue(id, out float thrownAt))
            {
                return;
            }
            if (Time.fixedTime > thrownAt)
            {
                ThrownAt.Remove(id); // launched; vanilla check is valid from now on
                return;
            }
            if (Plugin.FixThrowWhiff.Value && CanDealDamage(__instance))
            {
                CanDealDamage(__instance) = false;
                __state = true;
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Update")]
        private static void UpdatePostfix(WeaponPickUp __instance, bool __state)
        {
            if (__state)
            {
                CanDealDamage(__instance) = true;
            }
        }
    }
}
