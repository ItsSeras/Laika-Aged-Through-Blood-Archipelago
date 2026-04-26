using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.Quests.Goals;
using Laika.Quests.PlayMaker.FsmActions;
using Laika.UI;
using Laika.UI.InGame.Inventory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Runtime.InteropServices;

// AP save-state, per-slot persistence, and connection-state helpers.
[BepInPlugin("com.seras.laikaapprototype", "Laika AP Alpha", "0.01")]
public partial class LaikaMod : BaseUnityPlugin
{
    // Shared logger for Harmony patches.
    internal static ManualLogSource Log;

    internal static LaikaMod Instance;

    // Queue of pending Archipelago-style received items.
    internal static Queue<PendingItem> PendingItemQueue = new Queue<PendingItem>();

    // Prevent nested queue processing when UI refreshes trigger more hooks.
    internal static bool IsProcessingQueue = false;

    // NOTE: Ingredient & Cassette logging is currently inactive, but kept for future item-id discovery logging.
    // Prevents ingredient IDs from being logged more than once.
    // Without this, every UI refresh would spam the console repeatedly.
    internal static bool IngredientIdsLogged = false;

    // Prevents cassette IDs from logging repeatedly.
    internal static bool CassetteIdsLogged = false;

    // Tracks deaths during the current AP session only.
    internal static int LocalDeathsThisSession = 0;

    // Tracks deaths since the last DeathLink would send.
    internal static int DeathsSinceLastDeathLink = 0;

    // One-shot suppression counter for inbound DeathLink kills.
    // When set to 1, the next detected death will not count toward outbound DeathLink logic.
    internal static int SuppressedDeathLinksRemaining = 0;

    // Runtime AP world options loaded from slot_data cache.
    // Later this can be filled directly from a live AP Connected packet.
    internal static APWorldOptions WorldOptions = new APWorldOptions();

    // Development stress test toggle.
    // I turn this on when I want to force a batch of received items through the queue without needing a live AP send.
    internal static bool EnableDevelopmentStressTest = false;

    // Canvas-based dev overlay objects.
    internal static GameObject DevOverlayCanvasObject;
    internal static Text DevOverlayStatusText;
    internal static Text DevOverlayRecentLogText;

    internal static GameObject TitleAPPanelCanvasObject;
    internal static GameObject TitleAPPanelObject;
    internal static TMPro.TextMeshProUGUI TitleAPPanelText;

    internal static GameObject DevOverlayControllerObject;
    internal static DevOverlayController ActiveDevOverlayController;

    internal static bool HasAppliedLiveSlotData = false;

    public bool LocalDeathLinkOverrideEnabled = false;
    public bool LocalDeathLinkEnabled = false;

    public bool LocalDeathAmnestyOverrideEnabled = false;
    public bool LocalDeathAmnestyEnabled = false;
    public int LocalDeathAmnestyCount = 1;

    internal static int ActiveSaveSlotIndex = 0;

    internal static APSaveState SessionState = new APSaveState
    {
        SaveSlotIndex = 0,
        APEnabled = false,
        Connection = new APConnectionState()
    };

    // ===== Startup =====
    private void Awake()
    {
        Instance = this;

        Log = Logger;
        LogInfo("AP LIFECYCLE: Awake() entered.");

        ActiveSaveSlotIndex = 0;
        LoadSessionStateForSlot(ActiveSaveSlotIndex);

        new ArchipelagoClientManager();

        // Local cached slot_data fallback intentionally disabled.
        // Live AP slot_data should be the real source.

        enabled = true;

        LogInfo("AP TITLE PANEL: Main LaikaMod OnGUI support compiled in.");

        Log.LogInfo(
            $"Laika AP Prototype loaded. " +
            $"WeaponMode={WorldOptions.WeaponMode}, " +
            $"DevStress={EnableDevelopmentStressTest}, " +
            $"DeathLink={WorldOptions.DeathLinkEnabled}, " +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled}, " +
            $"DeathAmnestyCount={WorldOptions.DeathAmnestyCount}"
        );

        EnqueueRequiredStartingItems();

        if (EnableDevelopmentStressTest)
        {
            EnqueueDevelopmentStressTestItems();
        }

        Harmony harmony = new Harmony("com.seras.laikaapprototype");
        harmony.PatchAll();
        Log.LogInfo("Harmony patches applied.");

        foreach (var method in harmony.GetPatchedMethods())
        {
            LogInfo("PATCHED: " + method.DeclaringType.FullName + "." + method.Name);
        }
    }

    internal static void UpdateTitleScreenAPPanel()
    {
        //LogInfo("AP TITLE PANEL: UpdateTitleScreenAPPanel entered. SavePickerOpen=" + TitleScreenSavePickerOpen);

        if (!TitleScreenSavePickerOpen)
        {
            if (TitleAPPanelCanvasObject != null)
                TitleAPPanelCanvasObject.SetActive(false);

            return;
        }

        //LogInfo("AP TITLE PANEL: UpdateTitleScreenAPPanel running.");

        EnsureTitleScreenAPPanelExists();

        if (TitleAPPanelCanvasObject != null && !TitleAPPanelCanvasObject.activeSelf)
            TitleAPPanelCanvasObject.SetActive(true);

        int slotIndex = Mathf.Clamp(TitleScreenHighlightedSlotIndex, 0, 2);
        APSaveState state = PeekSessionStateForSlot(slotIndex);

        //LogInfo("AP TITLE PANEL: updating slot " + slotIndex);

        if (TitleAPPanelText == null)
            return;

        if (state == null)
        {
            TitleAPPanelText.text = "Archipelago\n\nNo AP state found.";
            return;
        }

        APConnectionState connection = state.Connection ?? new APConnectionState();

        bool ready =
            state.APEnabled &&
            !string.IsNullOrWhiteSpace(connection.Host) &&
            connection.Port > 0 &&
            !string.IsNullOrWhiteSpace(connection.SlotName);

        string statusText = BuildSaveSlotAPSummaryShort(slotIndex);

        string statusColor =
            !state.APEnabled ? "#9AA8B8" :
            ready ? "#59FF84" :
            "#DC143C";

        TitleAPPanelText.text =
            "<size=78><b>Archipelago Edition</b></size>\n\n" +
            "Save Slot " + (slotIndex + 1) + "\n\n" +
            "Status: <color=" + statusColor + ">" + statusText + "</color>\n" +
        "Host: " + (string.IsNullOrWhiteSpace(connection.Host)
            ? "<empty>"
            : connection.Host) + "\n" +
        "Port: " + (connection.Port > 0
            ? connection.Port.ToString()
            : "<empty>") + "\n" +
        "Slot: " + (string.IsNullOrWhiteSpace(connection.SlotName)
            ? "<empty>"
            : connection.SlotName) + "\n" +
        "Password: " + (string.IsNullOrWhiteSpace(connection.Password)
            ? "<empty>"
            : "<configured>");
    }

    internal static void RefreshVisibleSaveSlotAPLabels()
    {
        foreach (var label in UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
        {
            if (label == null)
                continue;

            if (label.gameObject.name != "APStatusLabel")
                continue;

            SaveSlotItem saveSlot = label.GetComponentInParent<SaveSlotItem>();
            if (saveSlot == null)
                continue;

            label.text = BuildSaveSlotAPSummaryShort(saveSlot.Idx);
            label.color = GetSaveSlotAPColor(saveSlot.Idx);
            label.gameObject.SetActive(true);
        }
    }

    internal static bool IsAPSettingsInputFocused()
    {
        if (!ShowAPSettingsPopup)
            return false;

        if (APSettingsTextInputActive)
            return true;

        if (EventSystem.current == null)
            return false;

        GameObject selected = EventSystem.current.currentSelectedGameObject;

        if (selected == null)
            return false;

        return selected.GetComponent<TMP_InputField>() != null;
    }

    internal static void EnsureTitleScreenAPPanelExists()
    {
        if (TitleAPPanelCanvasObject != null && TitleAPPanelText != null)
            return;

        LogInfo("AP TITLE PANEL: creating title canvas panel.");

        TitleAPPanelCanvasObject = new GameObject("LaikaAPTitlePanelCanvas");
        DontDestroyOnLoad(TitleAPPanelCanvasObject);

        Canvas canvas = TitleAPPanelCanvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;

        TitleAPPanelCanvasObject.AddComponent<CanvasScaler>();
        TitleAPPanelCanvasObject.AddComponent<GraphicRaycaster>();

        TitleAPPanelObject = new GameObject("LaikaAPTitlePanel");
        TitleAPPanelObject.transform.SetParent(TitleAPPanelCanvasObject.transform, false);

        RectTransform panelRect = TitleAPPanelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 0.5f);
        panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-50f, -5f);
        panelRect.sizeDelta = new Vector2(470f, 290f);

        Image panelImage = TitleAPPanelObject.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0f);

        GameObject textObject = new GameObject("LaikaAPTitlePanelText");
        textObject.transform.SetParent(TitleAPPanelObject.transform, false);

        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(22f, 18f);
        textRect.offsetMax = new Vector2(-22f, -18f);

        TitleAPPanelText = textObject.AddComponent<TMPro.TextMeshProUGUI>();

        GameObject buttonObject = new GameObject("LaikaAPTitleSettingsButton");
        buttonObject.transform.SetParent(TitleAPPanelCanvasObject.transform, false);

        RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(1f, 0f);
        buttonRect.anchoredPosition = new Vector2(-515f, 39f);
        buttonRect.sizeDelta = new Vector2(170f, 38f);

        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0f, 0f, 0f, 0f);
        buttonImage.raycastTarget = true;

        TitleAPButton = buttonObject.AddComponent<Button>();
        TitleAPButton.onClick.AddListener(() =>
        {
            OpenTitleScreenAPSettingsForHighlightedSlot();
        });

        GameObject buttonTextObject = new GameObject("LaikaAPTitleSettingsButtonText");
        buttonTextObject.transform.SetParent(buttonObject.transform, false);

        RectTransform buttonTextRect = buttonTextObject.AddComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;

        TitleAPButtonText = buttonTextObject.AddComponent<TMPro.TextMeshProUGUI>();
        TitleAPButtonText.text = "[F1] AP Config";
        TitleAPButtonText.fontSize = 42;
        TitleAPButtonText.enableWordWrapping = false;
        TitleAPButtonText.overflowMode = TMPro.TextOverflowModes.Overflow;
        TitleAPButtonText.alignment = TextAlignmentOptions.Center;
        TitleAPButtonText.alignment = TMPro.TextAlignmentOptions.Center;
        TitleAPButtonText.color = Color.white;

        TitleAPButtonObject = buttonObject;

        TMPro.TMP_Text[] tmpTexts = Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>();

        foreach (TMPro.TMP_Text tmp in tmpTexts)
        {
            if (tmp != null &&
                tmp.text != null &&
                tmp.text.Contains("New Game") &&
                tmp.font != null)
            {
                Log.LogInfo("AP TITLE PANEL: using TMP font from New Game: " + tmp.font.name);
                TitleAPPanelText.font = tmp.font;
                if (TitleAPButtonText != null)
                    TitleAPButtonText.font = tmp.font;
                break;
            }
        }


        TitleAPPanelText.fontSize = 48;
        TitleAPPanelText.characterSpacing = 3.5f;
        TitleAPPanelText.lineSpacing = 1.0f;
        TitleAPPanelText.enableWordWrapping = false;
        TitleAPPanelText.richText = true;
        TitleAPPanelText.alignment = TextAlignmentOptions.MidlineRight;
        TitleAPPanelText.enableWordWrapping = false;
        TitleAPPanelText.overflowMode = TMPro.TextOverflowModes.Overflow;
        TitleAPPanelText.color = Color.white;
    }

    internal static void OpenTitleScreenAPSettingsForHighlightedSlot()
    {
        int slotIndex = Mathf.Clamp(TitleScreenHighlightedSlotIndex, 0, 2);

        ActiveSaveSlotIndex = slotIndex;
        LoadSessionStateForSlot(slotIndex);

        ShowAPSettingsPopup = true;
        APSettingsTextInputActive = false;

        SetTitleScreenInputLocked(true);
        SetTitleScreenNavigationBlocked(true);
        SetTitleScreenSelectablesLocked(true);
        SetTitleScreenUINavigationLocked(true);

        if (APSettingsPanelObject != null)
        {
            UnityEngine.Object.Destroy(APSettingsPanelObject);
            APSettingsPanelObject = null;
        }

        EnsureAPSettingsPanelExists();
        APSettingsPanelObject.SetActive(true);
        UpdateTitleScreenAPPanel();
        RefreshVisibleSaveSlotAPLabels();

        LogInfo("AP TITLE: AP settings panel opened for slot " + slotIndex);
    }

    internal static void EnsureAPSettingsPanelExists()
    {
        if (APSettingsPanelObject != null)
            return;

        if (TitleAPPanelCanvasObject == null)
            EnsureTitleScreenAPPanelExists();

        APSettingsPanelObject = new GameObject("LaikaAPSettingsPanel");
        APSettingsPanelObject.transform.SetParent(TitleAPPanelCanvasObject.transform, false);

        RectTransform panelRect = APSettingsPanelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(60f, -60f);
        panelRect.sizeDelta = new Vector2(640f, 860f);

        Image panelImage = APSettingsPanelObject.AddComponent<Image>();
        APSettingsPanelObject.AddComponent<CanvasGroup>();
        panelImage.color = new Color(0f, 0f, 0f, 0.84f);

        GameObject titleObject = new GameObject("LaikaAPSettingsPanelTitle");
        titleObject.transform.SetParent(APSettingsPanelObject.transform, false);

        RectTransform titleRect = titleObject.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -18f);
        titleRect.sizeDelta = new Vector2(-40f, 72f);

        TMP_Text titleText = titleObject.AddComponent<TextMeshProUGUI>();
        titleText.text = "Archipelago Settings\n<size=22>Editing Save Slot " + (ActiveSaveSlotIndex + 1) + "</size>";
        titleText.fontSize = 34f;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;
        titleText.enableWordWrapping = false;
        titleText.overflowMode = TextOverflowModes.Overflow;
        titleText.font = TitleAPPanelText != null ? TitleAPPanelText.font : titleText.font;

        GameObject closeObject = new GameObject("LaikaAPSettingsCloseButton");
        closeObject.transform.SetParent(APSettingsPanelObject.transform, false);

        RectTransform closeRect = closeObject.AddComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-16f, -12f);
        closeRect.sizeDelta = new Vector2(60f, 44f);

        Image closeImage = closeObject.AddComponent<Image>();
        closeImage.color = new Color(0f, 0f, 0f, 0.01f);

        Button closeButton = closeObject.AddComponent<Button>();
        closeButton.onClick.AddListener(() =>
        {
            ShowAPSettingsPopup = false;
            APSettingsTextInputActive = false;
            SetTitleScreenInputLocked(false);
            SetTitleScreenNavigationBlocked(false);
            SetTitleScreenSelectablesLocked(false);
            SetTitleScreenUINavigationLocked(false);

            if (APSettingsPanelObject != null)
                APSettingsPanelObject.SetActive(false);

            LogInfo("AP TITLE: AP settings window closed.");
        });

        GameObject closeTextObject = new GameObject("LaikaAPSettingsCloseText");
        closeTextObject.transform.SetParent(closeObject.transform, false);

        RectTransform closeTextRect = closeTextObject.AddComponent<RectTransform>();
        closeTextRect.anchorMin = Vector2.zero;
        closeTextRect.anchorMax = Vector2.one;
        closeTextRect.offsetMin = Vector2.zero;
        closeTextRect.offsetMax = Vector2.zero;

        TMP_Text closeText = closeTextObject.AddComponent<TextMeshProUGUI>();
        closeText.text = "X";
        closeText.fontSize = 30f;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.color = Color.white;
        //closeText.font = TitleAPPanelText != null ? TitleAPPanelText.font : closeText.font;

        APSettingsPanelObject.SetActive(false);

        GameObject contentObject = new GameObject("LaikaAPSettingsContent");
        contentObject.transform.SetParent(APSettingsPanelObject.transform, false);

        RectTransform contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = new Vector2(0f, -95f);
        contentRect.sizeDelta = new Vector2(-50f, 660f);

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = 10f;
        layout.padding = new RectOffset(20, 20, 10, 10);
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        AddAPToggleButton(contentObject.transform, "Enable Archipelago",
            () => SessionState.APEnabled,
            value =>
            {
                SessionState.APEnabled = value;
                SaveSessionStateForSlot(ActiveSaveSlotIndex);
                UpdateTitleScreenAPPanel();
                RefreshVisibleSaveSlotAPLabels();
            });

        AddAPInput(contentObject.transform, "Host", SessionState.Connection.Host, value =>
        {
            SessionState.Connection.Host = value.Trim();
            SaveSessionStateForSlot(ActiveSaveSlotIndex);
            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });

        AddAPInput(contentObject.transform, "Port", SessionState.Connection.Port > 0 ? SessionState.Connection.Port.ToString() : "", value =>
        {
            value = value.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                SessionState.Connection.Port = 0;
            }
            else if (int.TryParse(value, out int parsedPort))
            {
                SessionState.Connection.Port = parsedPort;
            }
            else
            {
                SessionState.Connection.Port = 0;
            }

            SaveSessionStateForSlot(ActiveSaveSlotIndex);
            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });

        AddAPInput(contentObject.transform, "Slot Name", SessionState.Connection.SlotName, value =>
        {
            SessionState.Connection.SlotName = value.Trim();
            SaveSessionStateForSlot(ActiveSaveSlotIndex);
            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });

        AddAPInput(contentObject.transform, "Password", SessionState.Connection.Password, value =>
        {
            SessionState.Connection.Password = value;
            SaveSessionStateForSlot(ActiveSaveSlotIndex);
            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });

        AddAPToggleButton(contentObject.transform, "DeathLink",
            () => SessionState.Options.DeathLinkEnabled,
            value =>
            {
                SessionState.Options.DeathLinkEnabled = value;
                SaveSessionStateForSlot(ActiveSaveSlotIndex);
                UpdateTitleScreenAPPanel();
                RefreshVisibleSaveSlotAPLabels();
            });

        AddAPToggleButton(contentObject.transform, "Death Amnesty", () => SessionState.Options.DeathAmnestyEnabled, value =>
        {
            SessionState.Options.DeathAmnestyEnabled = value;
            SaveSessionStateForSlot(ActiveSaveSlotIndex);

            if (APSettingsPanelObject != null)
            {
                UnityEngine.Object.Destroy(APSettingsPanelObject);
                APSettingsPanelObject = null;
            }

            EnsureAPSettingsPanelExists();
            APSettingsPanelObject.SetActive(true);

            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });

        if (SessionState.Options.DeathAmnestyEnabled)
        {
            AddAPInput(contentObject.transform, "Death Amnesty Count", SessionState.Options.DeathAmnestyCount.ToString(), value =>
            {
                if (int.TryParse(value, out int parsedAmount))
                {
                    SessionState.Options.DeathAmnestyCount = Mathf.Clamp(parsedAmount, 0, 99);
                    SaveSessionStateForSlot(ActiveSaveSlotIndex);
                    UpdateTitleScreenAPPanel();
                    RefreshVisibleSaveSlotAPLabels();
                }
            });
        }

        AddAPActionButton(contentObject.transform, "Connect", () =>
        {
            SaveSessionStateForSlot(ActiveSaveSlotIndex);
            ConnectActiveSlotIfConfigured();
            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });
        AddAPActionButton(contentObject.transform, "Disconnect", () =>
        {
            if (ArchipelagoClientManager.Instance != null)
            {
                ArchipelagoClientManager.Instance.Disconnect("User requested disconnect from title screen");
            }

            UpdateTitleScreenAPPanel();
            RefreshVisibleSaveSlotAPLabels();
        });
    }

    private static TextMeshProUGUI AddAPLabel(Transform parent, string text)
    {
        GameObject obj = new GameObject("APSettingsLabel");
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 26f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Left;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 32f);

        return label;
    }

    private static TMP_InputField AddAPInput(Transform parent, string labelText, string value, Action<string> onChanged)
    {
        AddAPLabel(parent, labelText);

        GameObject inputObj = new GameObject("APSettingsInput");
        inputObj.transform.SetParent(parent, false);

        Image image = inputObj.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.45f);

        TMP_InputField input = inputObj.AddComponent<TMP_InputField>();

        Navigation noNav = new Navigation();
        noNav.mode = Navigation.Mode.None;

        Selectable selectable = input.GetComponent<Selectable>();
        if (selectable != null)
        {
            selectable.navigation = noNav;
        }

        input.lineType = TMP_InputField.LineType.SingleLine;
        input.richText = false;

        RectTransform rect = inputObj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 42f);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(inputObj.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 24f;
        text.color = Color.white;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 4f);
        textRect.offsetMax = new Vector2(-10f, -4f);

        input.textComponent = text;
        input.text = value ?? "";
        input.onValueChanged.AddListener(v => onChanged(v));

        input.onSelect.AddListener(_ =>
        {
            APSettingsTextInputActive = true;

            if (EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = false;
            }

            LogInfo("AP TITLE: input selected, title controls blocked.");
        });

        input.onDeselect.AddListener(_ =>
        {
            APSettingsTextInputActive = false;

            if (ShowAPSettingsPopup)
                SetTitleScreenNavigationBlocked(true);
            else
                SetTitleScreenNavigationBlocked(false);

            LogInfo("AP TITLE: input deselected.");
        });

        return input;
    }

    private static void AddAPToggleButton(Transform parent, string label, Func<bool> getValue, Action<bool> setValue)
    {
        GameObject obj = new GameObject("APSettingsToggle_" + label);
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);

        Button button = obj.AddComponent<Button>();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 42f);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.fontSize = 24f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.font = TitleAPPanelText != null ? TitleAPPanelText.font : text.font;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Action refresh = () =>
        {
            text.text = label + ": " + (getValue() ? "ON" : "OFF");
        };

        button.onClick.AddListener(() =>
        {
            bool next = !getValue();
            setValue(next);
            refresh();
        });

        refresh();
    }

    private static void AddAPActionButton(Transform parent, string label, Action onClick)
    {
        GameObject obj = new GameObject("APSettingsButton_" + label);
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        Button button = obj.AddComponent<Button>();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 44f);

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(obj.transform, false);

        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 26f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.font = TitleAPPanelText != null ? TitleAPPanelText.font : text.font;

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        button.onClick.AddListener(() =>
        {
            onClick?.Invoke();
        });
    }

    internal static void SetTitleScreenNavigationBlocked(bool blocked)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        if (blocked)
        {
            TitleScreenNavigationWasEnabled = eventSystem.sendNavigationEvents;
            eventSystem.sendNavigationEvents = false;
            eventSystem.SetSelectedGameObject(null);
        }
        else
        {
            eventSystem.sendNavigationEvents = TitleScreenNavigationWasEnabled;
        }
    }

    private static void DrawTitleScreenAPSettingsWindow(int windowId)
    {
        int slotIndex = Mathf.Clamp(ActiveSaveSlotIndex, 0, 2);

        if (SessionState == null || SessionState.SaveSlotIndex != slotIndex)
            LoadSessionStateForSlot(slotIndex);

        if (SessionState.Connection == null)
            SessionState.Connection = new APConnectionState();

        if (SessionState.Options == null)
            SessionState.Options = new APWorldOptions();

        GUILayout.Label("Save Slot " + (slotIndex + 1));
        GUILayout.Space(10);

        SessionState.APEnabled = GUILayout.Toggle(SessionState.APEnabled, "Enable Archipelago");

        GUILayout.Space(8);

        GUILayout.Label("Host");
        SessionState.Connection.Host = GUILayout.TextField(SessionState.Connection.Host ?? "");

        GUILayout.Label("Port");
        string portText = GUILayout.TextField(SessionState.Connection.Port > 0 ? SessionState.Connection.Port.ToString() : "");
        if (int.TryParse(portText, out int parsedPort))
            SessionState.Connection.Port = parsedPort;

        GUILayout.Label("Slot Name");
        SessionState.Connection.SlotName = GUILayout.TextField(SessionState.Connection.SlotName ?? "");

        GUILayout.Label("Password");
        SessionState.Connection.Password = GUILayout.PasswordField(SessionState.Connection.Password ?? "", '*');

        GUILayout.Space(12);

        SessionState.Options.DeathLinkEnabled =
            GUILayout.Toggle(SessionState.Options.DeathLinkEnabled, "DeathLink");

        SessionState.Options.DeathAmnestyEnabled =
            GUILayout.Toggle(SessionState.Options.DeathAmnestyEnabled, "Death Amnesty");

        if (SessionState.Options.DeathAmnestyEnabled)
        {
            GUILayout.Label("Death Amnesty Count");
            string amnestyText = GUILayout.TextField(SessionState.Options.DeathAmnestyCount.ToString());

            if (int.TryParse(amnestyText, out int parsedAmnesty))
                SessionState.Options.DeathAmnestyCount = Mathf.Clamp(parsedAmnesty, 0, 99);
        }

        GUILayout.Space(16);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Save AP Settings"))
        {
            SessionState.SaveSlotIndex = slotIndex;
            SaveSessionStateForSlot(slotIndex);
            ShowAPSettingsPopup = false;
            APSettingsTextInputActive = false;
            SetTitleScreenInputLocked(false);
            SetTitleScreenNavigationBlocked(false);
            SetTitleScreenSelectablesLocked(false);
            SetTitleScreenUINavigationLocked(false);

            LogInfo("AP TITLE: saved AP settings for slot " + slotIndex);
        }

        if (GUILayout.Button("Cancel"))
        {
            ShowAPSettingsPopup = false;
            APSettingsTextInputActive = false;
            SetTitleScreenInputLocked(false);
            SetTitleScreenNavigationBlocked(false);
            SetTitleScreenSelectablesLocked(false);
            SetTitleScreenUINavigationLocked(false);
        }

        GUILayout.EndHorizontal();

        GUI.DragWindow();
    }

    internal static void HideTitleScreenAPPanel()
    {
        if (TitleAPPanelCanvasObject != null)
        {
            TitleAPPanelCanvasObject.SetActive(false);
        }
    }

    internal static void SetTitleScreenInputLocked(bool locked)
    {
        try
        {
            TitleScreenView view = UnityEngine.Object.FindObjectOfType<TitleScreenView>();

            if (view == null)
                return;

            if (locked)
            {
                if (LockedTitleScreenView != view)
                {
                    LockedTitleScreenView = view;
                    TitleScreenWasEnabled = view.enabled;
                }

                if (view.enabled)
                {
                    view.enabled = false;
                    LogInfo("AP TITLE: TitleScreenView disabled while AP settings input is active.");
                }
            }
            else
            {
                if (LockedTitleScreenView != null)
                {
                    LockedTitleScreenView.enabled = TitleScreenWasEnabled;
                    LogInfo("AP TITLE: TitleScreenView restored after AP settings input.");
                }

                LockedTitleScreenView = null;
            }
        }
        catch (Exception ex)
        {
            LogWarning("AP TITLE: failed to lock/unlock TitleScreenView:\n" + ex);
        }
    }

    internal static void SetTitleScreenSelectablesLocked(bool locked)
    {
        try
        {
            if (locked)
            {
                foreach (Selectable selectable in UnityEngine.Object.FindObjectsOfType<Selectable>(true))
                {
                    if (selectable == null)
                        continue;

                    if (APSettingsPanelObject != null && selectable.transform.IsChildOf(APSettingsPanelObject.transform))
                        continue;

                    if (TitleAPButtonObject != null && selectable.transform.IsChildOf(TitleAPButtonObject.transform))
                        continue;

                    if (!LockedTitleSelectables.ContainsKey(selectable))
                        LockedTitleSelectables.Add(selectable, selectable.interactable);

                    selectable.interactable = false;
                }
            }
            else
            {
                foreach (var pair in LockedTitleSelectables)
                {
                    if (pair.Key != null)
                        pair.Key.interactable = pair.Value;
                }

                LockedTitleSelectables.Clear();
            }
        }
        catch (Exception ex)
        {
            LogWarning("AP TITLE: failed to lock title selectables:\n" + ex);
        }
    }

    internal static void SetTitleScreenUINavigationLocked(bool locked)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return;

        if (locked)
        {
            if (!HasSavedSendNavigationEvents)
            {
                SavedSendNavigationEvents = eventSystem.sendNavigationEvents;
                HasSavedSendNavigationEvents = true;
            }

            eventSystem.sendNavigationEvents = false;
            eventSystem.SetSelectedGameObject(null);

            LogInfo("AP TITLE: Unity UI navigation disabled.");
        }
        else
        {
            if (HasSavedSendNavigationEvents)
            {
                eventSystem.sendNavigationEvents = SavedSendNavigationEvents;
                HasSavedSendNavigationEvents = false;
            }

            LogInfo("AP TITLE: Unity UI navigation restored.");
        }
    }

    internal static void PollTitleScreenAPHotkey()
    {
        if (!TitleScreenSavePickerOpen || ShowAPSettingsPopup)
            return;

        if (GetF1PressedRaw() || Input.GetKeyDown(KeyCode.F1))
        {
            LogInfo("AP TITLE: F1 opened AP settings panel.");
            OpenTitleScreenAPSettingsForHighlightedSlot();
        }
    }

    internal static bool ShouldBlockVanillaTitleInput()
    {
        return ShowAPSettingsPopup || APSettingsTextInputActive || IsAPSettingsInputFocused();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F1 = 0x70;
    private static bool LastF1Down = false;

    private static bool GetF1PressedRaw()
    {
        bool isDown = (GetAsyncKeyState(VK_F1) & 0x8000) != 0;
        bool pressed = isDown && !LastF1Down;

        LastF1Down = isDown;
        return pressed;
    }

    private void Update()
    {
        PollTitleScreenAPHotkey();

        bool shouldLockTitleScreen = ShowAPSettingsPopup;

        SetTitleScreenInputLocked(shouldLockTitleScreen);
        SetTitleScreenNavigationBlocked(shouldLockTitleScreen);
        SetTitleScreenSelectablesLocked(shouldLockTitleScreen);
        SetTitleScreenUINavigationLocked(shouldLockTitleScreen);

        if (shouldLockTitleScreen)
        {
            Input.ResetInputAxes();
        }
    }

    // ===== Dev overlay state =====
    // Stores recent player-facing AP activity lines for the recent overlay.
    internal static Queue<string> OverlayLines = new Queue<string>();

    // Maximum number of lines to keep in the overlay at once.
    internal static int MaxOverlayLines = 15;

    // Recent-log box visibility is separate from the always-visible status HUD.
    internal static bool ShowRecentLogOverlay = false;
    internal static float RecentLogAutoHideDelaySeconds = 15f;

    // Tracks whether we already forced the parry unlock this session.
    // This prevents repeatedly writing the same progression flag.
    internal static bool ParryUnlockEnsuredThisSession = false;

    // One-shot suppression for cassette checks triggered by AP-granted cassette items.
    // If a cassette is granted through the AP receive-item path, the next matching cassette event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedCassetteChecks = new HashSet<string>();

    //Suppression for Puppy's Gifts/treats.
    // If a Puppy Gift is granted through the AP receive-item path, the next matching Puppy Gift event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedPuppyGiftChecks = new HashSet<string>();


    internal static bool ShowAPSettingsPopup = false;
    internal static bool TitleScreenSavePickerOpen = false;
    internal static int TitleScreenHighlightedSlotIndex = 0;
    internal Rect titleScreenAPPanelRect = new Rect(1120f, 220f, 460f, 340f);
    internal static Rect APSettingsWindowRect = new Rect(1120f, 120f, 520f, 560f);
    internal static GameObject APSettingsPanelObject;
    internal static Dictionary<Selectable, bool> LockedTitleSelectables = new Dictionary<Selectable, bool>();

    internal static GameObject TitleAPButtonObject;
    internal static TMPro.TextMeshProUGUI TitleAPButtonText;
    internal static Button TitleAPButton;

    internal static bool DraggingAPPanel = false;
    internal static Vector2 APPanelDragOffset;

    internal static bool APSettingsTextInputActive = false;
    internal static bool SavedSendNavigationEvents = true;
    internal static bool HasSavedSendNavigationEvents = false;


    internal static TitleScreenView LockedTitleScreenView = null;
    internal static bool TitleScreenWasEnabled = true;

    internal static bool TitleScreenNavigationWasEnabled = true;
}