using Laika.Cassettes;
using Laika.Inventory;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public partial class LaikaMod
{

    // ===== Logging helpers =====
    // Logs a message to both BepInEx and the in-game developer overlay.
    internal static void LogInfo(string message)
    {
        Log.LogInfo(message);
    }

    internal static void LogWarning(string message)
    {
        Log.LogWarning(message);
    }

    internal static void LogError(string message)
    {
        Log.LogError(message);
    }

    // Adds a player-facing AP activity line to the recent overlay.
    // This is intentionally separate from the normal debug logger so the recent overlay
    // only shows useful AP gameplay events instead of internal spam.
    internal static void AnnounceAPActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!ShouldShowAPActivityOverlay())
            return;

        EnsureDevOverlayCanvas();

        OverlayLines.Enqueue(message);

        while (OverlayLines.Count > MaxOverlayLines)
            OverlayLines.Dequeue();

        ShowRecentLogOverlay = true;

        if (ActiveDevOverlayController != null)
            ActiveDevOverlayController.ResetRecentLogAutoHideTimer();

        RefreshDevOverlay();
    }

    internal static string ColorizeOverlayMessage(string hexColor, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(hexColor))
            return message;

        return $"<color={hexColor}>{message}</color>";
    }

    internal static string OverlayColor(string hexColor, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(hexColor))
            return text;

        return $"<color={hexColor}>{text}</color>";
    }

    internal static bool IsLaikaApItemId(long itemId)
    {
        return ItemFactoriesByApId != null &&
               ItemFactoriesByApId.ContainsKey(itemId);
    }

    internal static string GetOverlayItemColorHex(long itemId, string apProvidedColorHex)
    {
        if (IsLaikaApItemId(itemId))
            return GetItemRarityColorHex(itemId);

        if (!string.IsNullOrWhiteSpace(apProvidedColorHex))
            return apProvidedColorHex;

        return "#FFFFFF";
    }

    internal static string GetItemRarityColorHex(long itemId)
    {
        // Progression, required for main route / major access.
        if (
            itemId == 1000L || // Dash
            itemId == 1001L || // Hook
            itemId == 1002L || // Maya's Pendant

            itemId == 1100L || // Shotgun weapon-mode unlock

            itemId == 1150L || // Rusty Spring, shotgun material
            itemId == 1160L    // Shotgun recipe / blueprint
        )
        {
            return "#C792EA"; // progression lavender
        }

        // Useful weapons, weapon upgrades, non-shotgun recipes/materials,
        // and useful quest/key items.
        if (
            (itemId >= 1101L && itemId <= 1140L) || // non-shotgun weapon unlocks
            (itemId >= 1110L && itemId <= 1114L) || // weapon upgrades
            (itemId >= 1151L && itemId <= 1153L) || // non-shotgun crafting materials
            (itemId >= 1161L && itemId <= 1163L) || // non-shotgun recipes
            (itemId >= 1165L && itemId <= 1197L)    // quest/key items not already caught above
        )
        {
            return "#FFD166"; // useful gold/yellow
        }

        // Puppy gifts.
        if (itemId >= 1900L && itemId <= 1906L)
            return "#FFB86C"; // puppy gift orange

        // Currency / filler / general resource.
        if (itemId == 1907L)
            return "#82AAFF"; // periwinkle blue

        // Ingredients.
        if (itemId >= 1950L && itemId <= 1964L)
            return "#2EC4B6"; // teal

        // Cassettes.
        if (itemId >= 1970L && itemId <= 1989L)
            return "#FF80AB"; // cassette pink

        // Map unlocks.
        if (itemId >= 2000L && itemId <= 2030L)
            return "#A3E635"; // lime/chartreuse

        return "#FFFFFF";
    }

    internal static string BuildCheckSentOverlayLine(
        string playerName,
        string itemName,
        long itemId,
        string locationName,
        string apProvidedItemColorHex = null)
    {
        string playerPart = OverlayColor("#C792EA", playerName);
        string verbPart = OverlayColor("#FFFFFF", " found their ");
        string itemPart = OverlayColor(GetOverlayItemColorHex(itemId, apProvidedItemColorHex), itemName);
        string locationPart = OverlayColor("#00E676", $" ({locationName})");

        return $"{playerPart}{verbPart}{itemPart}{locationPart}";
    }

    internal static string BuildFoundYourOwnItemOverlayLine(
        string itemName,
        long itemId,
        string locationName,
        string apProvidedItemColorHex = null)
    {
        string prefixPart = OverlayColor("#FFFFFF", "You found your ");
        string itemPart = OverlayColor(GetOverlayItemColorHex(itemId, apProvidedItemColorHex), itemName);
        string fromPart = OverlayColor("#FFFFFF", " from ");
        string locationPart = OverlayColor("#00E676", locationName + "!");

        return $"{prefixPart}{itemPart}{fromPart}{locationPart}";
    }

    internal static string BuildSentToOtherPlayerFromLocationOverlayLine(
        string senderName,
        string itemName,
        long itemId,
        string receiverName,
        string locationName,
        string apProvidedItemColorHex = null)
    {
        string senderPart = OverlayColor("#C792EA", senderName);
        string sentPart = OverlayColor("#FFFFFF", " sent ");
        string itemPart = OverlayColor(GetOverlayItemColorHex(itemId, apProvidedItemColorHex), itemName);
        string toPart = OverlayColor("#FFFFFF", " to ");
        string receiverPart = OverlayColor("#C792EA", receiverName);
        string fromPart = OverlayColor("#FFFFFF", " from ");
        string locationPart = OverlayColor("#00E676", locationName + "!");

        return $"{senderPart}{sentPart}{itemPart}{toPart}{receiverPart}{fromPart}{locationPart}";
    }

    internal static string BuildReceivedFromOtherPlayerOverlayLine(
        string itemName,
        long itemId,
        string senderName,
        string locationName,
        string apProvidedItemColorHex = null)
    {
        string localPlayerName =
            SessionState != null &&
            SessionState.Connection != null &&
            !string.IsNullOrWhiteSpace(SessionState.Connection.SlotName)
                ? SessionState.Connection.SlotName
                : null;

        bool isSelfSend =
            !string.IsNullOrWhiteSpace(localPlayerName) &&
            !string.IsNullOrWhiteSpace(senderName) &&
            string.Equals(localPlayerName, senderName, StringComparison.OrdinalIgnoreCase);

        string itemPart = OverlayColor(GetOverlayItemColorHex(itemId, apProvidedItemColorHex), itemName);

        if (isSelfSend)
        {
            string prefixPart = OverlayColor("#FFFFFF", "You found your ");
            string fromPart = OverlayColor("#FFFFFF", " from ");
            string locationPart = OverlayColor("#00E676", locationName + "!");

            return $"{prefixPart}{itemPart}{fromPart}{locationPart}";
        }

        string senderPart = OverlayColor("#C792EA", senderName);

        if (!string.IsNullOrWhiteSpace(locationName))
        {
            string prefixPart = OverlayColor("#FFFFFF", "You received ");
            string fromPart = OverlayColor("#FFFFFF", " from ");
            string atPart = OverlayColor("#FFFFFF", " at ");
            string locationPart = OverlayColor("#00E676", locationName + "!");

            return $"{prefixPart}{itemPart}{fromPart}{senderPart}{atPart}{locationPart}";
        }

        return
            OverlayColor("#FFFFFF", "You received ") +
            itemPart +
            OverlayColor("#FFFFFF", " from ") +
            senderPart +
            OverlayColor("#FFFFFF", "!");
    }

    internal static string BuildGrantedOverlayLine(string itemName, long itemId)
    {
        string prefixPart = OverlayColor("#00E676", "[AP] Granted: ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName + ".");

        return $"{prefixPart}{itemPart}";
    }
    internal static string BuildItemReceivedOverlayLine(string itemName, long itemId, string senderName)
    {
        string prefixPart = OverlayColor("#FFFFFF", "[AP] Received ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);

        if (string.IsNullOrWhiteSpace(senderName))
        {
            string itemPeriodPart = OverlayColor(GetItemRarityColorHex(itemId), ".");
            return $"{prefixPart}{itemPart}{itemPeriodPart}";
        }

        string fromPart = OverlayColor("#FFFFFF", " from ");
        string senderPart = OverlayColor("#C792EA", senderName);
        string whitePeriodPart = OverlayColor("#FFFFFF", ".");

        return $"{prefixPart}{itemPart}{fromPart}{senderPart}{whitePeriodPart}";
    }

    internal static void AnnounceAPInfo(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#FFFFFF", message));
    }

    internal static void AnnounceAPSuccess(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#7CFF7C", message));
    }

    internal static void AnnounceAPWarning(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#FFD166", message));
    }

    internal static void AnnounceAPError(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#FF7B7B", message));
    }

    internal static void AnnounceAPCheckSent(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#7FDBFF", message));
    }

    internal static void AnnounceAPItemReceived(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#C792EA", message));
    }

    internal static void AnnounceAPDeathLink(string message)
    {
        if (!ShouldShowAPActivityOverlay())
            return;

        AnnounceAPActivity(ColorizeOverlayMessage("#7FDBFF", message));
    }

    // Logs every ingredient ID the game has loaded.
    // This helps us discover real ingredient IDs for testing.
    internal static void LogAllIngredientIds()
    {
        // Grab the game's master item loader singleton.
        var loader = Singleton<ItemDataLoader>.Instance;

        // Safety check in case loader somehow isn't ready yet.
        if (loader == null)
        {
            LogWarning("ItemDataLoader is null.");
            return;
        }

        // Ask the game for every ingredient definition.
        var ingredients = loader.GetAllIngredientDatas();

        // Another safety check.
        if (ingredients == null)
        {
            LogWarning("GetAllIngredientDatas returned null.");
            return;
        }

        // Print how many ingredients exist total.
        LogInfo($"INGREDIENT LIST START: count={ingredients.Count}");

        // Loop through every ingredient.
        foreach (var ingredient in ingredients)
        {
            // Skip broken/null entries just in case.
            if (ingredient == null)
                continue;

            // Print the ingredient's internal ID.
            LogInfo($"INGREDIENT ID: {ingredient.id}");
        }

        LogInfo("INGREDIENT LIST END");
    }

    // Logs every cassette ID the game has loaded.
    // This helps us discover real cassette IDs for testing.
    internal static void LogAllCassetteIds()
    {
        // Grab the game's cassette data loader singleton.
        var loader = Singleton<CassettesDataLoader>.Instance;

        // Safety check in case loader is not ready yet.
        if (loader == null)
        {
            LogWarning("CassettesDataLoader is null.");
            return;
        }

        // Ask the game for all cassette IDs.
        var cassetteIds = loader.GetCassettesIds();

        // Another safety check.
        if (cassetteIds == null)
        {
            LogWarning("GetCassettesIds returned null.");
            return;
        }

        // Print how many cassettes exist total.
        LogInfo($"CASSETTE LIST START: count={cassetteIds.Count}");

        // Loop through every cassette ID.
        foreach (var cassetteId in cassetteIds)
        {
            if (string.IsNullOrEmpty(cassetteId))
                continue;

            LogInfo($"CASSETTE ID: {cassetteId}");
        }

        LogInfo("CASSETTE LIST END");
    }

    // Logs the current visible weapon inventory for debugging before/after queue processing.
    internal static void LogWeaponInventorySnapshot(string label)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            LogWarning($"{label}: WeaponsInventory is null.");
            return;
        }

        if (inventory.Weapons == null)
        {
            LogWarning($"{label}: Weapons list is null.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"WEAPON INVENTORY SNAPSHOT {label}: count={inventory.Weapons.Count}");

        foreach (var weapon in inventory.Weapons)
        {
            if (weapon == null)
                continue;

            sb.Append($" | {weapon.Id}");
        }

        LogInfo(sb.ToString());
    }

    internal static void ApplyDevOverlaySafeRootScale()
    {
        if (DevOverlaySafeRoot == null)
            return;

        float scaleX = UnityEngine.Screen.width / 2560f;
        float scaleY = UnityEngine.Screen.height / 1440f;

        float safeScale = Mathf.Min(scaleX, scaleY);

        DevOverlaySafeRoot.localScale = new Vector3(safeScale, safeScale, 1f);
        DevOverlaySafeRoot.anchoredPosition = Vector2.zero;
        DevOverlaySafeRoot.sizeDelta = new Vector2(2560f, 1440f);

        LastDevOverlayScreenWidth = UnityEngine.Screen.width;
        LastDevOverlayScreenHeight = UnityEngine.Screen.height;
    }

    internal static void UpdateDevOverlaySafeRootForResolutionChange()
    {
        if (DevOverlaySafeRoot == null)
            return;

        int width = UnityEngine.Screen.width;
        int height = UnityEngine.Screen.height;

        if (width == LastDevOverlayScreenWidth &&
            height == LastDevOverlayScreenHeight)
        {
            return;
        }

        LastDevOverlayScreenWidth = width;
        LastDevOverlayScreenHeight = height;

        ApplyDevOverlaySafeRootScale();

        Canvas.ForceUpdateCanvases();

        LogInfo($"DEV OVERLAY SCALE: resolution changed to {width}x{height}; reapplied 16:9 safe-root scale.");
    }

    // Creates the developer overlay canvas if it does not already exist.
    internal static void EnsureDevOverlayCanvas()
    {
        if (DevOverlayCanvasObject != null)
            return;

        Font builtInFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        Log.LogInfo($"Dev overlay font loaded: {(builtInFont != null)}");

        DevOverlayCanvasObject = new GameObject("LaikaAPDevOverlayCanvas");
        DontDestroyOnLoad(DevOverlayCanvasObject);

        Canvas canvas = DevOverlayCanvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        canvas.pixelPerfect = false;
        canvas.targetDisplay = 0;

        CanvasScaler scaler = DevOverlayCanvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        DevOverlayCanvasObject.AddComponent<GraphicRaycaster>();
        GameObject safeRootObject = new GameObject("LaikaAPDevOverlaySafeRoot");
        safeRootObject.transform.SetParent(DevOverlayCanvasObject.transform, false);

        DevOverlaySafeRoot = safeRootObject.AddComponent<RectTransform>();
        DevOverlaySafeRoot.anchorMin = new Vector2(0.5f, 0.5f);
        DevOverlaySafeRoot.anchorMax = new Vector2(0.5f, 0.5f);
        DevOverlaySafeRoot.pivot = new Vector2(0.5f, 0.5f);
        DevOverlaySafeRoot.sizeDelta = new Vector2(2560f, 1440f);
        DevOverlaySafeRoot.anchoredPosition = Vector2.zero;

        ApplyDevOverlaySafeRootScale();

        // ===== Permanent status panel =====
        GameObject statusPanel = new GameObject("OverlayStatusPanel");
        statusPanel.transform.SetParent(DevOverlaySafeRoot, false);

        Image statusBg = statusPanel.AddComponent<Image>();
        statusBg.color = new Color(0f, 0f, 0f, 0f);
        statusBg.raycastTarget = false;

        RectTransform statusPanelRect = statusPanel.GetComponent<RectTransform>();
        statusPanelRect.anchorMin = new Vector2(0f, 1f);
        statusPanelRect.anchorMax = new Vector2(0f, 1f);
        statusPanelRect.pivot = new Vector2(0f, 1f);
        statusPanelRect.anchoredPosition = new Vector2(24f, -24f);
        statusPanelRect.sizeDelta = new Vector2(620f, 42f);

        GameObject statusTextObj = new GameObject("OverlayStatusText");
        statusTextObj.transform.SetParent(statusPanel.transform, false);

        DevOverlayStatusText = statusTextObj.AddComponent<Text>();
        DevOverlayStatusText.font = builtInFont;
        DevOverlayStatusText.supportRichText = true;
        DevOverlayStatusText.fontSize = 16;
        DevOverlayStatusText.color = Color.white;
        DevOverlayStatusText.alignment = TextAnchor.UpperLeft;
        DevOverlayStatusText.fontStyle = FontStyle.Bold;
        DevOverlayStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        DevOverlayStatusText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform statusTextRect = statusTextObj.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0f, 0f);
        statusTextRect.anchorMax = new Vector2(1f, 1f);
        statusTextRect.offsetMin = new Vector2(26f, 12f);
        statusTextRect.offsetMax = new Vector2(0f, -12f);

        // ===== Recent log panel =====
        GameObject logPanel = new GameObject("OverlayRecentLogPanel");
        logPanel.transform.SetParent(DevOverlaySafeRoot, false);

        Image logBg = logPanel.AddComponent<Image>();
        logBg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform logPanelRect = logPanel.GetComponent<RectTransform>();
        logPanelRect.anchorMin = new Vector2(0f, 0f);
        logPanelRect.anchorMax = new Vector2(0f, 0f);
        logPanelRect.pivot = new Vector2(0f, 0f);
        logPanelRect.anchoredPosition = new Vector2(24f, 24f);
        logPanelRect.sizeDelta = new Vector2(700f, 240f);

        GameObject logTextObj = new GameObject("OverlayRecentLogText");
        logTextObj.transform.SetParent(logPanel.transform, false);

        DevOverlayRecentLogText = logTextObj.AddComponent<Text>();
        DevOverlayRecentLogText.font = builtInFont;
        DevOverlayRecentLogText.supportRichText = true;
        DevOverlayRecentLogText.fontSize = 15;
        DevOverlayRecentLogText.color = Color.white;
        DevOverlayRecentLogText.alignment = TextAnchor.UpperLeft;
        DevOverlayRecentLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
        DevOverlayRecentLogText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform logTextRect = logTextObj.GetComponent<RectTransform>();
        logTextRect.anchorMin = new Vector2(0f, 0f);
        logTextRect.anchorMax = new Vector2(1f, 1f);
        logTextRect.offsetMin = new Vector2(12f, 12f);
        logTextRect.offsetMax = new Vector2(-12f, -12f);

        Canvas.ForceUpdateCanvases();
        Log.LogInfo("Dev overlay canvas created.");
    }

    // Redraws both overlay panels based on the current shared overlay state.
    // This method does not own timing; it only reflects the latest state to the UI.
    // Refreshes the overlay text and visibility.
    internal static void RefreshDevOverlay()
    {
        if (DevOverlayCanvasObject == null || DevOverlayStatusText == null || DevOverlayRecentLogText == null)
            return;

        ApplyDevOverlaySafeRootScale();

        bool vanillaSettingsOpen = IsVanillaSettingsScreenOpen();

        string connectionState = "Not Connected";

        if (ArchipelagoClientManager.Instance != null)
        {
            if (ArchipelagoClientManager.Instance.IsConnected)
                connectionState = "Connected";
            else if (ArchipelagoClientManager.Instance.IsConnecting)
                connectionState = "Attempting to connect...";
        }

        Color statusColor = Color.red;

        if (connectionState == "Connected")
            statusColor = Color.green;
        else if (connectionState == "Attempting to connect...")
            statusColor = Color.yellow;

        DevOverlayStatusText.color = statusColor;

        int displaySlot = Mathf.Clamp(ActiveSaveSlotIndex, 0, 2) + 1;

        string displayConnectionState = connectionState;

        if (connectionState == "Connected")
            displayConnectionState = "Connected!";

        DevOverlayStatusText.text =
            $"Archipelago: {displayConnectionState} (Slot {displaySlot})";

        bool shouldShowStatus =
            SessionState != null &&
            SessionState.APEnabled;

        DevOverlayStatusText.transform.parent.gameObject.SetActive(shouldShowStatus && !vanillaSettingsOpen);
        bool shouldShowRecentLog =
            ShowRecentLogOverlay &&
            ShouldShowAPActivityOverlay();

        DevOverlayRecentLogText.transform.parent.gameObject.SetActive(shouldShowRecentLog && !vanillaSettingsOpen);

        if (ShowRecentLogOverlay)
        {
            DevOverlayRecentLogText.text =
                "Recent AP Activity:\n\n" +
                string.Join("\n", OverlayLines.ToArray());
        }
        else
        {
            DevOverlayRecentLogText.text = string.Empty;
        }
    }

    internal static bool IsVanillaSettingsScreenOpen()
    {
        try
        {
            // TMP text path
            foreach (TMPro.TMP_Text tmp in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
            {
                if (tmp == null || tmp.text == null || tmp.gameObject == null)
                    continue;

                if (!tmp.gameObject.activeInHierarchy)
                    continue;

                string text = tmp.text.Trim().ToUpperInvariant();

                if (IsVanillaSettingsMarkerText(text))
                    return true;
            }

            // Unity UI Text path
            foreach (UnityEngine.UI.Text uiText in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
            {
                if (uiText == null || uiText.text == null || uiText.gameObject == null)
                    continue;

                if (!uiText.gameObject.activeInHierarchy)
                    continue;

                string text = uiText.text.Trim().ToUpperInvariant();

                if (IsVanillaSettingsMarkerText(text))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsVanillaSettingsMarkerText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return
            text == "SCREEN RESOLUTION" ||
            text == "FULLSCREEN" ||
            text == "V-SYNC" ||
            text == "FRAME RATE CAP" ||
            text == "LANGUAGE" ||
            text == "SCREEN SHAKE" ||
            text == "CONTROLLER RUMBLE" ||
            text == "GAMEPAD BIKE SENSITIVITY" ||
            text == "ACCESSIBILITY" ||
            text == "AUTO AIM" ||
            text == "WALKIE SLOW TIME" ||
            text == "RESET DEFAULTS";
    }

    // This parameter is only used as a safe creation hook.
    // The controller itself is created on its own persistent object.
    internal static void EnsureRuntimeDevOverlay(WeaponsOverlay weaponsOverlay)
    {
        if (weaponsOverlay == null)
        {
            LogWarning("EnsureRuntimeDevOverlay: WeaponsOverlay was null.");
            return;
        }

        if (ActiveDevOverlayController != null)
            return;

        DevOverlayControllerObject = new GameObject("LaikaAPDevOverlayController");
        DontDestroyOnLoad(DevOverlayControllerObject);

        ActiveDevOverlayController = DevOverlayControllerObject.AddComponent<DevOverlayController>();

        Log.LogInfo("Created persistent DevOverlayController object.");
    }

    internal static string BuildReceivedItemAnnouncement(PendingItem item)
    {
        if (item == null)
            return "[AP] Received: <null item>";

        if (item.Amount > 1)
            return $"[AP] Received: {item.DisplayName} x{item.Amount}";

        return $"[AP] Received: {item.DisplayName}";
    }

    // Update only exists to ensure one-time initialization confirmation.
    // Recent log visibility is driven by AnnounceAPActivity(...) + Invoke-based auto-hide.
    public class DevOverlayController : MonoBehaviour
    {
        private bool initialized = false;
        private bool updateLoggedOnce = false;
        private Rect apDebugPanelRect = new Rect(10f, 10f, 340f, 420f);
        private bool showAPDebugPanel = false;
        private Coroutine apPollCoroutine;
        private string hostInput = "";
        private string portInput = "";
        private string slotNameInput = "";
        private string passwordInput = "";
        private bool textFieldsInitialized = false;

        void Start()
        {
            LaikaMod.Log.LogInfo("DevOverlayController Start() fired.");
            InitializeOverlay();
        }

        void OnEnable()
        {
            LaikaMod.Log.LogInfo("DevOverlayController OnEnable() fired.");
            InitializeOverlay();

            if (apPollCoroutine == null)
            {
                apPollCoroutine = StartCoroutine(APPollLoop());
                LaikaMod.LogInfo("AP POLL: started DevOverlayController coroutine.");
            }
        }

        private void OnDisable()
        {
            if (apPollCoroutine != null)
            {
                StopCoroutine(apPollCoroutine);
                apPollCoroutine = null;
                LaikaMod.LogInfo("AP POLL: stopped DevOverlayController coroutine.");
            }
        }

        private System.Collections.IEnumerator APPollLoop()
        {
            while (true)
            {
                if (ArchipelagoClientManager.Instance != null)
                {
                    if (ArchipelagoClientManager.Instance.IsConnected)
                    {
                        ArchipelagoClientManager.Instance.PumpReceivedItems();
                    }
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        void Update()
        {
            try
            {
                if (LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate &&
                    !LaikaMod.HeartglazeFlowerCleanupDone &&
                    Time.realtimeSinceStartup >= LaikaMod.HeartglazeFlowerCleanupReadyAt)
                {
                    if (LaikaMod.IsHeartglazeQuestReadyForFlowerRemoval())
                    {
                        LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                            "DevOverlayController.Update/HeartglazeQuestStepConfirmed"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"DevOverlayController.Update Heartglaze cleanup exception:\n{ex}");
            }

            if (!initialized)
                InitializeOverlay();

            LaikaMod.UpdateDevOverlaySafeRootForResolutionChange();

            if (!updateLoggedOnce)
            {
                updateLoggedOnce = true;
                LaikaMod.Log.LogInfo("DevOverlayController Update() is running.");
            }

            if (LaikaMod.IsAPSettingsInputFocused())
            {
                Input.ResetInputAxes();
                return;
            }

            if (APSettingsPanelObject != null && APSettingsPanelObject.activeSelf)
            {
                RectTransform rect = APSettingsPanelObject.GetComponent<RectTransform>();

                if (Input.GetMouseButtonDown(0))
                {
                    Vector2 mousePos = Input.mousePosition;

                    RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rect,
                        mousePos,
                        null,
                        out APPanelDragOffset
                    );

                    if (RectTransformUtility.RectangleContainsScreenPoint(rect, mousePos))
                    {
                        DraggingAPPanel = true;
                    }
                }

                if (Input.GetMouseButtonUp(0))
                {
                    DraggingAPPanel = false;
                }

                if (DraggingAPPanel)
                {
                    rect.position = Input.mousePosition;
                }
            }


        }

        void OnGUI()
        {
            if (!initialized)
                return;

            if (showAPDebugPanel)
            {
                apDebugPanelRect = GUI.Window(
                    928374,
                    apDebugPanelRect,
                    DrawAPDebugWindow,
                    "Laika AP Debug"
                );
            }
        }

        private void EnsureConnectionInputFieldsInitialized()
        {
            if (textFieldsInitialized)
                return;

            if (LaikaMod.SessionState == null || LaikaMod.SessionState.Connection == null)
                return;

            hostInput = LaikaMod.SessionState.Connection.Host ?? "";
            portInput = LaikaMod.SessionState.Connection.Port > 0
                ? LaikaMod.SessionState.Connection.Port.ToString()
                : "";
            slotNameInput = LaikaMod.SessionState.Connection.SlotName ?? "";
            passwordInput = LaikaMod.SessionState.Connection.Password ?? "";

            textFieldsInitialized = true;
        }

        public void ReloadConnectionInputFieldsFromSession()
        {
            hostInput = LaikaMod.SessionState != null && LaikaMod.SessionState.Connection != null
                ? LaikaMod.SessionState.Connection.Host ?? ""
                : "";

            portInput = LaikaMod.SessionState != null && LaikaMod.SessionState.Connection != null &&
                        LaikaMod.SessionState.Connection.Port > 0
                ? LaikaMod.SessionState.Connection.Port.ToString()
                : "";

            slotNameInput = LaikaMod.SessionState != null && LaikaMod.SessionState.Connection != null
                ? LaikaMod.SessionState.Connection.SlotName ?? ""
                : "";

            passwordInput = LaikaMod.SessionState != null && LaikaMod.SessionState.Connection != null
                ? LaikaMod.SessionState.Connection.Password ?? ""
                : "";

            textFieldsInitialized = true;
        }

        // Cancels any pending hide callback and schedules a new one.
        // This makes each new recent-log event extend the panel's visibility window.
        public void ResetRecentLogAutoHideTimer()
        {
            CancelInvoke(nameof(HideRecentLogs));
            Invoke(nameof(HideRecentLogs), LaikaMod.RecentLogAutoHideDelaySeconds);
        }

        // Called by Invoke(...) after RecentLogAutoHideDelaySeconds of inactivity.
        private void HideRecentLogs()
        {
            LaikaMod.Log.LogInfo("Recent log overlay auto-hidden.");
            LaikaMod.ShowRecentLogOverlay = false;
            LaikaMod.RefreshDevOverlay();
        }

        private void InitializeOverlay()
        {
            if (initialized)
                return;

            LaikaMod.EnsureDevOverlayCanvas();

            initialized = true;

            LaikaMod.ShowRecentLogOverlay = false;

            LaikaMod.RefreshDevOverlay();
            LaikaMod.Log.LogInfo("DevOverlayController initialized runtime overlay.");
        }

        private void DrawAPDebugWindow(int windowId)
        {
            GUILayout.BeginVertical();
            EnsureConnectionInputFieldsInitialized();

            GUILayout.Label($"AP Slot Index: {LaikaMod.ActiveSaveSlotIndex + 1}");

            GUILayout.Label($"AP Save File: slot{LaikaMod.ActiveSaveSlotIndex}.json");

            bool isConnected = ArchipelagoClientManager.Instance != null &&
                   ArchipelagoClientManager.Instance.IsConnected;

            bool isConnecting = ArchipelagoClientManager.Instance != null &&
                                ArchipelagoClientManager.Instance.IsConnecting;

            if (isConnecting)
            {
                GUILayout.Label("Connection: Attempting to connect...");
            }
            else
            {
                GUILayout.Label($"Connection: {(isConnected ? "Connected" : "Not Connected")}");
            }

            GUILayout.Space(8f);
            GUILayout.Label("Archipelago Settings");

            bool slotApEnabled = LaikaMod.SessionState != null && LaikaMod.SessionState.APEnabled;
            bool newSlotApEnabled = GUILayout.Toggle(slotApEnabled, "Enable AP for this save slot");
            if (newSlotApEnabled != slotApEnabled)
            {
                LaikaMod.SessionState.APEnabled = newSlotApEnabled;
                LaikaMod.SaveSessionState();
                LaikaMod.RefreshDevOverlay();
            }

            string oldHost = hostInput;
            string oldPort = portInput;
            string oldSlotName = slotNameInput;
            string oldPassword = passwordInput;

            GUILayout.Label("Host");
            hostInput = GUILayout.TextField(hostInput ?? "", 256);

            GUILayout.Label("Port");
            portInput = GUILayout.TextField(portInput ?? "", 16);

            GUILayout.Label("Slot Name");
            slotNameInput = GUILayout.TextField(slotNameInput ?? "", 128);

            GUILayout.Label("Password");
            passwordInput = GUILayout.PasswordField(passwordInput ?? "", '*', 128);

            bool inputsChanged =
                oldHost != hostInput ||
                oldPort != portInput ||
                oldSlotName != slotNameInput ||
                oldPassword != passwordInput;

            if (inputsChanged)
            {
                if (LaikaMod.SessionState.Connection == null)
                    LaikaMod.SessionState.Connection = new APConnectionState();

                LaikaMod.SessionState.Connection.Host = (hostInput ?? "").Trim();

                int previewPort;
                if (string.IsNullOrWhiteSpace(portInput))
                {
                    LaikaMod.SessionState.Connection.Port = 0;
                }
                else if (int.TryParse(portInput, out previewPort) && previewPort > 0)
                {
                    LaikaMod.SessionState.Connection.Port = previewPort;
                }
                else
                {
                    LaikaMod.SessionState.Connection.Port = 0;
                }

                LaikaMod.SessionState.Connection.SlotName = (slotNameInput ?? "").Trim();
                LaikaMod.SessionState.Connection.Password = passwordInput ?? "";

                LaikaMod.RefreshDevOverlay();
            }

            if (GUILayout.Button("Save AP Settings"))
            {
                int parsedPort;
                if (!int.TryParse(portInput, out parsedPort) || parsedPort <= 0)
                {
                    LaikaMod.AnnounceAPWarning("[AP] Invalid port.");
                }
                else
                {
                    if (LaikaMod.SessionState.Connection == null)
                        LaikaMod.SessionState.Connection = new APConnectionState();

                    LaikaMod.SessionState.Connection.Host = (hostInput ?? "").Trim();
                    LaikaMod.SessionState.Connection.Port = parsedPort;
                    LaikaMod.SessionState.Connection.SlotName = (slotNameInput ?? "").Trim();
                    LaikaMod.SessionState.Connection.Password = passwordInput ?? "";
                    LaikaMod.SaveSessionState();
                    LaikaMod.RefreshDevOverlay();
                    LaikaMod.AnnounceAPSuccess("[AP] Saved settings for this slot.");
                }
            }

            GUI.enabled = !isConnected && !isConnecting;

            if (GUILayout.Button(isConnecting ? "Attempting to connect..." : "Connect Active Slot"))
            {
                int parsedPort;
                if (!int.TryParse(portInput, out parsedPort) || parsedPort <= 0)
                {
                    LaikaMod.AnnounceAPWarning("[AP] Invalid port.");
                }
                else
                {
                    if (LaikaMod.SessionState.Connection == null)
                        LaikaMod.SessionState.Connection = new APConnectionState();

                    LaikaMod.SessionState.APEnabled = true;
                    LaikaMod.SessionState.Connection.Host = (hostInput ?? "").Trim();
                    LaikaMod.SessionState.Connection.Port = parsedPort;
                    LaikaMod.SessionState.Connection.SlotName = (slotNameInput ?? "").Trim();
                    LaikaMod.SessionState.Connection.Password = passwordInput ?? "";
                    LaikaMod.SaveSessionState();

                    LaikaMod.ConnectActiveSlotIfConfigured();
                }
            }

            GUI.enabled = isConnected && !isConnecting;

            if (GUILayout.Button("Disconnect"))
            {
                if (ArchipelagoClientManager.Instance != null)
                {
                    ArchipelagoClientManager.Instance.Disconnect("User requested disconnect");
                }
            }

            GUI.enabled = true;

            if (GUILayout.Button("Kill Player"))
            {
                var rider = GameObject.FindObjectOfType<RiderHead>();
                if (rider != null)
                {
                    rider.Kill();
                    LaikaMod.Log.LogInfo("AP DEBUG UI: forced player death.");
                }
            }

            if (GUILayout.Button("Trigger DeathLink Send"))
            {
                LaikaMod.OnPlayerDeathDetected("DEBUG BUTTON");
            }

            if (GUILayout.Button("Give Shotgun"))
            {
                LaikaMod.EnqueueItem(new PendingItem(
                    ItemKind.Weapon,
                    "I_W_SHOTGUN",
                    1,
                    "Shotgun"
                ));

                LaikaMod.ProcessPendingItemQueue("AP DEBUG UI");
            }

            GUILayout.Space(8f);

            if (LaikaMod.SessionState != null)
            {
                string host = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.Host : "<null>";
                int port = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.Port : 0;
                string slotName = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.SlotName : "<null>";
                bool currentApEnabled = LaikaMod.SessionState.APEnabled;

                GUILayout.Label($"AP Enabled: {currentApEnabled}");
                GUILayout.Label($"Host: {host}:{port}");
                GUILayout.Label($"Slot: {slotName}");
            }

            if (LaikaMod.TitleScreenSavePickerOpen)
            {
                GUI.Box(new Rect(20f, 20f, 260f, 90f),
                    "AP Title Debug\n" +
                    "SavePickerOpen: true\n" +
                    "Highlighted Slot: " + LaikaMod.TitleScreenHighlightedSlotIndex);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }
    }
}