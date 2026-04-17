using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("com.seras.laikaapprototype", "Laika AP Prototype", "1.0.0")]
public class LaikaMod : BaseUnityPlugin
{
    // Shared logger for Harmony patches.
    internal static ManualLogSource Log;

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

    // Prevents duplicate map-unlock check logs during the current session.
    internal static HashSet<string> SentMapUnlockChecksThisSession = new HashSet<string>();

    // Tracks deaths during the current AP session only.
    internal static int LocalDeathsThisSession = 0;

    // Tracks deaths since the last DeathLink would send.
    internal static int DeathsSinceLastDeathLink = 0;

    // One-shot suppression counter for inbound DeathLink kills.
    // When set to 1, the next detected death will not count toward outbound DeathLink logic.
    internal static int SuppressedDeathLinksRemaining = 0;

    // Temporary world options used for future YAML/config support.
    // For now this is hardcoded, but later it can be loaded from a YAML file.
    internal static APWorldOptions WorldOptions = new APWorldOptions();

    // Development mode toggle.
    // Enqueues development stress test items when set to true.
    internal static bool EnableDevelopmentStressTest = false;

    // Canvas-based dev overlay objects.
    internal static GameObject DevOverlayCanvasObject;
    internal static Text DevOverlayStatusText;
    internal static Text DevOverlayRecentLogText;

    internal static GameObject DevOverlayControllerObject;
    internal static DevOverlayController ActiveDevOverlayController;

    // ===== Startup =====
    private void Awake()
    {
        // Temporary hardcoded option for future YAML support.
        // Change this to Crafting to test unique-material weapon unlocks instead of direct weapon grants.
        WorldOptions.WeaponMode = WeaponGrantMode.Direct;

        // Temporary hardcoded DeathLink options for future YAML support.
        // For now this only controls local logging behavior.
        WorldOptions.DeathLinkEnabled = false;
        WorldOptions.DeathAmnestyEnabled = false;
        WorldOptions.DeathAmnestyCount = 3;

        // Save logger for static patches.
        Log = Logger;

        // Make absolutely sure the plugin component is enabled for Update() / OnGUI().
        enabled = true;

        // Confirm plugin loaded.
        Log.LogInfo(
            $"Laika AP Prototype loaded. " +
            $"WeaponMode={WorldOptions.WeaponMode}, " +
            $"DevStress={EnableDevelopmentStressTest}, " +
            $"DeathLink={WorldOptions.DeathLinkEnabled}, " +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled}, " +
            $"DeathAmnestyCount={WorldOptions.DeathAmnestyCount}"
        );

        // Development stress test items.
        if (EnableDevelopmentStressTest)
        {
            EnqueueDevelopmentStressTestItems();
        }

        // Apply all Harmony patches in this file.
        Harmony harmony = new Harmony("com.seras.laikaapprototype");
        harmony.PatchAll();

        Log.LogInfo("Harmony patches applied.");
    }

    // ===== Discovery / debug helpers =====
    // Logs a message to both BepInEx and the in-game developer overlay.
    internal static void LogInfo(string message)
    {
        Log.LogInfo(message);
        AddOverlayLine("[INFO] " + message);
    }

    internal static void LogWarning(string message)
    {
        Log.LogWarning(message);
        AddOverlayLine("[WARN] " + message);
    }

    internal static void LogError(string message)
    {
        Log.LogError(message);
        AddOverlayLine("[ERROR] " + message);
    }

    // Adds a new recent activity line, shows the recent-log panel,
    // and resets the auto-hide timer through the persistent overlay controller.
    internal static void AddOverlayLine(string line)
    {
        OverlayLines.Enqueue(line);

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

    // ===== Dev overlay state =====
    // Stores recent on-screen debug lines for the in-game developer overlay.
    internal static Queue<string> OverlayLines = new Queue<string>();

    // Maximum number of lines to keep in the overlay at once.
    internal static int MaxOverlayLines = 10;

    // Recent-log box visibility is separate from the always-visible status HUD.
    internal static bool ShowRecentLogOverlay = false;
    internal static float RecentLogAutoHideDelaySeconds = 10f;

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
        statusPanelRect.sizeDelta = new Vector2(320f, 150f);

        GameObject statusTextObj = new GameObject("OverlayStatusText");
        statusTextObj.transform.SetParent(statusPanel.transform, false);

        DevOverlayStatusText = statusTextObj.AddComponent<Text>();
        DevOverlayStatusText.font = builtInFont;
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

        DevOverlayStatusText.text =
            "Laika AP Dev Overlay\n\n" +
            $"Queue={PendingItemQueue.Count}\n" +
            $"SessionDeaths={LocalDeathsThisSession}\n" +
            $"DeathsSinceLastLink={DeathsSinceLastDeathLink}\n" +
            $"DeathLink={WorldOptions.DeathLinkEnabled}\n" +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled} ({WorldOptions.DeathAmnestyCount})";

        DevOverlayRecentLogText.transform.parent.gameObject.SetActive(ShowRecentLogOverlay);

        if (ShowRecentLogOverlay)
        {
            DevOverlayRecentLogText.text =
                "Recent Logs:\n\n" +
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

    // ===== Queue processing =====
    // Temporary stress test queue used during development.
    // Kept separate from Awake() so startup logic stays easier to read.
    internal static void EnqueueDevelopmentStressTestItems()
    {
        EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA", 250, "250 Viscera"));
        EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map Piece: Where Our Bikes Growl"));
        EnqueueItem(GetRocketLauncherUnlockItem());
        EnqueueItem(new PendingItem(ItemKind.WeaponUpgrade, "I_W_SNIPER", 1, "Progressive Sniper Rifle"));
        EnqueueItem(new PendingItem(ItemKind.Ingredient, "I_C_SARDINE", 1, "Sardine"));
        EnqueueItem(new PendingItem(ItemKind.Collectible, "I_CASSETTE_11", 1, "Cassette 11"));
        EnqueueItem(new PendingItem(ItemKind.PuppyTreat, "I_GAMEBOY", 1, "Handheld Console (Puppy's Treat)"));
        EnqueueItem(new PendingItem(ItemKind.KeyItem, "I_E_DASH", 1, "Nitrous Dash"));
        EnqueueItem(new PendingItem(ItemKind.Material, "I_BASALT", 1, "Basalt"));
        EnqueueItem(new PendingItem(ItemKind.Material, "I_METAL_GOOD", 1, "Refined Metal"));
        EnqueueItem(new PendingItem(ItemKind.Material, "I_MATERIAL_SHOTGUN", 1, "Rusty Spring (Shotgun Material)"));
    }

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Main queue processor. Called when game systems are in a good state.
    internal static void ProcessPendingItemQueue(string sourceTag)
    {
        if (IsProcessingQueue)
        {
            LogInfo($"{sourceTag}: queue processing already in progress, skipping nested call.");
            return;
        }

        if (PendingItemQueue.Count == 0)
        {
            Log.LogInfo($"{sourceTag}: no pending items to process.");
            return;
        }

        IsProcessingQueue = true;

        try
        {
            LogInfo($"{sourceTag}: starting queue processing. Count={PendingItemQueue.Count}");
            LogWeaponInventorySnapshot($"{sourceTag} BEFORE");

            Queue<PendingItem> remainingQueue = new Queue<PendingItem>();

            while (PendingItemQueue.Count > 0)
            {
                PendingItem item = PendingItemQueue.Dequeue();

                try
                {
                    bool granted = TryGrantPendingItem(item, sourceTag);

                    if (!granted)
                    {
                        LogWarning($"{sourceTag}: item not granted, re-queueing -> {item}");
                        remainingQueue.Enqueue(item);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"{sourceTag}: exception while processing {item}:\n{ex}");
                    remainingQueue.Enqueue(item);
                }
            }

            PendingItemQueue = remainingQueue;

            LogWeaponInventorySnapshot($"{sourceTag} AFTER");
            LogInfo($"{sourceTag}: queue processing finished. Remaining={PendingItemQueue.Count}");
        }
        finally
        {
            IsProcessingQueue = false;
        }
    }

    // ===== Grant handlers =====
    // Routes an item to the correct grant handler.
    internal static bool TryGrantPendingItem(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: processing {item}");

        switch (item.Kind)
        {
            case ItemKind.Currency:
                return TryGrantCurrency(item, sourceTag);

            case ItemKind.Weapon:
                return TryGrantWeapon(item, sourceTag);

            case ItemKind.WeaponUpgrade:
                return TryGrantWeaponUpgrade(item, sourceTag);

            case ItemKind.Ingredient:
                return TryGrantIngredient(item, sourceTag);

            case ItemKind.Material:
                return TryGrantMaterial(item, sourceTag);

            case ItemKind.Collectible:
                return TryGrantCollectible(item, sourceTag);
           
            case ItemKind.PuppyTreat:
                return TryGrantPuppyTreat(item, sourceTag);

            case ItemKind.KeyItem:
                return TryGrantKeyItem(item, sourceTag);

            case ItemKind.MapUnlock:
                return TryGrantMapUnlock(item, sourceTag);

            default:
                LogWarning($"{sourceTag}: unsupported item kind -> {item.Kind}");
                return false;
        }
    }

    // Grants Viscera through EconomyManager.
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        // Friendly log line for player-facing readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var economy = Singleton<EconomyManager>.Instance;

        if (economy == null)
        {
            LogWarning($"{sourceTag}: currency grant failed, EconomyManager is null.");
            return false;
        }

        try
        {
            // Read current money before adding.
            int before = economy.Money;
            LogInfo($"{sourceTag}: currency before grant = {before}");

            // Add the requested amount.
            economy.AddMoney(item.Amount);

            // Read current money after adding.
            int after = economy.Money;
            LogInfo($"{sourceTag}: currency after grant = {after}");

            // Success if the money increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
        }
    }

    // Ownership-based grant handlers.
    // These usually treat already-owned items as success so they do not remain stuck in the queue.
    internal static bool TryGrantWeapon(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: weapon grant failed, WeaponsInventory is null.");
            return false;
        }

        if (inventory.Weapons == null)
        {
            LogWarning($"{sourceTag}: weapon grant failed, Weapons list is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasWeapon(item.Id);
        LogInfo($"{sourceTag}: weapon {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
        {
            LogInfo($"{sourceTag}: skipping weapon {item.Id} because player already owns it.");
            return true;
        }

        bool addResult = inventory.AddWeapon(item.Id);
        LogInfo($"{sourceTag}: AddWeapon({item.Id}) returned {addResult}");

        bool ownedAfter = inventory.HasWeapon(item.Id);
        LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for weapon {item.Id}");

        return ownedAfter;
    }

    // Grants one weapon upgrade level using the game's own weapon upgrade method.
    internal static bool TryGrantWeaponUpgrade(PendingItem item, string sourceTag)
    {
        // Friendly log line for readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime weapons inventory singleton.
        var weaponsInventory = Singleton<WeaponsInventory>.Instance;

        // Safety check in case the manager is not ready yet.
        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, WeaponsInventory is null.");
            return false;
        }

        try
        {
            // Make sure the player actually owns the weapon before trying to upgrade it.
            bool alreadyOwned = weaponsInventory.HasWeapon(item.Id);
            LogInfo($"{sourceTag}: weapon upgrade target {item.Id}, alreadyOwned={alreadyOwned}");

            // If the player does not own the weapon yet, do not consume the queue item.
            if (!alreadyOwned)
            {
                LogWarning($"{sourceTag}: cannot upgrade weapon {item.Id} because player does not own it yet.");
                return false;
            }

            // Ask the game to upgrade the weapon by one level.
            bool upgradeResult = weaponsInventory.UpgradeWeapon(item.Id);
            LogInfo($"{sourceTag}: UpgradeWeapon({item.Id}) returned {upgradeResult}");

            // Trust the game's own result here.
            return upgradeResult;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while upgrading weapon {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants ingredients through InventoryManager using the item's internal ID.
    internal static bool TryGrantIngredient(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: ingredient grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Read amount before grant.
            int before = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: ingredient {item.Id} before grant = {before}");

            // Try to add the ingredient by item id.
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Read amount after grant.
            int after = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: ingredient {item.Id} after grant = {after}");

            // Success if amount increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting ingredient {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants crafting materials through InventoryManager using the item's internal ID.
    internal static bool TryGrantMaterial(PendingItem item, string sourceTag)
    {
        // Friendly log line for readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: material grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Read amount before grant.
            int before = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: material {item.Id} before grant = {before}");

            // Try to add the material by item id.
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Read amount after grant.
            int after = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: material {item.Id} after grant = {after}");

            // Success if amount increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting material {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a cassette / collectible using the game's cassette manager.
    internal static bool TryGrantCollectible(PendingItem item, string sourceTag)
    {
        // Grab the cassette manager singleton that owns cassette inventory.
        var cassettesManager = Singleton<CassettesManager>.Instance;

        // Safety check in case the manager is not ready yet.
        if (cassettesManager == null)
        {
            LogWarning($"{sourceTag}: collectible grant failed, CassettesManager is null.");
            return false;
        }

        try
        {
            // Check whether the player already owns this cassette.
            bool alreadyOwned = cassettesManager.HasCassette(item.Id);
            LogInfo($"{sourceTag}: cassette {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it doesn't stay in the queue.
            if (alreadyOwned)
            {
                LogInfo($"{sourceTag}: skipping cassette {item.Id} because player already owns it.");
                return true;
            }

            // Try to add the cassette by its internal ID.
            bool addResult = cassettesManager.AddCassetteToInventory(item.Id, null, false);
            LogInfo($"{sourceTag}: AddCassetteToInventory({item.Id}) returned {addResult}");

            // Check again after trying to add it.
            bool ownedAfter = cassettesManager.HasCassette(item.Id);
            LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for cassette {item.Id}");

            // Success if the player now owns it.
            return ownedAfter;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting cassette {item.Id}:\n{ex}");
            return false;
        }
    }

    // Puppy treats are tracked through the normal inventory system.
    // If already owned, treat the grant as success so the item does not stay stuck in the queue.
    internal static bool TryGrantPuppyTreat(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting puppy treat {item.DisplayName}");

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, InventoryManager is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasItem(item.Id);

        LogInfo($"{sourceTag}: puppy treat {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
            return true;

        bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);

        LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

        return addResult;
    }

    // Grants a key item through the game's inventory system.
    // The InventoryManager will internally route key items through AddKeyItem(...)
    internal static bool TryGrantKeyItem(PendingItem item, string sourceTag)
    {
        // Friendly log line so we can read logs more easily.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime inventory manager singleton.
        var inventory = Singleton<InventoryManager>.Instance;

        // Safety check in case inventory is not ready yet.
        if (inventory == null)
        {
            LogWarning($"{sourceTag}: key item grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Check whether the player already has this item before granting it.
            bool alreadyOwned = inventory.HasItem(item.Id);
            LogInfo($"{sourceTag}: key item {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it does not stay in the queue.
            if (alreadyOwned)
            {
                LogInfo($"{sourceTag}: skipping key item {item.Id} because player already owns it.");
                return true;
            }

            // Try to add it through InventoryManager.
            // If the item is marked as a key item internally, the game will route it through AddKeyItem(...).
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // If the upgrade item needs extra progression flags, apply them now.
            if (addResult)
            {
                ApplyKeyItemProgressionFlags(item, sourceTag);
            }

            // Key items/upgrades may not show up in the normal HasItem() check.
            // If AddItem() returned true, trust the game's internal key item handling.
            LogInfo($"{sourceTag}: assuming success from AddItem result for key item {item.Id}");

            return addResult;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting key item {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a map unlock through ProgressionData.
    // Renato's map popup and unlock flow use IDs like M_A_W06.
    internal static bool TryGrantMapUnlock(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;

        if (progressionManager == null)
        {
            LogWarning($"{sourceTag}: map unlock grant failed, ProgressionManager is null.");
            return false;
        }

        try
        {
            // Ask the game to unlock the target map area directly.
            progressionManager.ProgressionData.UnlockMapArea(item.Id);
            LogInfo($"{sourceTag}: UnlockMapArea({item.Id}) called successfully.");

            // For now, trust the game's internal unlock flow.
            return true;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while unlocking map area {item.Id}:\n{ex}");
            return false;
        }
    }

    // ===== Progression helpers =====
    // Applies extra progression flags needed for certain upgrades to actually become usable.
    // Some upgrade items are not fully functional from AddItem(...) alone.
    internal static void ApplyKeyItemProgressionFlags(PendingItem item, string sourceTag)
    {
        // Dash needs the G_DASH_UNLOCKED progression flag in addition to the item itself.
        if (item.Id == "I_E_DASH")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_DASH_UNLOCKED", true, false);
            LogInfo($"{sourceTag}: set progression flag G_DASH_UNLOCKED for Dash.");
        }

        // Hook needs the G_HOOK_UNLOCKED progression flag in addition to the item itself.
        else if (item.Id == "I_E_HOOK")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_HOOK_UNLOCKED", true, false);
            LogInfo($"{sourceTag}: set progression flag G_HOOK_UNLOCKED for Hook.");
        }
    }

    // ===== Display-name helpers =====
    // Helper that maps internal map IDs to readable display names.
    // Expand this as more map area IDs are confirmed.
    internal static string GetMapUnlockDisplayName(string mapAreaId)
    {
        switch (mapAreaId)
        {
            case "M_A_CAMP": return "Map Piece: Where We Live";
            case "M_A_W06": return "Map Piece: Where Our Bikes Growl";
            default: return $"Map Piece ({mapAreaId})";
        }
    }

    // ===== Weapon mode helpers =====
    // Returns the correct item definition for a major weapon unlock
    // based on the configured weapon grant mode.
    //
    // Direct mode:
    // Returns the weapon itself.
    //
    // Crafting mode:
    // Returns the weapon's unique crafting material instead.
    //
    // Note:
    // Crossbow does not currently have a clean one-to-one unique crafting material,
    // so it should remain direct-only until a better crafting-mode design is decided.
    internal static PendingItem GetWeaponUnlockItem(
        string directWeaponId,
        string directDisplayName,
        string craftingMaterialId,
        string craftingDisplayName)
    {
        if (WorldOptions.WeaponMode == WeaponGrantMode.Crafting)
        {
            return new PendingItem(ItemKind.Material, craftingMaterialId, 1, craftingDisplayName);
        }

        return new PendingItem(ItemKind.Weapon, directWeaponId, 1, directDisplayName);
    }
   
    // Specific weapon unlock helpers that use the generic weapon-mode resolver.
    internal static PendingItem GetRocketLauncherUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_ROCKETLAUNCHER",
            "Rocket Launcher",
            "I_MATERIAL_ROCKETLAUNCHER",
            "Missile (Rocket Launcher Material)"
        );
    }

    internal static PendingItem GetShotgunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SHOTGUN",
            "Shotgun",
            "I_MATERIAL_SHOTGUN",
            "Rusty Spring (Shotgun Material)"
        );
    }

    internal static PendingItem GetSniperUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SNIPER",
            "Sniper Rifle",
            "I_MATERIAL_SNIPER",
            "Magnifying Glass (Sniper Rifle Material)"
        );
    }

    internal static PendingItem GetMachineGunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_UZI",
            "Machine Gun",
            "I_MATERIAL_UZI",
            "Titanium Plates (Machine Gun Material)"
        );
    }

    // ===== DeathLink helpers =====
    // Kept for safety in case other game flows call the explicit RiderHead.Kill(bool, bool) overload.
    // Handles AP-local death counting after a real player death is detected.
    internal static void OnPlayerDeathDetected(string sourceTag, bool? useBlood = null, bool? moneySack = null)
    {
        try
        {
            // If this death was caused by a future incoming DeathLink,
            // do not count it toward outbound DeathLink logic.
            if (SuppressedDeathLinksRemaining > 0)
            {
                SuppressedDeathLinksRemaining--;

                LogInfo(
                    $"{sourceTag} | " +
                    $"Incoming DeathLink suppression consumed. " +
                    $"RemainingSuppressedDeaths={SuppressedDeathLinksRemaining}"
                );

                return;
            }

            // Increment AP-local counters only.
            // Do not use the game's total "deaths" stat for death amnesty.
            LocalDeathsThisSession++;
            DeathsSinceLastDeathLink++;

            LogInfo(
                $"{sourceTag} | " +
                $"Session={LocalDeathsThisSession}, " +
                $"SinceLink={DeathsSinceLastDeathLink}, " +
                $"blood={(useBlood.HasValue ? useBlood.Value.ToString() : "def")}, " +
                $"sack={(moneySack.HasValue ? moneySack.Value.ToString() : "def")}"
            );

            // Evaluate what DeathLink would do for this death.
            EvaluateDeathLinkAfterLocalDeath(sourceTag);
        }
        catch (Exception ex)
        {
            LogError($"OnPlayerDeathDetected exception:\n{ex}");
        }
    }

    // Evaluates whether the current local death should count toward DeathLink
    // and logs what would happen.
    // For now this is log-only scaffolding until real AP networking is added.
    internal static void EvaluateDeathLinkAfterLocalDeath(string sourceTag)
    {
        // If DeathLink is disabled entirely, do nothing.
        if (!WorldOptions.DeathLinkEnabled)
        {
            LogInfo($"{sourceTag}: DeathLink disabled. No outbound DeathLink would be sent.");
            return;
        }

        // If death amnesty is disabled, every valid local death would send immediately.
        if (!WorldOptions.DeathAmnestyEnabled)
        {
            LogInfo($"{sourceTag}: DEATHLINK WOULD SEND NOW (death amnesty disabled).");
            return;
        }

        // Safety clamp in case a bad config sets the threshold too low.
        int requiredDeaths = Math.Max(1, WorldOptions.DeathAmnestyCount);

        LogInfo(
            $"{sourceTag}: Death Amnesty Progress = {DeathsSinceLastDeathLink} / {requiredDeaths}"
        );

        // When enough deaths are reached, a DeathLink would send.
        if (DeathsSinceLastDeathLink >= requiredDeaths)
        {
            LogInfo($"{sourceTag}: DEATHLINK WOULD SEND NOW (death amnesty threshold reached).");

            // Reset the amnesty counter after a "send".
            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after simulated send.");
        }
    }

    // ===== Harmony patches =====
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
                // Temporarily disabled during overlay auto-hide/input testing.
                // LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;
                // Temporarily disabled during overlay auto-hide/input testing.
                // LogAllCassetteIds();
            }

            // Process AP queue afterward.
            ProcessPendingItemQueue("InitialItemGrant");
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

            LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");
        }
    }

    // Logs Renato's map popup data when the buy-map popup opens.
    // This helps us discover the real runtime mapAreaID values used by UnlockMapArea(...).
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

    // Tracks map unlock purchases/checks when Renato's map purchase actually succeeds.
    // Logs the real map area ID when the game performs the map unlock.
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

                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";

                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                // Avoid duplicate check logs during the same session.
                if (!LaikaMod.SentMapUnlockChecksThisSession.Contains(mapAreaId))
                {
                    LaikaMod.SentMapUnlockChecksThisSession.Add(mapAreaId);

                    // Prototype send-side check logging.
                    // Later replace this with actual Archipelago check sending.
                    string mapName = LaikaMod.GetMapUnlockDisplayName(mapAreaId);
                    LaikaMod.LogInfo($"CHECK SENT: {mapName} ({mapAreaId})");
                }
                else
                {
                    string mapName = LaikaMod.GetMapUnlockDisplayName(mapAreaId);
                    LaikaMod.LogInfo($"MAP CHECK ALREADY SENT THIS SESSION: {mapName} ({mapAreaId})");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch: exception while logging map unlock action:\n{ex}");
            }
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

    // Update only exists to ensure one-time initialization confirmation.
    // Recent log visibility is driven by AddOverlayLine(...) + Invoke-based auto-hide.
    public class DevOverlayController : MonoBehaviour
    {
        private bool initialized = false;
        private bool updateLoggedOnce = false;

        void Start()
        {
            LaikaMod.Log.LogInfo("DevOverlayController Start() fired.");
            InitializeOverlay();
        }

        void OnEnable()
        {
            LaikaMod.Log.LogInfo("DevOverlayController OnEnable() fired.");
            InitializeOverlay();
        }

        void Update()
        {
            if (!initialized)
                InitializeOverlay();

            if (!updateLoggedOnce)
            {
                updateLoggedOnce = true;
                LaikaMod.Log.LogInfo("DevOverlayController Update() is running.");
            }
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
    }
}

// ===== Models / enums =====
// High-level AP item categories.
public enum ItemKind
{
    Currency,

    Weapon,
    WeaponUpgrade,

    Ingredient,
    Material,

    Collectible,
    PuppyTreat,

    KeyItem,
    MapUnlock,

    Unknown
}

// Determines how major weapons should be granted in the future.
//
// Direct:
// The player receives the weapon itself outright.
// Example: Shotgun is granted as I_W_SHOTGUN.
//
// Crafting:
// The player receives the unique crafting material instead.
// Example: Shotgun unlock is represented by I_MATERIAL_SHOTGUN.
public enum WeaponGrantMode
{
    Direct,
    Crafting
}

// Stores future world-generation / slot options.
// Later this can be filled from YAML or another external config source.
public class APWorldOptions
{
    // Controls whether major weapons are granted directly
    // or represented by their unique crafting materials instead.
    public WeaponGrantMode WeaponMode { get; set; } = WeaponGrantMode.Direct;

    // Enables or disables DeathLink behavior entirely.
    // For now this only affects local logging/scaffolding.
    public bool DeathLinkEnabled { get; set; } = false;

    // Enables death amnesty behavior.
    // When enabled, local deaths only "send" a DeathLink after enough deaths accumulate.
    public bool DeathAmnestyEnabled { get; set; } = false;

    // Number of local deaths required before a DeathLink would send when amnesty is enabled.
    public int DeathAmnestyCount { get; set; } = 1;
}

// Represents one pending AP-style item waiting to be granted.
// This stores both the real internal game ID and a friendly display name.
public class PendingItem
{
    // What subsystem should handle this item.
    public ItemKind Kind { get; private set; }

    // The real internal game ID used by the mod/game code.
    // Example: I_W_SNIPER or I_C_MEAT
    public string Id { get; private set; }

    // Quantity / amount for the item.
    // Example: 250 viscera or 3 meat
    public int Amount { get; private set; }

    // Friendly player-facing name for logs/UI.
    // Example: "Sniper Rifle" or "Meat x3"
    public string DisplayName { get; private set; }

    public PendingItem(ItemKind kind, string id, int amount, string displayName)
    {
        Kind = kind;
        Id = id;
        Amount = amount;
        DisplayName = displayName;
    }

    public override string ToString()
    {
        return $"Kind={Kind}, Id={Id}, Amount={Amount}, DisplayName={DisplayName}";
    }
}