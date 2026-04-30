using HarmonyLib;
using Laika.Cassettes;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.Quests.PlayMaker.FsmActions;
using Laika.UI;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

// Harmony patches that hook Laika gameplay events into AP behavior.
public partial class LaikaMod
{
    // ===== Harmony patches =====
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

    [HarmonyPatch(typeof(WeaponsOverlay), "InitializeWeaponsData")]
    public class WeaponsOverlayPatch
    {
        static void Postfix(WeaponsOverlay __instance)
        {
            Log.LogInfo("WeaponsOverlay.InitializeWeaponsData postfix triggered.");

            EnsureRuntimeDevOverlay(__instance);

            // Only log ingredient IDs once per launch.
            if (!IngredientIdsLogged)
            {
                IngredientIdsLogged = true;
                // LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;
                // LogAllCassetteIds();
            }

            // Process queued AP-style items once the game UI/inventory systems are ready.
            LaikaMod.ProcessPendingItemQueue("WeaponsOverlayInitializeQueueProcess");
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


    [HarmonyPatch(typeof(QuestLog), "TryCloseQuest")]
    public class QuestClosePatch
    {
        static void Postfix(string questId, bool silent, bool __result)
        {
            // Only log successful full quest completions.
            if (!__result)
                return;

            LaikaMod.LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");

            if (questId == "Q_D_S_Flower")
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("QuestClosePatch");
            }

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(questId, out locationDefinition))
            {
                LaikaMod.LogWarning($"QuestClosePatch: no AP location definition found for questId={questId}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "QuestClosePatch");
        }
    }

    // Logs Renato's map popup data when the buy-map popup opens.
    // Useful for verifying mapAreaID and price against Renato's shop data.
    [HarmonyPatch(typeof(ShowBuyingMapPopup), "OnEnter")]
    public class ShowBuyingMapPopupPatch
    {
        static void Prefix(ShowBuyingMapPopup __instance)
        {
            try
            {
                // Safety check in case the FSM values are missing for some reason.
                if (__instance == null)
                {
                    LaikaMod.LogWarning("ShowBuyingMapPopupPatch: __instance was null.");
                    return;
                }

                // Read the real PlayMaker values that Renato's popup is using.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";
                int mapAreaPrice = __instance.mapAreaPrice != null ? __instance.mapAreaPrice.Value : -1;

                // Log both the area ID and the price so we can identify which map piece is which.
                LaikaMod.LogInfo($"RENATO MAP POPUP: mapAreaID={mapAreaId}, price={mapAreaPrice}");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"ShowBuyingMapPopupPatch: exception while logging Renato map popup:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(QuestLog), "TryCompleteQuestGoal")]
    public class QuestGoalCompleteReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("QuestGoalCompleteReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("QuestGoalCompleteReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"QuestGoalCompleteReconcilePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(QuestLog), "TryCompleteQuestGoal")]
    public class QuestLog_TryCompleteQuestGoal_HeartglazeCleanupPatch
    {
        static void Postfix(string questId, string goalId, bool __result)
        {
            try
            {
                LaikaMod.LogInfo(
                    $"QUEST GOAL COMPLETE EVENT: questId={questId}, goalId={goalId}, result={__result}"
                );

                if (!__result)
                    return;

                if (questId == "Q_D_S_Flower")
                {
                    LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                        $"QuestLog.TryCompleteQuestGoal/{goalId}"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"QuestLog_TryCompleteQuestGoal_HeartglazeCleanupPatch exception:\n{ex}");
            }
        }
    }

    // Tracks Renato map purchases as AP location checks.
    // Resolves the unlocked mapAreaID through the AP location registry.
    [HarmonyPatch(typeof(UnlockMapArea), "OnEnter")]
    public class UnlockMapAreaPatch
    {
        static void Postfix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                string mapAreaId = __instance.mapAreaID != null
                    ? __instance.mapAreaID.Value
                    : "<null>";

                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                    return;

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");

                // Suppress vanilla Renato reward AFTER vanilla flow finishes.
                var progressionManager = MonoSingleton<ProgressionManager>.Instance;
                if (progressionManager != null && progressionManager.ProgressionData != null)
                {
                    progressionManager.ProgressionData.LockMapArea(mapAreaId);
                    LaikaMod.LogInfo($"UnlockMapAreaPatch: re-locked vanilla Renato map reward {mapAreaId}.");
                }

                LaikaMod.TryReconcileKnownQuestSoftlocks("UnlockMapAreaPatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(CassetteData), typeof(bool) })]
    public class CassetteInventoryRealSourcePatch
    {
        static void Postfix(CassetteData cassette, bool silent, bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (cassette == null)
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassette was null.");
                    return;
                }

                string cassetteId = cassette.id;

                if (LaikaMod.IsGrantingAPItem)
                {
                    LaikaMod.SuppressedCassetteChecks.Remove(cassetteId);
                    LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: ignored AP-granted cassette {cassetteId}.");
                    return;
                }

                if (LaikaMod.SuppressedCassetteChecks.Remove(cassetteId))
                {
                    LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: suppressed cassette check for AP-granted cassette {cassetteId}.");
                    return;
                }

                if (string.IsNullOrEmpty(cassetteId))
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassetteId was null or empty.");
                    return;
                }

                LaikaMod.LogInfo(
                    $"CASSETTE INVENTORY SOURCE DETECTED: id={cassetteId}, silent={silent}, result={__result}"
                );

                LaikaMod.TryHandleCassetteLocationCheck(cassetteId, "CassetteInventoryRealSourcePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"CassetteInventoryRealSourcePatch exception:\n{ex}");
            }
        }
    }

    // Tracks boss clears through progression achievement keys.
    // Boss kill completion is persisted by the game as achievement-style flags.
    [HarmonyPatch(typeof(ProgressionData), "SetAchievement", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
    public class BossAchievementPatch
    {
        static void Prefix(string name, bool value, bool reset)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (!value || reset)
                return;

            if (name != "B_BOSS_00_DEFEATED" &&
                name != "B_BOSS_01_DEFEATED" &&
                name != "B_BOSS_ROSCO_DEFEATED" &&
                name != "B_BOSS_02_DEFEATED" &&
                name != "B_BOSS_03_DEFEATED" &&
                name != "BOSS_04_DEFEATED")
            {
                return;
            }

            if (LaikaMod.IsGrantingAPItem)
            {
                LaikaMod.LogInfo($"BOSS CLEAR IGNORED during AP item grant: name={name}, value={value}, reset={reset}");
                return;
            }

            LaikaMod.LogInfo($"BOSS CLEAR DETECTED: name={name}, value={value}, reset={reset}");

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(name, out locationDefinition))
            {
                LaikaMod.LogWarning($"BossAchievementPatch: no AP location definition found for name={name}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "BossAchievementPatch");
        }
    }

    [HarmonyPatch(typeof(Boss_02_PickFlower), "Enter")]
    public class Boss02PickFlowerLocationPatch
    {
        static void Postfix()
        {
            APLocationDefinition definition;
            if (!LaikaMod.TryGetLocationDefinition("I_PUPPY_FLOWER", out definition))
                return;

            LaikaMod.TrySendLocationCheck(definition, "Boss02PickFlowerLocationPatch");
        }
    }

    internal static void TryCleanupHeartglazeAfterQuestUpdate(string sourceTag)
    {
        if (!WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
            return;

        if (HeartglazeFlowerCleanupDone)
            return;

        try
        {
            if (!WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
            {
                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because cleanup is not armed.");
                return;
            }

            if (SessionState != null && SessionState.HeartglazeFlowerReceivedFromAP)
            {
                HeartglazeFlowerCleanupDone = true;
                WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because Heartglaze Flower was already received from AP.");
                return;
            }

            bool removed = TryRemoveInventoryReward(
                "I_PUPPY_FLOWER",
                1,
                sourceTag + "/HeartglazeQuestUpdateCleanup"
            );

            if (HeartglazeFlowerCleanupDone)
            {
                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because cleanup is already done.");
                return;
            }

            if (removed)
            {
                HeartglazeFlowerCleanupDone = true;
                WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                LogInfo($"{sourceTag}: removed Heartglaze Flower after quest update.");
            }
            else
            {
                LogWarning($"{sourceTag}: tried to remove Heartglaze Flower after quest update, but removal returned false.");
            }
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: Heartglaze quest-update cleanup failed:\n{ex}");
        }
    }

    [HarmonyPatch(typeof(D1_BossDoor), "CanInteract")]
    public class D1BossDoor_APKeyGatePatch
    {
        static void Postfix(D1_BossDoor __instance, ref bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (LaikaMod.Dungeon01FinalDoorAlreadyOpened())
                    return;

                if (LaikaMod.PlayerHasAllDungeon01PitKeys(__instance))
                    return;

                __result = false;
                LaikaMod.LogInfo("D1BossDoor_APKeyGatePatch: blocked final pit door because the player does not have all 3 AP pit keys.");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"D1BossDoor_APKeyGatePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddItem", new Type[] { typeof(ItemData), typeof(int), typeof(Action), typeof(bool) })]
    public class InventoryManager_AddItem_APLocationPatch
    {
        
        static bool Prefix(ItemData item, int amount, Action onAddedCallback, bool silent, ref bool __result)
        {
            if (!LaikaMod.IsGrantingAPItem && item.id == "I_PUPPY_FLOWER")
            {
                APLocationDefinition flowerDefinition;
                if (LaikaMod.TryGetLocationDefinition("I_PUPPY_FLOWER", out flowerDefinition))
                {
                    LaikaMod.TrySendLocationCheck(flowerDefinition, "InventoryManager_AddItem_APLocationPatch/PhysicalHeartglazePickup");
                }

                if (LaikaMod.SessionState != null && LaikaMod.SessionState.HeartglazeFlowerReceivedFromAP)
                {
                    LaikaMod.LogInfo("Physical Heartglaze pickup allowed to stay because Heartglaze Flower was already received from AP.");
                    return true;
                }

                LaikaMod.HeartglazeFlowerCleanupDone = false;
                LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = true;

                LaikaMod.LogInfo("Physical Heartglaze pickup detected. Cleanup armed after quest update.");
                return true;
            }

            try
            {

                if (item == null || string.IsNullOrEmpty(item.id))
                    return true;

                string sourceTag = "InventoryManager_AddItem_APLocationPatch";

                if (LaikaMod.IsGrantingAPItem)
                {
                    LaikaMod.LogInfo($"InventoryManager_AddItem_APLocationPatch: allowed AP-granted item {item.id}.");
                    return true;
                }

                string itemId = item.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return true;

                // Key items often start quests or advance dialogue.
                // Do not block them here. Let AddKeyItem run, then handle AP check/removal there.
                if (definition.Category == "KeyItem")
                {
                    LaikaMod.LogInfo(
                        $"{sourceTag}: key item {itemId} allowed through AddItem so vanilla quest logic can run."
                    );

                    return true;
                }

                if (!LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                    return true;

                LaikaMod.LogInfo(
                    $"INVENTORY LOCATION SOURCE BLOCKED: id={itemId}, category={definition.Category}, amount={amount}, silent={silent}"
                );

                LaikaMod.TrySendLocationCheck(definition, "InventoryManager_AddItem_APLocationPatch");

                // Pretend vanilla AddItem succeeded so quests/dialogue/shop flow continues,
                // but do not actually add the original item.
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_APLocationPatch exception:\n{ex}");
                return true;
            }
        }
    }

    static void Postfix(ItemData item, int amount, Action onAddedCallback, bool silent, bool __result)
    {
        try
        {
            if (!__result || item == null || string.IsNullOrEmpty(item.id))
                return;

            if (LaikaMod.IsGrantingAPItem)
                return;

            string itemId = item.id;

            APLocationDefinition definition;
            if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                return;

            if (definition.Category != "KeyItem")
                return;

            if (!LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                return;

            if (itemId == "I_PUPPY_FLOWER")
                return;

            LaikaMod.LogInfo(
                $"ADD ITEM KEYITEM REMOVAL FALLBACK: id={itemId}, location={definition.DisplayName}"
            );

            LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                itemId,
                1,
                "InventoryManager_AddItem_APLocationPatch/PostfixFallback"
            );
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"InventoryManager_AddItem_APLocationPatch.Postfix exception:\n{ex}");
        }
    }

    [HarmonyPatch(typeof(Boss_00), "OnEndingVideoEnd")]
    public class Boss00VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_00.OnEndingVideoEnd fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_01_Manager), "PrepareToKill")]
    public class Boss01VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_01_Manager.PrepareToKill fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_02), "OnEndingSequenceEnds")]
    public class Boss02VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_02.OnEndingSequenceEnds fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_02_LighthouseManager), "DefeatBoss")]
    public class Boss02LighthouseVerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_02_LighthouseManager.DefeatBoss fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_03), "Defeated")]
    public class Boss03VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_03.Defeated fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_04), "Kill")]
    public class Boss04VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_04.Kill fired.");
        }
    }

    // Detects player death when the parameterless RiderHead.Kill() overload is used.
    [HarmonyPatch(typeof(global::RiderHead), "Kill", new Type[] { })]
    public class PlayerDeathPatch_NoArgs
    {
        static void Prefix()
        {
            LaikaMod.OnPlayerDeathDetected("PLAYER DEATH DETECTED (Kill())");
        }
    }

    // Detects player death when the RiderHead.Kill(bool useBlood, bool moneySack) overload is used.
    [HarmonyPatch(typeof(global::RiderHead), "Kill", new Type[] { typeof(bool), typeof(bool) })]
    public class PlayerDeathPatch_WithArgs
    {
        static void Prefix(bool useBlood, bool moneySack)
        {
            LaikaMod.OnPlayerDeathDetected("PLAYER DEATH DETECTED (Kill(bool,bool))", useBlood, moneySack);
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddKeyItem", new Type[] { typeof(ItemData) })]
    public class KeyItemLocationSourcePatch
    {
        static void Postfix(ItemData __0, bool __result)
        {
            try
            {
                if (!__result || __0 == null || string.IsNullOrEmpty(__0.id))
                    return;

                string itemId = __0.id;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                if (!LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                    return;

                LaikaMod.LogInfo(
                    $"KEY ITEM LOCATION SOURCE DETECTED: id={itemId}, location={definition.DisplayName}"
                );

                if (
                    itemId == "I_TOY_BIKE" ||
                    itemId == "I_GAMEBOY" ||
                    itemId == "I_PLANT_PUPPY" ||
                    itemId == "I_TOY_ANIMAL" ||
                    itemId == "I_BOOK_MOTHER" ||
                    itemId == "I_DREAMCATCHER" ||
                    itemId == "I_UKULELE"
                )
                {
                    LaikaMod.TryHandlePuppyGiftLocationCheck(itemId, "KeyItemLocationSourcePatch");
                }
                else
                {
                    LaikaMod.TrySendLocationCheck(definition, "KeyItemLocationSourcePatch");
                }

                if (definition.Category == "KeyItem")
                {
                    if (itemId == "I_PUPPY_FLOWER")
                    {
                        if (LaikaMod.SessionState != null &&
                            LaikaMod.SessionState.HeartglazeFlowerReceivedFromAP)
                        {
                            LaikaMod.HeartglazeFlowerCleanupDone = true;
                            LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                            LaikaMod.LogInfo(
                                "Heartglaze cleanup not armed because Heartglaze Flower was already received from AP."
                            );

                            return;
                        }

                        if (LaikaMod.IsGrantingAPItem)
                        {
                            LaikaMod.LogInfo("Heartglaze cleanup skipped because this flower came from AP grant.");
                            return;
                        }

                        LaikaMod.HeartglazeFlowerCleanupDone = false;
                        LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = true;

                        LaikaMod.LogInfo("Heartglaze cleanup armed. Waiting for A Heart for Poochie quest update before removing flower.");

                        return;
                    }

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        1,
                        "KeyItemLocationSourcePatch"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"KeyItemLocationSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Laika.Quests.PlayMaker.FsmActions.TryCompleteQuestGoal), "OnEnter")]
    public class HeartglazeFlowerQuestGoalCleanupPatch
    {
        static void Postfix(Laika.Quests.PlayMaker.FsmActions.TryCompleteQuestGoal __instance)
        {
            try
            {
                if (!LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
                    return;

                if (LaikaMod.HeartglazeFlowerCleanupDone)
                    return;

                string questId = "";
                string goalId = "";

                try
                {
                    if (__instance.questId != null)
                        questId = __instance.questId.Value;

                    if (__instance.goalId != null)
                        goalId = __instance.goalId.Value;
                }
                catch
                {
                }

                LaikaMod.LogInfo(
                    $"HEARTGLAZE QUEST GOAL EVENT: questId={questId}, goalId={goalId}"
                );

                if (questId != "Q_D_S_Flower")
                    return;

                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                    $"HeartglazeFlowerQuestGoalCleanupPatch/{goalId}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"HeartglazeFlowerQuestGoalCleanupPatch exception:\n{ex}");
            }
        }
    }



    internal static void StartHeartglazeQuestAwareCleanup(string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: cannot start Heartglaze quest-aware cleanup because CoroutineRunner is null.");
            return;
        }

        CoroutineRunner.StartCoroutine(HeartglazeQuestAwareCleanupCoroutine(sourceTag));
    }

    private static IEnumerator HeartglazeQuestAwareCleanupCoroutine(string sourceTag)
    {
        // Let vanilla popup + quest goal transition finish.
        yield return new WaitForSecondsRealtime(1.0f);

        LogQuestGoals("Q_D_S_Flower", sourceTag + "/BeforeHeartglazeCleanup");

        TryCleanupHeartglazeAfterQuestUpdate(sourceTag + "/QuestAwareCleanup");

        yield return new WaitForSecondsRealtime(1.0f);

        if (WaitingToRemoveHeartglazeFlowerAfterQuestUpdate && !HeartglazeFlowerCleanupDone)
        {
            LogWarning($"{sourceTag}: Heartglaze still present after first cleanup attempt, retrying.");
            LogQuestGoals("Q_D_S_Flower", sourceTag + "/RetryHeartglazeCleanup");
            TryCleanupHeartglazeAfterQuestUpdate(sourceTag + "/QuestAwareCleanupRetry");
        }
    }

    internal static bool Dungeon01FinalDoorAlreadyOpened()
    {
        try
        {
            var progressionManager = MonoSingleton<ProgressionManager>.Instance;

            if (progressionManager == null || progressionManager.ProgressionData == null)
                return false;

            return
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_0") &&
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_1") &&
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_2");
        }
        catch (Exception ex)
        {
            LogError($"Dungeon01FinalDoorAlreadyOpened exception:\n{ex}");
            return false;
        }
    }

    internal static bool PlayerHasAllDungeon01PitKeys(D1_BossDoor door)
    {
        try
        {
            if (door == null)
                return false;

            var inventory = MonoSingleton<PlayerManager>.Instance.PlayerInventory;

            if (inventory == null)
                return false;

            var field = typeof(D1_BossDoor).GetField(
                "itemDatasNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (field == null)
            {
                LogWarning("PlayerHasAllDungeon01PitKeys: could not find D1_BossDoor.itemDatasNeeded.");
                return false;
            }

            ItemData[] neededItems = field.GetValue(door) as ItemData[];

            if (neededItems == null || neededItems.Length == 0)
            {
                LogWarning("PlayerHasAllDungeon01PitKeys: itemDatasNeeded was null or empty.");
                return false;
            }

            foreach (ItemData item in neededItems)
            {
                if (item == null || !inventory.HasItem(item, 1))
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"PlayerHasAllDungeon01PitKeys exception:\n{ex}");
            return false;
        }
    }

    internal static void ScheduleDelayedVanillaRewardRemoval(string itemId, int amount, string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: could not schedule vanilla reward removal for {itemId}; CoroutineRunner is null.");
            return;
        }

        LogInfo($"{sourceTag}: scheduled vanilla reward removal for {itemId} x{amount}.");

        CoroutineRunner.StartCoroutine(
            DelayedVanillaRewardRemovalCoroutine(itemId, amount, sourceTag)
        );
    }

    private static System.Collections.IEnumerator DelayedVanillaRewardRemovalCoroutine(
        string itemId,
        int amount,
        string sourceTag)
    {
        yield return null;
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemovalFrame1");

        yield return new WaitForSecondsRealtime(0.25f);
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemoval025");

        yield return new WaitForSecondsRealtime(0.75f);
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemoval100");
    }


    // Some AP softlocks happen right when a quest gets added or advanced.
    // I re-run the quest softlock reconciliation here so the fix can happen immediately
    // instead of waiting for a later zone load or queue wake-up.
    [HarmonyPatch(typeof(TryAddQuest), "OnEnter")]
    public class TryAddQuestReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("TryAddQuestReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryAddQuestReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryAddQuestReconcilePatch exception:\n{ex}");
            }
        }
    }

    // Some vanilla flows complete one goal and immediately move to the next.
    // If AP already gave the required item, that newly current goal can softlock on the spot.
    // Running the reconciliation here makes the fix happen much earlier.
    [HarmonyPatch(typeof(TryCompleteQuestGoal), "OnEnter")]
    public class TryCompleteQuestGoalReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("TryCompleteQuestGoalReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryCompleteQuestGoalReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryCompleteQuestGoalReconcilePatch exception:\n{ex}");
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

    [HarmonyPatch(typeof(InventoryManager), "AddItem", new Type[] {
    typeof(ItemData),
    typeof(int),
    typeof(Action),
    typeof(bool)
})]
    public class InventoryManager_AddItem_LoggerPatch
    {
        static void Prefix(ItemData item, int amount = 1, Action onAddedCallback = null, bool silent = false)
        {
            try
            {
                if (item == null)
                {
                    LaikaMod.LogInfo("[ITEM LOGGER] InventoryManager.AddItem called with null item.");
                    return;
                }

                LaikaMod.LogInfo(
                    $"[ITEM LOGGER] AddItem | ID: {item.id} | Name: {item.Name} | " +
                    $"Amount: {amount} | KeyItem: {item.IsKeyItem} | Recipe: {item.IsRecipeItem} | Silent: {silent}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_LoggerPatch exception:\n{ex}");
            }
        }
    }
}