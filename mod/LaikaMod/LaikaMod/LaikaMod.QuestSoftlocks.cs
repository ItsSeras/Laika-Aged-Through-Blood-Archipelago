using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using Laika.Quests.Goals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public partial class LaikaMod
{
    // Quest softlock reconciliation.
    // These helpers only repair specific AP timing issues where the player already owns
    // an item before vanilla quest logic reaches the step that expects it.

    // Looks for one active quest by id.
    // I only care about active quests here because these softlocks happen mid-quest.
    internal static QuestInstance FindActiveQuest(string questId)
    {
        QuestLog questLog = Singleton<QuestLog>.Instance;

        if (questLog == null)
            return null;

        List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

        if (activeQuests == null)
            return null;

        return activeQuests.Find(x => x != null && x.QuestId == questId);
    }

    // Some AP softlocks are not item grant failures.
    // They happen because the player already owns the required item before the vanilla quest step becomes current.
    // I reconcile those here by only completing the one blocked goal, not the whole quest.
    internal static void TryReconcileKnownQuestSoftlocks(string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not reconcile quest softlocks because QuestLog is null.");
                return;
            }

            TryReconcileOldWarfareShotgunGoal(questLog, sourceTag);
            TryReconcileTutorialHookGoal(questLog, sourceTag);
            TryReconcileDeferredHarpoonPieces(questLog, sourceTag);
            TryReconcileRadioSilenceDashBypass(questLog, sourceTag);
            TrySkipRadioSilenceBoatIntro(questLog, sourceTag);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while reconciling known quest softlocks:\n{ex}");
        }
    }

    internal static void TryReconcileDeferredHarpoonPieces(QuestLog questLog, string sourceTag)
    {
        try
        {
            if (SessionState == null)
                return;

            if (!IsRadioSilenceReadyForHarpoonPieces())
                return;

            bool grantedAny = false;

            bool piece1FromAP = SessionState.HarpoonPiece1ReceivedFromAP;
            bool piece2FromAP = SessionState.HarpoonPiece2ReceivedFromAP;

            bool piece1InInventory = InventoryHasItemSafe("I_HARPOON_PIECE_1");
            bool piece2InInventory = InventoryHasItemSafe("I_HARPOON_PIECE_2");

            // Do not deliver only one AP harpoon piece into the FixHarpoon step unless the other
            // piece is already available. One-piece delivery can leave Radio Silence in a bad partial state.
            bool canDeliverPiece1 =
                piece1FromAP &&
                (piece2FromAP || piece2InInventory);

            bool canDeliverPiece2 =
                piece2FromAP &&
                (piece1FromAP || piece1InInventory);

            if (!canDeliverPiece1 && !canDeliverPiece2)
            {
                LogInfo(
                    $"{sourceTag}: deferred harpoon pieces held because only one side is available. " +
                    $"piece1FromAP={piece1FromAP}, piece2FromAP={piece2FromAP}, " +
                    $"piece1Inventory={piece1InInventory}, piece2Inventory={piece2InInventory}"
                );

                return;
            }

            if (canDeliverPiece1)
            {
                grantedAny |= TryGrantDeferredHarpoonPieceNow(
                    "I_HARPOON_PIECE_1",
                    "Key Item: Carved Whale Tooth",
                    sourceTag + "/HarpoonPiece1"
                );
            }

            if (canDeliverPiece2)
            {
                grantedAny |= TryGrantDeferredHarpoonPieceNow(
                    "I_HARPOON_PIECE_2",
                    "Key Item: Long Rope",
                    sourceTag + "/HarpoonPiece2"
                );
            }

            if (grantedAny)
            {
                LogInfo($"{sourceTag}: deferred harpoon part delivered for Radio Silence.");

                if (!SessionState.HarpoonPieceDeferredDeliveryNoticeShown)
                {
                    SessionState.HarpoonPieceDeferredDeliveryNoticeShown = true;
                    SaveSessionState();

                    AnnounceAPActivity(
                        OverlayColor("#00E676", "[AP] Granted: ") +
                        OverlayColor("#FFD166", "Deferred harpoon parts delivered for Radio Silence.")
                    );
                }

                LogImportantQuestSnapshots(sourceTag + "/AfterDeferredHarpoonGrant");
            }
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TryReconcileDeferredHarpoonPieces failed:\n{ex}");
        }
    }

    internal static bool InventoryHasItemSafe(string itemId)
    {
        try
        {
            InventoryManager inventory = Singleton<InventoryManager>.Instance;

            if (inventory == null || string.IsNullOrEmpty(itemId))
                return false;

            return inventory.HasItem(itemId, 1);
        }
        catch
        {
            return false;
        }
    }

    internal static bool HasDashUnlockedForRadioSilenceBypass()
    {
        try
        {
            if (HasProgressionFlag("G_DASH_UNLOCKED"))
                return true;

            InventoryManager inventory = Singleton<InventoryManager>.Instance;

            if (inventory != null && inventory.HasItem("I_DASH", 1))
                return true;

            return false;
        }
        catch (Exception ex)
        {
            LogWarning($"HasDashUnlockedForRadioSilenceBypass failed:\n{ex}");
            return false;
        }
    }

    internal static void TryReconcileRadioSilenceDashBypass(QuestLog questLog, string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (questLog == null)
                return;

            QuestInstance quest = FindActiveQuest("Q_D_2_Lighthouse");

            if (quest == null)
                return;

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal == null)
                return;

            if (currentGoal.GoalId != "FixHarpoon")
                return;

            if (!HasDashUnlockedForRadioSilenceBypass())
                return;

            bool hasPiece1 = WasHarpoonPieceReceivedFromAP("I_HARPOON_PIECE_1");
            bool hasPiece2 = WasHarpoonPieceReceivedFromAP("I_HARPOON_PIECE_2");

            if (hasPiece1 && hasPiece2)
                return;

            LogInfo(
                $"{sourceTag}: Radio Silence dash bypass detected. " +
                $"Advancing past FixHarpoon because Dash is unlocked and harpoon pieces are not both available. " +
                $"hasPiece1={hasPiece1}, hasPiece2={hasPiece2}"
            );

            bool fixedHarpoon = questLog.TryCompleteQuestGoal("Q_D_2_Lighthouse", "FixHarpoon");

            LogInfo(
                $"{sourceTag}: Radio Silence dash bypass TryCompleteQuestGoal(FixHarpoon) returned {fixedHarpoon}."
            );

            if (!SessionState.RadioSilenceDashBypassNoticeShown)
            {
                SessionState.RadioSilenceDashBypassNoticeShown = true;
                SaveSessionState();

                AnnounceAPActivity(
                    OverlayColor("#00E676", "[AP] Radio Silence updated: ") +
                    OverlayColor("#FFD166", "Dash route bypassed the harpoon repair step.")
                );
            }

            LogImportantQuestSnapshots(sourceTag + "/AfterRadioSilenceDashBypass");
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TryReconcileRadioSilenceDashBypass failed:\n{ex}");
        }
    }

    // Old Warfare can get stuck if AP gives the player the shotgun early.
    // CraftShotgun is a GatherQuestGoal, so the normal TryCompleteQuestGoal / ForceComplete path
    // does not advance it correctly here. Instead, I re-run the goal's own CheckGoal() logic
    // once the player already owns the shotgun so the quest can move on to ShowShotgun normally.
    internal static void TryReconcileOldWarfareShotgunGoal(QuestLog questLog, string sourceTag)
    {
        QuestInstance quest = FindActiveQuest("Q_D_S_OldWarfare");

        if (quest == null)
            return;

        QuestGoal currentGoal = quest.GetCurrentGoal();

        if (currentGoal == null)
            return;

        WeaponsInventory weaponsInventory = Singleton<WeaponsInventory>.Instance;

        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: could not reconcile Old Warfare because WeaponsInventory is null.");
            return;
        }

        if (currentGoal.GoalId == "GiveRecipe" && weaponsInventory.HasWeapon("I_W_SHOTGUN"))
        {
            LogInfo($"{sourceTag}: Old Warfare is on GiveRecipe but player already has shotgun. Advancing recipe step.");
            questLog.TryCompleteQuestGoal("Q_D_S_OldWarfare", "GiveRecipe");
            return;
        }

        if (currentGoal.GoalId != "CraftShotgun")
            return;

        if (!weaponsInventory.HasWeapon("I_W_SHOTGUN"))
            return;

        LogInfo($"{sourceTag}: reconciling Old Warfare softlock by re-checking CraftShotgun because the player already owns the shotgun.");
        AnnounceAPActivity("[AP] Old Warfare updated because you already had the shotgun.");

        try
        {
            // GatherQuestGoal does not implement ForceComplete(), so I need to call its own
            // internal CheckGoal() path directly. That lets the quest use its normal gather-goal
            // completion logic instead of me faking a quest close or skipping ahead too far.
            MethodInfo checkGoalMethod = currentGoal.GetType().GetMethod(
                "CheckGoal",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (checkGoalMethod == null)
            {
                LogWarning($"{sourceTag}: could not find CheckGoal() on CraftShotgun goal.");
                return;
            }

            checkGoalMethod.Invoke(currentGoal, null);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while re-checking CraftShotgun:\n{ex}");
        }
    }

    // Bonehead's Hook can get stuck if AP gives the player the hook before the quest reaches GetHook.
    // In that case, the quest still waits on the first scripted goal even though the player already has the hook.
    // I only complete GetHook so the rest of the quest can still play out normally.
    internal static void TryReconcileTutorialHookGoal(QuestLog questLog, string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (questLog == null)
                return;

            QuestInstance quest = FindActiveQuest("Q_D_S_TutorialHook");

            if (quest == null)
                return;

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal == null)
                return;

            if (!HasAPHookUnlocked())
            {
                LogInfo(
                    $"{sourceTag}: TutorialHook reconcile skipped. CurrentGoal={currentGoal.GoalId}, AP Hook is not unlocked."
                );

                return;
            }

            LogInfo(
                $"{sourceTag}: TutorialHook reconcile check. CurrentGoal={currentGoal.GoalId}, AP Hook is unlocked."
            );

            if (currentGoal.GoalId == "GetHook")
            {
                LogInfo(
                    $"{sourceTag}: reconciling Bonehead's Hook by completing GetHook because AP Hook is already unlocked."
                );

                bool completedGetHook = questLog.TryCompleteQuestGoal(
                    "Q_D_S_TutorialHook",
                    "GetHook"
                );

                LogInfo(
                    $"{sourceTag}: TryCompleteQuestGoal(Q_D_S_TutorialHook, GetHook) returned {completedGetHook}."
                );

                if (completedGetHook)
                {
                    AnnounceAPActivity(
                        OverlayColor("#00E676", "[AP] Bonehead's Hook updated: ") +
                        OverlayColor("#FFD166", "Hook step completed because you already had Hook.")
                    );

                    LogImportantQuestSnapshots(sourceTag + "/AfterTutorialHookGetHook");
                }

                // Refresh quest/current goal immediately after completing GetHook.
                // This helps avoid Hectist dialogue reading the old goal state during the same interaction chain.
                quest = FindActiveQuest("Q_D_S_TutorialHook");

                if (quest == null)
                    return;

                currentGoal = quest.GetCurrentGoal();

                if (currentGoal == null)
                    return;

                LogInfo(
                    $"{sourceTag}: TutorialHook current goal after GetHook reconcile -> {currentGoal.GoalId}"
                );
            }

            // Do not force-close later goals yet. We need one more log from the broken state first.
            // If Hectist still softlocks, this log tells us the exact goalId to handle next.
            if (currentGoal.GoalId != "GetHook")
            {
                LogInfo(
                    $"{sourceTag}: TutorialHook reconcile finished. CurrentGoal={currentGoal.GoalId}."
                );
            }
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TryReconcileTutorialHookGoal failed:\n{ex}");
        }
    }

    // Quest diagnostic helpers.
    // These log active goal ids and progression state for AP-specific quest reconciliation issues.
    // Keep them out of normal runtime flow unless a quest interaction needs investigation.
    internal static void LogQuestGoals(string questId, string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because QuestLog is null.");
                return;
            }

            List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

            if (activeQuests == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because active quest list was null.");
                return;
            }

            QuestInstance quest = activeQuests.Find(x => x != null && x.QuestId == questId);

            if (quest == null)
            {
                LogInfo($"{sourceTag}: quest {questId} is not active.");
                return;
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT START -> {questId}");

            foreach (QuestGoal goal in quest.goals)
            {
                if (goal == null)
                    continue;

                LogInfo(
                    $"{sourceTag}: " +
                    $"quest={questId}, " +
                    $"goalId={goal.GoalId}, " +
                    $"goalType={goal.GetType().Name}, " +
                    $"completed={goal.Completed}, " +
                    $"current={goal.CurrentAmount}, " +
                    $"required={goal.RequiredAmount}, " +
                    $"description={goal.Description}"
                );
            }

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal != null)
            {
                LogInfo(
                    $"{sourceTag}: CURRENT GOAL -> " +
                    $"quest={questId}, " +
                    $"goalId={currentGoal.GoalId}, " +
                    $"goalType={currentGoal.GetType().Name}, " +
                    $"description={currentGoal.Description}"
                );
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT END -> {questId}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while logging quest goals for {questId}:\n{ex}");
        }
    }

    internal static void LogImportantQuestSnapshots(string sourceTag)
    {
        LogQuestGoals("Q_D_S_OldWarfare", sourceTag);
        LogQuestGoals("Q_D_S_TutorialHook", sourceTag);
        LogQuestGoals("Q_D_2_Lighthouse", sourceTag);
    }

    internal static bool IsHarpoonPieceId(string itemId)
    {
        return itemId == "I_HARPOON_PIECE_1" || itemId == "I_HARPOON_PIECE_2";
    }

    internal static bool IsBoneheadHookPartLocationId(string itemId)
    {
        return itemId == "I_HOOK_HEAD" || itemId == "I_HOOK_BODY";
    }

    internal static bool ShouldKeepBoneheadHookPartAfterLocationCheck(string itemId)
    {
        // The hook head is only the early pickup and can be removed after sending its AP check.
        if (itemId == "I_HOOK_HEAD")
            return false;

        // The hook body is the later construction/unlock pickup.
        // Keep it so our cleanup does not interfere with the vanilla quest completion moment.
        if (itemId == "I_HOOK_BODY")
            return true;

        return false;
    }

    internal static void MarkHarpoonPieceReceivedFromAP(string itemId, string sourceTag)
    {
        if (SessionState == null)
            return;

        if (itemId == "I_HARPOON_PIECE_1")
            SessionState.HarpoonPiece1ReceivedFromAP = true;
        else if (itemId == "I_HARPOON_PIECE_2")
            SessionState.HarpoonPiece2ReceivedFromAP = true;

        SaveSessionState();

        LogInfo($"{sourceTag}: recorded deferred AP harpoon piece {itemId}.");
    }

    internal static bool WasHarpoonPieceReceivedFromAP(string itemId)
    {
        if (SessionState == null)
            return false;

        if (itemId == "I_HARPOON_PIECE_1")
            return SessionState.HarpoonPiece1ReceivedFromAP;

        if (itemId == "I_HARPOON_PIECE_2")
            return SessionState.HarpoonPiece2ReceivedFromAP;

        return false;
    }

    internal static bool IsRadioSilenceReadyForHarpoonPieces()
    {
        try
        {
            QuestInstance quest = FindActiveQuest("Q_D_2_Lighthouse");

            if (quest == null)
                return false;

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal == null)
                return false;

            return currentGoal.GoalId == "FixHarpoon";
        }
        catch (Exception ex)
        {
            LogWarning($"IsRadioSilenceReadyForHarpoonPieces failed:\n{ex}");
            return false;
        }
    }

    internal static void AnnounceHarpoonDeferredNoticeOnce(string sourceTag)
    {
        if (SessionState == null)
            return;

        if (SessionState.HarpoonPieceDeferredNoticeShown)
            return;

        SessionState.HarpoonPieceDeferredNoticeShown = true;
        SaveSessionState();

        AnnounceAPWarning(
            "[AP] Harpoon part received. It will appear when Radio Silence reaches the harpoon repair step."
        );

        LogInfo($"{sourceTag}: shown one-time deferred harpoon notice.");
    }

    internal static bool TryGrantHarpoonPiece(PendingItem item, string sourceTag)
    {
        if (item == null || string.IsNullOrEmpty(item.Id))
            return false;

        MarkHarpoonPieceReceivedFromAP(item.Id, sourceTag);

        if (!IsRadioSilenceReadyForHarpoonPieces())
        {
            AnnounceHarpoonDeferredNoticeOnce(sourceTag);

            LogInfo(
                $"{sourceTag}: {item.DisplayName} received from AP. Deferring vanilla inventory grant until Radio Silence reaches FixHarpoon."
            );

            return true;
        }

        return TryGrantDeferredHarpoonPieceNow(item.Id, item.DisplayName, sourceTag);
    }

    internal static bool TryGrantDeferredHarpoonPieceNow(string itemId, string displayName, string sourceTag)
    {
        try
        {
            if (string.IsNullOrEmpty(itemId))
                return false;

            if (!WasHarpoonPieceReceivedFromAP(itemId))
                return false;

            var inventory = Singleton<InventoryManager>.Instance;

            if (inventory == null)
            {
                LogWarning($"{sourceTag}: cannot grant deferred harpoon piece {itemId}; InventoryManager is null.");
                return false;
            }

            if (inventory.HasItem(itemId, 1))
            {
                LogInfo($"{sourceTag}: deferred harpoon piece {itemId} already in inventory.");
                return true;
            }

            bool previousGrantingState = IsGrantingAPItem;
            IsGrantingAPItem = true;

            bool addResult;

            try
            {
                addResult = inventory.AddItem(itemId, 1, null, false);
            }
            finally
            {
                IsGrantingAPItem = previousGrantingState;
            }

            LogInfo($"{sourceTag}: deferred harpoon AddItem({itemId}) returned {addResult}.");

            if (addResult)
            {
                try
                {
                    MonoSingleton<PersistenceManager>.Instance.SaveGame();
                }
                catch
                {
                }
            }

            return addResult;
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TryGrantDeferredHarpoonPieceNow failed for {itemId}:\n{ex}");
            return false;
        }
    }

    internal static void TrySkipRadioSilenceBoatIntro(QuestLog questLog, string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (questLog == null)
                return;

            QuestInstance quest = FindActiveQuest("Q_D_2_Lighthouse");

            if (quest == null)
                return;

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal == null)
                return;

            if (currentGoal.GoalId != "Infiltrate")
                return;

            LogInfo($"{sourceTag}: Radio Silence boat intro candidate detected at goal Infiltrate.");

            // Conservative boat-intro bypass.
            // Complete only Infiltrate here and leave later goals to vanilla unless a specific softlock is confirmed.
            bool result = questLog.TryCompleteQuestGoal("Q_D_2_Lighthouse", "Infiltrate");

            LogInfo(
                $"{sourceTag}: Radio Silence boat intro skip TryCompleteQuestGoal(Infiltrate) returned {result}."
            );

            LogImportantQuestSnapshots(sourceTag + "/AfterRadioSilenceBoatIntroSkip");
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TrySkipRadioSilenceBoatIntro failed:\n{ex}");
        }
    }

    internal static void AnnounceHeartglazeDeferredNoticeOnce(string sourceTag)
    {
        if (SessionState == null)
            return;

        if (SessionState.HeartglazeFlowerDeferredNoticeShown)
            return;

        SessionState.HeartglazeFlowerDeferredNoticeShown = true;
        SaveSessionState();

        AnnounceAPActivity(
            OverlayColor("#FFD166", "The Heartglaze Flower was temporarily deferred for game function. It will appear after you defeat Woodcrawler and pick up the flower.")
        );

        LogInfo($"{sourceTag}: shown one-time Heartglaze deferred notice.");
    }
}
