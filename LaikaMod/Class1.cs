using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Inventory;
using Laika.Quests;
using Laika.UI.InGame.Inventory;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;

[BepInPlugin("com.seras.laikamod", "Laika AP Prototype", "1.0.0")]
public class LaikaMod : BaseUnityPlugin
{
    // Shared logger for Harmony patches.
    internal static ManualLogSource Log;

    // Queue of pending Archipelago-style received items.
    internal static Queue<PendingItem> PendingItemQueue = new Queue<PendingItem>();

    // Prevent nested queue processing when UI refreshes trigger more hooks.
    internal static bool IsProcessingQueue = false;

    private void Awake()
    {
        // Save logger for static patches.
        Log = Logger;

        // Confirm plugin loaded.
        Log.LogInfo("LaikaMod Awake() called. GENERALIZED QUEUE BUILD 24");

        // Sample test items in the queue.
        // Later these would come from Archipelago packets.
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_SNIPER", 1));
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_CROSSBOW", 1));
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_UZI", 1));

        // Example future items:
        EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA", 250));
        EnqueueItem(new PendingItem(ItemKind.Ingredient, "TEST_INGREDIENT", 3));
        EnqueueItem(new PendingItem(ItemKind.Collectible, "TEST_MIXTAPE", 1));

        // Apply all Harmony patches in this file.
        Harmony harmony = new Harmony("com.seras.laikaapprototype");
        harmony.PatchAll();

        Log.LogInfo("Harmony patches applied.");
    }

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        Log.LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Logs the visible runtime weapon inventory.
    internal static void LogWeaponSnapshot(string label)
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
        sb.Append($"VISIBLE WEAPON SNAPSHOT {label}: count={inventory.Weapons.Count}");

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
            LogWeaponSnapshot($"{sourceTag} BEFORE");

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

            LogWeaponSnapshot($"{sourceTag} AFTER");
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

    // Placeholder currency handler.
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        // TODO: Replace with actual Viscera / money manager call once found.
        Log.LogInfo($"{sourceTag}: TODO currency grant -> id={item.Id}, amount={item.Amount}");
        return false;
    }

    // Placeholder ingredient handler.
    internal static bool TryGrantIngredient(PendingItem item, string sourceTag)
    {
        // TODO: Replace with actual inventory ingredient add call once found.
        Log.LogInfo($"{sourceTag}: TODO ingredient grant -> id={item.Id}, amount={item.Amount}");
        return false;
    }

    // Placeholder collectible handler.
    internal static bool TryGrantCollectible(PendingItem item, string sourceTag)
    {
        // TODO: Replace with actual collectible unlock call once found.
        Log.LogInfo($"{sourceTag}: TODO collectible grant -> id={item.Id}, amount={item.Amount}");
        return false;
    }

    // Placeholder upgrade handler.
    internal static bool TryGrantUpgrade(PendingItem item, string sourceTag)
    {
        // TODO: Replace with actual upgrade unlock call once found.
        Log.LogInfo($"{sourceTag}: TODO upgrade grant -> id={item.Id}, amount={item.Amount}");
        return false;
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
            ProcessPendingItemQueue("InitializeWeaponsData");
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

// Generalized pending item entry.
public class PendingItem
{
    public ItemKind Kind { get; private set; }
    public string Id { get; private set; }
    public int Amount { get; private set; }

    public PendingItem(ItemKind kind, string id, int amount)
    {
        Kind = kind;
        Id = id;
        Amount = amount;
    }

    public override string ToString()
    {
        return $"Kind={Kind}, Id={Id}, Amount={Amount}";
    }
}