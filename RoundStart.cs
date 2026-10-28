using HarmonyLib;
using UnityEngine;

namespace CompetitiveMod
{
    // Online rounds start with StartCountDown: time is frozen, then a coroutine waits 1 s and sets
    // inFight (controls on) and managerTime = 1 (unfreeze). Every step of that costs whole frames:
    // the coroutine resumes after this frame's Controller.Update has already skipped input, and
    // TimeHandler only applies the new time scale in its next Update, so physics resumes a frame
    // after that. At 60 FPS that's ~2 frames (33 ms) after the countdown ends, at 400 FPS ~5 ms, so
    // high-FPS players get control first. Instead, start the fight at the top of the first frame
    // whose Controller.Update runs after the countdown ends, and unfreeze time in the same frame.
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartCountDown))]
    internal static class RoundStartPatch
    {
        private static readonly AccessTools.FieldRef<GameManager, bool> PlayingCountdown =
            AccessTools.FieldRefAccess<GameManager, bool>("mPlayingCountdown");
        private static readonly AccessTools.FieldRef<GameManager, CodeStateAnimation> WaitTextAnimator =
            AccessTools.FieldRefAccess<GameManager, CodeStateAnimation>("mWaitTextAnimator");
        private static readonly AccessTools.FieldRef<GameManager, CustomMapInfoSubscriberHandler> MapInfoHandler =
            AccessTools.FieldRefAccess<GameManager, CustomMapInfoSubscriberHandler>("m_CustomMapInfoHandler");
        private static readonly AccessTools.FieldRef<GameManager, CountDown> CountDownHandler =
            AccessTools.FieldRefAccess<GameManager, CountDown>("mCountDownHandler");

        private static GameManager pendingManager;
        private static float startAt;

        // Same as vanilla StartCountDown + CountDownCoroutine, minus the coroutine.
        private static bool Prefix(GameManager __instance)
        {
            if (!Plugin.FixRoundStart.Value)
            {
                return true;
            }
            WaitTextAnimator(__instance).state1 = false;
            if (!PlayingCountdown(__instance))
            {
                MapInfoHandler(__instance).HideMapInfo();
                PlayingCountdown(__instance) = true;
                TimeHandler.managerTime = 0f;
                CountDownHandler(__instance).Countdown();
                startAt = Time.realtimeSinceStartup + 1f;
                pendingManager = __instance;
            }
            return false;
        }

        internal static void TryStartFight()
        {
            if (pendingManager == null || Time.realtimeSinceStartup < startAt)
            {
                return;
            }
            GameManager.inFight = true;
            TimeHandler.managerTime = 1f;
            if (TimeHandler.pauseTime == 1f)
            {
                Time.timeScale = 1f; // vanilla waits for TimeHandler.Update next frame
            }
            PlayingCountdown(pendingManager) = false;
            pendingManager = null;
        }
    }

    // Runs before every controller's input handling; the first one each frame starts the fight.
    [HarmonyPatch(typeof(Controller), "Update")]
    internal static class RoundStartCheckPatch
    {
        private static void Prefix()
        {
            RoundStartPatch.TryStartFight();
        }
    }
}
