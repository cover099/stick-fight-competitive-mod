using HarmonyLib;

namespace CompetitiveMod
{
    // Joining a lobby: the joining game asks the host to accept it once a second and gives up after
    // 5 s ("No connection to the host could be made, please check firewall"), then allows another 5 s
    // for the host's game state. Setting up the Steam peer-to-peer connection (especially through
    // Steam's relays) often takes longer than that. Pausing "fixed" it only because these timers
    // count game time, which stops while paused. Give both steps 30 s instead.
    [HarmonyPatch(typeof(MultiplayerManager))]
    internal static class JoinTimeoutFix
    {
        private const float JoinTimeout = 30f;

        [HarmonyPrefix]
        [HarmonyPatch("TimeOutAfterSecondsIfNotAccepted")]
        private static void AcceptedPrefix(ref float v)
        {
            if (Plugin.FixJoinTimeout.Value && v < JoinTimeout)
            {
                v = JoinTimeout;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("TimeOutAfterSecondsIfNotInitialized")]
        private static void InitializedPrefix(ref float v)
        {
            if (Plugin.FixJoinTimeout.Value && v < JoinTimeout)
            {
                v = JoinTimeout;
            }
        }
    }
}
