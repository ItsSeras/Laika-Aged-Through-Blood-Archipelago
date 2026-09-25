using HarmonyLib;
using Laika.Economy.Shops;
using Laika.Inventory;
using Laika.Quests;
using Laika.Quests.PlayMaker.FsmActions;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class LaikaMod
{
    // Quest and quest-goal Harmony patches.
    // These bridge vanilla quest completion, AP checks, and AP-specific quest reconciliation.
    [HarmonyPatch(typeof(QuestLog), "TryCloseQuest")]
    public class QuestClosePatch
    {
        static void Postfix(string questId, bool silent, bool __result)
        {
            // Only log successful full quest completions.
            if (!__result)
                return;

            LaikaMod.LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");

            LaikaMod.TrySendQuestRewardCassetteLocationForCompletedQuest(
                questId,
                "QuestClosePatch"
            );

            LaikaMod.TrySendFetchItemLocationsForCompletedQuest(
                questId,
                "QuestClosePatch"
            );

            if (questId == "Q_D_S_Flower")
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("QuestClosePatch");
            }

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(questId, out locationDefinition))
            {
                LaikaMod.LogWarning($"QuestClosePatch: no AP location definition found for questId={questId}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "QuestClosePatch");
        }
    }

    [HarmonyPatch(typeof(QuestLog), "TryCompleteQuestGoal")]
    public class QuestLog_TutorialHookDebrisOrderPatch
    {
        static void Prefix(
            QuestLog __instance,
            string questId,
            string goalId)
        {
            try
            {
                if (questId != "Q_D_S_TutorialHook" ||
                    goalId != "ExplodeDebris" ||
                    LaikaMod.IsReconcilingTutorialHookBomb ||
                    LaikaMod.SessionState == null ||
                    !LaikaMod.SessionState.APEnabled)
                {
                    return;
                }

                if (!LaikaMod.HasAPHookUnlocked())
                    return;

                QuestInstance quest =
                    LaikaMod.FindActiveQuest(questId);

                if (quest == null || quest.goals == null)
                    return;

                if (!quest.goals.Exists(
                    goal => goal != null &&
                            goal.GoalId == "ExplodeDebris"))
                {
                    return;
                }

                if (!LaikaMod.SessionState.TutorialHookDebrisEventObserved)
                {
                    LaikaMod.SessionState.TutorialHookDebrisEventObserved = true;
                    LaikaMod.SaveSessionState();

                    LaikaMod.LogInfo(
                        "HOOK BOMB RECOVERY: recorded vanilla " +
                        "ExplodeDebris completion request."
                    );
                }

                // Repair the prerequisite before vanilla evaluates the
                // original ExplodeDebris request and chooses its FSM branch.
                LaikaMod.TryReconcileTutorialHookBombProgress(
                    __instance,
                    "QuestLog.TryCompleteQuestGoal/ExplodeDebris",
                    false
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    "QuestLog_TutorialHookDebrisOrderPatch failed:\n" + ex
                );
            }
        }
    }

    [HarmonyPatch(typeof(QuestLog), "TryCompleteQuestGoal")]
    public class QuestGoalCompleteReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("QuestGoalCompleteReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("QuestGoalCompleteReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"QuestGoalCompleteReconcilePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(QuestLog), "TryCompleteQuestGoal")]
    public class QuestLog_TryCompleteQuestGoal_APFollowupPatch
    {
        static void Postfix(string questId, string goalId, bool __result)
        {
            try
            {
                LaikaMod.LogInfo(
                    $"QUEST GOAL COMPLETE EVENT: questId={questId}, goalId={goalId}, result={__result}"
                );

                if (!__result)
                    return;

                // Heartglaze cleanup.
                if (questId == "Q_D_S_Flower")
                {
                    LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                        $"QuestLog.TryCompleteQuestGoal/{goalId}"
                    );
                }

                // Radio Silence:
                // AP may have supplied the two harpoon pieces before vanilla
                // spawned their physical pickups. Vanilla then consumes those
                // AP-provided items while completing FixHarpoon, meaning the
                // original pickup locations can become permanently unavailable.
                //
                // A successful FixHarpoon goal proves the items were accepted,
                // so claim their AP locations if they came from Archipelago.
                if (questId == "Q_D_2_Lighthouse" &&
                    goalId == "FixHarpoon")
                {
                    const string sourceTag =
                        "QuestLog.TryCompleteQuestGoal/FixHarpoon";

                    LaikaMod.TrySendFetchItemLocationIfAPTurnedIn(
                        questId,
                        "I_HARPOON_PIECE_1",
                        ItemKind.KeyItem,
                        sourceTag
                    );

                    LaikaMod.TrySendFetchItemLocationIfAPTurnedIn(
                        questId,
                        "I_HARPOON_PIECE_2",
                        ItemKind.KeyItem,
                        sourceTag
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError(
                    "QuestLog_TryCompleteQuestGoal_APFollowupPatch exception:\n" +
                    ex
                );
            }
        }
    }

    [HarmonyPatch(typeof(Boss_02_PickFlower), "Enter")]
    public class Boss02PickFlowerLocationPatch
    {
        static void Postfix()
        {
            APLocationDefinition definition;
            if (!LaikaMod.TryGetLocationDefinition("I_PUPPY_FLOWER", out definition))
                return;

            LaikaMod.TrySendLocationCheck(definition, "Boss02PickFlowerLocationPatch");
        }
    }

    internal static void TryCleanupHeartglazeAfterQuestUpdate(string sourceTag)
    {
        if (!WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
            return;

        if (HeartglazeFlowerCleanupDone)
            return;

        try
        {
            if (!WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
            {
                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because cleanup is not armed.");
                return;
            }

            if (SessionState != null && SessionState.HeartglazeFlowerReceivedFromAP)
            {
                HeartglazeFlowerCleanupDone = true;
                WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because Heartglaze Flower was already received from AP.");
                return;
            }

            bool removed = TryRemoveInventoryReward(
                "I_PUPPY_FLOWER",
                1,
                sourceTag + "/HeartglazeQuestUpdateCleanup"
            );

            if (HeartglazeFlowerCleanupDone)
            {
                LogInfo($"{sourceTag}: Heartglaze cleanup skipped because cleanup is already done.");
                return;
            }

            if (removed)
            {
                HeartglazeFlowerCleanupDone = true;
                WaitingToRemoveHeartglazeFlowerAfterQuestUpdate = false;

                LogInfo($"{sourceTag}: removed Heartglaze Flower after quest update.");
            }
            else
            {
                LogWarning($"{sourceTag}: tried to remove Heartglaze Flower after quest update, but removal returned false.");
            }
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: Heartglaze quest-update cleanup failed:\n{ex}");
        }
    }

    [HarmonyPatch(typeof(Laika.Quests.PlayMaker.FsmActions.TryCompleteQuestGoal), "OnEnter")]
    public class HeartglazeFlowerQuestGoalCleanupPatch
    {
        static void Postfix(Laika.Quests.PlayMaker.FsmActions.TryCompleteQuestGoal __instance)
        {
            try
            {
                if (!LaikaMod.WaitingToRemoveHeartglazeFlowerAfterQuestUpdate)
                    return;

                if (LaikaMod.HeartglazeFlowerCleanupDone)
                    return;

                string questId = "";
                string goalId = "";

                try
                {
                    if (__instance.questId != null)
                        questId = __instance.questId.Value;

                    if (__instance.goalId != null)
                        goalId = __instance.goalId.Value;
                }
                catch
                {
                }

                LaikaMod.LogInfo(
                    $"HEARTGLAZE QUEST GOAL EVENT: questId={questId}, goalId={goalId}"
                );

                if (questId != "Q_D_S_Flower")
                    return;

                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                    $"HeartglazeFlowerQuestGoalCleanupPatch/{goalId}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"HeartglazeFlowerQuestGoalCleanupPatch exception:\n{ex}");
            }
        }
    }



    internal static void StartHeartglazeQuestAwareCleanup(string sourceTag)
    {
        EnsureCoroutineRunner();

        if (CoroutineRunner == null)
        {
            LogWarning($"{sourceTag}: cannot start Heartglaze quest-aware cleanup because CoroutineRunner is null.");
            return;
        }

        CoroutineRunner.StartCoroutine(HeartglazeQuestAwareCleanupCoroutine(sourceTag));
    }

    private static IEnumerator HeartglazeQuestAwareCleanupCoroutine(string sourceTag)
    {
        // Let vanilla popup + quest goal transition finish.
        yield return new WaitForSecondsRealtime(1.0f);

        LogQuestGoals("Q_D_S_Flower", sourceTag + "/BeforeHeartglazeCleanup");

        TryCleanupHeartglazeAfterQuestUpdate(sourceTag + "/QuestAwareCleanup");

        yield return new WaitForSecondsRealtime(1.0f);

        if (WaitingToRemoveHeartglazeFlowerAfterQuestUpdate && !HeartglazeFlowerCleanupDone)
        {
            LogWarning($"{sourceTag}: Heartglaze still present after first cleanup attempt, retrying.");
            LogQuestGoals("Q_D_S_Flower", sourceTag + "/RetryHeartglazeCleanup");
            TryCleanupHeartglazeAfterQuestUpdate(sourceTag + "/QuestAwareCleanupRetry");
        }
    }

    [HarmonyPatch(typeof(DialogueFSMLauncher), "Interact")]
    public class DialogueFSMLauncher_TutorialHookGuardPatch
    {
        static bool Prefix(DialogueFSMLauncher __instance)
        {
            try
            {
                if (__instance == null ||
                    LaikaMod.SessionState == null ||
                    !LaikaMod.SessionState.APEnabled)
                {
                    return true;
                }

                // Temporary diagnostic. This does not block or advance dialogue.
                if (LaikaMod.HasAPHookUnlocked() &&
                    (__instance.gameObject.scene.name == "Tutorial_Hook" ||
                     __instance.gameObject.scene.name == "Wasteland_01_04"))
                {
                    QuestInstance diagnosticQuest =
                        LaikaMod.FindActiveQuest("Q_D_S_TutorialHook");

                    var diagnosticGoal = diagnosticQuest != null
                        ? diagnosticQuest.GetCurrentGoal()
                        : null;

                    string objectPath = __instance.gameObject.name;

                    for (Transform parent = __instance.transform.parent;
                         parent != null;
                         parent = parent.parent)
                    {
                        objectPath = parent.name + "/" + objectPath;
                    }

                    LaikaMod.LogInfo(
                        "HECTIST LAUNCHER TRACE: " +
                        "scene=" + __instance.gameObject.scene.name +
                        ", path=" + objectPath +
                        ", position=" + __instance.transform.position +
                        ", target=" +
                        (__instance.target != null
                            ? __instance.target.gameObject.name
                            : "<global/default>") +
                        ", text=" +
                        (__instance.textToLaunch != null
                            ? __instance.textToLaunch.name
                            : "<none>") +
                        ", template=" +
                        (__instance.fsmTemplate != null
                            ? __instance.fsmTemplate.name
                            : "<base/default>") +
                        ", currentGoal=" +
                        (diagnosticGoal != null
                            ? diagnosticGoal.GoalId
                            : "<none>")
                    );
                }

                if (__instance.gameObject.scene.name != "Tutorial_Hook" ||
                    __instance.gameObject.name != "Q_D_S_HookTutorial" ||
                    __instance.target == null ||
                    __instance.target.gameObject.name != "FSM")
                {
                    return true;
                }

                if (!LaikaMod.HasAPHookUnlocked())
                    return true;

                LaikaMod.TryReconcileTutorialHookBombProgress(
                    Singleton<QuestLog>.Instance,
                    "Hectist.BeforeInteract"
                );

                QuestInstance quest =
                    LaikaMod.FindActiveQuest("Q_D_S_TutorialHook");

                if (quest == null)
                    return true;

                var currentGoal = quest.GetCurrentGoal();

                if (currentGoal == null ||
                    currentGoal.GoalId != "BombBlock")
                {
                    return true;
                }

                LaikaMod.LogWarning(
                    "HECTIST GUARD: BombBlock is still incomplete after " +
                    "recovery. Blocking the known stale dialogue. " +
                    "DebrisEventObserved=" +
                    LaikaMod.SessionState.TutorialHookDebrisEventObserved
                );

                return false;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    "DialogueFSMLauncher TutorialHook guard failed:\n" + ex
                );

                return true;
            }
        }
    }

    // Removes AP quest/key items from a vendor after vanilla has already consumed
    // that item for quest progression.
    //
    // Do NOT call ShopController.RemoveFromStock() here.
    // Vanilla's implementation also charges the player money, which would make a
    // quest turn-in behave like a purchase.
    internal static void RemoveConsumedAPKeyItemsFromShopStock(
        Laika.Economy.Shops.ShopController shop,
        string sourceTag)
    {
        if (SessionState == null ||
            !SessionState.APEnabled ||
            shop == null ||
            shop.ItemStock == null)
        {
            return;
        }

        List<ItemData> items =
            new List<ItemData>(shop.ItemStock.Keys);

        foreach (ItemData item in items)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
                continue;

            // The book uses its dialogue completion as proof of reaching the location.
            // Shop cleanup must not bypass that requirement.
            if (item.id == "I_DICTIONARY")
            {
                TryRecoverMagicalBookLocation(
                    sourceTag + "/BookShopRecovery"
                );

                APLocationDefinition bookLocation;
                if (!TryGetLocationDefinition(item.id, out bookLocation) ||
                    !HasLocationBeenSent(bookLocation.LocationId))
                {
                    continue;
                }
            }

            if (!WasVanillaConsumedAPItem(ItemKind.KeyItem, item.id))
                continue;

            APLocationDefinition definition;
            if (!TryGetLocationDefinition(item.id, out definition) ||
                definition.Category != "KeyItem")
            {
                continue;
            }

            // Check even when stock is already zero, so earlier cleanup
            // can be recovered when this shop loads again.
            if (!HasLocationBeenSent(definition.LocationId))
            {
                TrySendLocationCheck(
                    definition,
                    sourceTag + "/ConsumedShopItemRecovery",
                    false
                );
            }

            // Do not remove remaining stock if sending failed or AP is offline.
            if (!HasLocationBeenSent(definition.LocationId))
            {
                LogWarning(
                    $"{sourceTag}: postponing consumed-item shop cleanup " +
                    $"until its location can be sent -> {item.id}"
                );
                continue;
            }

            if (shop.ItemStock[item] <= 0)
                continue;

            // Do not call RemoveFromStock: vanilla also charges money there.
            shop.ItemStock[item] = 0;

            LogInfo(
                $"{sourceTag}: removed consumed AP key item from shop " +
                $"after its location was recorded -> {item.id}, shop={shop.Id}"
            );
        }
    }

    [HarmonyPatch(
        typeof(Laika.Persistence.ProgressionData),
        "SetAchievement",
        new Type[] { typeof(string), typeof(bool), typeof(bool) }
    )]
    public static class MagicalBook_DialogueCompleted_APLocationPatch
    {
        static void Postfix(string name, bool value, bool reset)
        {
            if (name != "D_A_Dictionary_Bought" || !value || reset)
                return;

            // Read the resulting flag through the recovery helper.
            // Do not assume the requested value was actually applied.
            LaikaMod.TryRecoverMagicalBookLocation(
                "MagicalBookDialogueCompleted"
            );
        }
    }

    [HarmonyPatch(typeof(ShopController), "LoadData")]
    public class ShopController_LoadData_APConsumedKeyItemCleanupPatch
    {
        static void Postfix(ShopController __instance)
        {
            try
            {
                LaikaMod.RemoveConsumedAPKeyItemsFromShopStock(
                    __instance,
                    "ShopController.LoadData"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    "ShopController.LoadData AP cleanup failed:\n" +
                    ex
                );
            }
        }
    }



    // Some AP softlocks happen right when a quest gets added or advanced.
    // I re-run the quest softlock reconciliation here so the fix can happen immediately
    // instead of waiting for a later zone load or queue wake-up.
    [HarmonyPatch(typeof(TryAddQuest), "OnEnter")]
    public class TryAddQuestReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("TryAddQuestReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryAddQuestReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryAddQuestReconcilePatch exception:\n{ex}");
            }
        }
    }

    // Some vanilla flows complete one goal and immediately move to the next.
    // If AP already gave the required item, that newly current goal can softlock on the spot.
    // Running the reconciliation here makes the fix happen much earlier.
    [HarmonyPatch(typeof(TryCompleteQuestGoal), "OnEnter")]
    public class TryCompleteQuestGoalReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryCleanupHeartglazeAfterQuestUpdate("TryCompleteQuestGoalReconcilePatch");
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryCompleteQuestGoalReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryCompleteQuestGoalReconcilePatch exception:\n{ex}");
            }
        }
    }
}
