using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy.Shops;
using Laika.Inventory;
using Laika.UI;
using Laika.UI.InGame;
using Laika.UI.InGame.Inventory;
using Laika.UI.InGame.Shop;
using Laika.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class LaikaMod
{
    internal static string ActiveShopPurchaseItemId = null;

    internal static bool IsActiveShopPurchase(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return false;

        if (string.IsNullOrEmpty(ActiveShopPurchaseItemId))
            return false;

        return string.Equals(
            ActiveShopPurchaseItemId,
            itemId,
            StringComparison.Ordinal
        );
    }

    // ===== AP-aware normal shop / travelling merchant presentation =====

    private static readonly FieldInfo ShopScreenShopContentField =
        AccessTools.Field(typeof(ShopScreen), "shopContent");

    private static readonly FieldInfo ShopScreenPreviewField =
        AccessTools.Field(typeof(ShopScreen), "shopItemPreview");

    private static readonly FieldInfo ItemSlotDisplayField =
        AccessTools.Field(typeof(ItemSlotWidget), "display");

    private static readonly FieldInfo ItemDisplayIconField =
        AccessTools.Field(typeof(ItemDisplayWidget), "itemIcon");

    private static readonly FieldInfo ItemDisplayNameField =
        AccessTools.Field(typeof(ItemDisplayWidget), "itemName");

    private static readonly FieldInfo ItemPreviewImageField =
        AccessTools.Field(typeof(ItemPreviewWidget), "itemImage");

    private static readonly FieldInfo ItemPreviewNameField =
        AccessTools.Field(typeof(ItemPreviewWidget), "itemName");

    private static readonly FieldInfo ItemPreviewDescriptionField =
        AccessTools.Field(typeof(ItemPreviewWidget), "itemDescription");

    private static readonly FieldInfo ItemPreviewIngredientDescriptionField =
        AccessTools.Field(typeof(ItemPreviewWidget), "ingredientDescription");

    private static readonly FieldInfo ItemPreviewIngredientTagField =
        AccessTools.Field(typeof(ItemPreviewWidget), "ingredientTag");

    private static readonly FieldInfo ShopPreviewInventoryAmountField =
        AccessTools.Field(typeof(ShopItemPreviewWidget), "itemInventoryAmount");

    private static readonly FieldInfo ShopCartImageField =
        AccessTools.Field(typeof(ShopCart), "itemImage");

    private static readonly FieldInfo ShopCartNameField =
        AccessTools.Field(typeof(ShopCart), "itemName");

    private static readonly FieldInfo ShopCartInventoryAmountField =
        AccessTools.Field(typeof(ShopCart), "inventoryAmount");

    private static bool TryGetActiveAPShopLocation(
        ItemData itemData,
        out APLocationDefinition definition)
    {
        definition = null;

        if (SessionState == null || !SessionState.APEnabled ||
            itemData == null || string.IsNullOrEmpty(itemData.id))
        {
            return false;
        }

        if (!TryGetLocationDefinition(itemData.id, out definition))
            return false;

        // Once the AP location has already been checked, any remaining vanilla
        // stock is no longer representing an unresolved AP purchase.
        if (HasLocationBeenSent(definition.LocationId))
            return false;

        return true;
    }

    private static List<long> GetCurrentAPShopLocationIds(ShopController shop)
    {
        var result = new List<long>();

        if (shop == null || shop.ItemStock == null)
            return result;

        foreach (KeyValuePair<ItemData, int> entry in shop.ItemStock)
        {
            // This is the spoiler boundary: only stock the game has actually
            // made available right now is allowed to be scouted/hinted.
            if (entry.Key == null || entry.Value <= 0)
                continue;

            APLocationDefinition definition;
            if (!TryGetActiveAPShopLocation(entry.Key, out definition))
                continue;

            if (!result.Contains(definition.LocationId))
                result.Add(definition.LocationId);
        }

        return result;
    }

    private static bool TryGetCachedAPShopPreview(
        ItemData itemData,
        out APLocationDefinition definition,
        out APLocationPreview preview)
    {
        preview = null;

        if (!TryGetActiveAPShopLocation(itemData, out definition))
            return false;

        ArchipelagoClientManager client =
            ArchipelagoClientManager.Instance;

        if (client != null)
        {
            client.TryGetCachedLocationPreview(
                definition.LocationId,
                out preview
            );
        }

        return true;
    }

    private static string GetAPShopLoadingText()
    {
        ArchipelagoClientManager client =
            ArchipelagoClientManager.Instance;

        if (client == null || !client.IsConnected)
            return "Connect to Archipelago for preview.";

        return WorldOptions.AutomaticPurchaseHints
            ? "Creating hints and loading shop previews..."
            : "Loading AP shop preview...";
    }

    private static void ApplyAPShopGridPresentation(ShopScreen screen)
    {
        if (screen == null || SessionState == null || !SessionState.APEnabled)
            return;

        ItemsGrid grid = ShopScreenShopContentField?.GetValue(screen) as ItemsGrid;
        if (grid == null)
            return;

        Sprite logo = GetRenatoPopupLogoSprite();

        foreach (NavigableItem navigable in grid.Items)
        {
            ItemSlotWidget slot = navigable as ItemSlotWidget;
            if (slot == null || slot.DisplayData.ItemData == null)
                continue;

            APLocationDefinition definition;
            APLocationPreview preview;
            if (!TryGetCachedAPShopPreview(
                slot.DisplayData.ItemData,
                out definition,
                out preview))
            {
                continue;
            }

            ItemDisplayWidget display =
                ItemSlotDisplayField?.GetValue(slot) as ItemDisplayWidget;

            if (display == null)
                continue;

            Image icon = ItemDisplayIconField?.GetValue(display) as Image;
            if (icon != null && logo != null)
                icon.sprite = logo;

            TextMeshProUGUI name =
                ItemDisplayNameField?.GetValue(display) as TextMeshProUGUI;

            if (name != null)
                name.text = preview != null ? preview.ItemName : "AP Item";
        }
    }

    private static void RefreshHighlightedAPShopPreview(
        ShopScreen screen,
        ShopController shop)
    {
        if (screen == null || shop == null)
            return;

        ItemsGrid grid = ShopScreenShopContentField?.GetValue(screen) as ItemsGrid;
        ShopItemPreviewWidget previewWidget =
            ShopScreenPreviewField?.GetValue(screen) as ShopItemPreviewWidget;

        if (grid == null || previewWidget == null ||
            grid.HighlightedItemIdx < 0 ||
            grid.HighlightedItemIdx >= grid.Items.Count)
        {
            return;
        }

        ItemSlotWidget slot =
            grid.Items[grid.HighlightedItemIdx] as ItemSlotWidget;

        if (slot == null || slot.DisplayData.ItemData == null)
            return;

        ItemData itemData = slot.DisplayData.ItemData;

        previewWidget.SetUp(itemData);
        previewWidget.SetPrice(shop.GetPrice(itemData, 1));
    }

    private static IEnumerator RefreshAPShopPresentationC(
        ShopScreen screen,
        ShopController shop,
        List<long> locationIds)
    {
        ApplyAPShopGridPresentation(screen);

        ArchipelagoClientManager client =
            ArchipelagoClientManager.Instance;

        if (client == null || !client.IsConnected || locationIds.Count == 0)
        {
            RefreshHighlightedAPShopPreview(screen, shop);
            yield break;
        }

        float deadline = Time.unscaledTime + 10f;

        while (screen != null &&
               SessionState != null &&
               SessionState.APEnabled &&
               client.IsConnected &&
               Time.unscaledTime < deadline)
        {
            client.PrimeLocationPreviews(
                locationIds,
                true,
                "SHOP AP PREVIEW"
            );

            if (client.AreLocationPreviewsReady(locationIds))
                break;

            yield return null;
        }

        if (screen == null || SessionState == null || !SessionState.APEnabled)
            yield break;

        ApplyAPShopGridPresentation(screen);
        RefreshHighlightedAPShopPreview(screen, shop);
    }

    private static void BeginAPShopPresentation(
        ShopScreen screen,
        ShopController shop)
    {
        if (screen == null || shop == null ||
            SessionState == null || !SessionState.APEnabled)
        {
            return;
        }

        List<long> currentLocations =
            GetCurrentAPShopLocationIds(shop);

        LogInfo(
            $"SHOP AP PREVIEW: opened shop id={shop.Id}, title={shop.Title}, " +
            $"currentAPStock={currentLocations.Count}, " +
            $"autoHints={WorldOptions.AutomaticPurchaseHints}"
        );

        ApplyAPShopGridPresentation(screen);
        RefreshHighlightedAPShopPreview(screen, shop);

        if (CoroutineRunner != null)
        {
            CoroutineRunner.StartCoroutine(
                RefreshAPShopPresentationC(
                    screen,
                    shop,
                    currentLocations
                )
            );
        }
    }

    private static void ApplyAPShopPreviewWidget(
        ShopItemPreviewWidget widget,
        ItemData itemData)
    {
        if (widget == null)
            return;

        APLocationDefinition definition;
        APLocationPreview preview;
        if (!TryGetCachedAPShopPreview(itemData, out definition, out preview))
            return;

        Sprite logo = GetRenatoPopupLogoSprite();

        Image image = ItemPreviewImageField?.GetValue(widget) as Image;
        if (image != null && logo != null)
        {
            image.enabled = true;
            image.sprite = logo;
            image.preserveAspect = true;
        }

        TextMeshProUGUI itemName =
            ItemPreviewNameField?.GetValue(widget) as TextMeshProUGUI;

        if (itemName != null)
        {
            itemName.enabled = true;
            itemName.text = preview != null
                ? preview.ItemName.ToUpperInvariant()
                : "RANDOMIZED AP ITEM";
        }

        TextMeshProUGUI description =
            ItemPreviewDescriptionField?.GetValue(widget) as TextMeshProUGUI;

        if (description != null)
        {
            description.enabled = true;
            description.text = preview != null
                ? "For " + preview.RecipientName
                : GetAPShopLoadingText();
        }

        TextMeshProUGUI ingredientDescription =
            ItemPreviewIngredientDescriptionField?.GetValue(widget) as TextMeshProUGUI;

        if (ingredientDescription != null)
            ingredientDescription.enabled = false;

        Component ingredientTag =
            ItemPreviewIngredientTagField?.GetValue(widget) as Component;

        if (ingredientTag != null)
            ingredientTag.gameObject.SetActive(false);

        TextMeshProUGUI inventoryAmount =
            ShopPreviewInventoryAmountField?.GetValue(widget) as TextMeshProUGUI;

        if (inventoryAmount != null)
            inventoryAmount.text = "AP";
    }

    private static void ApplyAPShopCart(ShopCart cart, ShopCart.Data data)
    {
        if (cart == null || data == null || data.ItemData == null)
            return;

        APLocationDefinition definition;
        APLocationPreview preview;
        if (!TryGetCachedAPShopPreview(
            data.ItemData,
            out definition,
            out preview))
        {
            return;
        }

        Image image = ShopCartImageField?.GetValue(cart) as Image;
        Sprite logo = GetRenatoPopupLogoSprite();

        if (image != null && logo != null)
        {
            image.sprite = logo;
            image.preserveAspect = true;
        }

        TextMeshProUGUI itemName =
            ShopCartNameField?.GetValue(cart) as TextMeshProUGUI;

        if (itemName != null)
        {
            string body = preview != null
                ? preview.ItemName + "\nfor " + preview.RecipientName
                : "Randomized AP Item\n" + GetAPShopLoadingText();

            itemName.text = "<size=88%>" + body + "</size>";
        }

        TextMeshProUGUI inventoryAmount =
            ShopCartInventoryAmountField?.GetValue(cart) as TextMeshProUGUI;

        if (inventoryAmount != null)
            inventoryAmount.text = string.Empty;
    }

    [HarmonyPatch(typeof(ShopScreen), "SetUp")]
    public class ShopScreen_SetUp_APPreviewPatch
    {
        static void Postfix(ShopScreen __instance, object data)
        {
            try
            {
                if (SessionState == null || !SessionState.APEnabled)
                    return;

                ShopScreen.ShopScreenData shopData =
                    data as ShopScreen.ShopScreenData;

                if (shopData == null || shopData.ShopController == null)
                    return;

                BeginAPShopPresentation(
                    __instance,
                    shopData.ShopController
                );
            }
            catch (Exception ex)
            {
                LogWarning("SHOP AP PREVIEW: ShopScreen.SetUp failed:\n" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(ShopItemPreviewWidget), "SetUp")]
    public class ShopItemPreviewWidget_SetUp_APPreviewPatch
    {
        static void Postfix(ShopItemPreviewWidget __instance, object data)
        {
            try
            {
                if (SessionState == null || !SessionState.APEnabled)
                    return;

                ApplyAPShopPreviewWidget(
                    __instance,
                    data as ItemData
                );
            }
            catch (Exception ex)
            {
                LogWarning(
                    "SHOP AP PREVIEW: ShopItemPreviewWidget.SetUp failed:\n" + ex
                );
            }
        }
    }

    [HarmonyPatch(typeof(ShopCart), "SetUp")]
    public class ShopCart_SetUp_APPreviewPatch
    {
        static void Postfix(ShopCart __instance, object data)
        {
            try
            {
                if (SessionState == null || !SessionState.APEnabled)
                    return;

                ApplyAPShopCart(
                    __instance,
                    data as ShopCart.Data
                );
            }
            catch (Exception ex)
            {
                LogWarning("SHOP AP PREVIEW: ShopCart.SetUp failed:\n" + ex);
            }
        }
    }

    // Cassette and shop-source Harmony patches.
    // These detect real cassette pickups/rewards and convert them into AP location checks.
    internal static bool TryGetCassetteIdFromResourceDestructible(ResourceDestructible destructible, out string cassetteId)
    {
        cassetteId = null;

        try
        {
            if (destructible == null)
                return false;

            FieldInfo resourcesPoolField =
                typeof(ResourceDestructible).GetField(
                    "resourcesPool",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );

            if (resourcesPoolField == null)
                return false;

            ResourceData[] resourcesPool = resourcesPoolField.GetValue(destructible) as ResourceData[];

            if (resourcesPool == null || resourcesPool.Length == 0 || resourcesPool[0] == null)
                return false;

            if (resourcesPool[0].resourceObject == null)
                return false;

            ItemInstance itemInstance = resourcesPool[0].resourceObject.GetComponent<ItemInstance>();

            if (itemInstance == null || itemInstance.ItemData == null)
                return false;

            CassetteData cassetteData = itemInstance.ItemData as CassetteData;

            if (cassetteData == null)
                return false;

            cassetteId = cassetteData.id;
            return !string.IsNullOrEmpty(cassetteId);
        }
        catch (Exception ex)
        {
            LogWarning($"TryGetCassetteIdFromResourceDestructible failed:\n{ex}");
            return false;
        }
    }

    [HarmonyPatch(typeof(ResourceDestructible), "CanBeUsed")]
    public class ResourceDestructible_CanBeUsed_APCassetteSourcePatch
    {
        static void Postfix(ResourceDestructible __instance, ref bool __result)
        {
            try
            {
                if (__instance == null)
                    return;

                if (!(__instance is CassetteDestructible))
                    return;

                string cassetteId;
                if (!LaikaMod.TryGetCassetteIdFromResourceDestructible(__instance, out cassetteId))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                // If this cassette location has already been checked, the boombox should stay gone,
                // even if the cassette is not currently in vanilla cassette inventory.
                if (LaikaMod.HasSentLocationCheck(definition))
                {
                    __result = false;

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: hiding already-checked cassette source {cassetteId}."
                    );

                    return;
                }

                // If AP already gave this cassette but the physical source has not been checked,
                // force the source usable so the player can still shoot it and send the check.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                {
                    if (LaikaMod.IsQuestRewardCassetteId(cassetteId) &&
                        !LaikaMod.IsQuestRewardCassetteLocationAllowed(cassetteId))
                    {
                        __result = false;

                        LaikaMod.LogInfo(
                            $"AP CASSETTE SOURCE: not forcing early quest-reward cassette source visible for {cassetteId}; " +
                            "matching quest is not complete yet."
                        );

                        return;
                    }

                    __result = true;

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: forcing source visible for AP-owned unchecked cassette {cassetteId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"ResourceDestructible_CanBeUsed_APCassetteSourcePatch exception:\n{ex}");
            }
        }
    }

    // Physical cassette pickups (including cassettes dropped by boomboxes) do not
    // use InventoryManager.AddItem. CassettesManager shows its vanilla popup before
    // our AddCassetteToInventory postfix can identify/send the AP location, so arm
    // the popup rewrite from the concrete ItemInstance pickup instead.
    [HarmonyPatch(typeof(ItemInstance), "OnEquip")]
    public class ItemInstance_OnEquip_APCassetteLocationPopupPatch
    {
        static void Prefix(ItemInstance __instance, out string __state)
        {
            __state = null;

            try
            {
                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                CassetteData cassette = __instance.ItemData as CassetteData;
                if (cassette == null || string.IsNullOrEmpty(cassette.id))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassette.id, out definition))
                    return;

                // Quest-reward cassettes are handled by their completed-quest hooks,
                // not as generic physical pickups.
                if (LaikaMod.IsQuestRewardCassetteId(cassette.id))
                    return;

                // The player has concretely collected the physical cassette source.
                // Send/mark the AP check before vanilla creates its cassette popup so
                // ShowItemReceivedPopup can safely rebrand that same popup.
                if (!LaikaMod.HasSentLocationCheck(definition))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "ItemInstance_OnEquip_APCassetteLocationPopupPatch",
                        false
                    );
                }

                if (!LaikaMod.HasLocationBeenSent(definition.LocationId))
                {
                    LaikaMod.LogWarning(
                        $"AP PRESENTATION: physical cassette pickup was not marked sent before popup -> " +
                        $"{definition.DisplayName} ({cassette.id}). Keeping vanilla presentation."
                    );

                    return;
                }

                LaikaMod.ArmVanillaLocationPopupPresentation(
                    definition,
                    cassette.id
                );

                __state = cassette.id;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"ItemInstance_OnEquip_APCassetteLocationPopupPatch.Prefix exception:\n{ex}"
                );
            }
        }

        static void Postfix(string __state)
        {
            try
            {
                // Normally ShowItemReceivedPopup consumes the one-shot context. If
                // AddCassetteToInventory returned false and no popup was created,
                // clean it here so an unrelated later popup cannot be rewritten.
                if (!string.IsNullOrEmpty(__state))
                    LaikaMod.ClearVanillaLocationPopupPresentation(__state);
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"ItemInstance_OnEquip_APCassetteLocationPopupPatch.Postfix exception:\n{ex}"
                );
            }
        }

        static Exception Finalizer(string __state, Exception __exception)
        {
            try
            {
                if (!string.IsNullOrEmpty(__state))
                    LaikaMod.ClearVanillaLocationPopupPresentation(__state);
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"ItemInstance_OnEquip_APCassetteLocationPopupPatch.Finalizer cleanup failed:\n{ex}"
                );
            }

            return __exception;
        }
    }

    [HarmonyPatch(typeof(CassetteDestructible), "Destruction")]
    public class CassetteDestructible_Destruction_APOwnedCassetteSourcePatch
    {
        static void Prefix(CassetteDestructible __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                string cassetteId;
                if (!LaikaMod.TryGetCassetteIdFromResourceDestructible(__instance, out cassetteId))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                // This is the AP-owned cassette case:
                // vanilla leaves the boombox available because we forced it visible,
                // but the dropped cassette may not re-add because the player already owns it.
                // So breaking the boombox itself is enough to count the location.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                {
                    if (LaikaMod.IsQuestRewardCassetteId(cassetteId) &&
                        !LaikaMod.IsQuestRewardCassetteLocationAllowed(cassetteId))
                    {
                        LaikaMod.LogInfo(
                            $"AP CASSETTE SOURCE: blocked boombox destruction check for early quest-reward cassette {cassetteId}; " +
                            "matching quest is not complete yet."
                        );

                        return;
                    }

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "CassetteDestructible_Destruction_APOwnedCassetteSourcePatch",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"AP CASSETTE SOURCE: sent check on boombox destruction for AP-owned cassette {cassetteId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassetteDestructible_Destruction_APOwnedCassetteSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassetteAdder), "Start")]
    public class CassetteAdder_Start_APCassetteSourcePatch
    {
        private static readonly FieldInfo CassetteField =
            typeof(CassetteAdder).GetField(
                "cassette",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static bool Prefix(CassetteAdder __instance)
        {
            try
            {
                if (__instance == null || CassetteField == null)
                    return true;

                CassetteData cassette = CassetteField.GetValue(__instance) as CassetteData;

                if (cassette == null || string.IsNullOrEmpty(cassette.id))
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(cassette.id, out definition))
                    return true;

                if (LaikaMod.HasSentLocationCheck(definition))
                {
                    __instance.gameObject.SetActive(false);

                    LaikaMod.LogInfo(
                        $"AP CASSETTE ADDER: blocked already-checked cassette source {cassette.id}."
                    );

                    return false;
                }

                // Quest-reward cassettes are not physical pickup sources.
                // They should only send from QuestClosePatch after their matching quest completes.
                if (LaikaMod.IsQuestRewardCassetteId(cassette.id))
                {
                    if (!LaikaMod.IsQuestRewardCassetteLocationAllowed(cassette.id))
                    {
                        __instance.gameObject.SetActive(false);

                        LaikaMod.LogInfo(
                            $"AP CASSETTE ADDER: blocked early quest-reward cassette source {cassette.id}; " +
                            "matching quest is not complete yet."
                        );

                        return false;
                    }

                    LaikaMod.LogInfo(
                        $"AP CASSETTE ADDER: quest-reward cassette source {cassette.id} is allowed because matching quest is complete."
                    );
                }

                // If AP already gave a normal physical cassette and this source has not been checked,
                // send the location now and keep the AP-owned cassette.
                // Quest-reward cassettes should not use this path unless their quest is already complete.
                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassette.id))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "CassetteAdder_Start_APOwnedCassette",
                        false
                    );

                    __instance.gameObject.SetActive(false);

                    LaikaMod.LogInfo(
                        $"AP CASSETTE ADDER: sent check for AP-owned cassette source {cassette.id} without removing cassette."
                    );

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassetteAdder_Start_APCassetteSourcePatch exception:\n{ex}");
                return true;
            }
        }
    }

    internal static bool HasCassetteLocationBeenChecked(string cassetteId)
    {
        APLocationDefinition definition;

        if (!TryGetLocationDefinition(cassetteId, out definition))
            return false;

        return HasSentLocationCheck(definition);
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(string), typeof(Action), typeof(bool) })]
    public class CassettesManager_AddCassetteById_JakobCollectionPatch
    {
        static void Prefix(string cassetteId, Action finishedCallback, bool silent)
        {
            try
            {
                if (LaikaMod.IsGrantingAPItem)
                    return;

                if (cassetteId != "I_COLLECTION_JAKOB")
                    return;

                if (LaikaMod.SessionState != null && LaikaMod.SessionState.APEnabled)
                {
                    LaikaMod.ArmForcedVanillaPopupPresentation(
                        "LOCATION SENT!",
                        "Cassette Tape: Jakob's Music Collection",
                        cassetteId
                    );
                }

                var manager = Singleton<CassettesManager>.Instance;
                var loader = Singleton<CassettesDataLoader>.Instance;

                if (manager == null || loader == null || manager.CassettesInventory == null)
                {
                    LaikaMod.LogWarning("Jakob collection cassette pre-remove skipped because cassette manager/loader/inventory was null.");
                    return;
                }

                foreach (string childCassetteId in LaikaMod.JakobMusicCollectionCassetteIds)
                {
                    CassetteData childCassette = loader.FindCassette(childCassetteId);

                    if (childCassette == null)
                    {
                        LaikaMod.LogWarning($"Jakob collection cassette pre-remove could not find cassette {childCassetteId}.");
                        continue;
                    }

                    if (!manager.HasCassette(childCassette))
                        continue;

                    bool removed = manager.CassettesInventory.Remove(childCassette);

                    if (removed)
                    {
                        LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Add(childCassetteId);

                        LaikaMod.LogInfo(
                            $"Jakob collection: temporarily removed already-owned AP cassette {childCassetteId} so vanilla collection add can complete."
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassettesManager_AddCassetteById_JakobCollectionPatch.Prefix exception:\n{ex}");
            }
        }

        static void Postfix(string cassetteId, Action finishedCallback, bool silent, bool __result)
        {
            try
            {
                if (cassetteId != "I_COLLECTION_JAKOB")
                    return;

                if (__result)
                    return;

                // Fallback safety: if vanilla collection still failed after temporary removals,
                // restore anything we removed so the player does not lose AP-owned cassettes.
                var manager = Singleton<CassettesManager>.Instance;
                var loader = Singleton<CassettesDataLoader>.Instance;

                if (manager == null || loader == null)
                    return;

                foreach (string childCassetteId in LaikaMod.JakobMusicCollectionCassetteIds)
                {
                    if (!LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Remove(childCassetteId))
                        continue;

                    CassetteData childCassette = loader.FindCassette(childCassetteId);

                    if (childCassette == null || manager.HasCassette(childCassette))
                        continue;

                    bool previousGrantingState = LaikaMod.IsGrantingAPItem;
                    LaikaMod.IsGrantingAPItem = true;

                    try
                    {
                        manager.AddCassetteToInventory(childCassetteId, null, true);
                    }
                    finally
                    {
                        LaikaMod.IsGrantingAPItem = previousGrantingState;
                    }

                    LaikaMod.LogWarning(
                        $"Jakob collection: restored {childCassetteId} because vanilla collection add returned false."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CassettesManager_AddCassetteById_JakobCollectionPatch.Postfix exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(CassetteData), typeof(bool) })]
    public class CassetteInventoryRealSourcePatch
    {
        static void Postfix(CassetteData cassette, bool silent, bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (cassette == null)
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassette was null.");
                    return;
                }

                string cassetteId = cassette.id;

                if (string.IsNullOrEmpty(cassetteId))
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassetteId was null or empty.");
                    return;
                }

                if (LaikaMod.IsGrantingAPItem)
                {
                    LaikaMod.SuppressedCassetteChecks.Remove(cassetteId);
                    LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: ignored AP-granted cassette {cassetteId}.");
                    return;
                }

                if (LaikaMod.SuppressedCassetteChecks.Remove(cassetteId))
                {
                    LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: suppressed cassette check for AP-granted cassette {cassetteId}.");
                    return;
                }

                if (LaikaMod.TemporarilyRemovedCassettesForVanillaCollectionReAdd.Remove(cassetteId))
                {
                    LaikaMod.LogInfo(
                        $"CASSETTE INVENTORY SOURCE DETECTED FOR AP-OWNED JAKOB COLLECTION TAPE: id={cassetteId}, silent={silent}, result={__result}"
                    );

                    APLocationDefinition keptDefinition;
                    if (LaikaMod.TryGetLocationDefinition(cassetteId, out keptDefinition))
                    {
                        LaikaMod.TrySendLocationCheck(
                            keptDefinition,
                            "CassetteInventoryRealSourcePatch/JakobCollectionAlreadyOwned",
                            false
                        );
                    }
                    else
                    {
                        LaikaMod.LogWarning(
                            $"CassetteInventoryRealSourcePatch: no AP location definition found for kept Jakob collection cassette {cassetteId}."
                        );
                    }

                    LaikaMod.LogInfo(
                        $"CassetteInventoryRealSourcePatch: kept {cassetteId} after Jakob collection re-add because it was already owned from AP."
                    );

                    try
                    {
                        MonoSingleton<PersistenceManager>.Instance.SaveGame();
                        LaikaMod.LogInfo($"CassetteInventoryRealSourcePatch: forced save after keeping Jakob collection AP cassette {cassetteId}.");
                    }
                    catch (Exception ex)
                    {
                        LaikaMod.LogWarning($"CassetteInventoryRealSourcePatch: save failed after keeping Jakob collection AP cassette {cassetteId}:\n{ex}");
                    }

                    return;
                }

                if (LaikaMod.ConsumeArmedCassetteLocationCheck(cassetteId, "CassetteInventoryRealSourcePatch"))
                {
                    LaikaMod.LogInfo(
                        $"CASSETTE INVENTORY SOURCE DETECTED FROM ARMED SOURCE: id={cassetteId}, silent={silent}, result={__result}"
                    );

                    LaikaMod.TryHandleCassetteLocationCheck(
                        cassetteId,
                        "CassetteInventoryRealSourcePatch/ArmedSource"
                    );

                    return;
                }

                if (silent && LaikaMod.IsJakobCollectionCassetteId(cassetteId))
                {
                    LaikaMod.LogInfo(
                        $"CASSETTE INVENTORY SOURCE DETECTED FROM JAKOB COLLECTION: id={cassetteId}, silent={silent}, result={__result}"
                    );

                    LaikaMod.TryHandleCassetteLocationCheck(
                        cassetteId,
                        "CassetteInventoryRealSourcePatch/JakobCollection"
                    );

                    return;
                }

                if (silent)
                {
                    LaikaMod.LogInfo(
                        $"CassetteInventoryRealSourcePatch: ignored silent non-Jakob cassette add {cassetteId}; not trusted as a location source."
                    );

                    return;
                }

                if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                {
                    LaikaMod.LogInfo(
                        $"CassetteInventoryRealSourcePatch: ignored unarmed already-AP-owned cassette add {cassetteId}; waiting for explicit shop/boombox/source-specific handler."
                    );

                    return;
                }

                // Quest-reward cassettes are granted by vanilla during camp/night quest wrap-up scenes.
                // They are NOT safe to treat as generic physical pickups.
                // Their AP locations should only be sent from TrySendQuestRewardCassetteLocationForCompletedQuest.
                if (LaikaMod.IsQuestRewardCassetteId(cassetteId))
                {
                    LaikaMod.LogInfo(
                        $"CassetteInventoryRealSourcePatch: ignored unarmed quest-reward cassette add {cassetteId}; " +
                        "quest-reward cassette locations are only sent from completed quest hooks."
                    );

                    return;
                }

                // Normal non-silent cassette pickup.
                // This covers physical cassette pickups like Heartglaze Hope when the player does NOT already own
                // the AP cassette. These should send the AP location and then remove the vanilla cassette reward.
                LaikaMod.LogInfo(
                    $"CASSETTE INVENTORY SOURCE DETECTED FROM NORMAL PICKUP: id={cassetteId}, silent={silent}, result={__result}"
                );

                LaikaMod.TryHandleCassetteLocationCheck(
                    cassetteId,
                    "CassetteInventoryRealSourcePatch/NormalPickup"
                );

                return;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"CassetteInventoryRealSourcePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Laika.UI.InGame.Shop.ShopScreen), "OnBuySucceded")]
    public class ShopScreen_OnBuySucceded_APLocationPatch
    {
        static void Prefix(ItemData itemData, int amount)
        {
            try
            {
                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (itemData == null)
                    return;

                LaikaMod.ActiveShopPurchaseItemId = itemData.id;

                LaikaMod.LogInfo(
                    $"SHOP PURCHASE CONTEXT: armed for {itemData.id}."
                );

                if (itemData is CassetteData)
                {
                    string cassetteId = itemData.id;

                    if (string.IsNullOrEmpty(cassetteId))
                        return;

                    APLocationDefinition definition;
                    if (!LaikaMod.TryGetLocationDefinition(cassetteId, out definition))
                        return;

                    if (definition.Category != "Cassette")
                        return;

                    // If the player already owns the cassette from AP, vanilla may not truly add it again.
                    // Send the location at purchase time so shop cassettes like Overthinker still work.
                    if (LaikaMod.HasReceivedAPItem(ItemKind.Collectible, cassetteId))
                    {
                        LaikaMod.LogInfo(
                            $"SHOP CASSETTE PURCHASE DETECTED FOR AP-OWNED CASSETTE: id={cassetteId}, location={definition.DisplayName}"
                        );

                        LaikaMod.TrySendLocationCheck(
                            definition,
                            "ShopScreen_OnBuySucceded_APLocationPatch/APOwnedCassettePurchase",
                            false
                        );

                        return;
                    }

                    LaikaMod.ArmCassetteLocationCheck(
                        cassetteId,
                        "ShopScreen_OnBuySucceded_APLocationPatch/CassettePurchase"
                    );

                    return;
                }

                APLocationDefinition itemDefinition;
                if (!LaikaMod.TryGetLocationDefinition(itemData.id, out itemDefinition))
                    return;

                // Same idea for AP-owned shop key items. If AddItem refuses because the player already owns it,
                // the generic AddItem patch may never see a successful add.
                if (
                    itemDefinition.Category == "KeyItem" &&
                    LaikaMod.HasReceivedAPItem(ItemKind.KeyItem, itemData.id)
                )
                {
                    LaikaMod.LogInfo(
                        $"SHOP KEY ITEM PURCHASE DETECTED FOR AP-OWNED ITEM: id={itemData.id}, location={itemDefinition.DisplayName}"
                    );

                    LaikaMod.TrySendLocationCheck(
                        itemDefinition,
                        "ShopScreen_OnBuySucceded_APLocationPatch/APOwnedKeyItemPurchase",
                        false
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"ShopScreen_OnBuySucceded_APLocationPatch exception:\n{ex}");
            }
        }

        static void Postfix(Laika.UI.InGame.Shop.ShopScreen __instance)
        {
            try
            {
                if (LaikaMod.SessionState != null &&
                    LaikaMod.SessionState.APEnabled &&
                    __instance != null)
                {
                    ShopController shop =
                        AccessTools.Field(typeof(ShopScreen), "shop")?.GetValue(__instance)
                            as ShopController;

                    if (shop != null)
                        BeginAPShopPresentation(__instance, shop);
                }

                if (!string.IsNullOrEmpty(LaikaMod.ActiveShopPurchaseItemId))
                {
                    LaikaMod.LogInfo(
                        $"SHOP PURCHASE CONTEXT: cleared for {LaikaMod.ActiveShopPurchaseItemId}."
                    );
                }

                LaikaMod.ActiveShopPurchaseItemId = null;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"ShopScreen_OnBuySucceded_APLocationPatch.Postfix exception:\n{ex}"
                );

                LaikaMod.ActiveShopPurchaseItemId = null;
            }
        }

        static Exception Finalizer(Exception __exception)
        {
            LaikaMod.ActiveShopPurchaseItemId = null;
            return __exception;
        }
    }

    internal static bool IsJakobMusicCollectionCassette(string cassetteId)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return false;

        foreach (string id in JakobMusicCollectionCassetteIds)
        {
            if (cassetteId == id)
                return true;
        }

        return false;
    }
}
