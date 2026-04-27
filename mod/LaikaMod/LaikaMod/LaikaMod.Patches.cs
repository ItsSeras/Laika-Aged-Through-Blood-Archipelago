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
            ProcessPendingItemQueue("InitialItemGrant");
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

    // Tracks Renato map purchases as AP location checks.
    // Resolves the unlocked mapAreaID through the AP location registry.
    [HarmonyPatch(typeof(UnlockMapArea), "OnEnter")]
    public class UnlockMapAreaPatch
    {
        static void Prefix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                {
                    LaikaMod.LogWarning("UnlockMapAreaPatch: __instance was null.");
                    return;
                }

                // Read the raw internal mapAreaID for AP location resolution.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";

                // Log the raw map ID even if no AP location definition exists yet.
                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                {
                    LaikaMod.LogWarning(
                        $"UnlockMapAreaPatch: no AP location definition found for mapAreaID={mapAreaId}"
                    );
                    return;
                }

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch: exception while logging map unlock action:\n{ex}");
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

            if (!value)
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
    public class PuppyGiftKeyItemSourcePatch
    {
        static void Postfix(ItemData __0, bool __result)
        {
            try
            {
                // Ignore failed grants because they did not actually add the Puppy item.
                if (!__result)
                    return;

                // Harmony positional argument __0 is the original ItemData parameter.
                ItemData itemData = __0;

                if (itemData == null)
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemData was null.");
                    return;
                }

                string itemId = itemData.id;

                if (string.IsNullOrEmpty(itemId))
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemId was null or empty.");
                    return;
                }

                // Helpful debug log so we can confirm which Puppy items naturally route through AddKeyItem(...).
                LaikaMod.LogInfo(
                    $"KEY ITEM SOURCE DETECTED: id={itemId}, name={itemData.Name}, result={__result}"
                );

                // Only route known Puppy gift IDs into the AP Puppy location handler.
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
                    // Debug log so we can prove the Puppy gift is actually being routed
                    // into the AP Puppy location-check handler.
                    LaikaMod.LogInfo(
                        $"PuppyGiftKeyItemSourcePatch: routing puppy gift id {itemId} into TryHandlePuppyGiftLocationCheck."
                    );

                    LaikaMod.TryHandlePuppyGiftLocationCheck(itemId, "PuppyGiftKeyItemSourcePatch");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"PuppyGiftKeyItemSourcePatch exception:\n{ex}");
            }
        }
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
}