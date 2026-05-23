using HarmonyLib;
using Laika.Cassettes;
using Laika.Inventory;
using Laika.Persistence;
using System;
using System.Reflection;

public partial class LaikaMod
{
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
