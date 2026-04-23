using Laika.Cassettes;
using Laika.Inventory;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
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

        OverlayLines.Enqueue(message);

        while (OverlayLines.Count > MaxOverlayLines)
        {
            OverlayLines.Dequeue();
        }

        ShowRecentLogOverlay = true;

        if (ActiveDevOverlayController != null)
        {
            ActiveDevOverlayController.ResetRecentLogAutoHideTimer();
        }

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

    internal static string GetItemRarityColorHex(long itemId)
    {
        // Filler
        if (itemId >= 1900L)
            return "#6EC1FF"; // blue

        // Progression / useful / key-style
        if ((itemId >= 1000L && itemId < 1200L) || itemId == 1970L)
            return "#C792EA"; // purple/pink

        // Default fallback
        return "#FFFFFF";
    }

    internal static string BuildCheckSentOverlayLine(string playerName, string itemName, long itemId, string locationName)
    {
        string playerPart = OverlayColor("#C792EA", playerName);
        string verbPart = OverlayColor("#FFFFFF", " found their ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);
        string locationPart = OverlayColor("#00E676", $" ({locationName})");

        return $"{playerPart}{verbPart}{itemPart}{locationPart}";
    }

    internal static string BuildFoundYourOwnItemOverlayLine(string itemName, long itemId, string locationName)
    {
        string prefixPart = OverlayColor("#FFFFFF", "You found your ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);
        string fromPart = OverlayColor("#FFFFFF", " from ");
        string locationPart = OverlayColor("#00E676", locationName);
        string exclaimPart = OverlayColor("#FFFFFF", "!");

        return $"{prefixPart}{itemPart}{fromPart}{locationPart}{exclaimPart}";
    }

    internal static string BuildSentToOtherPlayerFromLocationOverlayLine(string senderName, string itemName, long itemId, string receiverName, string locationName)
    {
        string senderPart = OverlayColor("#C792EA", senderName);
        string sentPart = OverlayColor("#FFFFFF", " sent ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);
        string toPart = OverlayColor("#FFFFFF", " to ");
        string receiverPart = OverlayColor("#C792EA", receiverName);
        string fromPart = OverlayColor("#FFFFFF", " from ");
        string locationPart = OverlayColor("#00E676", locationName);
        string exclaimPart = OverlayColor("#FFFFFF", "!");

        return $"{senderPart}{sentPart}{itemPart}{toPart}{receiverPart}{fromPart}{locationPart}{exclaimPart}";
    }

    internal static string BuildReceivedFromOtherPlayerOverlayLine(string itemName, long itemId, string senderName, string locationName)
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

        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);

        if (isSelfSend)
        {
            string prefixPart = OverlayColor("#FFFFFF", "You found your ");
            string fromPart = OverlayColor("#FFFFFF", " from ");
            string locationPart = OverlayColor("#00E676", locationName);
            string exclaimPart = OverlayColor("#FFFFFF", "!");

            return $"{prefixPart}{itemPart}{fromPart}{locationPart}{exclaimPart}";
        }

        string youPart = OverlayColor("#FFFFFF", "You received ");
        string senderPart = OverlayColor("#C792EA", senderName);

        if (!string.IsNullOrWhiteSpace(locationName))
        {
            string fromPart = OverlayColor("#FFFFFF", " from ");
            string fromTheirPart = OverlayColor("#FFFFFF", " from their ");
            string locationPart = OverlayColor("#00E676", locationName);
            string exclaimPart = OverlayColor("#FFFFFF", "!");

            return $"{youPart}{itemPart}{fromPart}{senderPart}{fromTheirPart}{locationPart}{exclaimPart}";
        }

        return $"{youPart}{itemPart}{OverlayColor("#FFFFFF", " from ")}{senderPart}{OverlayColor("#FFFFFF", "!")}";
    }

    internal static string BuildItemReceivedOverlayLine(string itemName, long itemId, string senderName)
    {
        string prefixPart = OverlayColor("#FFFFFF", "[AP] Received ");
        string itemPart = OverlayColor(GetItemRarityColorHex(itemId), itemName);
        string senderPart = string.IsNullOrWhiteSpace(senderName)
            ? string.Empty
            : OverlayColor("#C792EA", $" from {senderName}");

        return $"{prefixPart}{itemPart}{senderPart}";
    }

    internal static void AnnounceAPInfo(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#FFFFFF", message));
    }

    internal static void AnnounceAPSuccess(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#7CFF7C", message));
    }

    internal static void AnnounceAPWarning(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#FFD166", message));
    }

    internal static void AnnounceAPError(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#FF7B7B", message));
    }

    internal static void AnnounceAPCheckSent(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#7FDBFF", message));
    }

    internal static void AnnounceAPItemReceived(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#C792EA", message));
    }

    internal static void AnnounceAPDeathLink(string message)
    {
        AnnounceAPActivity(ColorizeOverlayMessage("#FF5C5C", message));
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        DevOverlayCanvasObject.AddComponent<GraphicRaycaster>();

        // ===== Permanent status panel =====
        GameObject statusPanel = new GameObject("OverlayStatusPanel");
        statusPanel.transform.SetParent(DevOverlayCanvasObject.transform, false);

        Image statusBg = statusPanel.AddComponent<Image>();
        statusBg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform statusPanelRect = statusPanel.GetComponent<RectTransform>();
        statusPanelRect.anchorMin = new Vector2(0f, 0f);
        statusPanelRect.anchorMax = new Vector2(0f, 0f);
        statusPanelRect.pivot = new Vector2(0f, 0f);
        statusPanelRect.anchoredPosition = new Vector2(20f, 300f);
        statusPanelRect.sizeDelta = new Vector2(360f, 190f);

        GameObject statusTextObj = new GameObject("OverlayStatusText");
        statusTextObj.transform.SetParent(statusPanel.transform, false);

        DevOverlayStatusText = statusTextObj.AddComponent<Text>();
        DevOverlayStatusText.font = builtInFont;
        DevOverlayStatusText.supportRichText = true;
        DevOverlayStatusText.fontSize = 16;
        DevOverlayStatusText.color = Color.white;
        DevOverlayStatusText.alignment = TextAnchor.UpperLeft;
        DevOverlayStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        DevOverlayStatusText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform statusTextRect = statusTextObj.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0f, 0f);
        statusTextRect.anchorMax = new Vector2(1f, 1f);
        statusTextRect.offsetMin = new Vector2(10f, 10f);
        statusTextRect.offsetMax = new Vector2(-10f, -10f);

        // ===== Recent log panel =====
        GameObject logPanel = new GameObject("OverlayRecentLogPanel");
        logPanel.transform.SetParent(DevOverlayCanvasObject.transform, false);

        Image logBg = logPanel.AddComponent<Image>();
        logBg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform logPanelRect = logPanel.GetComponent<RectTransform>();
        logPanelRect.anchorMin = new Vector2(0f, 0f);
        logPanelRect.anchorMax = new Vector2(0f, 0f);
        logPanelRect.pivot = new Vector2(0f, 0f);
        logPanelRect.anchoredPosition = new Vector2(20f, 20f);
        logPanelRect.sizeDelta = new Vector2(700f, 240f);

        GameObject logTextObj = new GameObject("OverlayRecentLogText");
        logTextObj.transform.SetParent(logPanel.transform, false);

        DevOverlayRecentLogText = logTextObj.AddComponent<Text>();
        DevOverlayRecentLogText.font = builtInFont;
        DevOverlayRecentLogText.supportRichText = true;
        DevOverlayRecentLogText.fontSize = 12;
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

        string connectionState = "Not Connected";

        if (ArchipelagoClientManager.Instance != null)
        {
            if (ArchipelagoClientManager.Instance.IsConnecting)
                connectionState = "Connecting...";
            else if (ArchipelagoClientManager.Instance.IsConnected)
                connectionState = "Connected";
        }

        string currentHost = SessionState != null && SessionState.Connection != null
            ? SessionState.Connection.Host
            : "<none>";

        int currentPort = SessionState != null && SessionState.Connection != null
            ? SessionState.Connection.Port
            : 0;

        string currentSlot = SessionState != null && SessionState.Connection != null
            ? SessionState.Connection.SlotName
            : "<none>";

        DevOverlayStatusText.text =
            "Laika AP Status\n\n" +
            $"Connection={connectionState}\n" +
            $"APEnabled={SessionState.APEnabled}\n" +
            $"Host={currentHost}:{currentPort}\n" +
            $"Slot={currentSlot}\n" +
            $"Queue={PendingItemQueue.Count}\n" +
            $"WeaponMode={WorldOptions.WeaponMode}\n" +
            $"SessionDeaths={LocalDeathsThisSession}\n" +
            $"DeathsSinceLastLink={DeathsSinceLastDeathLink}\n" +
            $"DeathLink={WorldOptions.DeathLinkEnabled}\n" +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled} ({WorldOptions.DeathAmnestyCount})";

        DevOverlayRecentLogText.transform.parent.gameObject.SetActive(ShowRecentLogOverlay);

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
        private bool showAPDebugPanel = true;
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
            if (!initialized)
                InitializeOverlay();

            if (Input.GetKeyDown(KeyCode.F10))
            {
                showAPDebugPanel = !showAPDebugPanel;

                LaikaMod.Log.LogInfo(
                    $"AP DEBUG UI visibility toggled -> {showAPDebugPanel}"
                );
            }

            if (!updateLoggedOnce)
            {
                updateLoggedOnce = true;
                LaikaMod.Log.LogInfo("DevOverlayController Update() is running.");
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
                GUILayout.Label("Connection: Connecting...");
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

            if (GUILayout.Button(isConnecting ? "Connecting..." : "Connect Active Slot"))
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

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }
    }
}