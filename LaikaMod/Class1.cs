using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
using System.Text;
using Laika.PlayMaker.FsmActions;

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

    // Prevents duplicate map-unlock check logs during the current session.
    internal static HashSet<string> SentMapUnlockChecks = new HashSet<string>();

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
        EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map Piece: Where Our Bikes Growl"));

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
           
            case ItemKind.PuppyTreat:
                return TryGrantPuppyTreat(item, sourceTag);

            case ItemKind.Upgrade:
                return TryGrantUpgrade(item, sourceTag);

            case ItemKind.MapUnlock:
                return TryGrantMapUnlock(item, sourceTag);

            case ItemKind.WeaponUpgrade:
                return TryGrantWeaponUpgrade(item, sourceTag);

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

    // Grants one weapon upgrade level using the game's own weapon upgrade method.
    internal static bool TryGrantWeaponUpgrade(PendingItem item, string sourceTag)
    {
        // Friendly log line for readability.
        Log.LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime weapons inventory singleton.
        var weaponsInventory = Singleton<WeaponsInventory>.Instance;

        // Safety check in case the manager is not ready yet.
        if (weaponsInventory == null)
        {
            Log.LogWarning($"{sourceTag}: weapon upgrade grant failed, WeaponsInventory is null.");
            return false;
        }

        try
        {
            // Make sure the player actually owns the weapon before trying to upgrade it.
            bool alreadyOwned = weaponsInventory.HasWeapon(item.Id);
            Log.LogInfo($"{sourceTag}: weapon upgrade target {item.Id}, alreadyOwned={alreadyOwned}");

            // If the player does not own the weapon yet, do not consume the queue item.
            if (!alreadyOwned)
            {
                Log.LogWarning($"{sourceTag}: cannot upgrade weapon {item.Id} because player does not own it yet.");
                return false;
            }

            // Ask the game to upgrade the weapon by one level.
            bool upgradeResult = weaponsInventory.UpgradeWeapon(item.Id);
            Log.LogInfo($"{sourceTag}: UpgradeWeapon({item.Id}) returned {upgradeResult}");

            // Trust the game's own result here.
            return upgradeResult;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while upgrading weapon {item.Id}:\n{ex}");
            return false;
        }
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

    // Grants a "Puppy's Treat" key item.
    internal static bool TryGrantPuppyTreat(PendingItem item, string sourceTag)
    {
        Log.LogInfo($"{sourceTag}: granting Puppy Treat {item.DisplayName}");

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            Log.LogWarning($"{sourceTag}: puppy treat grant failed, InventoryManager is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasItem(item.Id);

        Log.LogInfo($"{sourceTag}: puppy treat {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
            return true;

        bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);

        Log.LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

        return addResult;
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

            // If the upgrade item needs extra progression flags, apply them now.
            if (addResult)
            {
                ApplyUpgradeProgressionFlags(item, sourceTag);
            }

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

    // Applies extra progression flags needed for certain upgrades to actually become usable.
    // Some upgrade items are not fully functional from AddItem(...) alone.
    internal static void ApplyUpgradeProgressionFlags(PendingItem item, string sourceTag)
    {
        // Dash needs the G_DASH_UNLOCKED progression flag in addition to the item itself.
        if (item.Id == "I_E_DASH")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_DASH_UNLOCKED", true, false);
            Log.LogInfo($"{sourceTag}: set progression flag G_DASH_UNLOCKED for Dash.");
        }

        // Hook needs the G_HOOK_UNLOCKED progression flag in addition to the item itself.
        else if (item.Id == "I_E_HOOK")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_HOOK_UNLOCKED", true, false);
            Log.LogInfo($"{sourceTag}: set progression flag G_HOOK_UNLOCKED for Hook.");
        }
    }

    // Grants a map/traversal unlock through ProgressionData.
    // Renato's map popup and unlock flow use IDs like M_A_W06.
    internal static bool TryGrantMapUnlock(PendingItem item, string sourceTag)
    {
        Log.LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;

        if (progressionManager == null)
        {
            Log.LogWarning($"{sourceTag}: Map unlock grant failed, ProgressionManager is null.");
            return false;
        }

        try
        {
            // Ask the game to unlock the target map area directly.
            progressionManager.ProgressionData.UnlockMapArea(item.Id);
            Log.LogInfo($"{sourceTag}: UnlockMapArea({item.Id}) called successfully.");

            // For now, trust the game's internal unlock flow.
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"{sourceTag}: exception while unlocking map area {item.Id}:\n{ex}");
            return false;
        }
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
                LaikaMod.Log.LogWarning("ShowBuyingMapPopupPatch: __instance was null.");
                return;
            }

            // Read the real PlayMaker values that Renato's popup is using.
            string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";
            int mapAreaPrice = __instance.mapAreaPrice != null ? __instance.mapAreaPrice.Value : -1;

            // Log both the area ID and the price so we can identify which map piece is which.
            LaikaMod.Log.LogInfo($"RENATO MAP POPUP: mapAreaID={mapAreaId}, price={mapAreaPrice}");
        }
        catch (Exception ex)
        {
            LaikaMod.Log.LogError($"ShowBuyingMapPopupPatch: exception while logging Renato map popup:\n{ex}");
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
                LaikaMod.Log.LogWarning("UnlockMapAreaPatch: __instance was null.");
                return;
            }

            string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";

            LaikaMod.Log.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

            // Avoid duplicate check logs during the same session.
            if (!LaikaMod.SentMapUnlockChecks.Contains(mapAreaId))
            {
                LaikaMod.SentMapUnlockChecks.Add(mapAreaId);

                // Prototype send-side check logging.
                // Later replace this with actual Archipelago check sending.
                LaikaMod.Log.LogInfo($"CHECK SENT: map_unlock:{mapAreaId}");
            }
            else
            {
                LaikaMod.Log.LogInfo($"MAP CHECK ALREADY SENT THIS SESSION: map_unlock:{mapAreaId}");
            }
        }
        catch (Exception ex)
        {
            LaikaMod.Log.LogError($"UnlockMapAreaPatch: exception while logging map unlock action:\n{ex}");
        }
    }
}

// High-level AP item categories.
public enum ItemKind
{
    Weapon,
    WeaponUpgrade,
    Currency,
    Ingredient,
    Collectible,
    PuppyTreat,
    Upgrade,
    MapUnlock,
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