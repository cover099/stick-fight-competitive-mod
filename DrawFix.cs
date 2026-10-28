using System;
using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Vanilla ends the round the moment one player is left. If that player also dies before the next
    // map starts (a draw), KillPlayer ends the round a second time: the host sends a second map
    // change, which restarts the map transition halfway through (broken map), and every machine runs
    // StartMatch again, which credits LastWinner (never cleared, still the first "winner") a second win.
    //
    // Fix: only the first round end per transition goes through (host send, map change receive and
    // offline), and if nobody is left alive after the round ended it's a draw: no win is credited (or
    // the credited one is taken back), the crown stays with whoever had it, and the win text says DRAW.
    internal static class DrawFix
    {
        // Safety net so a round end that never reached a new map (failed load, lobby closed) can't
        // block round ends forever.
        private const float Timeout = 30f;
        private const float NotEnded = float.NegativeInfinity;

        // When each kind of round end last went through; each expires on its own.
        private static float hostSentMapChange = NotEnded;
        private static float receivedMapChange = NotEnded;
        private static float offlineRoundEnded = NotEnded;

        private static bool draw;
        private static Controller creditedWinner;

        private static bool crownChanged;
        private static Controller previousKing;
        private static Vector3 previousCrownPosition;

        private static readonly Action<GameManager, Controller> SetLastWinner =
            AccessTools.MethodDelegate<Action<GameManager, Controller>>(AccessTools.PropertySetter(typeof(GameManager), nameof(GameManager.LastWinner)));
        private static readonly AccessTools.FieldRef<GameManager, Crown> GameCrown =
            AccessTools.FieldRefAccess<GameManager, Crown>("crown");
        private static readonly AccessTools.FieldRef<Crown, Transform> CrownTarget =
            AccessTools.FieldRefAccess<Crown, Transform>("target");

        internal static bool RoundEnded => Recent(hostSentMapChange) || Recent(receivedMapChange) || Recent(offlineRoundEnded);

        private static bool Recent(float endedAt) => Time.realtimeSinceStartup - endedAt <= Timeout;

        // Returns false if this round already ended this way (a duplicate round end to drop).
        internal static bool TryEndRound(ref float endedAt)
        {
            if (Recent(endedAt))
            {
                return false;
            }
            if (!RoundEnded)
            {
                ResetDraw(); // first round end of a new round: forget anything left from an old one
            }
            endedAt = Time.realtimeSinceStartup;
            return true;
        }

        internal static ref float HostSentMapChange => ref hostSentMapChange;
        internal static ref float ReceivedMapChange => ref receivedMapChange;
        internal static ref float OfflineRoundEnded => ref offlineRoundEnded;

        internal static void Reset()
        {
            hostSentMapChange = receivedMapChange = offlineRoundEnded = NotEnded;
            ResetDraw();
        }

        private static void ResetDraw()
        {
            draw = false;
            creditedWinner = null;
            crownChanged = false;
            previousKing = null;
        }

        internal static bool AnyoneAlive(GameManager gm)
        {
            foreach (Controller player in gm.playersAlive)
            {
                if (player != null && !player.GetComponent<CharacterInformation>().isDead)
                {
                    return true;
                }
            }
            return false;
        }

        internal static void CheckForDraw(GameManager gm)
        {
            // In the menu lobby, the "round end" just starts the game; there are no wins to fix.
            if (draw || !RoundEnded || GameManager.stillInMenu || AnyoneAlive(gm))
            {
                return;
            }
            draw = true;
            Plugin.Log.LogInfo("Round ended in a draw, no win given.");
            if (creditedWinner != null)
            {
                // StartMatch already ran and gave the win; take it back.
                creditedWinner.GetComponent<CharacterStats>().wins--;
                creditedWinner = null;
                WinCounterUI ui = UnityEngine.Object.FindObjectOfType<WinCounterUI>();
                if (ui != null)
                {
                    ui.RefreshWinTexts();
                }
            }
            else
            {
                // Not credited yet (online, the map change hasn't arrived); make sure it won't be.
                SetLastWinner(gm, null);
            }
            ShowDraw(gm);
        }

        internal static bool IsDraw => draw;

        internal static void BeforeStartMatch(GameManager gm)
        {
            if (draw)
            {
                SetLastWinner(gm, null);
            }
            creditedWinner = gm.LastWinner;
        }

        internal static void ShowDraw(GameManager gm)
        {
            Crown crown = GameCrown(gm);
            if (crownChanged && crown != null)
            {
                if (previousKing != null)
                {
                    crown.SetNewKing(previousKing, win: false);
                }
                else
                {
                    crown.crownBarrer = null;
                    CrownTarget(crown) = null;
                    crown.transform.position = previousCrownPosition;
                }
            }
            if (gm.winText != null)
            {
                gm.winText.text = "DRAW";
                gm.winText.color = Color.white;
            }
        }

        internal static void OnNewKing(Crown crown)
        {
            if (crownChanged)
            {
                return;
            }
            crownChanged = true;
            previousKing = crown.crownBarrer;
            previousCrownPosition = crown.transform.position;
        }
    }

    // Host: only send one map change per round.
    [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.ChangeMap))]
    internal static class DrawHostChangeMapPatch
    {
        private static bool Prefix()
        {
            return !Plugin.FixDraws.Value || DrawFix.TryEndRound(ref DrawFix.HostSentMapChange);
        }
    }

    // Everyone online (host included): only act on one map change per round, in case the host
    // doesn't have the mod.
    [HarmonyPatch(typeof(MultiplayerManager), nameof(MultiplayerManager.OnMapChanged))]
    internal static class DrawReceiveMapChangePatch
    {
        private static bool Prefix()
        {
            if (!Plugin.FixDraws.Value)
            {
                return true;
            }
            if (!DrawFix.TryEndRound(ref DrawFix.ReceivedMapChange))
            {
                Plugin.Log.LogInfo("Ignored a second map change in the same round (draw).");
                return false;
            }
            // The other player's death may have reached us before the host's map change did.
            DrawFix.CheckForDraw(GameManager.Instance);
            return true;
        }
    }

    // Offline: only end the round once.
    [HarmonyPatch(typeof(GameManager), "AllButOnePlayersDied")]
    internal static class DrawOfflineRoundEndPatch
    {
        private static bool Prefix()
        {
            return !Plugin.FixDraws.Value || DrawFix.TryEndRound(ref DrawFix.OfflineRoundEnded);
        }
    }

    // A round end with no winner (index 255) leaves LastWinner as last round's winner, who then
    // gets credited again. Clear it.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.NetworkAllPlayersDiedButOne))]
    internal static class DrawNoWinnerPatch
    {
        private static readonly Action<GameManager, Controller> SetLastWinner =
            AccessTools.MethodDelegate<Action<GameManager, Controller>>(AccessTools.PropertySetter(typeof(GameManager), nameof(GameManager.LastWinner)));

        private static void Prefix(GameManager __instance, byte winner)
        {
            if (Plugin.FixDraws.Value && winner == byte.MaxValue)
            {
                SetLastWinner(__instance, null);
            }
        }
    }

    // A death after the round ended that leaves nobody alive is a draw.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.KillPlayer))]
    internal static class DrawKillPatch
    {
        private static void Postfix(GameManager __instance)
        {
            if (Plugin.FixDraws.Value)
            {
                DrawFix.CheckForDraw(__instance);
            }
        }
    }

    // StartMatch credits LastWinner with the win. Remember who, or credit nobody on a draw.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartMatch))]
    internal static class DrawStartMatchPatch
    {
        private static void Prefix(GameManager __instance)
        {
            if (Plugin.FixDraws.Value)
            {
                DrawFix.BeforeStartMatch(__instance);
            }
        }

        // Online, the crown and win text are set just before StartMatch; undo them on a draw.
        private static void Postfix(GameManager __instance)
        {
            if (Plugin.FixDraws.Value && DrawFix.IsDraw)
            {
                DrawFix.ShowDraw(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Crown), nameof(Crown.SetNewKing))]
    internal static class DrawCrownPatch
    {
        private static void Prefix(Crown __instance, bool win)
        {
            if (win)
            {
                DrawFix.OnNewKing(__instance);
            }
        }
    }

    // The new round has started.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.ReviveAllPlayers))]
    internal static class DrawResetPatch
    {
        private static void Postfix(bool newMap)
        {
            if (newMap)
            {
                DrawFix.Reset();
            }
        }
    }

    // Leaving a lobby: nothing from its last round may carry into the next lobby.
    [HarmonyPatch(typeof(MatchmakingHandler), nameof(MatchmakingHandler.Disconnect))]
    internal static class DrawLeaveLobbyPatch
    {
        private static void Postfix()
        {
            DrawFix.Reset();
        }
    }
}
