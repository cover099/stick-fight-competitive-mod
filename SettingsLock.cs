using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace CompetitiveMod
{
    // Locks game settings to the ruleset in Rules. The game reads options from PlayerPrefs, so the
    // locked values are written there before every read; the UI shows them as locked and refuses edits.
    // In online lobbies the host's options are what count, so a host running this mod plays the ruleset.
    internal static class SettingsLock
    {
        internal static void ApplyOptionPrefs()
        {
            foreach (KeyValuePair<string, int> option in Rules.Options)
            {
                if (!PlayerPrefs.HasKey(option.Key) || PlayerPrefs.GetInt(option.Key) != option.Value)
                {
                    PlayerPrefs.SetInt(option.Key, option.Value);
                }
            }
        }

        internal static void ApplyAll()
        {
            if (Plugin.LockGameSettings.Value)
            {
                ApplyOptionPrefs();
            }
            if (Plugin.LockMaps.Value)
            {
                PlayerPrefs.SetString("SavedMaps", Rules.SavedMaps);
            }
            if (Plugin.LockWeapons.Value)
            {
                PlayerPrefs.SetString("SavedWeapons", Rules.SavedWeapons);
            }
        }

        internal static bool IsLocked(OptionsButton button)
        {
            Transform parent = button.transform.parent;
            return Plugin.LockGameSettings.Value && parent != null && Rules.Options.ContainsKey(parent.name);
        }

        // Snap a toggle back to its locked state. Setting isOn re-fires onValueChanged, but with the
        // value now matching, the handler's prefix just returns.
        internal static void Revert(Toggle toggle, bool locked)
        {
            if (toggle != null && toggle.isOn != locked)
            {
                toggle.isOn = locked;
            }
        }
    }

    // Every options read (startup, each row change) goes through CheckOptions.
    [HarmonyPatch(typeof(OptionsHolder), nameof(OptionsHolder.CheckOptions))]
    internal static class CheckOptionsPatch
    {
        private static void Prefix()
        {
            if (Plugin.LockGameSettings.Value)
            {
                SettingsLock.ApplyOptionPrefs();
            }
        }
    }

    // A client in someone else's lobby uses the host's options; warn if they aren't the ruleset.
    [HarmonyPatch(typeof(OptionsHolder), nameof(OptionsHolder.NetworkOptionsChanged))]
    internal static class NetworkOptionsPatch
    {
        private static void Postfix(byte[] data)
        {
            if (!Plugin.LockGameSettings.Value || data == null || data.Length < 4)
            {
                return;
            }
            if (data[0] != Rules.Options["Maps"] || data[1] != Rules.Options["HP"] || data[2] != Rules.Options["Regen"] || data[3] != Rules.Options["weaponSpawns"])
            {
                Plugin.Log.LogWarning($"Host's game options differ from the competitive ruleset (Maps={data[0]} HP={data[1]} Regen={data[2]} WeaponSpawns={data[3]}).");
            }
        }
    }

    [HarmonyPatch(typeof(OptionsButton))]
    internal static class OptionsButtonPatch
    {
        private static readonly Action<OptionsButton> LockButton =
            AccessTools.MethodDelegate<Action<OptionsButton>>(AccessTools.Method(typeof(OptionsButton), "LockButton"));
        private static readonly AccessTools.FieldRef<OptionsButton, TMPro.TextMeshProUGUI> Text =
            AccessTools.FieldRefAccess<OptionsButton, TMPro.TextMeshProUGUI>("text");

        [HarmonyPrefix]
        [HarmonyPatch(nameof(OptionsButton.MoveIndexRight))]
        private static bool MoveRightPrefix(OptionsButton __instance)
        {
            return !SettingsLock.IsLocked(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(OptionsButton.MoveIndexLeft))]
        private static bool MoveLeftPrefix(OptionsButton __instance)
        {
            return !SettingsLock.IsLocked(__instance);
        }

        // Show the game's own lock icon (what clients see in a host's lobby). Init unlocks buttons
        // offline / for the host, so re-lock after it too.
        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void StartPostfix(OptionsButton __instance)
        {
            Lock(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(OptionsButton.Init))]
        private static void InitPostfix(OptionsButton __instance)
        {
            Lock(__instance);
        }

        private static readonly AccessTools.FieldRef<OptionsButton, bool> Locked =
            AccessTools.FieldRefAccess<OptionsButton, bool>("mLocked");

        private static void Lock(OptionsButton button)
        {
            bool weaponsRow = Plugin.LockWeapons.Value && IsWeaponsRow(button);
            if ((!SettingsLock.IsLocked(button) && !weaponsRow) || Text(button) == null)
            {
                return;
            }
            EnsureLockIcon(button);
            LockButton(button);
            if (weaponsRow)
            {
                // Show the lock, but keep the screen openable so the locked selection can be viewed.
                Locked(button) = false;
            }
        }

        // The row whose click opens the weapon selection screen.
        private static bool IsWeaponsRow(OptionsButton button)
        {
            foreach (Button uiButton in button.GetComponentsInChildren<Button>(includeInactive: true))
            {
                for (int i = 0; i < uiButton.onClick.GetPersistentEventCount(); i++)
                {
                    if (uiButton.onClick.GetPersistentMethodName(i) == nameof(OptionsButton.OpenWeaponScreen))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Only rows that a host can lock online (HP, regen, ...) have a "Lock" icon in the scene. For
        // the others, copy one from a row that has it.
        private static void EnsureLockIcon(OptionsButton button)
        {
            if (FindLockIcon(button) != null)
            {
                return;
            }
            foreach (OptionsButton other in Resources.FindObjectsOfTypeAll<OptionsButton>())
            {
                Transform template = FindLockIcon(other);
                if (template == null)
                {
                    continue;
                }
                Transform icon = UnityEngine.Object.Instantiate(template.gameObject, button.transform, worldPositionStays: false).transform;
                icon.name = template.name;
                icon.localPosition = template.localPosition;
                icon.localRotation = template.localRotation;
                icon.localScale = template.localScale;
                icon.gameObject.SetActive(false);
                return;
            }
            Plugin.Log.LogWarning($"No lock icon found to copy for options row '{button.transform.parent?.name}'.");
        }

        private static Transform FindLockIcon(OptionsButton button)
        {
            foreach (Transform t in button.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name.ToLower() == "lock")
                {
                    return t;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(MapSelectionHandler))]
    internal static class MapSelectionLockPatch
    {
        private static readonly AccessTools.FieldRef<MapSelectionHandler, List<MapSelectionHandler.MapCategoryUI>> Categories =
            AccessTools.FieldRefAccess<MapSelectionHandler, List<MapSelectionHandler.MapCategoryUI>>("m_Categories");
        private static readonly AccessTools.FieldRef<MapSelectionHandler, Toggle> RandomOrderToggle =
            AccessTools.FieldRefAccess<MapSelectionHandler, Toggle>("m_RandomOrderMapsToggle");
        private static readonly AccessTools.FieldRef<MapSelectionHandler, bool> RandomOrder =
            AccessTools.FieldRefAccess<MapSelectionHandler, bool>("m_RandomOrderMaps");

        [HarmonyPrefix]
        [HarmonyPatch("GetSavedData")]
        private static void GetSavedDataPrefix()
        {
            if (Plugin.LockMaps.Value)
            {
                PlayerPrefs.SetString("SavedMaps", Rules.SavedMaps);
            }
        }

        // Grey out every toggle on the shown page, the category toggles and random order.
        [HarmonyPostfix]
        [HarmonyPatch("PopulatePage")]
        private static void PopulatePagePostfix(MapSelectionHandler __instance)
        {
            if (!Plugin.LockMaps.Value)
            {
                return;
            }
            foreach (MapSelectionHandler.MapCategoryUI category in Categories(__instance))
            {
                if (category.CategoryToggle != null)
                {
                    category.CategoryToggle.interactable = false;
                }
                foreach (SingleMapUI map in category.CategoryMaps)
                {
                    if (map.MapToggle != null)
                    {
                        map.MapToggle.interactable = false;
                    }
                }
            }
            if (RandomOrderToggle(__instance) != null)
            {
                RandomOrderToggle(__instance).interactable = false;
            }
        }

        // Fallbacks if a change gets through anyway (e.g. controller shortcut for random order).
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MapSelectionHandler.OnSingleMapToggle))]
        private static bool SingleMapPrefix(MapSelectionHandler __instance, Toggle mapToggle)
        {
            if (!Plugin.LockMaps.Value)
            {
                return true;
            }
            SingleMapUI map = __instance.FindSingleMapByIndex(mapToggle.GetComponent<SingleWeaponCellUI>().MapIndex);
            if (map != null)
            {
                SettingsLock.Revert(mapToggle, map.IsLocallyActive);
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnMapCategoryToggle")]
        private static bool CategoryPrefix(MapSelectionHandler __instance, Toggle t)
        {
            if (!Plugin.LockMaps.Value)
            {
                return true;
            }
            MapSelectionHandler.MapCategoryUI category = Categories(__instance).Find(c => c.CategoryToggle == t);
            if (category != null)
            {
                SettingsLock.Revert(t, category.IsActive);
            }
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch("OnRandomOrderMapsToggleClicked")]
        private static bool RandomOrderPrefix(MapSelectionHandler __instance)
        {
            if (!Plugin.LockMaps.Value)
            {
                return true;
            }
            SettingsLock.Revert(RandomOrderToggle(__instance), RandomOrder(__instance));
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponSelectionHandler))]
    internal static class WeaponSelectionLockPatch
    {
        // Each weapon/category toggle's state right after loading the locked selection.
        private static readonly Dictionary<Toggle, bool> LockedStates = new Dictionary<Toggle, bool>();

        [HarmonyPrefix]
        [HarmonyPatch("GetSavedData")]
        private static void GetSavedDataPrefix()
        {
            if (Plugin.LockWeapons.Value)
            {
                PlayerPrefs.SetString("SavedWeapons", Rules.SavedWeapons);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch("Start")]
        private static void StartPostfix(WeaponSelectionHandler __instance)
        {
            LockedStates.Clear();
            if (!Plugin.LockWeapons.Value)
            {
                return;
            }
            foreach (Toggle toggle in __instance.GetComponentsInChildren<Toggle>(includeInactive: true))
            {
                LockedStates[toggle] = toggle.isOn;
                toggle.interactable = false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WeaponSelectionHandler.OnSingleWeaponToggle))]
        private static bool SingleWeaponPrefix(Toggle weaponToggle)
        {
            return Block(weaponToggle);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(WeaponSelectionHandler.OnWeaponCategoryToggle))]
        private static bool CategoryPrefix(Toggle weaponToggle)
        {
            return Block(weaponToggle);
        }

        private static bool Block(Toggle toggle)
        {
            // Before Start has snapshotted (initial load), let the game set things up normally.
            if (!LockedStates.TryGetValue(toggle, out bool locked))
            {
                return true;
            }
            SettingsLock.Revert(toggle, locked);
            return false;
        }
    }
}
