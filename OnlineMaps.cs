using HarmonyLib;

namespace CompetitiveMod
{
    // The host never picks maps on LevelSelection.multiplayerBlackList online, even if they're
    // ticked in the map list: Desert 2, 7, 8 (scenes 6, 11, 12), Factory 5 (17) and Winter 13 (69).
    // The list is only checked here when picking the next map; clients load whatever the host
    // sends, so emptying it lets the host play them online.
    [HarmonyPatch(typeof(LevelSelection), nameof(LevelSelection.GetNextLevel))]
    internal static class OnlineMapsPatch
    {
        private static void Prefix(LevelSelection __instance)
        {
            if (Plugin.AllowAllMapsOnline.Value && __instance.multiplayerBlackList != null && __instance.multiplayerBlackList.Length > 0)
            {
                Plugin.Log.LogInfo($"Allowing local-only maps online (scenes {string.Join(", ", System.Array.ConvertAll(__instance.multiplayerBlackList, i => i.ToString()))}).");
                __instance.multiplayerBlackList = new int[0];
            }
        }
    }
}
