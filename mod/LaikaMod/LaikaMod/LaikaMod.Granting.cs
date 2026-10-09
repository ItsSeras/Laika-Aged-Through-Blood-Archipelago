using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

public partial class LaikaMod
{
    // AP item grant handlers.
    // Each ItemKind has its own grant path so Laika-specific edge cases stay isolated.

    internal static bool TryGrantPendingItem(PendingItem item, string sourceTag)
    {
        if (item == null)
            return false;

        RememberReceivedAPItem(item);

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
                    return item.ReceivedItemIndex >= 0
                        ? TryGrantRecordedInventoryConsumable(item, sourceTag)
                        : TryGrantIngredient(item, sourceTag);

                case ItemKind.Material:
                    return item.ReceivedItemIndex >= 0
                        ? TryGrantRecordedInventoryConsumable(item, sourceTag)
                        : TryGrantMaterial(item, sourceTag);

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
            SaveData save = null;
            string marker = null;
            if (item.ReceivedItemIndex >= 0)
            {
                bool alreadyApplied;
                if (!PrepareAPConsumableGrant(item, out save, out marker, out alreadyApplied))
                    return false;
                if (alreadyApplied)
                    return true;
            }

            if ((long)before + amountToGrant > int.MaxValue)
            {
                LogWarning($"{sourceTag}: holding currency receipt to avoid integer overflow.");
                return false;
            }

            // AddMoney changes Money before invoking UI/event callbacks. A save
            // from one of those callbacks must capture the receipt and balance.
            if (marker != null)
                AddAPConsumableMarker(save, marker, amountToGrant);

            LogInfo($"{sourceTag}: currency before grant = {before}");

            try
            {
                economy.AddMoney(amountToGrant);
            }
            catch (Exception ex)
            {
                // Native AddMoney mutates Money before notifying listeners.
                // A listener failure must not make us repeat an applied receipt.
                LogWarning($"{sourceTag}: currency was applied but its update callback failed:\n{ex}");
            }

            int after = economy.Money;
            LogInfo($"{sourceTag}: currency after grant = {after}");

            return true;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
        }
    }

    internal static bool IsSaveTrackedConsumable(ItemKind kind)
    {
        return kind == ItemKind.Currency || kind == ItemKind.Ingredient || kind == ItemKind.Material;
    }

    private static bool PrepareAPConsumableGrant(
        PendingItem item, out SaveData save, out string marker, out bool alreadyApplied)
    {
        save = null;
        marker = null;
        alreadyApplied = false;
        if (!CanProcessAPItems() || item.ReceivedSessionIdentity != SessionState.SessionIdentityKey)
            return false;

        APConsumableJournal journal = EnsureAPConsumableJournal();
        if (journal == null)
            return false;

        if (item.ReceivedItemIndex < journal.LegacyReceiptFloor)
        {
            alreadyApplied = true;
            return true;
        }

        int amount = item.Kind == ItemKind.Currency ? ResolveCurrencyAmount(item) : item.Amount;
        APConsumableReceipt receipt = journal.Receipts.Find(
            entry => entry != null && entry.ReceivedItemIndex == item.ReceivedItemIndex);
        if (receipt == null || receipt.Kind != item.Kind || receipt.ApItemId != item.ApItemId ||
            receipt.ItemId != item.Id || receipt.Amount != amount)
        {
            LogWarning("AP RECEIPTS: consumable is not in the durable journal; leaving it pending.");
            return false;
        }

        save = MonoSingleton<ProgressionManager>.Instance.LocalSaveData;
        marker = APConsumableMarker(item.ReceivedSessionIdentity, item.ReceivedItemIndex);
        alreadyApplied = HasAPConsumableMarker(save, marker);
        return true;
    }

    private static PersistentValue AddAPConsumableMarker(SaveData save, string marker, int amount)
    {
        if (save.values == null)
            save.values = new List<PersistentValue>();
        var value = new PersistentValue { id = marker, value = amount.ToString(CultureInfo.InvariantCulture) };
        save.values.Add(value);
        return value;
    }

    internal static bool TryGrantRecordedInventoryConsumable(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<InventoryManager>.Instance;
        if (inventory == null)
            return false;

        SaveData save;
        string marker;
        bool alreadyApplied;
        if (!PrepareAPConsumableGrant(item, out save, out marker, out alreadyApplied))
            return false;
        if (alreadyApplied)
            return true;

        int before = inventory.GetItemAmount(item.Id);
        PersistentValue receiptMarker = AddAPConsumableMarker(save, marker, item.Amount);
        bool granted = item.Kind == ItemKind.Ingredient
            ? TryGrantIngredient(item, sourceTag)
            : TryGrantMaterial(item, sourceTag);

        // A vanilla popup callback can fail after AddItem changed the count.
        // Retain the receipt in that case so a retry cannot double the items.
        if (granted || inventory.GetItemAmount(item.Id) > before)
            return true;

        save.values.Remove(receiptMarker);
        return false;
    }

    internal static APConsumableJournal EnsureAPConsumableJournal()
    {
        if (SessionState == null || !SessionState.APEnabled ||
            SessionState.SaveSlotIndex != ActiveSaveSlotIndex ||
            string.IsNullOrWhiteSpace(SessionState.SessionIdentityKey))
        {
            return null;
        }

        APConsumableJournal previous = SessionState.ConsumableJournal;
        if (previous != null && previous.SessionIdentityKey == SessionState.SessionIdentityKey)
        {
            if (previous.Receipts == null)
                previous.Receipts = new List<APConsumableReceipt>();
            return previous;
        }

        var journal = new APConsumableJournal
        {
            SessionIdentityKey = SessionState.SessionIdentityKey,
            LegacyReceiptFloor = Math.Max(0, SessionState.LastProcessedReceivedItemIndex)
        };
        SessionState.ConsumableJournal = journal;
        if (!TrySaveSessionStateForSlot(ActiveSaveSlotIndex))
        {
            SessionState.ConsumableJournal = previous;
            return null;
        }

        LogInfo("AP RECEIPTS: initialized consumable journal. LegacyReceiptFloor=" +
            journal.LegacyReceiptFloor + ". Earlier receipts are not automatically refunded.");
        return journal;
    }

    internal static bool TryRememberAPConsumableReceipt(PendingItem item, int index)
    {
        APConsumableJournal journal = EnsureAPConsumableJournal();
        if (journal == null || item == null || !IsSaveTrackedConsumable(item.Kind) || index < 0)
            return false;

        item.SetReceivedItemIndex(index, journal.SessionIdentityKey);
        if (index < journal.LegacyReceiptFloor)
            return true;

        int amount = item.Kind == ItemKind.Currency ? ResolveCurrencyAmount(item) : item.Amount;
        if (amount <= 0)
            return false;

        APConsumableReceipt existing = journal.Receipts.Find(
            entry => entry != null && entry.ReceivedItemIndex == index);
        if (existing != null)
        {
            bool matches = existing.Kind == item.Kind && existing.ApItemId == item.ApItemId &&
                existing.ItemId == item.Id && existing.Amount == amount;
            if (!matches)
                LogWarning("AP RECEIPTS: conflicting receipt at index " + index + "; delivery paused.");
            return matches;
        }

        var receipt = new APConsumableReceipt
        {
            ReceivedItemIndex = index,
            Kind = item.Kind,
            ApItemId = item.ApItemId,
            ItemId = item.Id,
            DisplayName = item.DisplayName,
            Amount = amount
        };
        journal.Receipts.Add(receipt);
        if (!TrySaveSessionStateForSlot(ActiveSaveSlotIndex))
        {
            journal.Receipts.Remove(receipt);
            return false;
        }

        return true;
    }

    private static string APConsumableMarker(string identity, int index)
    {
        return "__LAIKA_AP_CONSUMABLE_V1__|" + identity + "|" +
            index.ToString(CultureInfo.InvariantCulture);
    }

    private static bool HasAPConsumableMarker(SaveData save, string marker)
    {
        return save != null && save.values != null &&
            save.values.Exists(value => value != null && value.id == marker);
    }

    internal static void EnqueueMissingAPConsumableReceipts()
    {
        if (!CanProcessAPItems())
            return;

        APConsumableJournal journal = SessionState.ConsumableJournal;
        if (journal == null || journal.SessionIdentityKey != SessionState.SessionIdentityKey ||
            journal.Receipts == null)
        {
            return;
        }

        SaveData save = MonoSingleton<ProgressionManager>.Instance.LocalSaveData;
        foreach (APConsumableReceipt receipt in journal.Receipts)
        {
            if (receipt == null || !IsSaveTrackedConsumable(receipt.Kind) ||
                receipt.ReceivedItemIndex < journal.LegacyReceiptFloor ||
                receipt.Amount <= 0 || string.IsNullOrEmpty(receipt.ItemId) ||
                HasAPConsumableMarker(save, APConsumableMarker(journal.SessionIdentityKey, receipt.ReceivedItemIndex)))
            {
                continue;
            }

            bool alreadyQueued = false;
            foreach (PendingItem pending in PendingItemQueue)
            {
                if (pending != null && pending.Kind == receipt.Kind &&
                    pending.ReceivedItemIndex == receipt.ReceivedItemIndex &&
                    pending.ReceivedSessionIdentity == journal.SessionIdentityKey)
                {
                    alreadyQueued = true;
                    break;
                }
            }
            if (alreadyQueued)
                continue;

            var item = new PendingItem(receipt.Kind, receipt.ItemId, receipt.Amount, receipt.DisplayName);
            item.SetApItemId(receipt.ApItemId);
            item.SetReceivedItemIndex(receipt.ReceivedItemIndex, journal.SessionIdentityKey);
            // No sender metadata: this is a restore, not a new received-item popup.
            EnqueueItem(item);
            LogInfo("AP RECEIPTS: restoring consumable absent from loaded save -> index=" +
                receipt.ReceivedItemIndex + ", item=" + receipt.ItemId + ", amount=" + receipt.Amount);
        }
    }

    internal static void ScheduleAPConsumableRecovery(SaveData save, int slotIndex)
    {
        if (SessionState == null || !SessionState.APEnabled || SessionState.ConsumableJournal == null)
            return;

        EnsureCoroutineRunner();
        if (CoroutineRunner != null)
            CoroutineRunner.StartCoroutine(RecoverAPConsumablesWhenReady(save, slotIndex));
    }

    private static IEnumerator RecoverAPConsumablesWhenReady(SaveData save, int slotIndex)
    {
        while (ReferenceEquals(save, APItemDeliverySaveData) && slotIndex == ActiveSaveSlotIndex)
        {
            if (SessionState == null || !SessionState.APEnabled)
                yield break;

            if (CanProcessAPItems() && !IsProcessingQueue)
            {
                // Works with the server offline because the receipt journal is local.
                ProcessPendingItemQueue("AP ConsumableRestore");
                yield break;
            }
            yield return null;
        }
    }

    internal static void SaveCompletedAPShopPurchase()
    {
        if (!CanProcessAPItems())
            return;

        var persistence = MonoSingleton<PersistenceManager>.Instance;
        if (persistence == null || !persistence.CanSave)
            return;

        try
        {
            // OnBuySucceded has now charged money, updated stock and granted or
            // suppressed the vanilla reward. Save the complete transaction so
            // a sent AP check cannot persist while its currency cost rolls back.
            persistence.SaveGame(false);
        }
        catch (Exception ex)
        {
            LogWarning("AP CURRENCY: save after completed shop purchase failed:\n" + ex);
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
            ScheduleShotgunQuestReconcile(sourceTag + "/ShotgunGrant");
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

            // Mark suppression BEFORE calling AddCassetteToInventory.
            // The cassette inventory patch fires during this call, so doing it after is too late.
            SuppressedCassetteChecks.Add(item.Id);
            LogInfo($"{sourceTag}: cassette {item.Id} marked for one-shot check suppression before grant.");

            bool addResult = cassettesManager.AddCassetteToInventory(item.Id, null, false);
            LogInfo($"{sourceTag}: AddCassetteToInventory({item.Id}) returned {addResult}");

            bool ownedAfter = cassettesManager.HasCassette(item.Id);
            LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for cassette {item.Id}");

            if (!ownedAfter)
            {
                SuppressedCassetteChecks.Remove(item.Id);
                LogWarning($"{sourceTag}: cassette {item.Id} was not owned after grant, removed suppression token.");
            }

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

    internal static bool ShouldBypassHeartglazeFlowerDeferral(string sourceTag)
    {
        bool flowerPickupChecked = HasSentAPLocationByInternalId(
            "I_PUPPY_FLOWER",
            sourceTag,
            "Key Item: Heartglaze Flower"
        );

        bool woodcrawlerChecked = HasSentAPLocationByInternalId(
            "B_BOSS_ROSCO_DEFEATED",
            sourceTag,
            "Boss Defeated: A Long Lost Woodcrawler"
        );

        return flowerPickupChecked && woodcrawlerChecked;
    }

    private static bool HasSentAPLocationByInternalId(string internalId, string sourceTag, string friendlyName)
    {
        try
        {
            if (SessionState == null || SessionState.SentLocationIds == null)
            {
                LogInfo($"{sourceTag}: Heartglaze bypass check failed because SessionState/SentLocationIds is not ready.");
                return false;
            }

            APLocationDefinition definition;
            if (!TryGetLocationDefinition(internalId, out definition))
            {
                LogWarning($"{sourceTag}: Heartglaze bypass check could not find AP location definition for {friendlyName} / {internalId}.");
                return false;
            }

            bool sent = SessionState.SentLocationIds.Contains(definition.LocationId);

            LogInfo(
                $"{sourceTag}: Heartglaze bypass prerequisite {friendlyName} -> sent={sent}, locationId={definition.LocationId}."
            );

            return sent;
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: Heartglaze bypass check failed for {friendlyName} / {internalId}:\n{ex}");
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

        if (IsHarpoonPieceId(item.Id))
        {
            return TryGrantHarpoonPiece(item, sourceTag);
        }

        if (item.Id == "I_PUPPY_FLOWER")
        {
            if (SessionState != null)
            {
                SessionState.HeartglazeFlowerReceivedFromAP = true;
                SaveSessionState();
            }

            if (!ShouldBypassHeartglazeFlowerDeferral(sourceTag))
            {
                AnnounceHeartglazeDeferredNoticeOnce(sourceTag);

                LogInfo(
                    $"{sourceTag}: Heartglaze Flower received from AP. Deferring actual vanilla inventory grant until physical flower pickup."
                );

                return true;
            }

            HeartglazeFlowerCleanupDone = true;
            WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

            LogInfo(
                $"{sourceTag}: Heartglaze Flower received from AP after the physical flower check and Woodcrawler boss were already completed. Bypassing defer and granting directly."
            );
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
                // Reuse the existing GetHook reconciliation and location
                // recovery when Hook arrives after the quest was accepted.
                // Later cave, bomb and statue steps retain their existing logic.
                TryReconcileTutorialHookGoal(
                    Singleton<Laika.Quests.QuestLog>.Instance,
                    $"{sourceTag}: hook grant follow-up"
                );

                LogImportantQuestSnapshots(
                    $"{sourceTag}: hook grant follow-up"
                );
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
    private static bool TryGrantMapUnlock(PendingItem item, string sourceTag)
    {
        try
        {
            if (item == null || string.IsNullOrEmpty(item.Id))
                return false;

            LogInfo($"{sourceTag}: granting AP map visual unlock {item.DisplayName}");

            RememberAPMapUnlock(item.Id);

            RefreshMapAreaVisuals(item.Id);

            try
            {
                MonoSingleton<PersistenceManager>.Instance.SaveGame();
                LogInfo($"{sourceTag}: forced save after AP map visual unlock {item.Id}.");
            }
            catch (Exception ex)
            {
                LogWarning($"{sourceTag}: save failed after AP map visual unlock {item.Id}:\n{ex}");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TryGrantMapUnlock failed for {item}:\n{ex}");
            return false;
        }
    }

    // Some abilities or items require the player to have certain flags
    // triggered in order to properly apply. These assist in ensuring
    // any of those crucial features function as intended.
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

        // Progression needs to be applied in order to leave the
        // boss zone for A Long Lost Woodcrawler.
        else if (item.Id == "I_PUPPY_FLOWER")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("I_PUPPY_FLOWER", true, false);
            LogInfo($"{sourceTag}: set progression flag I_PUPPY_FLOWER for Heartglaze Flower.");
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

        if (progressionManager == null || progressionManager.ProgressionData == null)
        {
            LogWarning($"{sourceTag}: could not unlock parry because ProgressionManager/ProgressionData is not ready.");
            return;
        }

        progressionManager.ProgressionData.SetAchievement("G_PARRY_UNLOCKED", true, false);
        ParryUnlockEnsuredThisSession = true;

        LogInfo($"{sourceTag}: set progression flag G_PARRY_UNLOCKED.");
    }
}
