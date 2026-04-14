using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Quests;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
using System.Text;

[BepInPlugin("com.seras.laikaapprototype", "Laika AP Prototype", "1.0.0")]
public class LaikaMod : BaseUnityPlugin
{
    // Shared logger for Harmony patches.
    internal static ManualLogSource Log;

    // Queue of pending Archipelago-style received items.
    internal static Queue<PendingItem> PendingItemQueue = new Queue<PendingItem>();

    // Prevent nested queue processing when UI refreshes trigger more hooks.
    internal static bool IsProcessingQueue = false;

    // Prevents ingredient IDs from being logged more than once.
    // Without this, every UI refresh would spam the console repeatedly.
    internal static bool IngredientIdsLogged = false;

    // Prevents cassette IDs from logging repeatedly.
    internal static bool CassetteIdsLogged = false;

    private void Awake()
    {
        // Save logger for static patches.
        Log = Logger;

        // Confirm plugin loaded.
        Log.LogInfo("LaikaMod Awake() called. GENERALIZED QUEUE BUILD 25");

        // Known-good sanity test.
        // Internal ID is VISCERA, but the friendly name is what we want players to see later.
        EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA", 250, "250 Viscera"));

        // Current subsystem under investigation.
        // Maya's Pendant is explicitly handled in AddKeyItem(...), so it is a great first real test.
        EnqueueItem(new PendingItem(ItemKind.Upgrade, "I_MAYA_PENDANT", 1, "Maya's Pendant"));

        // Apply all Harmony patches in this file.
        Harmony harmony = new Harmony("com.seras.laikaapprototype");
        harmony.PatchAll();

        Log.LogInfo("Harmony patches applied.");
    }

    // Logs every ingredient ID the game has loaded.
    // This helps us discover REAL ingredient IDs for testing.
    internal static void LogAllIngredientIds()
    {
        // Grab the game's master item loader singleton.
        var loader = Singleton<ItemDataLoader>.Instance;

        // Safety check in case loader somehow isn't ready yet.
        if (loader == null)
        {
            Log.LogWarning("ItemDataLoader is null.");
            return;
        }

        // Ask the game for every ingredient definition.
        var ingredients = loader.GetAllIngredientDatas();

        // Another safety check.
        if (ingredients == null)
        {
            Log.LogWarning("GetAllIngredientDatas returned null.");
            return;
        }

        // Print how many ingredients exist total.
        Log.LogInfo($"INGREDIENT LIST START: count={ingredients.Count}");

        // Loop through every ingredient.
        foreach (var ingredient in ingredients)
        {
            // Skip broken/null entries just in case.
            if (ingredient == null)
                continue;

            // Print the ingredient's internal ID.
            Log.LogInfo($"INGREDIENT ID: {ingredient.id}");
        }

        Log.LogInfo("INGREDIENT LIST END");
    }

    // Logs every cassette ID the game has loaded.
    // This helps us discover REAL cassette IDs for testing.
    internal static void LogAllCassetteIds()
    {
        // Grab the game's cassette data loader singleton.
        var loader = Singleton<CassettesDataLoader>.Instance;

        // Safety check in case loader is not ready yet.
        if (loader == null)
        {
            Log.LogWarning("CassettesDataLoader is null.");
            return;
        }

        // Ask the game for all cassette IDs.
        var cassetteIds = loader.GetCassettesIds();

        // Another safety check.
        if (cassetteIds == null)
        {
            Log.LogWarning("GetCassetteIds returned null.");
            return;
        }

        // Print how many cassettes exist total.
        Log.LogInfo($"CASSETTE LIST START: count={cassetteIds.Count}");

        // Loop through every cassette ID.
        foreach (var cassetteId in cassetteIds)
        {
            if (string.IsNullOrEmpty(cassetteId))
                continue;

            Log.LogInfo($"CASSETTE ID: {cassetteId}");
        }

        Log.LogInfo("CASSETTE LIST END");
    }

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        Log.LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Logs the current visible weapon inventory for debugging before/after queue processing.
    internal static void LogWeaponInventorySnapshot(string label)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            Log.LogWarning($"{label}: WeaponsInventory is null.");
            return;
        }

        if (inventory.Weapons == null)
        {
            Log.LogWarning($"{label}: Weapons list is null.");
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

        Log.LogInfo(sb.ToString());
    }

    // Main queue processor. Called when game systems are in a good state.
    internal static void ProcessPendingItemQueue(string sourceTag)
    {
        if (IsProcessingQueue)
        {
            Log.LogInfo($"{sourceTag}: queue processing already in progress, skipping nested call.");
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
            Log.LogInfo($"{sourceTag}: starting queue processing. Count={PendingItemQueue.Count}");
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
                        Log.LogWarning($"{sourceTag}: item not granted, re-queueing -> {item}");
                        remainingQueue.Enqueue(item);
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError($"{sourceTag}: exception while processing {item}:\n{ex}");
                    remainingQueue.Enqueue(item);
                }
            }

            PendingItemQueue = remainingQueue;

            LogWeaponInventorySnapshot($"{sourceTag} AFTER");
            Log.LogInfo($"{sourceTag}: queue processing finished. Remaining={PendingItemQueue.Count}");
        }
        finally
        {
            IsProcessingQueue = false;
        }
    }

    // Routes an item to the correct grant handler.
    internal static bool TryGrantPendingItem(PendingItem item, string sourceTag)
    {
        Log.LogInfo($"{sourceTag}: processing {item}");

        switch (item.Kind)
        {
            case ItemKind.Weapon:
                return TryGrantWeapon(item, sourceTag);

            case ItemKind.Currency:
                return TryGrantCurrency(item, sourceTag);

            case ItemKind.Ingredient:
                return TryGrantIngredient(item, sourceTag);

            case ItemKind.Collectible:
                return TryGrantCollectible(item, sourceTag);

            case ItemKind.Upgrade:
                return TryGrantUpgrade(item, sourceTag);

            case ItemKind.FastTravel:
                return TryGrantFastTravel(item, sourceTag);

            default:
                Log.LogWarning($"{sourceTag}: unsupported item kind -> {item.Kind}");
                return false;
        }
    }

    // Working weapon grant handler.
    internal static bool TryGrantWeapon(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            Log.LogWarning($"{sourceTag}: weapon grant failed, WeaponsInventory is null.");
            return false;
        }

        if (inventory.Weapons == null)
        {
            Log.LogWarning($"{sourceTag}: weapon grant failed, Weapons list is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasWeapon(item.Id);
        Log.LogInfo($"{sourceTag}: weapon {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
        {
            Log.LogInfo($"{sourceTag}: skipping weapon {item.Id} because player already owns it.");
            return true;
        }

        bool addResult = inventory.AddWeapon(item.Id);
        Log.LogInfo($"{sourceTag}: AddWeapon({item.Id}) returned {addResult}");

        bool ownedAfter = inventory.HasWeapon(item.Id);
        Log.LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for weapon {item.Id}");

        return ownedAfter;
    }

    // Real currency handler using EconomyManager.
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        // Friendly log line for player-facing readability.
        Log.LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var economy = Singleton<EconomyManager>.Instance;

        if (economy == null)
        {
            Log.LogWarning($"{sourceTag}: currency grant failed, EconomyManager is null.");
            return false;
        }

        try
        {
            // Read current money before adding.
            int before = economy.Money;
            Log.LogInfo($"{sourceTag}: currency before grant = {before}");

            // Add the requested amount.
            economy.AddMoney(item.Amount);

            // Read current money after adding.
            int after = economy.Money;
            Log.LogInfo($"{sourceTag}: currency after grant = {after}");

            // Success if the money increased.
            return after > before;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
        }
    }

    // Real currency handler using InventoryManager.
    internal static bool TryGrantIngredient(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            Log.LogWarning($"{sourceTag}: ingredient grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Read amount before grant.
            int before = inventory.GetItemAmount(item.Id);
            Log.LogInfo($"{sourceTag}: ingredient {item.Id} before grant = {before}");

            // Try to add the ingredient by item id.
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            Log.LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Read amount after grant.
            int after = inventory.GetItemAmount(item.Id);
            Log.LogInfo($"{sourceTag}: ingredient {item.Id} after grant = {after}");

            // Success if amount increased.
            return after > before;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while granting ingredient {item.Id}:\n{ex}");
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
            Log.LogWarning($"{sourceTag}: collectible grant failed, CassettesManager is null.");
            return false;
        }

        try
        {
            // Check whether the player already owns this cassette.
            bool alreadyOwned = cassettesManager.HasCassette(item.Id);
            Log.LogInfo($"{sourceTag}: cassette {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it doesn't stay in the queue.
            if (alreadyOwned)
            {
                Log.LogInfo($"{sourceTag}: skipping cassette {item.Id} because player already owns it.");
                return true;
            }

            // Try to add the cassette by its internal ID.
            bool addResult = cassettesManager.AddCassetteToInventory(item.Id, null, false);
            Log.LogInfo($"{sourceTag}: AddCassetteToInventory({item.Id}) returned {addResult}");

            // Check again after trying to add it.
            bool ownedAfter = cassettesManager.HasCassette(item.Id);
            Log.LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for cassette {item.Id}");

            // Success if the player now owns it.
            return ownedAfter;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while granting cassette {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a key item / upgrade through the game's inventory system.
    // The InventoryManager will internally route key items through AddKeyItem(...)
    internal static bool TryGrantUpgrade(PendingItem item, string sourceTag)
    {
        // Friendly log line so we can read logs more easily.
        Log.LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime inventory manager singleton.
        var inventory = Singleton<InventoryManager>.Instance;

        // Safety check in case inventory is not ready yet.
        if (inventory == null)
        {
            Log.LogWarning($"{sourceTag}: upgrade grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Check whether the player already has this item before granting it.
            bool alreadyOwned = inventory.HasItem(item.Id);
            Log.LogInfo($"{sourceTag}: upgrade/key item {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it does not stay in the queue.
            if (alreadyOwned)
            {
                Log.LogInfo($"{sourceTag}: skipping upgrade/key item {item.Id} because player already owns it.");
                return true;
            }

            // Try to add it through InventoryManager.
            // If the item is marked as a key item internally, the game will route it through AddKeyItem(...).
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            Log.LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Key items/upgrades may not show up in the normal HasItem() check.
            // If AddItem() returned true, trust the game's internal key item handling.
            Log.LogInfo($"{sourceTag}: assuming success from AddItem result for upgrade/key item {item.Id}");

            return addResult;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while granting upgrade/key item {item.Id}:\n{ex}");
            return false;
        }
    }

    // Placeholder fast travel handler.
    internal static bool TryGrantFastTravel(PendingItem item, string sourceTag)
    {
        // TODO: Replace with actual teleport / map unlock call once found.
        Log.LogInfo($"{sourceTag}: TODO fast travel grant -> id={item.Id}, amount={item.Amount}");
        return false;
    }

    [HarmonyPatch(typeof(WeaponsOverlay), "InitializeWeaponsData")]
    public class WeaponsOverlayPatch
    {
        static void Postfix()
        {
            Log.LogInfo("WeaponsOverlay.InitializeWeaponsData postfix triggered.");

            // Only log ingredient IDs once per launch.
            if (!IngredientIdsLogged)
            {
                IngredientIdsLogged = true;

                LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;

                LogAllCassetteIds();
            }

            ProcessPendingItemQueue("InitialItemGrant");

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

            Log.LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");
        }
    }
}

// High-level AP item categories.
public enum ItemKind
{
    Weapon,
    Currency,
    Ingredient,
    Collectible,
    Upgrade,
    FastTravel,
    Unknown
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