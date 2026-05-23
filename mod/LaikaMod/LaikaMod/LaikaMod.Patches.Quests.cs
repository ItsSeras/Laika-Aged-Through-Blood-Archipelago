using HarmonyLib;
using Laika.Quests;
using Laika.Quests.PlayMaker.FsmActions;
using System;
using System.Collections;
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
    public class QuestLog_TryCompleteQuestGoal_HeartglazeCleanupPatch
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

                if (questId == "Q_D_S_Flower")
                {
                    LaikaMod.TryCleanupHeartglazeAfterQuestUpdate(
                        $"QuestLog.TryCompleteQuestGoal/{goalId}"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"QuestLog_TryCompleteQuestGoal_HeartglazeCleanupPatch exception:\n{ex}");
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
