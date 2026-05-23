using System.Reflection;
using HarmonyLib;
using Laika.Inventory;
using System;
using UnityEngine;

public partial class LaikaMod
{
    // Inventory and key-item Harmony patches.
    // These turn real item pickups into AP checks and suppress vanilla rewards when needed.
    [HarmonyPatch(typeof(InventoryManager), "RemoveItem", new Type[] { typeof(ItemData), typeof(int), typeof(bool) })]
    public class InventoryManager_RemoveItem_VanillaConsumeAPItemPatch
    {
        static void Postfix(ItemData item, int amount, bool silent, bool __result)
        {
            try
            {
                if (!__result || item == null || string.IsNullOrEmpty(item.id))
                    return;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                if (LaikaMod.SuppressVanillaConsumeTracking)
                    return;

                ItemKind kind;
                if (!LaikaMod.TryGetItemKindForInventoryLocation(item.id, out kind))
                    return;

                if (!LaikaMod.HasReceivedAPItem(kind, item.id))
                    return;

                LaikaMod.RememberVanillaConsumedAPItem(kind, item.id);

                LaikaMod.LogInfo(
                    $"VANILLA CONSUMED AP ITEM: id={item.id}, kind={kind}, amount={amount}, silent={silent}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"InventoryManager_RemoveItem_VanillaConsumeAPItemPatch exception:\n{ex}");
            }
        }
    }

    internal static readonly string[] JakobMusicCollectionCassetteIds =
{
    "I_CASSETTE_1", // Bloody Sunset
    "I_CASSETTE_2", // Playing in the Sun
    "I_CASSETTE_3", // Lullaby of the Dead
    "I_CASSETTE_4", // Blue Limbo
    "I_CASSETTE_5", // The Whisper
};

    private static readonly FieldInfo DoorInteractionSceneToLoadField =
        typeof(DoorInteraction).GetField(
            "sceneToLoad",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    [HarmonyPatch(typeof(InventoryManager), "AddItem", new Type[] { typeof(ItemData), typeof(int), typeof(Action), typeof(bool) })]
    public class InventoryManager_AddItem_APLocationPatch
    {
        static bool Prefix(ItemData item, int amount, Action onAddedCallback, bool silent, ref bool __result)
        {
            try
            {
                if (item == null || string.IsNullOrEmpty(item.id))
                    return true;

                string sourceTag = "InventoryManager_AddItem_APLocationPatch";

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (LaikaMod.IsGrantingAPItem)
                {
                    LaikaMod.LogInfo($"InventoryManager_AddItem_APLocationPatch: allowed AP-granted item {item.id}.");
                    return true;
                }

                string itemId = item.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return true;


                if (itemId == "I_HOOK_BODY" &&
                    LaikaMod.SessionState != null &&
                    LaikaMod.SessionState.APEnabled &&
                    !LaikaMod.HasAPHookUnlocked())
                {
                    LaikaMod.LogWarning(
                        $"{sourceTag}: blocked early I_HOOK_BODY vanilla reward because Hook is not unlocked from AP yet."
                    );

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/EarlyHookBodyBlocked"
                    );

                    LaikaMod.AnnounceAPWarning(
                        "[AP] Hook Body check sent, but vanilla Hook unlock was blocked because Hook has not been received from AP."
                    );

                    __result = true;
                    return false;
                }

                // Key items often start quests or advance dialogue.
                // Do not block them here. Let AddKeyItem run, then handle AP check/removal there.
                if (definition.Category == "KeyItem")
                {
                    LaikaMod.LogInfo(
                        $"{sourceTag}: key item {itemId} allowed through AddItem so vanilla quest logic can run."
                    );

                    return true;
                }

                // Puppy Gifts are also dialogue/quest-sensitive.
                // Stargazer/Dreamcatcher specifically needs the real vanilla AddItem/AddKeyItem flow
                // so the original popup/callback/dialogue can complete.
                if (definition.Category == "PuppyGift")
                {
                    var inventory = Singleton<InventoryManager>.Instance;

                    if (inventory != null && inventory.HasItem(itemId, 1))
                    {
                        bool removed = LaikaMod.TryRemoveInventoryReward(
                            itemId,
                            amount > 0 ? amount : 1,
                            $"{sourceTag}/PreRemoveAlreadyOwnedPuppyGift"
                        );

                        if (removed)
                        {
                            LaikaMod.TemporarilyRemovedForVanillaReAdd.Add(itemId);

                            LaikaMod.LogInfo(
                                $"{sourceTag}: temporarily removed already-owned puppy gift {itemId} " +
                                "so vanilla AddItem can succeed and avoid dialogue softlock."
                            );
                        }
                    }

                    LaikaMod.LogInfo(
                        $"{sourceTag}: puppy gift {itemId} allowed through AddItem so vanilla dialogue/reward flow can run."
                    );

                    return true;
                }

                if (!LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                    return true;

                LaikaMod.LogInfo(
                    $"INVENTORY LOCATION SOURCE BLOCKED: id={itemId}, category={definition.Category}, amount={amount}, silent={silent}"
                );

                LaikaMod.TrySendLocationCheck(definition, "InventoryManager_AddItem_APLocationPatch");

                // Pretend vanilla AddItem succeeded so quests/dialogue/shop flow continues,
                // but do not actually add the original item.
                __result = true;
                return false;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_APLocationPatch exception:\n{ex}");
                return true;
            }
        }

        static void Postfix(ItemData item, int amount, Action onAddedCallback, bool silent, bool __result)
        {
            try
            {
                if (item == null || string.IsNullOrEmpty(item.id))
                    return;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                string itemId = item.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                if (definition.Category != "KeyItem" &&
                    definition.Category != "Material" &&
                    definition.Category != "PuppyGift")
                {
                    return;
                }

                if (itemId == "I_PUPPY_FLOWER")
                    return;

                if (!__result)
                {
                    LaikaMod.LogInfo(
                        $"ADD ITEM LOCATION FALLBACK DETECTED AFTER VANILLA ADD FAILED/ALREADY OWNED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                    );

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PostfixAlreadyOwnedFallback"
                    );

                    if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                    {
                        var inventory = Singleton<InventoryManager>.Instance;

                        if (inventory != null && !inventory.HasItem(itemId, 1))
                        {
                            bool previousGrantingState = LaikaMod.IsGrantingAPItem;
                            LaikaMod.IsGrantingAPItem = true;

                            try
                            {
                                inventory.AddItem(item, amount > 0 ? amount : 1, null, true);
                            }
                            finally
                            {
                                LaikaMod.IsGrantingAPItem = previousGrantingState;
                            }

                            LaikaMod.LogWarning(
                                $"InventoryManager_AddItem_APLocationPatch: restored {itemId} after vanilla AddItem still failed following temporary removal."
                            );
                        }
                    }

                    return;
                }

                if (LaikaMod.IsHarpoonPieceId(itemId))
                {
                    LaikaMod.LogInfo(
                        $"HARPOON LOCATION FALLBACK DETECTED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                    );

                    if (LaikaMod.WasHarpoonPieceReceivedFromAP(itemId))
                    {
                        LaikaMod.TrySendLocationCheck(
                            definition,
                            "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup/APOwned",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"Physical harpoon pickup {itemId} sent AP check and stayed in inventory because that harpoon piece was already received from AP."
                        );

                        return;
                    }

                    if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                    {
                        LaikaMod.TrySendLocationCheck(
                            definition,
                            "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup/AlreadyOwnedReAdd",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"InventoryManager_AddItem_APLocationPatch: kept harpoon piece {itemId} after vanilla re-add because it was already owned before this vanilla reward."
                        );

                        return;
                    }

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup"
                    );

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PhysicalHarpoonPickup"
                    );

                    LaikaMod.LogInfo(
                        $"Physical harpoon pickup {itemId} sent AP check and scheduled vanilla reward removal because AP has not delivered it yet."
                    );

                    return;
                }

                LaikaMod.LogInfo(
                    $"ADD ITEM LOCATION FALLBACK DETECTED: id={itemId}, location={definition.DisplayName}, category={definition.Category}"
                );

                if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/AlreadyOwnedReAdd",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"InventoryManager_AddItem_APLocationPatch: kept {itemId} after vanilla re-add because it was already owned before this vanilla reward."
                    );

                    return;
                }

                if (definition.Category == "PuppyGift")
                {
                    LaikaMod.TryHandlePuppyGiftLocationCheck(
                        itemId,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/PuppyGift",
                        true
                    );

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback/PuppyGift"
                    );

                    LaikaMod.LogInfo(
                        $"InventoryManager_AddItem_APLocationPatch: puppy gift {itemId} was a vanilla location reward, so it was scheduled for removal after sending the AP check."
                    );

                    return;
                }

                LaikaMod.TrySendLocationCheck(
                    definition,
                    "InventoryManager_AddItem_APLocationPatch/PostfixFallback"
                );

                if (LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                {
                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        amount > 0 ? amount : 1,
                        "InventoryManager_AddItem_APLocationPatch/PostfixFallback"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_APLocationPatch.Postfix exception:\n{ex}");
            }
        }
    }

    internal static bool IsPitKeyId(string itemId)
    {
        return
            itemId == "I_D_Dungeon_01_door_piece_1" ||
            itemId == "I_D_Dungeon_01_door_piece_2" ||
            itemId == "I_D_Dungeon_01_door_piece_3";
    }

    internal static bool HasSentLocationCheck(APLocationDefinition definition)
    {
        if (definition == null)
            return false;

        if (SessionState == null || SessionState.SentLocationIds == null)
            return false;

        return SessionState.SentLocationIds.Contains(definition.LocationId);
    }

    [HarmonyPatch(typeof(ItemInstance), "Start")]
    public class ItemInstance_Start_APOwnedPitKeyVisibilityPatch
    {
        static void Postfix(ItemInstance __instance)
        {
            try
            {
                if (__instance == null || __instance.ItemData == null)
                    return;

                string itemId = __instance.ItemData.id;

                bool isPitKey = LaikaMod.IsPitKeyId(itemId);
                bool isHarpoonPiece = LaikaMod.IsHarpoonPieceId(itemId);

                if (!isPitKey && !isHarpoonPiece)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                // If the location was already checked, vanilla hiding is fine.
                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                bool shouldForceVisible = false;

                if (isPitKey && LaikaMod.HasReceivedAPItem(ItemKind.KeyItem, itemId))
                {
                    shouldForceVisible = true;
                }

                if (isHarpoonPiece && LaikaMod.WasHarpoonPieceReceivedFromAP(itemId))
                {
                    shouldForceVisible = true;
                }

                if (!shouldForceVisible)
                    return;

                if (!__instance.gameObject.activeSelf)
                {
                    __instance.gameObject.SetActive(true);
                }

                if (isPitKey)
                {
                    LaikaMod.LogInfo(
                        $"PIT KEY PICKUP: forced visible for AP-owned unchecked pit key {itemId}."
                    );
                }
                else if (isHarpoonPiece)
                {
                    LaikaMod.LogInfo(
                        $"HARPOON PICKUP: forced visible for AP-owned unchecked harpoon piece {itemId}."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"ItemInstance_Start_APOwnedPitKeyVisibilityPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddKeyItem", new Type[] { typeof(ItemData) })]
    public class KeyItemLocationSourcePatch
    {
        static bool Prefix(ItemData __0, ref bool __result)
        {
            try
            {
                if (__0 == null || string.IsNullOrEmpty(__0.id))
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (LaikaMod.IsGrantingAPItem)
                    return true;

                string itemId = __0.id;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return true;

                if (definition.Category != "PuppyGift")
                    return true;

                // Important:
                // Do not block AddKeyItem for Puppy Gifts anymore.
                // Stargazer/Dreamcatcher needs vanilla AddKeyItem to really run so the original
                // reward popup/callback/dialogue flow can complete.
                LaikaMod.LogInfo(
                    $"KeyItemLocationSourcePatch.Prefix: allowing puppy gift AddKeyItem to run normally -> {itemId}"
                );

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"KeyItemLocationSourcePatch Prefix exception:\n{ex}");
                return true;
            }
        }

        static void Postfix(ItemData __0, bool __result)
        {
            try
            {
                if (!__result || __0 == null || string.IsNullOrEmpty(__0.id))
                    return;

                string itemId = __0.id;

                if (LaikaMod.IsGrantingAPItem)
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(itemId, out definition))
                    return;

                if (!LaikaMod.ShouldSuppressVanillaInventoryReward(definition))
                    return;

                LaikaMod.LogInfo(
                    $"KEY ITEM LOCATION SOURCE DETECTED: id={itemId}, location={definition.DisplayName}"
                );

                if (
                    itemId == "I_TOY_BIKE" ||
                    itemId == "I_GAMEBOY" ||
                    itemId == "I_PLANT_PUPPY" ||
                    itemId == "I_TOY_ANIMAL" ||
                    itemId == "I_BOOK_MOTHER" ||
                    itemId == "I_DREAMCATCHER" ||
                    itemId == "I_UKULELE"
                )
                {
                    bool wasTemporarilyRemovedForVanillaReAdd =
                        LaikaMod.TemporarilyRemovedForVanillaReAdd.Remove(itemId);

                    if (wasTemporarilyRemovedForVanillaReAdd)
                    {
                        LaikaMod.TryHandlePuppyGiftLocationCheck(
                            itemId,
                            "KeyItemLocationSourcePatch/AlreadyOwnedPuppyGiftReAdd",
                            false
                        );

                        LaikaMod.LogInfo(
                            $"KeyItemLocationSourcePatch: kept already-owned puppy gift {itemId} after vanilla re-add."
                        );
                    }
                    else
                    {
                        LaikaMod.TryHandlePuppyGiftLocationCheck(
                            itemId,
                            "KeyItemLocationSourcePatch",
                            true
                        );
                    }

                    return;
                }
                else
                {
                    LaikaMod.TrySendLocationCheck(definition, "KeyItemLocationSourcePatch");
                }

                if (LaikaMod.TemporarilyRemovedForVanillaReAdd.Contains(itemId))
                {
                    LaikaMod.LogInfo(
                        $"KeyItemLocationSourcePatch: not scheduling vanilla reward removal for {itemId} because it was temporarily removed for vanilla re-add."
                    );

                    return;
                }

                if (definition.Category == "KeyItem")
                {
                    if (itemId == "I_PUPPY_FLOWER")
                    {
                        if (LaikaMod.SessionState != null &&
                            LaikaMod.SessionState.HeartglazeFlowerReceivedFromAP)
                        {
                            LaikaMod.HeartglazeFlowerCleanupDone = true;
                            LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                            LaikaMod.LogInfo(
                                "Heartglaze cleanup not armed because Heartglaze Flower was already received from AP."
                            );

                            return;
                        }

                        if (LaikaMod.IsGrantingAPItem)
                        {
                            LaikaMod.LogInfo("Heartglaze cleanup skipped because this flower came from AP grant.");
                            return;
                        }

                        LaikaMod.HeartglazeFlowerCleanupDone = false;
                        LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = true;

                        LaikaMod.LogInfo("Heartglaze cleanup armed. Waiting for A Heart for Poochie quest update before removing flower.");

                        return;
                    }

                    LaikaMod.ScheduleDelayedVanillaRewardRemoval(
                        itemId,
                        1,
                        "KeyItemLocationSourcePatch"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"KeyItemLocationSourcePatch exception:\n{ex}");
            }
        }
    }

    internal static void ScheduleDelayedVanillaRewardRemoval(string itemId, int amount, string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: could not schedule vanilla reward removal for {itemId}; CoroutineRunner is null.");
            return;
        }

        LogInfo($"{sourceTag}: scheduled vanilla reward removal for {itemId} x{amount}.");

        CoroutineRunner.StartCoroutine(
            DelayedVanillaRewardRemovalCoroutine(itemId, amount, sourceTag)
        );
    }

    private static System.Collections.IEnumerator DelayedVanillaRewardRemovalCoroutine(
        string itemId,
        int amount,
        string sourceTag)
    {
        yield return null;
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemovalFrame1");

        yield return new WaitForSecondsRealtime(0.25f);
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemoval025");

        yield return new WaitForSecondsRealtime(0.75f);
        TryRemoveInventoryReward(itemId, amount, sourceTag + "/DelayedRemoval100");
    }

    [HarmonyPatch(typeof(InventoryManager), "AddItem", new Type[] {
    typeof(ItemData),
    typeof(int),
    typeof(Action),
    typeof(bool)
})]
    public class InventoryManager_AddItem_LoggerPatch
    {
        static void Prefix(ItemData item, int amount = 1, Action onAddedCallback = null, bool silent = false)
        {
            try
            {
                if (item == null)
                {
                    LaikaMod.LogInfo("[ITEM LOGGER] InventoryManager.AddItem called with null item.");
                    return;
                }

                LaikaMod.LogInfo(
                    $"[ITEM LOGGER] AddItem | ID: {item.id} | Name: {item.Name} | " +
                    $"Amount: {amount} | KeyItem: {item.IsKeyItem} | Recipe: {item.IsRecipeItem} | Silent: {silent}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"InventoryManager_AddItem_LoggerPatch exception:\n{ex}");
            }
        }
    }
}