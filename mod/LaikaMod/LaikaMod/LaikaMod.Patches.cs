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
using UnityEngine.SceneManagement;

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

    internal static bool TryGetCassetteIdFromResourceDestructible(ResourceDestructible destructible, out string cassetteId)
    {
        cassetteId = null;

        try
        {
            if (destructible == null)
                return false;

            FieldInfo resourcesPoolField =
                typeof(ResourceDestructible).GetField(
                    "resourcesPool",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );

            if (resourcesPoolField == null)
                return false;

            ResourceData[] resourcesPool = resourcesPoolField.GetValue(destructible) as ResourceData[];

            if (resourcesPool == null || resourcesPool.Length == 0 || resourcesPool[0] == null)
                return false;

            if (resourcesPool[0].resourceObject == null)
                return false;

            ItemInstance itemInstance = resourcesPool[0].resourceObject.GetComponent<ItemInstance>();

            if (itemInstance == null || itemInstance.ItemData == null)
                return false;

            CassetteData cassetteData = itemInstance.ItemData as CassetteData;

            if (cassetteData == null)
                return false;

            cassetteId = cassetteData.id;
            return !string.IsNullOrEmpty(cassetteId);
        }
        catch (Exception ex)
        {
            LogWarning($"TryGetCassetteIdFromResourceDestructible failed:\n{ex}");
            return false;
        }
    }

    [HarmonyPatch(typeof(ResourceDestructible), "CanBeUsed")]
    public class ResourceDestructible_CanBeUsed_APCassetteSourcePatch
    {
        static void Postfix(ResourceDestructible __instance, ref bool __result)
        {
            try
            {
                if (__instance == null)
                    return;

                if (!(__instance is CassetteDestructible))
                    return;

                string cassetteId;
                if (!LaikaMod.TryGetCassetteIdFromResourceDestructible(__instance, out cassetteId))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                // If this cassette location has already been checked, the boombox should stay gone,
                // even if the cassette is not currently in vanilla cassette inventory.
                if (LaikaMod.HasSentLocationCheck(definition))
                {
                    __result = false;

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: hiding already-checked cassette source {cassetteId}."
                    );

                    return;
                }

                // If AP already gave this cassette but the physical source has not been checked,
                // force the source usable so the player can still shoot it and send the check.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                {
                    __result = true;

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: forcing source visible for AP-owned unchecked cassette {cassetteId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"ResourceDestructible_CanBeUsed_APCassetteSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassetteDestructible), "Destruction")]
    public class CassetteDestructible_Destruction_APOwnedCassetteSourcePatch
    {
        static void Prefix(CassetteDestructible __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                string cassetteId;
                if (!LaikaMod.TryGetCassetteIdFromResourceDestructible(__instance, out cassetteId))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                // This is the AP-owned cassette case:
                // vanilla leaves the boombox available because we forced it visible,
                // but the dropped cassette may not re-add because the player already owns it.
                // So breaking the boombox itself is enough to count the location.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "CassetteDestructible_Destruction_APOwnedCassetteSourcePatch",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: sent check on boombox destruction for AP-owned cassette {cassetteId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassetteDestructible_Destruction_APOwnedCassetteSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassetteAdder), "Start")]
    public class CassetteAdder_Start_APCassetteSourcePatch
    {
        private static readonly FieldInfo CassetteField =
            typeof(CassetteAdder).GetField(
                "cassette",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static bool Prefix(CassetteAdder __instance)
        {
            try
            {
                if (__instance == null || CassetteField == null)
                    return true;

                CassetteData cassette = CassetteField.GetValue(__instance) as CassetteData;

                if (cassette == null || string.IsNullOrEmpty(cassette.id))
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassette.id, out definition))
                    return true;

                if (LaikaMod.HasSentLocationCheck(definition))
                {
                    __instance.gameObject.SetActive(false);

                    LaikaMod.LogInfo(
                        $"AP CASSETTE ADDER: blocked already-checked cassette source {cassette.id}."
                    );

                    return false;
                }

                // If AP already gave the cassette and this source has not been checked,
                // send the location now and keep the AP-owned cassette.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassette.id))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "CassetteAdder_Start_APOwnedCassette",
                        false
                    );

                    __instance.gameObject.SetActive(false);

                    LaikaMod.LogInfo(
                        $"AP CASSETTE ADDER: sent check for AP-owned cassette source {cassette.id} without removing cassette."
                    );

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassetteAdder_Start_APCassetteSourcePatch exception:\n{ex}");
                return true;
            }
        }
    }

    internal static bool HasCassetteLocationBeenChecked(string cassetteId)
    {
        APLocationDefinition definition;

        if (!TryGetLocationDefinition(cassetteId, out definition))
            return false;

        return HasSentLocationCheck(definition);
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

    [HarmonyPatch(typeof(PersistenceManager), "OnSceneLoaded")]
    public class PersistenceManager_OnSceneLoaded_APReconcilePatch
    {
        static void Postfix(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene.name == "LoadingScreen")
                    return;

                if (MonoSingleton<SceneLoader>.Instance != null &&
                    MonoSingleton<SceneLoader>.Instance.CurrentSceneIsTitleScreen)
                {
                    return;
                }

                LaikaMod.LogInfo($"AP SCENE RECONCILE: scene loaded -> {scene.name}");
                LaikaMod.ScheduleSceneLoadedAPReconcile($"PersistenceManager.OnSceneLoaded/{scene.name}");
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PersistenceManager_OnSceneLoaded_APReconcilePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(MapArea), "OnEnable")]
    public class MapArea_OnEnable_APVisualUnlockPatch
    {
        private static readonly FieldInfo MapAreaIdField =
            typeof(MapArea).GetField(
                "mapAreaID",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(MapArea __instance)
        {
            try
            {
                if (__instance == null || MapAreaIdField == null)
                    return;

                string mapAreaId = MapAreaIdField.GetValue(__instance) as string;

                if (string.IsNullOrEmpty(mapAreaId))
                    return;

                if (!LaikaMod.IsAPMapAreaLocation(mapAreaId))
                    return;

                if (LaikaMod.HasAPMapUnlock(mapAreaId))
                {
                    __instance.Enable(true);

                    LaikaMod.LogInfo($"AP MAP VISUAL: forced visible on MapArea.OnEnable -> {mapAreaId}");
                    return;
                }

                if (LaikaMod.SessionState != null && LaikaMod.SessionState.APEnabled)
                {
                    __instance.Enable(false);

                    LaikaMod.LogInfo($"AP MAP VISUAL: forced hidden on MapArea.OnEnable because AP has not granted it -> {mapAreaId}");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"MapArea_OnEnable_APVisualUnlockPatch exception:\n{ex}");
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
        static bool Prefix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                string mapAreaId = __instance.mapAreaID != null
                    ? __instance.mapAreaID.Value
                    : "<null>";

                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                    return true;

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");

                LaikaMod.TryReconcileKnownQuestSoftlocks("UnlockMapAreaPatch");

                // Let vanilla continue so Renato's normal purchased/disappeared state is saved.
                // The Postfix/MapArea.OnEnable patches will control the actual map visual.
                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch.Prefix exception:\n{ex}");
                return true;
            }
        }

        static void Postfix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                string mapAreaId = __instance.mapAreaID != null
                    ? __instance.mapAreaID.Value
                    : "<null>";

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (!LaikaMod.IsAPMapAreaLocation(mapAreaId))
                    return;

                if (LaikaMod.HasAPMapUnlock(mapAreaId))
                {
                    LaikaMod.RefreshMapAreaVisuals(mapAreaId);

                    LaikaMod.LogInfo(
                        $"UnlockMapAreaPatch: vanilla purchase completed for {mapAreaId}; map kept visible because AP already granted it."
                    );
                }
                else
                {
                    LaikaMod.HideMapAreaVisuals(mapAreaId, "UnlockMapAreaPatch/Postfix");

                    LaikaMod.LogInfo(
                        $"UnlockMapAreaPatch: vanilla purchase completed for {mapAreaId}; map visual hidden because AP has not granted it yet."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"UnlockMapAreaPatch.Postfix exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(string), typeof(Action), typeof(bool) })]
    public class CassettesManager_AddCassetteById_JakobCollectionPatch
    {
        static void Prefix(string cassetteId, Action finishedCallback, bool silent)
        {
            try
            {
                if (LaikaMod.IsGrantingAPItem)
                    return;

                if (cassetteId != "I_COLLECTION_JAKOB")
                    return;

                var manager = Singleton<CassettesManager>.Instance;
                var loader = Singleton<CassettesDataLoader>.Instance;

                if (manager == null || loader == null || manager.CassettesInventory == null)
                {
                    LaikaMod.LogWarning("Jakob collection cassette pre-remove skipped because cassette manager/loader/inventory was null.");
                    return;
                }

                foreach (string childCassetteId in LaikaMod.JakobMusicCollectionCassetteIds)
                {
                    CassetteData childCassette = loader.FindCassette(childCassetteId);

                    if (childCassette == null)
                    {
                        LaikaMod.LogWarning($"Jakob collection cassette pre-remove could not find cassette {childCassetteId}.");
                        continue;
                    }

                    if (!manager.HasCassette(childCassette))
                        continue;

                    bool removed = manager.CassettesInventory.Remove(childCassette);

                    if (removed)
                    {
                        LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Add(childCassetteId);

                        LaikaMod.LogInfo(
                            $"Jakob collection: temporarily removed already-owned AP cassette {childCassetteId} so vanilla collection add can complete."
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassettesManager_AddCassetteById_JakobCollectionPatch.Prefix exception:\n{ex}");
            }
        }

        static void Postfix(string cassetteId, Action finishedCallback, bool silent, bool __result)
        {
            try
            {
                if (cassetteId != "I_COLLECTION_JAKOB")
                    return;

                if (__result)
                    return;

                // Fallback safety: if vanilla collection still failed after temporary removals,
                // restore anything we removed so the player does not lose AP-owned cassettes.
                var manager = Singleton<CassettesManager>.Instance;
                var loader = Singleton<CassettesDataLoader>.Instance;

                if (manager == null || loader == null)
                    return;

                foreach (string childCassetteId in LaikaMod.JakobMusicCollectionCassetteIds)
                {
                    if (!LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Remove(childCassetteId))
                        continue;

                    CassetteData childCassette = loader.FindCassette(childCassetteId);

                    if (childCassette == null || manager.HasCassette(childCassette))
                        continue;

                    bool previousGrantingState = LaikaMod.IsGrantingAPItem;
                    LaikaMod.IsGrantingAPItem = true;

                    try
                    {
                        manager.AddCassetteToInventory(childCassetteId, null, true);
                    }
                    finally
                    {
                        LaikaMod.IsGrantingAPItem = previousGrantingState;
                    }

                    LaikaMod.LogWarning(
                        $"Jakob collection: restored {childCassetteId} because vanilla collection add returned false."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassettesManager_AddCassetteById_JakobCollectionPatch.Postfix exception:\n{ex}");
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

                if (LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Remove(cassetteId))
                {
                    LaikaMod.LogInfo(
                        $"CASSETTE INVENTORY SOURCE DETECTED FOR AP-OWNED JAKOB COLLECTION TAPE: id={cassetteId}, silent={silent}, result={__result}"
                    );

                    APLocationDefinition keptDefinition;
                    if (LaikaMod.TryGetLocationDefinition(cassetteId, out keptDefinition))
                    {
                        LaikaMod.TrySendLocationCheck(
                            keptDefinition,
                            "CassetteInventoryRealSourcePatch/JakobCollectionAlreadyOwned",
                            false
                        );
                    }
                    else
                    {
                        LaikaMod.LogWarning(
                            $"CassetteInventoryRealSourcePatch: no AP location definition found for kept Jakob collection cassette {cassetteId}."
                        );
                    }

                    LaikaMod.LogInfo(
                        $"CassetteInventoryRealSourcePatch: kept {cassetteId} after Jakob collection re-add because it was already owned from AP."
                    );

                    try
                    {
                        MonoSingleton<PersistenceManager>.Instance.SaveGame();
                        LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: forced save after keeping Jakob collection AP cassette {cassetteId}.");
                    }
                    catch (Exception ex)
                    {
                        LaikaMod.LogWarning($"CassetteInventoryRealSourcePatch: save failed after keeping Jakob collection AP cassette {cassetteId}:\n{ex}");
                    }

                    return;
                }

                LaikaMod.LogInfo(
                    $"CASSETTE INVENTORY SOURCE DETECTED: id={cassetteId}, silent={silent}, result={__result}"
                );

                LaikaMod.TryHandleCassetteLocationCheck(cassetteId, "CassetteInventoryRealSourcePatch");

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                    return;

                if (LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                {
                    LaikaMod.TryRemoveCassetteReward(
                        cassetteId,
                        "CassetteInventoryRealSourcePatch"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"CassetteInventoryRealSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "RemoveItem", new Type[] { typeof(ItemData), typeof(int), typeof(bool) })]
    public class InventoryManager_RemoveItem_VanillaConsumeAPItemPatch
    {
        static void Postfix(ItemData item, int amount, bool silent, bool __result)
        {
            try
            {
                if (!__result || item == null || string.IsNullOrEmpty(item.id))
                    return;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                if (LaikaMod.SuppressVanillaConsumeTracking)
                    return;

                ItemKind kind;
                if (!LaikaMod.TryGetItemKindForInventoryLocation(item.id, out kind))
                    return;

                if (!LaikaMod.HasReceivedAPItem(kind, item.id))
                    return;

                LaikaMod.RememberVanillaConsumedAPItem(kind, item.id);

                LaikaMod.LogInfo(
                    $"VANILLA CONSUMED AP ITEM: id={item.id}, kind={kind}, amount={amount}, silent={silent}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"InventoryManager_RemoveItem_VanillaConsumeAPItemPatch exception:\n{ex}");
            }
        }
    }

    internal static bool IsAPMapAreaLocation(string mapAreaId)
    {
        if (string.IsNullOrEmpty(mapAreaId))
            return false;

        APLocationDefinition definition;
        if (!TryGetLocationDefinition(mapAreaId, out definition))
            return false;

        if (definition.DisplayName != null &&
            definition.DisplayName.StartsWith("Map Piece:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return
            definition.Category == "Map" ||
            definition.Category == "MapUnlock" ||
            definition.Category == "MapPiece";
    }

    internal static void HideMapAreaVisuals(string mapAreaId, string sourceTag)
    {
        try
        {
            if (string.IsNullOrEmpty(mapAreaId))
                return;

            FieldInfo mapAreaIdField =
                typeof(MapArea).GetField(
                    "mapAreaID",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

            if (mapAreaIdField == null)
            {
                LogWarning($"{sourceTag}: could not hide map visuals because MapArea.mapAreaID field was not found.");
                return;
            }

            int hiddenCount = 0;

            foreach (MapArea area in Resources.FindObjectsOfTypeAll<MapArea>())
            {
                if (area == null)
                    continue;

                string currentId = mapAreaIdField.GetValue(area) as string;

                if (currentId != mapAreaId)
                    continue;

                area.Enable(false);
                hiddenCount++;
            }

            LogInfo($"{sourceTag}: hid {hiddenCount} MapArea object(s) for vanilla-only map area {mapAreaId}.");
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: HideMapAreaVisuals failed for {mapAreaId}:\n{ex}");
        }
    }

    internal static readonly string[] JakobMusicCollectionCassetteIds =
{
    "I_CASSETTE_1", // Bloody Sunset
    "I_CASSETTE_2", // Playing in the Sun
    "I_CASSETTE_3", // Lullaby of the Dead
    "I_CASSETTE_4", // Blue Limbo
    "I_CASSETTE_5", // The Whisper
};

    internal static bool IsJakobMusicCollectionCassette(string cassetteId)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return false;

        foreach (string id in JakobMusicCollectionCassetteIds)
        {
            if (cassetteId == id)
                return true;
        }

        return false;
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

    internal static float BoneheadHookCaveBlockNoticeLastShownAt = -9999f;

    private static readonly FieldInfo DoorInteractionSceneToLoadField =
        typeof(DoorInteraction).GetField(
            "sceneToLoad",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    internal static bool HasProgressionFlag(string achievementId)
    {
        try
        {
            if (string.IsNullOrEmpty(achievementId))
                return false;

            var progressionManager = MonoSingleton<ProgressionManager>.Instance;

            if (progressionManager == null || progressionManager.ProgressionData == null)
                return false;

            return progressionManager.ProgressionData.HasAchievement(achievementId);
        }
        catch (Exception ex)
        {
            LogWarning($"HasProgressionFlag({achievementId}) failed:\n{ex}");
            return false;
        }
    }

    internal static bool HasAPHookUnlocked()
    {
        try
        {
            if (HasProgressionFlag("G_HOOK_UNLOCKED"))
                return true;

            var inventory = Singleton<InventoryManager>.Instance;

            if (inventory != null && inventory.HasItem("I_E_HOOK", 1))
                return true;

            return false;
        }
        catch (Exception ex)
        {
            LogWarning($"HasAPHookUnlocked failed:\n{ex}");
            return false;
        }
    }

    internal static string GetDoorSceneToLoad(DoorInteraction door)
    {
        try
        {
            if (door == null || DoorInteractionSceneToLoadField == null)
                return "";

            return DoorInteractionSceneToLoadField.GetValue(door) as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static bool IsBoneheadHookCaveDoor(DoorInteraction door)
    {
        if (door == null)
            return false;

        string sceneToLoad = GetDoorSceneToLoad(door);
        string doorId = door.ID ?? "";
        string objectName = door.gameObject != null ? door.gameObject.name ?? "" : "";

        return
            sceneToLoad.Contains("Tutorial_Hook") ||
            sceneToLoad.Contains("ZN_Tutorial_Hook") ||
            sceneToLoad.Contains("Where_Chaos_Plots") ||
            doorId.Contains("Tutorial_Hook") ||
            doorId.Contains("ZN_Tutorial_Hook") ||
            doorId.Contains("Where_Chaos_Plots") ||
            objectName.Contains("Tutorial_Hook") ||
            objectName.Contains("ZN_Tutorial_Hook") ||
            objectName.Contains("Where_Chaos_Plots");
    }

    internal static bool ShouldBlockBoneheadHookCaveDoor(DoorInteraction door, string sourceTag, bool announce)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return false;

            if (!IsBoneheadHookCaveDoor(door))
                return false;

            bool hasHook = HasAPHookUnlocked();

            if (hasHook)
                return false;

            if (announce)
            {
                LogInfo(
                    $"{sourceTag}: blocked Where Chaos Plots / Tutorial Hook cave entrance because AP Hook is not unlocked. " +
                    $"sceneToLoad={GetDoorSceneToLoad(door)}, doorId={door.ID}"
                );
            }

            if (announce)
            {
                float now = Time.unscaledTime;

                if (now - BoneheadHookCaveBlockNoticeLastShownAt >= 3.0f)
                {
                    BoneheadHookCaveBlockNoticeLastShownAt = now;

                    AnnounceAPWarning(
                        "[AP] Where Chaos Plots is blocked until Hook is unlocked. This prevents a vanilla hook softlock."
                    );
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: ShouldBlockBoneheadHookCaveDoor failed:\n{ex}");
            return false;
        }
    }

    [HarmonyPatch(typeof(Checkpoint), "CanInteract")]
    public class Checkpoint_CanInteract_APBoneheadHookCaveGatePatch
    {
        static void Postfix(Checkpoint __instance, ref bool __result)
        {
            try
            {
                if (!__result)
                    return;

                DoorInteraction door = __instance as DoorInteraction;

                if (door == null)
                    return;

                if (LaikaMod.ShouldBlockBoneheadHookCaveDoor(
                    door,
                    "Checkpoint_CanInteract_APBoneheadHookCaveGatePatch",
                    false
                ))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"Checkpoint_CanInteract_APBoneheadHookCaveGatePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(DoorInteraction), "Interact")]
    public class DoorInteraction_Interact_APBoneheadHookCaveGatePatch
    {
        static bool Prefix(DoorInteraction __instance)
        {
            try
            {
                if (LaikaMod.ShouldBlockBoneheadHookCaveDoor(
                    __instance,
                    "DoorInteraction_Interact_APBoneheadHookCaveGatePatch",
                    true
                ))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"DoorInteraction_Interact_APBoneheadHookCaveGatePatch exception:\n{ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddItem", new Type[] { typeof(ItemData), typeof(int), typeof(Action), typeof(bool) })]
    public class InventoryManager_AddItem_APLocationPatch
    {
        static bool Prefix(ItemData item, int amount, Action onAddedCallback, bool silent, ref bool __result)
        {
            try
            {
                if (item == null || string.IsNullOrEmpty(item.id))
                    return true;

                string sourceTag = "InventoryManager_AddItem_APLocationPatch";

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (LaikaMod.IsGrantingAPItem)
                {
                    LaikaMod.LogInfo($"InventoryManager_AddItem_APLocationPatch: allowed AP-granted item {item.id}.");
                    return true;
                }

                string itemId = item.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return true;


                if (itemId == "I_HOOK_BODY" &&
                    LaikaMod.SessionState != null &&
                    LaikaMod.SessionState.APEnabled &&
                    !LaikaMod.HasAPHookUnlocked())
                {
                    LaikaMod.LogWarning(
                        $"{sourceTag}: blocked early I_HOOK_BODY vanilla reward because Hook is not unlocked from AP yet."
                    );

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/EarlyHookBodyBlocked"
                    );

                    LaikaMod.AnnounceAPWarning(
                        "[AP] Hook Body check sent, but vanilla Hook unlock was blocked because Hook has not been received from AP."
                    );

                    __result = true;
                    return false;
                }

                // Key items often start quests or advance dialogue.
                // Do not block them here. Let AddKeyItem run, then handle AP check/removal there.
                if (definition.Category == "KeyItem")
                {
                    LaikaMod.LogInfo(
                        $"{sourceTag}: key item {itemId} allowed through AddItem so vanilla quest logic can run."
                    );

                    return true;
                }

                // Puppy Gifts are also dialogue/quest-sensitive.
                // Stargazer/Dreamcatcher specifically needs the real vanilla AddItem/AddKeyItem flow
                // so the original popup/callback/dialogue can complete.
                if (definition.Category == "PuppyGift")
                {
                    var inventory = Singleton<InventoryManager>.Instance;

                    if (inventory != null && inventory.HasItem(itemId, 1))
                    {
                        bool removed = LaikaMod.TryRemoveInventoryReward(
                            itemId,
                            amount > 0 ? amount : 1,
                            $"{sourceTag}/PreRemoveAlreadyOwnedPuppyGift"
                        );

                        if (removed)
                        {
                            LaikaMod.TemporarilyRemovedForVanillaReAdd.Add(itemId);

                            LaikaMod.LogInfo(
                                $"{sourceTag}: temporarily removed already-owned puppy gift {itemId} " +
                                "so vanilla AddItem can succeed and avoid dialogue softlock."
                            );
                        }
                    }

                    LaikaMod.LogInfo(
                        $"{sourceTag}: puppy gift {itemId} allowed through AddItem so vanilla dialogue/reward flow can run."
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

        static void Postfix(ItemData item, int amount, Action onAddedCallback, bool silent, bool __result)
        {
            try
            {
                if (item == null || string.IsNullOrEmpty(item.id))
                    return;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                string itemId = item.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                if (definition.Category != "KeyItem" &&
                    definition.Category != "Material" &&
                    definition.Category != "PuppyGift")
                {
                    return;
                }

                if (itemId == "I_PUPPY_FLOWER")
                    return;

                if (!__result)
                {
                    LaikaMod.LogInfo(
                        $"ADD ITEM LOCATION FALLBACK DETECTED AFTER VANILLA ADD FAILED/ALREADY OWNED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                    );

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PostfixAlreadyOwnedFallback"
                    );

                    if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                    {
                        var inventory = Singleton<InventoryManager>.Instance;

                        if (inventory != null && !inventory.HasItem(itemId, 1))
                        {
                            bool previousGrantingState = LaikaMod.IsGrantingAPItem;
                            LaikaMod.IsGrantingAPItem = true;

                            try
                            {
                                inventory.AddItem(item, amount > 0 ? amount : 1, null, true);
                            }
                            finally
                            {
                                LaikaMod.IsGrantingAPItem = previousGrantingState;
                            }

                            LaikaMod.LogWarning(
                                $"InventoryManager_AddItem_APLocationPatch: restored {itemId} after vanilla AddItem still failed following temporary removal."
                            );
                        }
                    }

                    return;
                }

                if (LaikaMod.IsHarpoonPieceId(itemId))
                {
                    LaikaMod.LogInfo(
                        $"HARPOON LOCATION FALLBACK DETECTED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                    );

                    if (LaikaMod.WasHarpoonPieceReceivedFromAP(itemId))
                    {
                        LaikaMod.TrySendLocationCheck(
                            definition,
                            "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup/APOwned",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"Physical harpoon pickup {itemId} sent AP check and stayed in inventory because that harpoon piece was already received from AP."
                        );

                        return;
                    }

                    if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                    {
                        LaikaMod.TrySendLocationCheck(
                            definition,
                            "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup/AlreadyOwnedReAdd",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"InventoryManager_AddItem_APLocationPatch: kept harpoon piece {itemId} after vanilla re-add because it was already owned before this vanilla reward."
                        );

                        return;
                    }

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup"
                    );

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup"
                    );

                    LaikaMod.LogInfo(
                        $"Physical harpoon pickup {itemId} sent AP check and scheduled vanilla reward removal because AP has not delivered it yet."
                    );

                    return;
                }

                LaikaMod.LogInfo(
                    $"ADD ITEM LOCATION FALLBACK DETECTED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                );

                if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/AlreadyOwnedReAdd",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"InventoryManager_AddItem_APLocationPatch: kept {itemId} after vanilla re-add because it was already owned before this vanilla reward."
                    );

                    return;
                }

                if (definition.Category == "PuppyGift")
                {
                    LaikaMod.TryHandlePuppyGiftLocationCheck(
                        itemId,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/PuppyGift",
                        true
                    );

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/PuppyGift"
                    );

                    LaikaMod.LogInfo(
                        $"InventoryManager_AddItem_APLocationPatch: puppy gift {itemId} was a vanilla location reward, so it was scheduled for removal after sending the AP check."
                    );

                    return;
                }

                LaikaMod.TrySendLocationCheck(
                    definition,
                    "InventoryManager_AddItem_APLocationPatch/PostfixFallback"
                );

                if (LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                {
                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_APLocationPatch.Postfix exception:\n{ex}");
            }
        }
    }

    internal static bool IsPitKeyId(string itemId)
    {
        return
            itemId == "I_D_Dungeon_01_door_piece_1" ||
            itemId == "I_D_Dungeon_01_door_piece_2" ||
            itemId == "I_D_Dungeon_01_door_piece_3";
    }

    internal static bool HasSentLocationCheck(APLocationDefinition definition)
    {
        if (definition == null)
            return false;

        if (SessionState == null || SessionState.SentLocationIds == null)
            return false;

        return SessionState.SentLocationIds.Contains(definition.LocationId);
    }

    [HarmonyPatch(typeof(ItemInstance), "Start")]
    public class ItemInstance_Start_APOwnedPitKeyVisibilityPatch
    {
        static void Postfix(ItemInstance __instance)
        {
            try
            {
                if (__instance == null || __instance.ItemData == null)
                    return;

                string itemId = __instance.ItemData.id;

                bool isPitKey = LaikaMod.IsPitKeyId(itemId);
                bool isHarpoonPiece = LaikaMod.IsHarpoonPieceId(itemId);

                if (!isPitKey && !isHarpoonPiece)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                // If the location was already checked, vanilla hiding is fine.
                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                bool shouldForceVisible = false;

                if (isPitKey && LaikaMod.HasReceivedAPItem(ItemKind.KeyItem, itemId))
                {
                    shouldForceVisible = true;
                }

                if (isHarpoonPiece && LaikaMod.WasHarpoonPieceReceivedFromAP(itemId))
                {
                    shouldForceVisible = true;
                }

                if (!shouldForceVisible)
                    return;

                if (!__instance.gameObject.activeSelf)
                {
                    __instance.gameObject.SetActive(true);
                }

                if (isPitKey)
                {
                    LaikaMod.LogInfo(
                        $"PIT KEY PICKUP: forced visible for AP-owned unchecked pit key {itemId}."
                    );
                }
                else if (isHarpoonPiece)
                {
                    LaikaMod.LogInfo(
                        $"HARPOON PICKUP: forced visible for AP-owned unchecked harpoon piece {itemId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"ItemInstance_Start_APOwnedPitKeyVisibilityPatch exception:\n{ex}");
            }
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

    [HarmonyPatch(typeof(PuppyGiftPickItem), "Start")]
    public class PuppyGiftPickItem_Start_APOwnedUncheckedGiftPatch
    {
        private static readonly FieldInfo GiftField =
            typeof(PuppyGiftPickItem).GetField(
                "gift",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        private static readonly FieldInfo GfxField =
            typeof(PuppyGiftPickItem).GetField(
                "gfx",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(PuppyGiftPickItem __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (GiftField == null || GfxField == null)
                    return;

                ItemData gift = GiftField.GetValue(__instance) as ItemData;
                if (gift == null || string.IsNullOrEmpty(gift.id))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(gift.id, out definition))
                    return;

                if (definition.Category != "PuppyGift")
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                if (!LaikaMod.HasReceivedAPItem(ItemKind.PuppyTreat, gift.id))
                    return;

                SpriteRenderer gfx = GfxField.GetValue(__instance) as SpriteRenderer;
                if (gfx != null)
                {
                    gfx.enabled = true;
                }

                LaikaMod.LogInfo(
                    $"PUPPY GIFT PICKUP: kept AP-owned unchecked gift visible -> {gift.id}, location={definition.DisplayName}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PuppyGiftPickItem_Start_APOwnedUncheckedGiftPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PuppyGiftPickItem), "CanInteract")]
    public class PuppyGiftPickItem_CanInteract_APOwnedUncheckedGiftPatch
    {
        private static readonly FieldInfo GiftField =
            typeof(PuppyGiftPickItem).GetField(
                "gift",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(PuppyGiftPickItem __instance, ref bool __result)
        {
            try
            {
                if (__result)
                    return;

                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (GiftField == null)
                    return;

                ItemData gift = GiftField.GetValue(__instance) as ItemData;
                if (gift == null || string.IsNullOrEmpty(gift.id))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(gift.id, out definition))
                    return;

                if (definition.Category != "PuppyGift")
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                if (!LaikaMod.HasReceivedAPItem(ItemKind.PuppyTreat, gift.id))
                    return;

                __result = true;

                LaikaMod.LogInfo(
                    $"PUPPY GIFT PICKUP: allowing AP-owned unchecked gift interaction -> {gift.id}, location={definition.DisplayName}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PuppyGiftPickItem_CanInteract_APOwnedUncheckedGiftPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddKeyItem", new Type[] { typeof(ItemData) })]
    public class KeyItemLocationSourcePatch
    {
        static bool Prefix(ItemData __0, ref bool __result)
        {
            try
            {
                if (__0 == null || string.IsNullOrEmpty(__0.id))
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (LaikaMod.IsGrantingAPItem)
                    return true;

                string itemId = __0.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return true;

                if (definition.Category != "PuppyGift")
                    return true;

                // Important:
                // Do not block AddKeyItem for Puppy Gifts anymore.
                // Stargazer/Dreamcatcher needs vanilla AddKeyItem to really run so the original
                // reward popup/callback/dialogue flow can complete.
                LaikaMod.LogInfo(
                    $"KeyItemLocationSourcePatch.Prefix: allowing puppy gift AddKeyItem to run normally -> {itemId}"
                );

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"KeyItemLocationSourcePatch Prefix exception:\n{ex}");
                return true;
            }
        }

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
                    bool wasTemporarilyRemovedForVanillaReAdd =
                        LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId);

                    if (wasTemporarilyRemovedForVanillaReAdd)
                    {
                        LaikaMod.TryHandlePuppyGiftLocationCheck(
                            itemId,
                            "KeyItemLocationSourcePatch/AlreadyOwnedPuppyGiftReAdd",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"KeyItemLocationSourcePatch: kept already-owned puppy gift {itemId} after vanilla re-add."
                        );
                    }
                    else
                    {
                        LaikaMod.TryHandlePuppyGiftLocationCheck(
                            itemId,
                            "KeyItemLocationSourcePatch",
                            true
                        );
                    }

                    return;
                }
                else
                {
                    LaikaMod.TrySendLocationCheck(definition, "KeyItemLocationSourcePatch");
                }

                if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Contains(itemId))
                {
                    LaikaMod.LogInfo(
                        $"KeyItemLocationSourcePatch: not scheduling vanilla reward removal for {itemId} because it was temporarily removed for vanilla re-add."
                    );

                    return;
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