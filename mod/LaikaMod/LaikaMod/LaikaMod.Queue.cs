using BepInEx;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using Laika.Quests.Goals;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
    }

    internal static bool IsGrantingAPItem = false;

    private static readonly HashSet<string> DeferredUpgradeNoticesShown = new HashSet<string>();

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        if (item == null)
            return;

        if (item.Kind == ItemKind.WeaponUpgrade)
        {
            foreach (PendingItem queuedItem in PendingItemQueue)
            {
                if (queuedItem != null &&
                    queuedItem.Kind == ItemKind.WeaponUpgrade &&
                    queuedItem.Id == item.Id)
                {
                    LogInfo($"QUEUE: weapon upgrade already pending, not stacking duplicate reconcile entry -> {queuedItem}");
                    return;
                }
            }
        }

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

                        bool isReconcileRestore =
                            sourceTag != null &&
                            (
                                sourceTag.Contains("APSceneReconcile") ||
                                sourceTag.Contains("AP Reconcile") ||
                                sourceTag.Contains("WeaponsOverlayInitializeQueueProcess")
                            );

                        if (isReconcileRestore)
                        {
                            LaikaMod.LogInfo($"{sourceTag}: restored AP item silently without duplicate overlay popup -> {item.DisplayName}");
                        }
                        else
                        {
                            LaikaMod.AnnounceAPActivity(
                                LaikaMod.BuildGrantedOverlayLine(item.DisplayName, item.ApItemId)
                            );
                        }
                    }
                    else
                    {
                        if (ShouldKeepPendingAfterFailedGrant(item, sourceTag))
                        {
                            remainingQueue.Enqueue(item);
                            LaikaMod.LogInfo($"{sourceTag}: deferred grant kept pending -> {item}");
                            string noticeKey = item.Id + "|" + item.DisplayName;

                            if (DeferredUpgradeNoticesShown.Add(noticeKey))
                            {
                                LaikaMod.AnnounceAPWarning($"[AP] Holding upgrade until weapon is owned: {item.DisplayName}");
                            }
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

    internal static bool ShouldKeepPendingAfterFailedGrant(PendingItem item, string sourceTag)
    {
        if (item == null)
            return false;

        if (item.Kind == ItemKind.Currency)
        {
            return Singleton<EconomyManager>.Instance == null;
        }

        if (item.Kind == ItemKind.Ingredient || item.Kind == ItemKind.Material)
        {
            return Singleton<InventoryManager>.Instance == null;
        }

        if (item.Kind == ItemKind.MapUnlock)
        {
            var progressionManager = MonoSingleton<ProgressionManager>.Instance;
            return progressionManager == null || progressionManager.ProgressionData == null;
        }

        if (item.Kind == ItemKind.KeyItem || item.Kind == ItemKind.PuppyTreat)
        {
            return Singleton<InventoryManager>.Instance == null;
        }

        if (item.Kind == ItemKind.Collectible)
        {
            return Singleton<CassettesManager>.Instance == null;
        }

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
}