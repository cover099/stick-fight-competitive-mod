using HarmonyLib;
using Steamworks;
using TMPro;
using UnityEngine;

namespace CompetitiveMod
{
    // Master on/off switch for the whole mod (off by default, saved: [General] Enabled in the config,
    // or the game's preferences in the locked build).
    // Changed with the COMP MOD lever on the first screen or the /comp chat command. Online the
    // host's setting applies to everyone (the sync fixes need both players to agree), so only the
    // host can change it there, and only in the lobby, not mid-match.
    internal static class CompSwitch
    {
        private const int Channel = 47; // game uses 0-11, mod 42-46
        private const byte Magic = 0xFD;
        private const float BroadcastInterval = 2f;

        private static bool active;
        private static bool initialized;
        private static bool hostKnown;
        private static bool hostState;
        private static float lastBroadcast = -999f;

        // Cached once per frame: patches read this constantly.
        internal static bool Active => initialized ? active : Plugin.MasterEnabled;

        internal static bool OnlineClient => MatchmakingHandler.IsNetworkMatch && !MultiplayerManager.IsServer;

        internal static void Tick()
        {
            bool network = MatchmakingHandler.IsNetworkMatch;
            bool server = network && MultiplayerManager.IsServer;
            if (!network)
            {
                hostKnown = false;
            }
            else
            {
                Poll();
            }
            bool now = network && !server && hostKnown ? hostState : Plugin.MasterEnabled;
            if (server && (now != active || Time.realtimeSinceStartup - lastBroadcast > BroadcastInterval))
            {
                Broadcast(now);
            }
            if (!initialized || now != active)
            {
                bool changed = initialized;
                active = now;
                initialized = true;
                if (changed)
                {
                    Plugin.Log.LogInfo($"CompetitiveMod is now {(now ? "ON" : "OFF")}" + (network && !server ? " (set by the host)" : string.Empty));
                }
                if (now)
                {
                    SettingsLock.ApplyAll();
                }
            }
        }

        // Returns a message to show the player, or null if they may not change it right now.
        internal static string Set(bool on)
        {
            if (OnlineClient)
            {
                return "Comp mod: only the host can change it (currently " + (Active ? "ON" : "OFF") + ")";
            }
            if (MatchmakingHandler.IsNetworkMatch && !GameManager.stillInMenu)
            {
                return "Comp mod: can only be changed in the lobby";
            }
            Plugin.MasterEnabled = on; // saved right away
            return "Comp mod " + (on ? "ON" : "OFF");
        }

        private static void Broadcast(bool on)
        {
            lastBroadcast = Time.realtimeSinceStartup;
            MultiplayerManager manager = GameManager.Instance != null ? GameManager.Instance.mMultiplayerManager : null;
            if (manager == null)
            {
                return;
            }
            byte[] data = { Magic, (byte)(on ? 1 : 0) };
            CSteamID self = SteamUser.GetSteamID();
            foreach (ConnectedClientData client in manager.ConnectedClients)
            {
                if (client != null && client.ClientID.IsValid() && client.ClientID != self)
                {
                    SteamNetworking.SendP2PPacket(client.ClientID, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, Channel);
                }
            }
        }

        private static void Poll()
        {
            while (SteamNetworking.IsP2PPacketAvailable(out uint size, Channel))
            {
                byte[] data = new byte[size];
                if (!SteamNetworking.ReadP2PPacket(data, size, out uint read, out CSteamID sender, Channel) || read < 2 || data[0] != Magic)
                {
                    continue;
                }
                MatchmakingHandler matchmaking = MatchmakingHandler.Instance;
                if (matchmaking != null && sender == SteamMatchmaking.GetLobbyOwner(matchmaking.CurrentLobby))
                {
                    hostKnown = true;
                    hostState = data[1] == 1;
                }
            }
        }
    }

    // Chat commands. The reply shows as a speech bubble on your own screen only.
    //   /comp                     toggle the whole mod
    //   /comp on | off            switch the whole mod on or off
    //   /comp <setting> on | off  change one fix or lock in the config file, e.g. /comp FixedStepPhysics off
    //   /comp <setting>           show one setting
    //   /comp list                show which fixes and locks are off (the full list goes to the log)
    [HarmonyPatch(typeof(ChatManager), "SendChatMessage")]
    internal static class CompCommandPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ChatManager __instance, ref string message)
        {
            if (message == null)
            {
                return true;
            }
            string[] parts = message.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].ToLower() != "/comp")
            {
                return true;
            }
            string reply = Run(parts);
            Plugin.Log.LogInfo(reply);
            __instance.Talk(reply);
            message = string.Empty;
            return false;
        }

        private static string Run(string[] parts)
        {
            string arg = parts.Length > 1 ? parts[1].ToLower() : string.Empty;
            if (arg == string.Empty || arg == "on" || arg == "off")
            {
                return CompSwitch.Set(arg == string.Empty ? !CompSwitch.Active : arg == "on");
            }
            if (arg == "list")
            {
                System.Collections.Generic.List<string> off = new System.Collections.Generic.List<string>();
                foreach (ModSetting setting in Plugin.Settings.Values)
                {
                    Plugin.Log.LogInfo($"  {setting.Key} = {(setting.Configured ? "on" : "off")}");
                    if (!setting.Configured)
                    {
                        off.Add(setting.Key);
                    }
                }
                string state = "Comp mod " + (CompSwitch.Active ? "ON" : "OFF");
                return off.Count == 0 ? state + ", all fixes on" : state + ", off: " + string.Join(", ", off.ToArray());
            }
            if (!Plugin.Settings.TryGetValue(parts[1], out ModSetting target))
            {
                return "Unknown setting '" + parts[1] + "' (/comp list shows them in the log)";
            }
            if (parts.Length < 3)
            {
                return target.Key + " is " + (target.Configured ? "on" : "off");
            }
            string value = parts[2].ToLower();
            if (value != "on" && value != "off")
            {
                return "Use /comp " + target.Key + " on or off";
            }
            if (!target.TrySet(value == "on"))
            {
                return "Settings are locked in this build";
            }
            return target.Key + " " + value.ToUpper() + " (saved)";
        }
    }

    // A copy of the online lobby's host lever ("ALLOW No/Yes"), relabelled COMP MOD OFF/ON and put
    // on the left of the first screen ("PRESS EVERY BUTTON TO JOIN"), where the original sits on
    // its own screen. It doesn't swing: it leans towards the current setting and flips when you
    // knock into it.
    [HarmonyPatch(typeof(GameManager), "Start")]
    internal static class CompLeverPatch
    {
        private const string CloneName = "CompModLever";

        private static void Postfix()
        {
            HostLeverHandler template = null;
            foreach (HostLeverHandler handler in Resources.FindObjectsOfTypeAll<HostLeverHandler>())
            {
                if (handler.gameObject.scene.IsValid() && handler.name != CloneName)
                {
                    template = handler;
                    break;
                }
            }
            Transform serverCanvas = template != null ? template.transform.parent : null;
            // The first screen's canvas is nested (LocalCanvas/RigidBody/LocalCanvas); the inner one
            // has the same placement, rotation and scale as the lobby's ServerCanvas.
            Transform localCanvas = serverCanvas != null && serverCanvas.parent != null ? serverCanvas.parent.Find("LocalCanvas/RigidBody/LocalCanvas") : null;
            if (localCanvas == null)
            {
                Plugin.Log.LogWarning("Comp lever: couldn't find the lobby lever or the first screen.");
                return;
            }
            if (localCanvas.Find(CloneName) != null)
            {
                return;
            }
            GameObject clone = Object.Instantiate(template.gameObject, localCanvas, false);
            clone.name = CloneName;
            clone.SetActive(true);
            Object.DestroyImmediate(clone.GetComponent<HostLeverHandler>());
            Transform pivot = clone.transform.Find("Pivot");
            Transform lever = pivot != null ? pivot.Find("Lever") : null;
            if (lever == null)
            {
                Plugin.Log.LogWarning("Comp lever: couldn't find the lever to copy.");
                Object.Destroy(clone);
                return;
            }
            ConfigurableJoint joint = lever.GetComponent<ConfigurableJoint>();
            if (joint != null)
            {
                Object.DestroyImmediate(joint);
            }
            Rigidbody rig = lever.GetComponent<Rigidbody>();
            if (rig != null)
            {
                rig.isKinematic = true;
            }
            lever.localRotation = Quaternion.identity;
            lever.gameObject.SetActive(true);
            SetText(clone.transform, "No", "OFF");
            SetText(clone.transform, "Yes", "ON");
            SetText(clone.transform, "ALLOW", "COMP MOD");
            CompLever comp = clone.AddComponent<CompLever>();
            comp.Pivot = pivot;
            lever.gameObject.AddComponent<CompLeverHit>().Owner = comp;
        }

        private static void SetText(Transform root, string child, string text)
        {
            Transform label = root.Find(child);
            TextMeshPro tmp = label != null ? label.GetComponent<TextMeshPro>() : null;
            if (tmp != null)
            {
                tmp.text = text;
            }
        }
    }

    internal class CompLever : MonoBehaviour
    {
        private const float LeanAngle = 40f;
        private const float Cooldown = 0.75f;

        internal Transform Pivot;
        private float lastFlip = -999f;

        // Lean right (ON side) or left (OFF side). The first screen's canvas hides it elsewhere.
        private void Update()
        {
            Pivot.localRotation = Quaternion.Euler(0f, 0f, CompSwitch.Active ? -LeanAngle : LeanAngle);
        }

        internal void Hit(Collision collision)
        {
            Controller player = collision.transform.root.GetComponent<Controller>();
            if (player == null || !player.HasControl || Time.realtimeSinceStartup - lastFlip < Cooldown)
            {
                return;
            }
            lastFlip = Time.realtimeSinceStartup;
            string reply = CompSwitch.Set(!CompSwitch.Active);
            Plugin.Log.LogInfo(reply);
        }
    }

    internal class CompLeverHit : MonoBehaviour
    {
        internal CompLever Owner;

        private void OnCollisionEnter(Collision collision)
        {
            if (Owner != null)
            {
                Owner.Hit(collision);
            }
        }
    }
}
