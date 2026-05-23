using HarmonyLib;
using Laika.Persistence;
using Laika.UI;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

public partial class LaikaMod
{
    // Title screen, save-slot, AP panel, and input-blocking Harmony patches.
    [HarmonyPatch(typeof(Laika.UI.SaveSlotItem), "SetUp")]
    public class SaveSlotItem_SetUp_APPatch
    {
        static void Postfix(Laika.UI.SaveSlotItem __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var field = typeof(Laika.UI.SaveSlotItem).GetField("slotName", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null)
                    return;

                object slotNameObject = field.GetValue(__instance);
                if (slotNameObject == null)
                    return;

                Component sourceTextComponent = slotNameObject as Component;
                if (sourceTextComponent == null)
                    return;

                GameObject sourceObject = sourceTextComponent.gameObject;
                Transform parent = sourceObject.transform.parent;
                if (parent == null)
                    return;

                string apSummary = LaikaMod.BuildSaveSlotAPSummaryShort(__instance.Idx);
                Color apColor = LaikaMod.GetSaveSlotAPColor(__instance.Idx);

                Transform existing = parent.Find("APStatusLabel");
                GameObject apLabelObject;

                if (existing != null)
                {
                    apLabelObject = existing.gameObject;
                }
                else
                {
                    apLabelObject = UnityEngine.Object.Instantiate(sourceObject, parent);
                    apLabelObject.name = "APStatusLabel";
                }

                apLabelObject.SetActive(true);

                RectTransform sourceRect = sourceObject.GetComponent<RectTransform>();
                RectTransform apRect = apLabelObject.GetComponent<RectTransform>();

                if (sourceRect != null && apRect != null)
                {
                    apRect.anchorMin = new Vector2(0f, 0.5f);
                    apRect.anchorMax = new Vector2(0f, 0.5f);
                    apRect.pivot = new Vector2(0f, 0.5f);

                    apRect.anchoredPosition = new Vector2(420f, -42f);
                    apRect.sizeDelta = new Vector2(300f, 40f);
                    apRect.localScale = Vector3.one;
                }

                var textProperty = apLabelObject.GetComponent(sourceTextComponent.GetType()).GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty != null && textProperty.CanWrite)
                {
                    textProperty.SetValue(apLabelObject.GetComponent(sourceTextComponent.GetType()), apSummary, null);
                }

                var fontSizeProperty = apLabelObject.GetComponent(sourceTextComponent.GetType()).GetType().GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
                if (fontSizeProperty != null && fontSizeProperty.CanWrite)
                {
                    object currentFontSize = fontSizeProperty.GetValue(apLabelObject.GetComponent(sourceTextComponent.GetType()), null);
                    if (currentFontSize is float)
                        fontSizeProperty.SetValue(apLabelObject.GetComponent(sourceTextComponent.GetType()), 28f, null);
                    else if (currentFontSize is int)
                        fontSizeProperty.SetValue(apLabelObject.GetComponent(sourceTextComponent.GetType()), 28, null);
                }

                var colorProperty = apLabelObject.GetComponent(sourceTextComponent.GetType()).GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
                if (colorProperty != null && colorProperty.CanWrite)
                {
                    colorProperty.SetValue(apLabelObject.GetComponent(sourceTextComponent.GetType()), apColor, null);
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning("SaveSlotItem AP patch failed:\n" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "PlayOptionSelected")]
    public class TitleScreenView_PlayOptionSelected_APPatch
    {
        static void Postfix()
        {
            LaikaMod.TitleScreenSavePickerOpen = true;
            LaikaMod.LogInfo("AP TITLE: PlayOptionSelected fired. Save picker opened.");
            LaikaMod.UpdateTitleScreenAPPanel();
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "Back")]
    public class TitleScreenView_Back_APPatch
    {
        static void Postfix()
        {
            LaikaMod.TitleScreenSavePickerOpen = false;
            LaikaMod.ShowAPSettingsPopup = false;
            LaikaMod.APSettingsTextInputActive = false;
            LaikaMod.SetTitleScreenInputLocked(false);
            LaikaMod.HideTitleScreenAPPanel();
            LaikaMod.SetTitleScreenInputLocked(false);
            LaikaMod.SetTitleScreenNavigationBlocked(false);
            LaikaMod.SetTitleScreenSelectablesLocked(false);
            LaikaMod.SetTitleScreenUINavigationLocked(false);

            LaikaMod.LogInfo("AP TITLE: Back fired. Save picker closed.");
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "OnSaveSlotHighlighted")]
    public class TitleScreenView_OnSaveSlotHighlighted_APPatch
    {
        static bool Prefix(NavigableItem item)
        {
            if (LaikaMod.ShowAPSettingsPopup || LaikaMod.APSettingsTextInputActive || LaikaMod.IsAPSettingsInputFocused())
            {
                LaikaMod.LogInfo("AP TITLE: OnSaveSlotHighlighted blocked because AP settings panel is open.");
                return false;
            }

            return true;
        }

        static void Postfix(NavigableItem item)
        {
            if (LaikaMod.ShowAPSettingsPopup || LaikaMod.APSettingsTextInputActive || LaikaMod.IsAPSettingsInputFocused())
            {
                return;
            }

            try
            {
                LaikaMod.LogInfo("AP TITLE: OnSaveSlotHighlighted fired.");

                if (item == null)
                    return;

                LaikaMod.TitleScreenHighlightedSlotIndex = item.Idx;
                LaikaMod.LogInfo("AP TITLE: highlighted slot = " + item.Idx);

                if (LaikaMod.TitleScreenSavePickerOpen)
                {
                    LaikaMod.UpdateTitleScreenAPPanel();
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning("AP TITLE highlight patch failed:\n" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(NavigationGroup), "CheckNavigation")]
    public class NavigationGroup_CheckNavigation_APPatch
    {
        static bool Prefix(NavigationGroup __instance)
        {
            if (LaikaMod.ShouldBlockVanillaTitleInput())
            {
                // (Commented to avoid console spam
                // LaikaMod.LogInfo("AP TITLE: NavigationGroup.CheckNavigation blocked because AP settings panel is open.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(NavigationGroup), "SelectHighlightedItem")]
    public class NavigationGroup_SelectHighlightedItem_APTitleBlockPatch
    {
        static bool Prefix(NavigationGroup __instance)
        {
            try
            {
                if (!LaikaMod.ShouldBlockVanillaTitleInput())
                    return true;

                LaikaMod.LogInfo("AP TITLE: NavigationGroup.SelectHighlightedItem blocked because AP settings panel/input is open.");
                return false;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"NavigationGroup_SelectHighlightedItem_APTitleBlockPatch exception:\n{ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "Update")]
    public class TitleScreenView_Update_APInputBlockPatch
    {
        static bool Prefix()
        {
            if (LaikaMod.ShowAPSettingsPopup || LaikaMod.APSettingsTextInputActive || LaikaMod.IsAPSettingsInputFocused())
            {
                LaikaMod.UpdateTitleScreenAPPanel();
                LaikaMod.RefreshVisibleSaveSlotAPLabels();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "PlayOptionSelected")]
    public class TitleScreenView_PlayOptionSelected_APInputBlockPatch
    {
        static bool Prefix()
        {
            if (LaikaMod.ShowAPSettingsPopup || LaikaMod.APSettingsTextInputActive || LaikaMod.IsAPSettingsInputFocused())
            {
                LaikaMod.LogInfo("AP TITLE: PlayOptionSelected blocked by AP settings input.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), new Type[] { typeof(KeyCode) })]
    public class UnityInput_GetKeyDown_APTitleBlockPatch
    {
        static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (key == KeyCode.W ||
                key == KeyCode.S ||
                key == KeyCode.A ||
                key == KeyCode.D ||
                key == KeyCode.UpArrow ||
                key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow ||
                key == KeyCode.RightArrow ||
                key == KeyCode.X ||
                key == KeyCode.Space ||
                key == KeyCode.Return ||
                key == KeyCode.KeypadEnter ||
                key == KeyCode.Escape)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetButtonDown), new Type[] { typeof(string) })]
    public class UnityInput_GetButtonDown_APTitleBlockPatch
    {
        static bool Prefix(string buttonName, ref bool __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (buttonName == "Vertical" ||
                buttonName == "Horizontal" ||
                buttonName == "Submit" ||
                buttonName == "Cancel")
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetButton), new Type[] { typeof(string) })]
    public class UnityInput_GetButton_APTitleBlockPatch
    {
        static bool Prefix(string buttonName, ref bool __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (buttonName == "Vertical" ||
                buttonName == "Horizontal" ||
                buttonName == "Submit" ||
                buttonName == "Cancel")
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetButtonUp), new Type[] { typeof(string) })]
    public class UnityInput_GetButtonUp_APTitleBlockPatch
    {
        static bool Prefix(string buttonName, ref bool __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (buttonName == "Vertical" ||
                buttonName == "Horizontal" ||
                buttonName == "Submit" ||
                buttonName == "Cancel")
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetKey), new Type[] { typeof(KeyCode) })]
    public class UnityInput_GetKey_APTitleBlockPatch
    {
        static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (key == KeyCode.W ||
                key == KeyCode.S ||
                key == KeyCode.A ||
                key == KeyCode.D ||
                key == KeyCode.UpArrow ||
                key == KeyCode.DownArrow ||
                key == KeyCode.LeftArrow ||
                key == KeyCode.RightArrow ||
                key == KeyCode.X ||
                key == KeyCode.Space ||
                key == KeyCode.Return ||
                key == KeyCode.KeypadEnter ||
                key == KeyCode.Escape)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetAxisRaw), new Type[] { typeof(string) })]
    public class UnityInput_GetAxisRaw_APTitleBlockPatch
    {
        static bool Prefix(string axisName, ref float __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (axisName == "Vertical" || axisName == "Horizontal")
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Input), nameof(Input.GetAxis), new Type[] { typeof(string) })]
    public class UnityInput_GetAxis_APTitleBlockPatch
    {
        static bool Prefix(string axisName, ref float __result)
        {
            if (!LaikaMod.ShouldBlockVanillaTitleInput())
                return true;

            if (axisName == "Vertical" || axisName == "Horizontal")
            {
                __result = 0f;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(StandaloneInputModule), "Process")]
    public class StandaloneInputModule_Process_APTitleBlockPatch
    {
        static bool Prefix()
        {
            if (LaikaMod.ShouldBlockVanillaTitleInput())
            {
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(TitleScreenView), "Update")]
    public class TitleScreenView_Update_APHotkeyPatch
    {
        static void Postfix()
        {
            LaikaMod.PollTitleScreenAPHotkey();

            if (!LaikaMod.TitleScreenSavePickerOpen &&
                !LaikaMod.ShowAPSettingsPopup &&
                !LaikaMod.SuppressTitleUIForSlotLoad)
            {
                LaikaMod.UpdateMainMenuArchipelagoEditionText(false);
            }
            else if (LaikaMod.MainMenuArchipelagoEditionCanvasObject != null)
            {
                LaikaMod.MainMenuArchipelagoEditionCanvasObject.SetActive(false);
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "StartNewGame", new Type[] { typeof(int) })]
    public class PersistenceManager_StartNewGame_APSlotBindPatch
    {
        static void Prefix(int slot)
        {
            try
            {
                LaikaMod.BindToGameSaveSlot(
                    slot,
                    "PersistenceManager.StartNewGame",
                    autoConnectIfConfigured: true
                );

                LaikaMod.TitleScreenSavePickerOpen = false;
                LaikaMod.ShowAPSettingsPopup = false;
                LaikaMod.APSettingsTextInputActive = false;
                LaikaMod.HideTitleScreenAPPanel();
                LaikaMod.SetTitleScreenInputLocked(false);
                LaikaMod.SetTitleScreenNavigationBlocked(false);
                LaikaMod.SetTitleScreenSelectablesLocked(false);
                LaikaMod.SetTitleScreenUINavigationLocked(false);

                if (LaikaMod.APSettingsPanelObject != null)
                    LaikaMod.APSettingsPanelObject.SetActive(false);
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"StartNewGame AP slot bind patch failed:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "ContinueGame", new Type[] { typeof(int), typeof(Action) })]
    public class PersistenceManager_ContinueGame_APSlotBindPatch
    {
        static void Prefix(int slot)
        {
            try
            {
                LaikaMod.BindToGameSaveSlot(
                    slot,
                    "PersistenceManager.ContinueGame",
                    autoConnectIfConfigured: true
                );

                LaikaMod.TitleScreenSavePickerOpen = false;
                LaikaMod.ShowAPSettingsPopup = false;
                LaikaMod.APSettingsTextInputActive = false;
                LaikaMod.HideTitleScreenAPPanel();
                LaikaMod.SetTitleScreenInputLocked(false);
                LaikaMod.SetTitleScreenNavigationBlocked(false);
                LaikaMod.SetTitleScreenSelectablesLocked(false);
                LaikaMod.SetTitleScreenUINavigationLocked(false);

                if (LaikaMod.APSettingsPanelObject != null)
                    LaikaMod.APSettingsPanelObject.SetActive(false);
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"ContinueGame AP slot bind patch failed:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "DeleteGame", new Type[] { typeof(int) })]
    public class PersistenceManager_DeleteGame_APCleanupPatch
    {
        static void Postfix(int slot)
        {
            try
            {
                string apStatePath = LaikaMod.GetAPStatePathForSlot(slot);

                if (System.IO.File.Exists(apStatePath))
                {
                    System.IO.File.Delete(apStatePath);
                    LaikaMod.LogInfo($"AP STATE: deleted AP state file for deleted Laika save slot {slot}");
                }

                if (LaikaMod.ActiveSaveSlotIndex == slot)
                {
                    if (ArchipelagoClientManager.Instance != null &&
                        (ArchipelagoClientManager.Instance.IsConnected || ArchipelagoClientManager.Instance.IsConnecting))
                    {
                        ArchipelagoClientManager.Instance.Disconnect("Underlying Laika save was deleted");
                    }

                    LaikaMod.LoadSessionStateForSlot(slot);
                    LaikaMod.RefreshDevOverlay();
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"DeleteGame AP cleanup patch failed:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "ExitToTitleScreen")]
    public class PersistenceManager_ExitToTitleScreen_APDisconnectPatch
    {
        static void Prefix()
        {
            if (ArchipelagoClientManager.Instance != null &&
                ArchipelagoClientManager.Instance.IsConnected)
            {
                ArchipelagoClientManager.Instance.Disconnect("Returned to title screen");
                LaikaMod.AnnounceAPActivity("[AP] Disconnected because player returned to title screen.");
                LaikaMod.DisconnectedBecauseReturnedToTitle = true;
                LaikaMod.HasEnteredGameplayWhileConnected = false;
            }
        }
    }
}
