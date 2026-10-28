using HarmonyLib;

namespace CompetitiveMod
{
    // A blocked punch stuns the attacker by setting Fighting.counter to -1.5 (no attacking or
    // blocking until it counts back up past 0). Nothing resets it between maps, and a dead player's
    // counter doesn't count while they're inactive, so the stun carries into the next round.
    // Clear any leftover cooldown when players are revived for a new map (offline and online both
    // end the map transition with ReviveAllPlayers). Weapons are already removed by then, so a
    // negative counter can only be a leftover stun or reload.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReviveAllPlayers))]
    internal static class ClearStunOnNewMapPatch
    {
        private static void Postfix(bool newMap)
        {
            if (!Plugin.FixStunCarryOver.Value || !newMap || ControllerHandler.Instance == null)
            {
                return;
            }
            foreach (Controller player in ControllerHandler.Instance.players)
            {
                if (player == null)
                {
                    continue;
                }
                Fighting fighting = player.GetComponent<Fighting>();
                if (fighting != null && fighting.counter < 0f)
                {
                    fighting.counter = 0f;
                }
            }
        }
    }

    // The first kill in the menu lobby ends the lobby "round", and StartMatch then counts it as a
    // win for the survivor, so a player starts the real match one win up. Don't credit it.
    // Runs before DrawFix's StartMatch prefix so that sees no winner either.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartMatch))]
    internal static class NoLobbyWinPatch
    {
        private static readonly System.Action<GameManager, Controller> SetLastWinner =
            AccessTools.MethodDelegate<System.Action<GameManager, Controller>>(AccessTools.PropertySetter(typeof(GameManager), nameof(GameManager.LastWinner)));

        [HarmonyPriority(Priority.First)]
        private static void Prefix(GameManager __instance)
        {
            if (Plugin.FixLobbyWin.Value && GameManager.stillInMenu && __instance.LastWinner != null)
            {
                SetLastWinner(__instance, null);
            }
        }
    }
}
