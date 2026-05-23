using Laika.Cassettes;
using Laika.Inventory;
using Laika.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public partial class LaikaMod
{
    // AP reconciliation helpers.
    // These restore important AP-owned state after scene loads, reconnects, save reloads,
    // or manager initialization timing issues.
    internal static bool IsImportantReconcileKind(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.Weapon:
            case ItemKind.WeaponUpgrade:
            case ItemKind.KeyItem:
            case ItemKind.PuppyTreat:
            case ItemKind.Collectible:
            case ItemKind.MapUnlock:
                return true;

            default:
                return false;
        }
    }

    internal static bool TryBuildMissingImportantReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        if (expectedItem == null)
            return false;

        try
        {
            switch (expectedItem.Kind)
            {
                case ItemKind.Weapon:
                    return TryBuildMissingWeaponReconcileItem(expectedItem, out missingItem);

                case ItemKind.WeaponUpgrade:
                    return TryBuildMissingWeaponUpgradeReconcileItem(expectedItem, out missingItem);

                case ItemKind.KeyItem:
                case ItemKind.PuppyTreat:
                    return TryBuildMissingInventoryReconcileItem(expectedItem, out missingItem);

                case ItemKind.Collectible:
                    return TryBuildMissingCassetteReconcileItem(expectedItem, out missingItem);

                case ItemKind.MapUnlock:
                    return TryBuildMissingMapUnlockReconcileItem(expectedItem, out missingItem);

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"TryBuildMissingImportantReconcileItem failed for {expectedItem}:\n{ex}");
            return false;
        }
    }

    private static bool TryBuildMissingWeaponReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        var weaponsInventory = Singleton<WeaponsInventory>.Instance;
        if (weaponsInventory == null)
            return false;

        if (weaponsInventory.HasWeapon(expectedItem.Id))
            return false;

        missingItem = new PendingItem(
            expectedItem.Kind,
            expectedItem.Id,
            1,
            expectedItem.DisplayName
        );

        return true;
    }

    private static bool TryBuildMissingWeaponUpgradeReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        var weaponsInventory = Singleton<WeaponsInventory>.Instance;
        if (weaponsInventory == null)
            return false;

        var itemLoader = Singleton<ItemDataLoader>.Instance;
        if (itemLoader == null)
            return false;

        ItemDataWeapon weaponData = itemLoader.FindWeapon(expectedItem.Id);
        if (weaponData == null)
            return false;

        bool hasWeapon = weaponsInventory.HasWeapon(expectedItem.Id);

        // If AP sent the upgrade before the player owns/crafts the weapon,
        // keep it pending so it can apply later.
        if (!hasWeapon)
        {
            missingItem = new PendingItem(
                ItemKind.WeaponUpgrade,
                expectedItem.Id,
                expectedItem.Amount,
                expectedItem.DisplayName
            );

            LogInfo($"AP RECONCILE: weapon upgrade still waiting for weapon -> {missingItem}");
            return true;
        }

        WeaponInstance weaponInstance = weaponsInventory.GetWeaponInstance(weaponData);
        if (weaponInstance == null)
            return false;

        int currentInternalLevel = weaponInstance.Level;
        int expectedUpgradeSteps = Math.Max(0, expectedItem.Amount);
        int targetInternalLevel = Math.Min(3, expectedUpgradeSteps);
        int missingUpgradeSteps = targetInternalLevel - currentInternalLevel;

        if (missingUpgradeSteps <= 0)
            return false;

        missingItem = new PendingItem(
            ItemKind.WeaponUpgrade,
            expectedItem.Id,
            missingUpgradeSteps,
            expectedItem.DisplayName
        );

        LogInfo($"AP RECONCILE: weapon upgrade missing levels -> {missingItem}");
        return true;
    }

    private static bool TryBuildMissingInventoryReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        if (expectedItem != null && expectedItem.Id == "I_MAYA_PENDANT")
        {
            if (HasProgressionFlag("G_FREE_TELEPORTS_UNLOCKED"))
                return false;
        }

        if (WasVanillaConsumedAPItem(expectedItem.Kind, expectedItem.Id))
        {
            LogInfo(
                $"AP RECONCILE: not restoring {expectedItem.Id} because vanilla consumed this AP item."
            );

            return false;
        }

        if (expectedItem.Kind == ItemKind.KeyItem && IsHarpoonPieceId(expectedItem.Id))
        {
            if (WasHarpoonPieceReceivedFromAP(expectedItem.Id))
            {
                LogInfo(
                    $"AP RECONCILE: deferred harpoon piece already recorded from AP, not restoring to vanilla inventory yet -> {expectedItem.Id}"
                );

                return false;
            }
        }

        var inventory = Singleton<InventoryManager>.Instance;
        if (inventory == null)
            return false;

        bool alreadyOwned = false;

        try
        {
            alreadyOwned = inventory.HasItem(expectedItem.Id);
        }
        catch
        {
            alreadyOwned = inventory.GetItemAmount(expectedItem.Id) > 0;
        }

        if (alreadyOwned)
            return false;

        missingItem = new PendingItem(
            expectedItem.Kind,
            expectedItem.Id,
            1,
            expectedItem.DisplayName
        );

        return true;
    }

    private static bool TryBuildMissingCassetteReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        var cassettesManager = Singleton<CassettesManager>.Instance;
        if (cassettesManager == null)
            return false;

        if (cassettesManager.HasCassette(expectedItem.Id))
            return false;

        missingItem = new PendingItem(
            ItemKind.Collectible,
            expectedItem.Id,
            1,
            expectedItem.DisplayName
        );

        return true;
    }

    private static bool TryBuildMissingMapUnlockReconcileItem(PendingItem expectedItem, out PendingItem missingItem)
    {
        missingItem = null;

        if (expectedItem == null || string.IsNullOrEmpty(expectedItem.Id))
            return false;

        bool apVisualUnlocked = HasAPMapUnlock(expectedItem.Id);

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;
        bool vanillaUnlocked =
            progressionManager != null &&
            progressionManager.ProgressionData != null &&
            progressionManager.ProgressionData.HasMapAreaUnlocked(expectedItem.Id);

        if (apVisualUnlocked)
        {
            RefreshMapAreaVisuals(expectedItem.Id);

            LogInfo(
                $"AP RECONCILE: AP map area already available, refreshed visuals -> {expectedItem.Id} " +
                $"apVisualUnlocked={apVisualUnlocked}, vanillaUnlocked={vanillaUnlocked}"
            );

            return false;
        }

        if (vanillaUnlocked)
        {
            LogInfo(
                $"AP RECONCILE: vanilla has map area unlocked, but AP has not granted it yet -> {expectedItem.Id}. " +
                $"Treating as missing for AP visual state."
            );
        }

        missingItem = new PendingItem(
            ItemKind.MapUnlock,
            expectedItem.Id,
            1,
            expectedItem.DisplayName
        );

        return true;
    }

    internal static void ScheduleShotgunQuestReconcile(string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: could not schedule shotgun quest reconcile because CoroutineRunner is null.");
            return;
        }

        CoroutineRunner.StartCoroutine(ShotgunQuestReconcileCoroutine(sourceTag));
    }

    private static System.Collections.IEnumerator ShotgunQuestReconcileCoroutine(string sourceTag)
    {
        yield return null;
        TryReconcileKnownQuestSoftlocks(sourceTag + "/Frame1");

        yield return new WaitForSecondsRealtime(0.15f);
        TryReconcileKnownQuestSoftlocks(sourceTag + "/Delay015");

        yield return new WaitForSecondsRealtime(0.5f);
        TryReconcileKnownQuestSoftlocks(sourceTag + "/Delay050");
    }

    internal static void ScheduleSceneLoadedAPReconcile(string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: could not schedule scene-load AP reconcile because CoroutineRunner is null.");
            return;
        }

        CoroutineRunner.StartCoroutine(SceneLoadedAPReconcileCoroutine(sourceTag));
    }

    private static System.Collections.IEnumerator SceneLoadedAPReconcileCoroutine(string sourceTag)
    {
        yield return null;
        yield return null;

        if (ArchipelagoClientManager.Instance != null && ArchipelagoClientManager.Instance.IsConnected)
        {
            ArchipelagoClientManager.Instance.ForceReconcileReceivedItems(sourceTag + "/Frame2");
        }

        yield return new WaitForSecondsRealtime(0.25f);

        if (ArchipelagoClientManager.Instance != null && ArchipelagoClientManager.Instance.IsConnected)
        {
            ArchipelagoClientManager.Instance.ForceReconcileReceivedItems(sourceTag + "/Delay025");
        }

        yield return new WaitForSecondsRealtime(1.0f);

        if (ArchipelagoClientManager.Instance != null && ArchipelagoClientManager.Instance.IsConnected)
        {
            ArchipelagoClientManager.Instance.ForceReconcileReceivedItems(sourceTag + "/Delay100");
        }
    }

    internal static void RememberAPMapUnlock(string mapAreaId)
    {
        if (SessionState == null || string.IsNullOrEmpty(mapAreaId))
            return;

        if (SessionState.APUnlockedMapAreaIds == null)
            SessionState.APUnlockedMapAreaIds = new List<string>();

        if (!SessionState.APUnlockedMapAreaIds.Contains(mapAreaId))
        {
            SessionState.APUnlockedMapAreaIds.Add(mapAreaId);
            SaveSessionState();

            LogInfo($"AP MAP STATE: remembered AP map unlock {mapAreaId}.");
        }
    }

    internal static bool HasAPMapUnlock(string mapAreaId)
    {
        if (SessionState == null || string.IsNullOrEmpty(mapAreaId))
            return false;

        if (SessionState.APUnlockedMapAreaIds == null)
            return false;

        return SessionState.APUnlockedMapAreaIds.Contains(mapAreaId);
    }

    internal static void RefreshMapAreaVisuals(string mapAreaId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mapAreaId))
                return;

            int found = 0;

            MapArea[] mapAreas = UnityEngine.Object.FindObjectsOfType<MapArea>(true);

            foreach (MapArea mapArea in mapAreas)
            {
                if (mapArea == null)
                    continue;

                FieldInfo field = typeof(MapArea).GetField(
                    "mapAreaID",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                if (field == null)
                {
                    LogWarning("MAP REFRESH: could not find MapArea.mapAreaID field.");
                    return;
                }

                string currentId = field.GetValue(mapArea) as string;

                if (!string.Equals(currentId, mapAreaId, StringComparison.OrdinalIgnoreCase))
                    continue;

                found++;

                mapArea.gameObject.SetActive(true);
                mapArea.Enable(true);

                LogInfo($"MAP REFRESH: enabled MapArea object {mapArea.name} for {mapAreaId}.");
            }

            LogInfo($"MAP REFRESH: found {found} MapArea object(s) for {mapAreaId}.");
        }
        catch (Exception ex)
        {
            LogWarning($"RefreshMapAreaVisuals failed for {mapAreaId}:\n{ex}");
        }
    }
}
