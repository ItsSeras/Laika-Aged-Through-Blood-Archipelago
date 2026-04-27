using System;
using System.Collections.Generic;
using System.Reflection;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Quests;
using Laika.Quests.Goals;
using Laika.Persistence;
using BepInEx;

// Pending item queue, item granting, reconciliation, and DeathLink helpers.
public partial class LaikaMod
{
    // ===== Queue processing =====
    // Development-only queue entries go here when I want to force-test item grants.
    // These are commented examples for every ItemKind currently supported by the grant handler.
    // I can uncomment one or more lines as needed instead of trying to remember the right format.
    internal static void EnqueueDevelopmentStressTestItems()
    {
        // ===== Currency =====
        // Raw money/viscera grant.
        // The Id is mostly just a label here since Currency routes through TryGrantCurrency(...).
        // Amount is what really matters.
        // EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA", 50000, "Viscera"));


        // ===== Weapon =====
        // Direct weapon ownership grant.
        // Use this when I want the player to immediately own a full weapon.
        // Example weapon ids I have confirmed:
        // I_W_PISTOL, I_W_UZI, I_W_SHOTGUN, I_W_SNIPER, I_W_CROSSBOW, I_W_ROCKETLAUNCHER
        // EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_UZI", 1, "Machine Gun"));

        // ===== WeaponUpgrade =====
        // Adds weapon upgrade steps from the weapon's current level.
        // Example: a fresh weapon at displayed level 1 plus amount 3 should end at displayed level 4.
        // Use the base weapon id here, not a separate "upgrade item" id.
        // EnqueueItem(new PendingItem(ItemKind.WeaponUpgrade, "I_W_PISTOL", 3, "Pistol Upgrade 3"));

        // ===== Ingredient =====
        // Adds normal recipe/cooking ingredients through InventoryManager.
        // Use this for consumable crafting/cooking inputs that stack by amount.
        // Example confirmed ids:
        // I_C_BEANS, I_C_CORN, I_C_WORMS, I_C_ONION, I_C_CHILLY, I_C_GHOSTPEPPER, I_C_LEMON, I_C_GARLIC
        // I_C_MEAT, I_C_JACKFRUIT, I_C_SARDINE, I_C_COCO, I_C_COFFEE, I_C_WHISKEY, I_C_TOMATO
        // EnqueueItem(new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 5, "Coffee Can"));

        // ===== Material =====
        // Adds crafting materials/resources through InventoryManager.
        // Use this for stackable build/upgrade materials instead of unique progression items.
        // Example confirmed ids:
        // Common: I_BASALT, I_BONE, I_CALCIUM, I_METAL_BAD, I_SHALE, I_LEATHER_BAD, I_WOOD
        // Rare: I_METAL_GOOD, I_SCRAPS_UPCYCLE, I_SCRAPS_RUSTY, I_CABLE, I_LEATHER_GOOD
        // Unique: I_MATERIAL_SHOTGUN, I_MATERIAL_ROCKETLAUNCHER, I_MATERIAL_UZI, I_MATERIAL_SNIPER
        // EnqueueItem(new PendingItem(ItemKind.Material, "I_METAL_GOOD", 10, "Refined Metal"));

        // ===== Collectible =====
        // Grants a cassette/collectible directly through the cassette manager.
        // Use the cassette's internal id here.
        // Example confirmed ids:
        // I_CASSETTE_1 through I_CASSETTE_16, plus things like I_CASSETTE_D01, I_CASSETTE_D02, etc.
        // EnqueueItem(new PendingItem(ItemKind.Collectible, "I_CASSETTE_7", 1, "Cassette Tape: The Hero"));

        // ===== PuppyTreat =====
        // Grants one of Puppy's gifts through the Puppy gift path.
        // This path also uses suppression so AP-received Puppy gifts do not falsely send local checks.
        // Example confirmed ids:
        // I_TOY_BIKE, I_GAMEBOY, I_PLANT_PUPPY, I_TOY_ANIMAL, I_BOOK_MOTHER, I_DREAMCATCHER, I_UKULELE
        // EnqueueItem(new PendingItem(ItemKind.PuppyTreat, "I_DREAMCATCHER", 1, "Dreamcatcher"));

        // ===== KeyItem =====
        // Grants a unique progression/key item through InventoryManager.
        // Some of these also need progression flags after grant, which ApplyKeyItemProgressionFlags(...) handles.
        // Example confirmed ids:
        // I_DASH, I_E_HOOK, I_MAYA_PENDANT
        // EnqueueItem(new PendingItem(ItemKind.KeyItem, "I_DASH", 1, "Dash"));

        // ===== MapUnlock =====
        // Unlocks a map piece/area directly through ProgressionData.
        // Use the internal map area id here.
        // Example confirmed ids:
        // M_A_W06, M_A_W07_TOP, M_A_W07_BOTTOM, etc.
        // EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map Piece: Where Our Bikes Growl"));

        EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA_100", 0, "Viscera x100"));
        EnqueueItem(new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 1, "Coffee Beans"));
        EnqueueItem(new PendingItem(ItemKind.Collectible, "I_CASSETTE_1", 1, "Cassette: Bloody Sunset"));
        EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map: Where Our Bikes Growl"));
    }

    internal static bool IsGrantingAPItem = false;

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Main queue processor.
    // This runs when the game is in a safe enough state to grant AP items and also acts as a fallback
    // recovery point for quest softlocks on older saves.
    internal static void ProcessPendingItemQueue(string sourceTag)
    {
        if (IsProcessingQueue)
        {
            LogInfo($"{sourceTag}: queue processing already in progress, skipping nested call.");
            return;
        }

        // Some AP softlocks are not item grant failures.
        // They happen because the player already owns the required item before the vanilla quest reaches
        // the step that expects it. I only fix the blocked step here so the rest of the quest can continue normally.
        TryReconcileKnownQuestSoftlocks(sourceTag);

        if (PendingItemQueue.Count == 0)
        {
            Log.LogInfo($"{sourceTag}: no pending items to process.");
            return;
        }

        // Make sure parry/reflect is unlocked once the game managers are alive.
        // Awake() is too early because ProgressionManager is still null there.
        TryEnsureParryUnlockedOnce(sourceTag);

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

                    if (granted)
                    {
                        LaikaMod.LogInfo($"{sourceTag}: grant succeeded -> {item}");
                        LaikaMod.AnnounceAPSuccess($"[AP] Granted: {item.DisplayName}");
                    }
                    else
                    {
                        if (ShouldKeepPendingAfterFailedGrant(item, sourceTag))
                        {
                            remainingQueue.Enqueue(item);
                            LaikaMod.LogInfo($"{sourceTag}: deferred grant kept pending -> {item}");
                            LaikaMod.AnnounceAPActivity($"[AP] Holding upgrade until weapon is owned: {item.DisplayName}");
                        }
                        else
                        {
                            LaikaMod.LogWarning($"{sourceTag}: grant failed -> {item}");
                            LaikaMod.AnnounceAPWarning($"[AP] Failed to grant: {item.DisplayName}");
                        }
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
    // This is the main router for received items.
    // Each ItemKind goes through its own grant path so I can keep the weird edge cases isolated.
    internal static bool TryGrantPendingItem(PendingItem item, string sourceTag)
    {
        if (item == null)
            return false;

        IsGrantingAPItem = true;

        try
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
        finally
        {
            IsGrantingAPItem = false;
        }
    }

    internal static bool ShouldKeepPendingAfterFailedGrant(PendingItem item, string sourceTag)
    {
        if (item == null)
            return false;

        if (item.Kind != ItemKind.WeaponUpgrade)
            return false;

        var weaponsInventory = Singleton<WeaponsInventory>.Instance;
        if (weaponsInventory == null)
            return true;

        var itemLoader = Singleton<ItemDataLoader>.Instance;
        if (itemLoader == null)
            return true;

        ItemDataWeapon weaponData = itemLoader.FindWeapon(item.Id);
        if (weaponData == null)
        {
            LogWarning($"{sourceTag}: not deferring weapon upgrade because FindWeapon({item.Id}) returned null.");
            return false;
        }

        return !weaponsInventory.HasWeapon(item.Id);
    }

    // Grants Viscera through EconomyManager.
    // If the AP item has a positive Amount, use that directly.
    // Otherwise, infer a fixed amount from the item's internal id.
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var economy = Singleton<EconomyManager>.Instance;

        if (economy == null)
        {
            LogWarning($"{sourceTag}: currency grant failed, EconomyManager is null.");
            return false;
        }

        try
        {
            int amountToGrant = ResolveCurrencyAmount(item);

            if (amountToGrant <= 0)
            {
                LogWarning($"{sourceTag}: currency grant failed, resolved amount was {amountToGrant} for item {item.Id}");
                return false;
            }

            int before = economy.Money;
            LogInfo($"{sourceTag}: currency before grant = {before}");

            economy.AddMoney(amountToGrant);

            int after = economy.Money;
            LogInfo($"{sourceTag}: currency after grant = {after}");

            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
        }
    }

    // Resolves how much money a currency AP item should award.
    // I let PendingItem.Amount win if it is already populated,
    // otherwise I map known AP currency ids here.
    internal static int ResolveCurrencyAmount(PendingItem item)
    {
        if (item == null)
            return 0;

        if (item.Amount > 0)
            return item.Amount;

        switch (item.Id)
        {
            case "VISCERA_10":
                return 10;

            case "VISCERA_25":
                return 25;

            case "VISCERA_50":
                return 50;

            case "VISCERA_100":
                return 100;

            default:
                // Keep old generic VISCERA as a fallback while transitioning.
                if (item.Id == "VISCERA")
                    return 100;

                return 0;
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

        if (ownedAfter && item.Id == "I_W_SHOTGUN")
        {
            LogImportantQuestSnapshots($"{sourceTag}: shotgun grant follow-up");
        }

        return ownedAfter;
    }

    // Grants weapon upgrades as additional upgrade steps from the weapon's current level.
    // Internal weapon levels are zero-based in Laika:
    // internal 0 = displayed level 1
    // internal 1 = displayed level 2
    // internal 2 = displayed level 3
    // internal 3 = displayed level 4
    //
    // That means if a fresh weapon is newly granted and starts at internal level 0,
    // an AP upgrade amount of 3 should move it to internal level 3, which is displayed level 4.
    internal static bool TryGrantWeaponUpgrade(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var weaponsInventory = Singleton<WeaponsInventory>.Instance;
        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, WeaponsInventory is null.");
            return false;
        }

        var itemLoader = Singleton<ItemDataLoader>.Instance;
        if (itemLoader == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, ItemDataLoader is null.");
            return false;
        }

        try
        {
            bool alreadyOwned = weaponsInventory.HasWeapon(item.Id);
            LogInfo($"{sourceTag}: weapon upgrade target {item.Id}, alreadyOwned={alreadyOwned}");

            if (!alreadyOwned)
            {
                LogWarning($"{sourceTag}: cannot upgrade weapon {item.Id} because player does not own it yet.");
                return false;
            }

            ItemDataWeapon weaponData = itemLoader.FindWeapon(item.Id);
            if (weaponData == null)
            {
                LogWarning($"{sourceTag}: weapon upgrade grant failed, FindWeapon({item.Id}) returned null.");
                return false;
            }

            WeaponInstance weaponInstance = weaponsInventory.GetWeaponInstance(weaponData);
            if (weaponInstance == null)
            {
                LogWarning($"{sourceTag}: weapon upgrade grant failed, GetWeaponInstance({item.Id}) returned null.");
                return false;
            }

            // Laika weapon levels are zero-based internally.
            int currentInternalLevel = weaponInstance.Level;

            // item.Amount means "how many upgrade steps to add from where the weapon is now".
            int targetInternalLevel = currentInternalLevel + item.Amount;

            // Laika weapons cap at displayed level 4, which is internal level 3.
            // Clamp here so I never push a weapon past the game's valid level data.
            int maxInternalLevel = 3;
            if (targetInternalLevel > maxInternalLevel)
            {
                targetInternalLevel = maxInternalLevel;
            }

            LogInfo(
                $"{sourceTag}: weapon {item.Id} currentInternalLevel={currentInternalLevel}, " +
                $"targetInternalLevel={targetInternalLevel}, " +
                $"currentDisplayedLevel={currentInternalLevel + 1}, " +
                $"targetDisplayedLevel={targetInternalLevel + 1}"
            );

            if (currentInternalLevel >= targetInternalLevel)
            {
                LogInfo($"{sourceTag}: weapon {item.Id} is already at or above target level.");
                return true;
            }

            while (currentInternalLevel < targetInternalLevel)
            {
                bool upgradeResult = weaponsInventory.UpgradeWeapon(item.Id);
                LogInfo($"{sourceTag}: UpgradeWeapon({item.Id}) returned {upgradeResult}");

                weaponInstance = weaponsInventory.GetWeaponInstance(weaponData);
                if (weaponInstance == null)
                {
                    LogWarning($"{sourceTag}: weapon upgrade grant failed, weapon instance disappeared after upgrade.");
                    return false;
                }

                int newInternalLevel = weaponInstance.Level;
                LogInfo(
                    $"{sourceTag}: weapon {item.Id} level after upgrade attempt = " +
                    $"internal {newInternalLevel} / displayed {newInternalLevel + 1}"
                );

                if (newInternalLevel <= currentInternalLevel)
                {
                    LogWarning(
                        $"{sourceTag}: weapon {item.Id} stopped upgrading before target level. " +
                        $"CurrentInternal={newInternalLevel}, TargetInternal={targetInternalLevel}"
                    );
                    break;
                }

                currentInternalLevel = newInternalLevel;
            }

            bool reachedTarget = currentInternalLevel >= targetInternalLevel;
            LogInfo(
                $"{sourceTag}: weapon {item.Id} reachedTarget={reachedTarget}, " +
                $"finalInternalLevel={currentInternalLevel}, " +
                $"finalDisplayedLevel={currentInternalLevel + 1}"
            );

            return reachedTarget;
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

        if (item.Id == "I_COLLECTION_JAKOB")
        {
            LogWarning($"{sourceTag}: collectible {item.Id} is a bundle/progression reward, not a directly grantable cassette.");
            return true;
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

            if (ownedAfter)
            {
                SuppressedCassetteChecks.Add(item.Id);
                LogInfo($"{sourceTag}: cassette {item.Id} marked for one-shot check suppression.");
            }

            // Success if the player now owns it.
            return ownedAfter;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting cassette {item.Id}:\n{ex}");
            return false;
        }
    }

    // Puppy gifts are a little weird compared to normal inventory items.
    // I resolve the ItemData first and try the ItemData overload, then fall back to the string overload.
    // Suppression gets added first so AP-received gifts do not come back through the real source patch
    // and count as locally found checks.
    internal static bool TryGrantPuppyTreat(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting puppy treat {item.DisplayName}");

        // Grab the game's runtime inventory manager.
        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, InventoryManager is null.");
            return false;
        }

        // Grab the game's runtime item database so we can resolve the real ItemData object.
        var itemLoader = Singleton<ItemDataLoader>.Instance;

        if (itemLoader == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, ItemDataLoader is null.");
            return false;
        }

        // Normalize once so suppression and location handling always use the same ID format.
        string normalizedGiftId = NormalizePuppyGiftId(item.Id);

        // If the player already owns the Puppy gift, treat that as success so it does not stay stuck in the queue.
        bool alreadyOwned = inventory.HasItem(item.Id);
        LogInfo($"{sourceTag}: puppy treat {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
        {
            LogInfo($"{sourceTag}: skipping puppy gift {item.Id} because player already owns it.");
            return true;
        }

        // Add suppression BEFORE any grant attempt so our Puppy source patch sees it in time.
        SuppressedPuppyGiftChecks.Add(normalizedGiftId);
        LogInfo($"{sourceTag}: puppy gift {normalizedGiftId} pre-marked for one-shot check suppression.");

        bool grantSucceeded = false;

        try
        {
            // Resolve the game's real ItemData for this Puppy gift.
            ItemData itemData = itemLoader.Find(item.Id);

            if (itemData == null)
            {
                LogWarning($"{sourceTag}: puppy treat grant failed, ItemDataLoader.Find({item.Id}) returned null.");
            }
            else
            {
                // First try the generic AddItem(...) path using the resolved ItemData.
                // Even though we cannot directly call AddKeyItem(...) from here,
                // InventoryManager.AddItem(...) may still internally route certain Puppy gifts
                // through the game's own key-item logic.
                bool addItemFromDataResult = inventory.AddItem(itemData, item.Amount, null, false);
                LogInfo($"{sourceTag}: AddItem(ItemData:{item.Id}, {item.Amount}) returned {addItemFromDataResult}");

                grantSucceeded = addItemFromDataResult;
            }

            // If AddItem(ItemData, ...) did not work, fall back to the string-id version.
            // Some items in Laika appear to behave differently depending on which overload is used.
            if (!grantSucceeded)
            {
                bool addItemResult = inventory.AddItem(item.Id, item.Amount, null, false);
                LogInfo($"{sourceTag}: fallback AddItem(string:{item.Id}, {item.Amount}) returned {addItemResult}");

                grantSucceeded = addItemResult;
            }

            // If the game still says it failed, remove suppression so a future retry is clean.
            if (!grantSucceeded)
            {
                SuppressedPuppyGiftChecks.Remove(normalizedGiftId);
                LogWarning($"{sourceTag}: puppy gift {normalizedGiftId} grant failed, suppression removed.");
                return false;
            }

            // Final ownership check for logging only.
            bool ownedAfter = inventory.HasItem(item.Id);
            LogInfo($"{sourceTag}: puppy treat {item.Id}, ownedAfter={ownedAfter}");

            return true;
        }
        catch (Exception ex)
        {
            // If anything throws, remove suppression so the queue can safely retry later.
            SuppressedPuppyGiftChecks.Remove(normalizedGiftId);
            LogError($"{sourceTag}: exception while granting puppy treat {item.Id}:\n{ex}");
            return false;
        }
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

            if (addResult)
            {
                ApplyKeyItemProgressionFlags(item, sourceTag);
            }

            // Key items/upgrades may not show up in the normal HasItem() check.
            // If AddItem() returned true, trust the game's internal key item handling.
            LogInfo($"{sourceTag}: assuming success from AddItem result for key item {item.Id}");

            if (addResult && item.Id == "I_E_HOOK")
            {
                LogImportantQuestSnapshots($"{sourceTag}: hook grant follow-up");
            }

            return addResult;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting key item {item.Id}:\n{ex}");
            return false;
        }
    }

    // Map pieces are just progression unlocks under the hood,
    // so I can grant them straight through ProgressionData using the internal map ID.
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
    // Some key items are not enough by themselves.
    // Vanilla also flips progression flags when the player gets them, so I mirror that here
    // when AP gives the item early.
    internal static void ApplyKeyItemProgressionFlags(PendingItem item, string sourceTag)
    {
        // Dash needs the G_DASH_UNLOCKED progression flag in addition to the item itself.
        if (item.Id == "I_DASH")
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

    // Vanilla gives parry through the tutorial, so the player can never miss it there.
    // In AP that is not guaranteed, so I force the progression flag once the game managers are actually alive.
    // This only needs to happen once per launch.
    internal static void TryEnsureParryUnlockedOnce(string sourceTag)
    {
        if (ParryUnlockEnsuredThisSession)
            return;

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;

        if (progressionManager == null)
        {
            LogWarning($"{sourceTag}: could not unlock parry because ProgressionManager is null.");
            return;
        }

        // This is the actual achievement checked by ParryShield.Update().
        progressionManager.ProgressionData.SetAchievement("G_PARRY_UNLOCKED", true, false);
        ParryUnlockEnsuredThisSession = true;

        LogInfo($"{sourceTag}: set progression flag G_PARRY_UNLOCKED.");
    }

    // Temporary quest-debug helpers.
    // These are useful when I need to inspect active goal ids and progression state for a broken quest.
    // They should stay out of normal runtime flow unless I am actively diagnosing a quest issue.
    internal static void LogQuestGoals(string questId, string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because QuestLog is null.");
                return;
            }

            List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

            if (activeQuests == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because active quest list was null.");
                return;
            }

            QuestInstance quest = activeQuests.Find(x => x != null && x.QuestId == questId);

            if (quest == null)
            {
                LogInfo($"{sourceTag}: quest {questId} is not active.");
                return;
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT START -> {questId}");

            foreach (QuestGoal goal in quest.goals)
            {
                if (goal == null)
                    continue;

                LogInfo(
                    $"{sourceTag}: " +
                    $"quest={questId}, " +
                    $"goalId={goal.GoalId}, " +
                    $"goalType={goal.GetType().Name}, " +
                    $"completed={goal.Completed}, " +
                    $"current={goal.CurrentAmount}, " +
                    $"required={goal.RequiredAmount}, " +
                    $"description={goal.Description}"
                );
            }

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal != null)
            {
                LogInfo(
                    $"{sourceTag}: CURRENT GOAL -> " +
                    $"quest={questId}, " +
                    $"goalId={currentGoal.GoalId}, " +
                    $"goalType={currentGoal.GetType().Name}, " +
                    $"description={currentGoal.Description}"
                );
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT END -> {questId}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while logging quest goals for {questId}:\n{ex}");
        }
    }

    internal static void LogImportantQuestSnapshots(string sourceTag)
    {
        LogQuestGoals("Q_D_S_OldWarfare", sourceTag);
        LogQuestGoals("Q_D_S_TutorialHook", sourceTag);
        LogQuestGoals("Q_D_2_Lighthouse", sourceTag);
    }

    // Centralized check-send path for all AP-style locations.
    // This is the one place that should handle duplicate suppression and persistent sent-state.
    internal static void TrySendLocationCheck(APLocationDefinition definition, string sourceTag)
    {
        if (definition == null)
        {
            LogWarning($"{sourceTag}: TrySendLocationCheck called with null definition.");
            return;
        }

        if (HasLocationBeenSent(definition.LocationId))
        {
            LogInfo($"{sourceTag}: check already sent -> {definition.DisplayName} ({definition.LocationId})");
            return;
        }

        if (ArchipelagoClientManager.Instance != null)
        {
            ArchipelagoClientManager.Instance.SendLocationCheck(definition);
            TryConsumeVanillaLocationReward(definition, sourceTag);
        }
        else
        {
            LogWarning($"{sourceTag}: ArchipelagoClientManager missing, check not sent -> {definition.DisplayName}");
        }
    }

    // Attempts to remove vanilla item rewwards from the player.
    internal static void TryConsumeVanillaLocationReward(APLocationDefinition definition, string sourceTag)
    {
        if (definition == null)
            return;

        if (definition.Category == "Cassette")
        {
            TryRemoveCassetteReward(definition.InternalId, sourceTag);
            return;
        }

        if (definition.Category == "PuppyGift" ||
            definition.Category == "KeyItem" ||
            definition.Category == "Material" ||
            definition.Category == "Ingredient")
        {
            TryRemoveInventoryReward(definition.InternalId, 1, sourceTag);
            return;
        }

        // Do not consume quest-completion, boss, or map-unlock rewards here.
        // Those are progression/check states, not the randomized item reward itself.
    }

    internal static void TryRemoveInventoryReward(string itemId, int amount, string sourceTag)
    {
        if (string.IsNullOrEmpty(itemId))
            return;

        var inventory = Singleton<InventoryManager>.Instance;
        if (inventory == null)
        {
            LogWarning($"{sourceTag}: could not remove vanilla reward {itemId}; InventoryManager is null.");
            return;
        }

        try
        {
            bool removed = inventory.RemoveItem(itemId, amount, true);
            LogInfo($"{sourceTag}: remove vanilla inventory reward {itemId} x{amount} -> {removed}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception removing vanilla inventory reward {itemId}:\n{ex}");
        }
    }

    internal static void TryRemoveCassetteReward(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return;

        var manager = Singleton<CassettesManager>.Instance;
        var loader = Singleton<CassettesDataLoader>.Instance;

        if (manager == null || loader == null)
        {
            LogWarning($"{sourceTag}: could not remove vanilla cassette {cassetteId}; cassette manager/loader is null.");
            return;
        }

        try
        {
            CassetteData cassette = loader.FindCassette(cassetteId);
            if (cassette == null)
            {
                LogWarning($"{sourceTag}: could not remove vanilla cassette {cassetteId}; FindCassette returned null.");
                return;
            }

            bool removed = manager.CassettesInventory != null && manager.CassettesInventory.Remove(cassette);
            LogInfo($"{sourceTag}: remove vanilla cassette reward {cassetteId} -> {removed}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception removing vanilla cassette reward {cassetteId}:\n{ex}");
        }
    }

    // Centralized cassette check handler.
    // This consumes one-shot suppression for AP-granted cassette items so that receiving
    // a cassette does not accidentally count as finding its real in-world location.
    internal static void TryHandleCassetteLocationCheck(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
        {
            LogWarning($"{sourceTag}: cassetteId was null or empty.");
            return;
        }

        if (SuppressedCassetteChecks.Remove(cassetteId))
        {
            LogInfo($"{sourceTag}: suppressed cassette check for AP-granted cassette {cassetteId}.");
            return;
        }

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(cassetteId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for cassetteId={cassetteId}");
            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag);
    }

    internal static string NormalizePuppyGiftId(string giftId)
    {
        if (string.IsNullOrEmpty(giftId))
            return giftId;

        string normalized = giftId.Trim();

        // Keep this here in case the game ever gives us something like I_TOY_BIKEgift.
        if (normalized.EndsWith("gift", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 4);
        }

        return normalized;
    }

    internal static bool ConsumeSuppressedPuppyGiftCheck(string giftId, string sourceTag)
    {
        if (string.IsNullOrEmpty(giftId))
            return false;

        if (SuppressedPuppyGiftChecks.Remove(giftId))
        {
            LogInfo($"{sourceTag}: suppressed puppy gift check for AP-granted item {giftId}.");
            return true;
        }

        return false;
    }

    // Handles a real Puppy gift source and turns it into an AP location check.
    // If the gift came from AP instead of being found naturally, suppression eats it here
    // so the player does not get a fake local check for an item they were only sent.
    internal static void TryHandlePuppyGiftLocationCheck(string giftId, string sourceTag)
    {
        if (string.IsNullOrEmpty(giftId))
        {
            LogWarning($"{sourceTag}: puppy gift id was null or empty.");
            return;
        }

        string normalizedGiftId = NormalizePuppyGiftId(giftId);

        LogInfo($"{sourceTag}: puppy gift rawId={giftId}, normalizedId={normalizedGiftId}");

        if (ConsumeSuppressedPuppyGiftCheck(normalizedGiftId, sourceTag))
            return;

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(normalizedGiftId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for puppy gift id={normalizedGiftId}");
            return;
        }

        if (locationDefinition.Category != "PuppyGift")
        {
            LogWarning(
                $"{sourceTag}: resolved id {normalizedGiftId} but category was {locationDefinition.Category}, not PuppyGift."
            );
            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag);
    }

    // Looks for one active quest by id.
    // I only care about active quests here because these softlocks happen mid-quest.
    internal static QuestInstance FindActiveQuest(string questId)
    {
        QuestLog questLog = Singleton<QuestLog>.Instance;

        if (questLog == null)
            return null;

        List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

        if (activeQuests == null)
            return null;

        return activeQuests.Find(x => x != null && x.QuestId == questId);
    }

    // Some AP softlocks are not item grant failures.
    // They happen because the player already owns the required item before the vanilla quest step becomes current.
    // I reconcile those here by only completing the one blocked goal, not the whole quest.
    internal static void TryReconcileKnownQuestSoftlocks(string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not reconcile quest softlocks because QuestLog is null.");
                return;
            }

            TryReconcileOldWarfareShotgunGoal(questLog, sourceTag);
            TryReconcileTutorialHookGoal(questLog, sourceTag);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while reconciling known quest softlocks:\n{ex}");
        }
    }

    // Old Warfare can get stuck if AP gives the player the shotgun early.
    // CraftShotgun is a GatherQuestGoal, so the normal TryCompleteQuestGoal / ForceComplete path
    // does not advance it correctly here. Instead, I re-run the goal's own CheckGoal() logic
    // once the player already owns the shotgun so the quest can move on to ShowShotgun normally.
    internal static void TryReconcileOldWarfareShotgunGoal(QuestLog questLog, string sourceTag)
    {
        QuestInstance quest = FindActiveQuest("Q_D_S_OldWarfare");

        if (quest == null)
            return;

        QuestGoal currentGoal = quest.GetCurrentGoal();

        if (currentGoal == null)
            return;

        if (currentGoal.GoalId != "CraftShotgun")
            return;

        WeaponsInventory weaponsInventory = Singleton<WeaponsInventory>.Instance;

        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: could not reconcile Old Warfare because WeaponsInventory is null.");
            return;
        }

        if (!weaponsInventory.HasWeapon("I_W_SHOTGUN"))
            return;

        LogInfo($"{sourceTag}: reconciling Old Warfare softlock by re-checking CraftShotgun because the player already owns the shotgun.");
        AnnounceAPActivity("[AP] Old Warfare updated because you already had the shotgun.");

        try
        {
            // GatherQuestGoal does not implement ForceComplete(), so I need to call its own
            // internal CheckGoal() path directly. That lets the quest use its normal gather-goal
            // completion logic instead of me faking a quest close or skipping ahead too far.
            MethodInfo checkGoalMethod = currentGoal.GetType().GetMethod(
                "CheckGoal",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (checkGoalMethod == null)
            {
                LogWarning($"{sourceTag}: could not find CheckGoal() on CraftShotgun goal.");
                return;
            }

            checkGoalMethod.Invoke(currentGoal, null);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while re-checking CraftShotgun:\n{ex}");
        }
    }

    // Bonehead's Hook can get stuck if AP gives the player the hook before the quest reaches GetHook.
    // In that case, the quest still waits on the first scripted goal even though the player already has the hook.
    // I only complete GetHook so the rest of the quest can still play out normally.
    internal static void TryReconcileTutorialHookGoal(QuestLog questLog, string sourceTag)
    {
        QuestInstance quest = FindActiveQuest("Q_D_S_TutorialHook");

        if (quest == null)
            return;

        QuestGoal currentGoal = quest.GetCurrentGoal();

        if (currentGoal == null)
            return;

        if (currentGoal.GoalId != "GetHook")
            return;

        InventoryManager inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: could not reconcile TutorialHook because InventoryManager is null.");
            return;
        }

        if (!inventory.HasItem("I_E_HOOK"))
            return;

        LogInfo($"{sourceTag}: reconciling Bonehead's Hook softlock by completing GetHook because the player already owns the hook.");
        AnnounceAPActivity("[AP] Bonehead's Hook updated because you already had the hook.");

        questLog.TryCompleteQuestGoal("Q_D_S_TutorialHook", "GetHook");
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

    //Forces the player to start with a pistol. The player doesn't require
    //it. However, if the player interacts with Jakob too early via a
    //different weapon, then there is a chance the player will never
    //acquire the pistol at all. This enforces it.
    internal static void EnqueueRequiredStartingItems()
    {
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_PISTOL", 1, "Pistol"));
    }

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

                AnnounceAPDeathLink("[AP] Local death detected.");

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
        bool effectiveDeathLinkEnabled =
            WorldOptions.DeathLinkEnabled ||
            (SessionState != null &&
             SessionState.Options != null &&
             SessionState.Options.DeathLinkEnabled);

        if (!effectiveDeathLinkEnabled)
        {
            AnnounceAPDeathLink("[AP] Local death detected. DeathLink is disabled.");
            LogInfo($"{sourceTag}: DeathLink disabled. No outbound DeathLink would be sent.");
            return;
        }

        bool effectiveDeathAmnestyEnabled =
            WorldOptions.DeathAmnestyEnabled ||
            (SessionState != null &&
             SessionState.Options != null &&
             SessionState.Options.DeathAmnestyEnabled);

        if (!effectiveDeathAmnestyEnabled)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty disabled).");

            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            return;
        }

        int effectiveDeathAmnestyCount =
            SessionState != null && SessionState.Options != null
                ? SessionState.Options.DeathAmnestyCount
                : WorldOptions.DeathAmnestyCount;

        int requiredDeaths = Math.Max(1, effectiveDeathAmnestyCount);

        LogInfo($"{sourceTag}: Death Amnesty Progress = {DeathsSinceLastDeathLink} / {requiredDeaths}");

        AnnounceAPActivity(
            $"[AP] Your suffering inches closer to your friends... ({DeathsSinceLastDeathLink}/{requiredDeaths})"
        );

        if (DeathsSinceLastDeathLink >= requiredDeaths)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty threshold reached).");

            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after real send.");
        }
    }
}