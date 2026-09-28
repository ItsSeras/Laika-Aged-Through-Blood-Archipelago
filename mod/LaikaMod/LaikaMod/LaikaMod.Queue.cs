using BepInEx;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using Laika.Quests.Goals;
using Laika.UI.InGame;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Pending item queue, item granting, reconciliation, and DeathLink helpers.
public partial class LaikaMod
{
    // ===== AP presentation popup queue =====
    // Item grants and location checks never wait for these popups. Presentation is
    // cosmetic only and is shown later when Laika has no other in-game screen open.

    private sealed class APPresentationPopupRequest
    {
        internal Sprite Sprite;
        internal string Title;
        internal string Message;
        internal Action OnCloseCallback;
        internal string DebugTag;
    }

    private static readonly Queue<APPresentationPopupRequest>
        PendingPresentationPopups = new Queue<APPresentationPopupRequest>();

    private static readonly Dictionary<PendingItem, APPresentationPopupRequest>
        CapturedGrantPresentations =
            new Dictionary<PendingItem, APPresentationPopupRequest>();

    internal static PendingItem ActiveAPGrantPresentationItem;

    // Vanilla key-item rewards sometimes must be allowed to run so their original
    // popup callback can advance quest/dialogue state. While such an AP location
    // reward is being added, keep a one-shot context so the vanilla popup can be
    // rebranded as an AP location check without suppressing its callback.
    private static APLocationDefinition ActiveVanillaLocationPopupDefinition;
    private static string ActiveVanillaLocationPopupItemId;

    internal static void ArmVanillaLocationPopupPresentation(
        APLocationDefinition definition,
        string sourceItemId)
    {
        if (definition == null || string.IsNullOrEmpty(sourceItemId))
            return;

        if (SessionState == null || !SessionState.APEnabled)
            return;

        ActiveVanillaLocationPopupDefinition = definition;
        ActiveVanillaLocationPopupItemId = sourceItemId;

        LogInfo(
            $"AP PRESENTATION: armed vanilla location popup rewrite -> " +
            $"{definition.DisplayName} ({sourceItemId})"
        );
    }

    internal static void ArmVanillaLocationPopupPresentation(
        APLocationDefinition definition,
        ItemData item,
        bool silent)
    {
        if (definition == null || item == null || string.IsNullOrEmpty(item.id))
            return;

        if (SessionState == null || !SessionState.APEnabled)
            return;

        if (IsGrantingAPItem || silent || item.IsSilentItem || !item.IsKeyItem)
            return;

        ArmVanillaLocationPopupPresentation(definition, item.id);
    }

    internal static void ClearVanillaLocationPopupPresentation(string itemId)
    {
        if (string.IsNullOrEmpty(ActiveVanillaLocationPopupItemId))
            return;

        if (!string.IsNullOrEmpty(itemId) &&
            !string.Equals(
                ActiveVanillaLocationPopupItemId,
                itemId,
                StringComparison.Ordinal))
        {
            return;
        }

        ActiveVanillaLocationPopupDefinition = null;
        ActiveVanillaLocationPopupItemId = null;
    }

    private static string APPresentationPlainText(string value)
    {
        return (value ?? string.Empty)
            .Replace('<', '‹')
            .Replace('>', '›')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    private static bool ShouldShowReceivedItemPopup(PendingItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.SourcePlayerName))
            return false;

        switch (item.Kind)
        {
            case ItemKind.Weapon:
            case ItemKind.WeaponUpgrade:
            case ItemKind.Collectible:
            case ItemKind.PuppyTreat:
            case ItemKind.KeyItem:
                return true;

            // Ingredients, materials, currency and map pieces are intentionally
            // quiet because their normal Laika acquisition is not a major popup.
            default:
                return false;
        }
    }

    private static Sprite ResolveReceivedItemPresentationSprite(PendingItem item)
    {
        try
        {
            if (item == null)
                return null;

            var loader = Singleton<ItemDataLoader>.Instance;
            if (loader == null)
                return null;

            ItemData data = null;

            if (item.Kind == ItemKind.Weapon ||
                item.Kind == ItemKind.WeaponUpgrade)
            {
                data = loader.FindWeapon(item.Id);
            }
            else
            {
                data = loader.Find(item.Id);
            }

            if (data == null)
                return null;

            return data.BigIcon != null ? data.BigIcon : data.Icon;
        }
        catch (Exception ex)
        {
            LogWarning(
                $"AP PRESENTATION: failed resolving received-item sprite for " +
                $"{item?.DisplayName}:\n{ex}"
            );
            return null;
        }
    }

    private static void EnqueuePresentationPopup(
        Sprite sprite,
        string title,
        string message,
        Action onCloseCallback,
        string debugTag)
    {
        PendingPresentationPopups.Enqueue(
            new APPresentationPopupRequest
            {
                Sprite = sprite,
                Title = title,
                Message = message,
                OnCloseCallback = onCloseCallback,
                DebugTag = debugTag
            }
        );

        LogInfo(
            $"AP PRESENTATION: queued popup -> {debugTag}. " +
            $"Pending={PendingPresentationPopups.Count}"
        );
    }

    internal static void ProcessPendingPresentationPopups()
    {
        if (PendingPresentationPopups.Count == 0)
            return;

        if (IsActuallyOnTitleScreen())
            return;

        InGameUIManager ui = MonoSingleton<InGameUIManager>.Instance;
        if (ui == null || ui.IsScreenOpen)
            return;

        APPresentationPopupRequest request = PendingPresentationPopups.Dequeue();

        try
        {
            Sprite sprite = request.Sprite ?? GetRenatoPopupLogoSprite();

            ui.ShowItemReceivedPopup(
                sprite,
                request.Title ?? string.Empty,
                request.Message ?? string.Empty,
                delegate
                {
                    try
                    {
                        request.OnCloseCallback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        LogWarning(
                            $"AP PRESENTATION: popup close callback failed for " +
                            $"{request.DebugTag}:\n{ex}"
                        );
                    }
                }
            );

            LogInfo(
                $"AP PRESENTATION: showing popup -> {request.DebugTag}. " +
                $"Remaining={PendingPresentationPopups.Count}"
            );
        }
        catch (Exception ex)
        {
            LogWarning(
                $"AP PRESENTATION: failed showing popup {request.DebugTag}:\n{ex}"
            );

            try
            {
                request.OnCloseCallback?.Invoke();
            }
            catch
            {
            }
        }
    }

    internal static void QueueSentLocationPresentation(
        APLocationDefinition definition)
    {
        if (definition == null || CoroutineRunner == null)
            return;

        CoroutineRunner.StartCoroutine(
            WaitForSentLocationPresentation(definition)
        );
    }

    private static System.Collections.IEnumerator WaitForSentLocationPresentation(
        APLocationDefinition definition)
    {
        float deadline = Time.unscaledTime + 10f;

        while (Time.unscaledTime < deadline)
        {
            ArchipelagoClientManager client =
                ArchipelagoClientManager.Instance;

            if (client == null || !client.IsConnected)
                yield break;

            APLocationPreview preview = client.GetLocationPreview(
                definition.LocationId,
                false,
                "AP SEND PREVIEW"
            );

            if (preview != null)
            {
                int localSlot =
                    SessionState != null && SessionState.Connection != null
                        ? SessionState.Connection.Slot
                        : 0;

                // Self-sends are represented by the received-item popup instead,
                // where we can use the actual Laika item artwork.
                if (preview.RecipientSlot != localSlot)
                {
                    EnqueuePresentationPopup(
                        GetRenatoPopupLogoSprite(),
                        "ITEM SENT!",
                        APPresentationPlainText(preview.ItemName) +
                            "\nTo " +
                            APPresentationPlainText(preview.RecipientName),
                        null,
                        "sent " + definition.DisplayName
                    );
                }

                yield break;
            }

            yield return null;
        }

        LogWarning(
            $"AP PRESENTATION: timed out waiting for sent-item preview -> " +
            $"{definition.DisplayName}"
        );
    }

    private static void CaptureReceivedPresentation(
        PendingItem item,
        Sprite vanillaSprite,
        Action onCloseCallback)
    {
        if (item == null || item.PresentationQueued)
            return;

        item.MarkPresentationQueued();

        if (!ShouldShowReceivedItemPopup(item))
            return;

        Sprite sprite =
            ResolveReceivedItemPresentationSprite(item) ?? vanillaSprite;

        CapturedGrantPresentations[item] =
            new APPresentationPopupRequest
            {
                Sprite = sprite,
                Title = "ITEM RECEIVED!",
                Message = APPresentationPlainText(item.DisplayName) +
                    "\nFrom " +
                    APPresentationPlainText(item.SourcePlayerName),
                OnCloseCallback = onCloseCallback,
                DebugTag = "received " + item.DisplayName
            };
    }

    private static void QueueReceivedItemPresentationIfNeeded(PendingItem item)
    {
        if (item == null)
            return;

        if (!ShouldShowReceivedItemPopup(item))
        {
            CapturedGrantPresentations.Remove(item);
            return;
        }

        APPresentationPopupRequest captured;
        if (CapturedGrantPresentations.TryGetValue(item, out captured))
        {
            CapturedGrantPresentations.Remove(item);
            PendingPresentationPopups.Enqueue(captured);
            LogInfo(
                $"AP PRESENTATION: committed captured received popup -> " +
                $"{item.DisplayName}. Pending={PendingPresentationPopups.Count}"
            );
            return;
        }

        // Some grant types (notably weapons) do not invoke Laika's vanilla
        // ItemReceivedPopup. Give those the same AP presentation as a fallback.
        item.MarkPresentationQueued();

        EnqueuePresentationPopup(
            ResolveReceivedItemPresentationSprite(item),
            "ITEM RECEIVED!",
            APPresentationPlainText(item.DisplayName) +
                "\nFrom " +
                APPresentationPlainText(item.SourcePlayerName),
            null,
            "received fallback " + item.DisplayName
        );
    }

    private static void DiscardCapturedGrantPresentation(PendingItem item)
    {
        if (item == null)
            return;

        CapturedGrantPresentations.Remove(item);
        item.ResetPresentationQueued();
    }

    // Intercept popups created during a concrete AP item grant. The actual item
    // grant continues immediately, while its visual presentation is serialized
    // through our cosmetic queue so back-to-back received items do not overwrite
    // each other or fight shop/camera screens.
    [HarmonyPatch(typeof(InGameUIManager), "ShowItemReceivedPopup")]
    public class InGameUIManager_ShowItemReceivedPopup_APPresentationPatch
    {
        static bool Prefix(
            ref Sprite sprite,
            ref string localizedTitle,
            ref string localizedMessage,
            Action onCloseCallback)
        {
            try
            {
                // Vanilla quest/key-item rewards need their real popup to stay alive
                // because its close callback may advance dialogue or restore control.
                // Change only the presentation and let the original popup/callback run.
                if (!IsGrantingAPItem &&
                    SessionState != null &&
                    SessionState.APEnabled &&
                    ActiveVanillaLocationPopupDefinition != null)
                {
                    APLocationDefinition location =
                        ActiveVanillaLocationPopupDefinition;

                    string itemId = ActiveVanillaLocationPopupItemId;
                    ClearVanillaLocationPopupPresentation(itemId);

                    if (HasLocationBeenSent(location.LocationId))
                    {
                        Sprite apLogo = GetRenatoPopupLogoSprite();
                        if (apLogo != null)
                            sprite = apLogo;

                        localizedTitle = "LOCATION SENT!";
                        localizedMessage =
                            APPresentationPlainText(location.DisplayName);

                        LogInfo(
                            $"AP PRESENTATION: rewrote vanilla location popup -> " +
                            $"{location.DisplayName} ({location.LocationId})"
                        );
                    }
                    else
                    {
                        LogWarning(
                            $"AP PRESENTATION: vanilla location popup fired before " +
                            $"location was marked sent -> {location.DisplayName}. " +
                            "Keeping vanilla text."
                        );
                    }

                    return true;
                }

                PendingItem item = ActiveAPGrantPresentationItem;

                if (!IsGrantingAPItem || item == null ||
                    string.IsNullOrWhiteSpace(item.SourcePlayerName))
                {
                    return true;
                }

                CaptureReceivedPresentation(
                    item,
                    sprite,
                    onCloseCallback
                );

                // If this AP item is intentionally silent, preserve a rare
                // callback rather than dropping it. Our AP grant calls normally
                // pass null here, but this keeps the interception defensive.
                if (!ShouldShowReceivedItemPopup(item) &&
                    onCloseCallback != null)
                {
                    onCloseCallback();
                }

                LogInfo(
                    $"AP PRESENTATION: intercepted vanilla received popup for " +
                    $"{item.DisplayName}. Eligible={ShouldShowReceivedItemPopup(item)}"
                );

                return false;
            }
            catch (Exception ex)
            {
                LogWarning(
                    "AP PRESENTATION: received-popup interception failed; " +
                    "allowing vanilla popup.\n" + ex
                );
                return true;
            }
        }
    }

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
        EnqueueRequiredStartingItems();

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
                    item.ResetPresentationQueued();
                    ActiveAPGrantPresentationItem = item;

                    bool granted;
                    try
                    {
                        granted = TryGrantPendingItem(item, sourceTag);
                    }
                    finally
                    {
                        ActiveAPGrantPresentationItem = null;
                    }

                    if (granted)
                    {
                        QueueReceivedItemPresentationIfNeeded(item);
                        // A first AP weapon can activate G_GUN_RECEIVED during this pass.
                        // Queue the missing Pistol now so this pass can deliver it too.
                        if (item.Kind == ItemKind.Weapon &&
                            item.Id != "I_W_PISTOL")
                        {
                            EnqueueRequiredStartingItems();
                        }

                        LaikaMod.LogInfo($"{sourceTag}: grant succeeded -> {item}");

                        if (item.Kind == ItemKind.KeyItem && item.Id == "I_DICTIONARY")
                        {
                            TryRecoverMagicalBookLocation(sourceTag);
                        }

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
                        DiscardCapturedGrantPresentation(item);

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
                    ActiveAPGrantPresentationItem = null;
                    DiscardCapturedGrantPresentation(item);
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