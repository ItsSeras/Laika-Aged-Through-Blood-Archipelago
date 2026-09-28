using Laika.Cassettes;
using Laika.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class LaikaMod
{
    private static readonly string[] GoalPuppyGiftInternalIds = new string[]
    {
        "I_TOY_BIKE",
        "I_GAMEBOY",
        "I_PLANT_PUPPY",
        "I_TOY_ANIMAL",
        "I_BOOK_MOTHER",
        "I_DREAMCATCHER",
        "I_UKULELE"
    };

    private static readonly string[] GoalWastelanderQuestIds = new string[]
    {
        "Q_D_A_MusiciansDrums",
        "Q_D_A_MusiciansErhu",
        "Q_D_A_MusiciansFlute",
        "Q_D_A_MusiciansGuitar",
        "Q_D_A_MusiciansPiano",
        "Q_D_A_MusiciansVoice"
    };

    private sealed class APGoalProgressSnapshot
    {
        public bool BossesEnabled;
        public bool PuppyGiftsEnabled;
        public bool WastelandersEnabled;

        public int EnabledCategoryCount;
        public int CompletedCategoryCount;
        public int GoalAmount;
        public int GoalsRemaining;

        public int SelectedBossCount;
        public int BossCompletedCount;
        public int BossesNeededRemaining;
        public bool BossesComplete;
        public readonly List<string> RemainingBossNames = new List<string>();

        public int PuppyGiftCompletedCount;
        public int PuppyGiftsNeededRemaining;
        public bool PuppyGiftsComplete;
        public readonly List<string> RemainingPuppyGiftNames = new List<string>();

        public int WastelanderCompletedCount;
        public int WastelandersNeededRemaining;
        public bool WastelandersComplete;
        public readonly List<string> RemainingWastelanderNames = new List<string>();
    }

    private static bool IsGoalCategoryEnabled(string category)
    {
        if (WorldOptions == null || WorldOptions.GoalCategories == null)
            return false;

        foreach (string value in WorldOptions.GoalCategories)
        {
            if (string.Equals(value, category, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetBossGoalLocationKey(string bossName)
    {
        switch (bossName)
        {
            case "A Hundred Hungry Beaks":
                return "B_BOSS_00_DEFEATED";
            case "A Long Lost Woodcrawler":
                return "B_BOSS_ROSCO_DEFEATED";
            case "A Caterpillar Made of Sadness":
                return "B_BOSS_01_DEFEATED";
            case "A Gargantuan Swimcrab":
                return "B_BOSS_02_DEFEATED";
            case "Pope Melva VIII":
                return "B_BOSS_03_DEFEATED";
            case "Two-Beak God":
                return "BOSS_04_DEFEATED";
            default:
                return null;
        }
    }

    private static string GetPuppyGiftGoalDisplayName(string itemId)
    {
        switch (itemId)
        {
            case "I_TOY_BIKE":
                return "Toy Bike";
            case "I_GAMEBOY":
                return "Handheld Console";
            case "I_PLANT_PUPPY":
                return "Tangerine Tree";
            case "I_TOY_ANIMAL":
                return "Toy Animal";
            case "I_BOOK_MOTHER":
                return "Great-Great-Grandma's Novella";
            case "I_DREAMCATCHER":
                return "Dreamcatcher";
            case "I_UKULELE":
                return "Ukulele";
            default:
                return itemId ?? "Unknown Gift";
        }
    }

    private static string GetWastelanderGoalDisplayName(string questId)
    {
        switch (questId)
        {
            case "Q_D_A_MusiciansDrums":
                return "Fogg's Only Wish";
            case "Q_D_A_MusiciansErhu":
                return "The Last Erhu";
            case "Q_D_A_MusiciansFlute":
                return "Clean Your Beak";
            case "Q_D_A_MusiciansGuitar":
                return "Desperately in Need of Music";
            case "Q_D_A_MusiciansPiano":
                return "Sober Up";
            case "Q_D_A_MusiciansVoice":
                return "Oooo Ooo Oo O Ooo";
            default:
                return questId ?? "Unknown Wastelander Quest";
        }
    }

    private static APGoalProgressSnapshot BuildAPGoalProgressSnapshot()
    {
        APGoalProgressSnapshot snapshot = new APGoalProgressSnapshot();

        if (WorldOptions == null)
            return snapshot;

        snapshot.GoalAmount = WorldOptions.GoalAmount;
        snapshot.BossesEnabled = IsGoalCategoryEnabled("bosses");
        snapshot.PuppyGiftsEnabled = IsGoalCategoryEnabled("puppy_gifts");
        snapshot.WastelandersEnabled = IsGoalCategoryEnabled("wastelanders");

        snapshot.EnabledCategoryCount =
            (snapshot.BossesEnabled ? 1 : 0) +
            (snapshot.PuppyGiftsEnabled ? 1 : 0) +
            (snapshot.WastelandersEnabled ? 1 : 0);

        if (snapshot.BossesEnabled && WorldOptions.BossGoals != null)
        {
            // OptionSet values should already be unique. The HashSet also protects
            // the display/evaluation from legacy cached lists that were observed
            // to contain duplicate entries before live slot_data replaced them.
            HashSet<string> uniqueBosses =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string bossName in WorldOptions.BossGoals)
            {
                if (string.IsNullOrWhiteSpace(bossName) || !uniqueBosses.Add(bossName))
                    continue;

                string locationKey = GetBossGoalLocationKey(bossName);
                if (string.IsNullOrEmpty(locationKey))
                    continue;

                snapshot.SelectedBossCount++;

                APLocationDefinition definition;
                bool defeated =
                    TryGetLocationDefinition(locationKey, out definition) &&
                    HasLocationBeenSent(definition.LocationId);

                if (defeated)
                    snapshot.BossCompletedCount++;
                else
                    snapshot.RemainingBossNames.Add(bossName);
            }
        }

        snapshot.BossesComplete =
            snapshot.BossesEnabled &&
            snapshot.SelectedBossCount > 0 &&
            WorldOptions.BossGoalAmount >= 1 &&
            WorldOptions.BossGoalAmount <= snapshot.SelectedBossCount &&
            snapshot.BossCompletedCount >= WorldOptions.BossGoalAmount;

        snapshot.BossesNeededRemaining = Math.Max(
            0,
            WorldOptions.BossGoalAmount - snapshot.BossCompletedCount
        );

        if (snapshot.PuppyGiftsEnabled)
        {
            foreach (string itemId in GoalPuppyGiftInternalIds)
            {
                if (HasReceivedAPItem(ItemKind.PuppyTreat, itemId))
                {
                    snapshot.PuppyGiftCompletedCount++;
                }
                else
                {
                    snapshot.RemainingPuppyGiftNames.Add(
                        GetPuppyGiftGoalDisplayName(itemId)
                    );
                }
            }
        }

        snapshot.PuppyGiftsComplete =
            snapshot.PuppyGiftsEnabled &&
            snapshot.PuppyGiftCompletedCount >= WorldOptions.PuppyGiftGoalAmount;

        snapshot.PuppyGiftsNeededRemaining = Math.Max(
            0,
            WorldOptions.PuppyGiftGoalAmount - snapshot.PuppyGiftCompletedCount
        );

        if (snapshot.WastelandersEnabled)
        {
            foreach (string questId in GoalWastelanderQuestIds)
            {
                APLocationDefinition definition;
                bool completed =
                    TryGetLocationDefinition(questId, out definition) &&
                    HasLocationBeenSent(definition.LocationId);

                if (completed)
                {
                    snapshot.WastelanderCompletedCount++;
                }
                else
                {
                    snapshot.RemainingWastelanderNames.Add(
                        GetWastelanderGoalDisplayName(questId)
                    );
                }
            }
        }

        snapshot.WastelandersComplete =
            snapshot.WastelandersEnabled &&
            snapshot.WastelanderCompletedCount >= WorldOptions.WastelanderGoalAmount;

        snapshot.WastelandersNeededRemaining = Math.Max(
            0,
            WorldOptions.WastelanderGoalAmount - snapshot.WastelanderCompletedCount
        );

        snapshot.CompletedCategoryCount =
            (snapshot.BossesComplete ? 1 : 0) +
            (snapshot.PuppyGiftsComplete ? 1 : 0) +
            (snapshot.WastelandersComplete ? 1 : 0);

        snapshot.GoalsRemaining = Math.Max(
            0,
            snapshot.GoalAmount - snapshot.CompletedCategoryCount
        );

        return snapshot;
    }

    internal static void EvaluateAPGoalCompletion(string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (WorldOptions == null)
                return;

            // Goal-related events are also the natural refresh points for the
            // optional live checklist. Keep updating it even after CLIENT_GOAL
            // has already been reported so its final state remains accurate.
            RefreshDevOverlay();

            if (SessionState.GoalReported)
                return;

            APGoalProgressSnapshot snapshot = BuildAPGoalProgressSnapshot();

            if (snapshot.EnabledCategoryCount <= 0)
            {
                LogWarning($"{sourceTag}: GOAL EVAL skipped because no goal categories are enabled.");
                return;
            }

            if (snapshot.GoalAmount < 1 ||
                snapshot.GoalAmount > snapshot.EnabledCategoryCount)
            {
                LogWarning(
                    $"{sourceTag}: GOAL EVAL invalid goal configuration. " +
                    $"GoalAmount={snapshot.GoalAmount}, EnabledCategories={snapshot.EnabledCategoryCount}."
                );
                return;
            }

            LogInfo(
                $"{sourceTag}: GOAL EVAL -> " +
                $"Categories={snapshot.CompletedCategoryCount}/{snapshot.GoalAmount}, " +
                $"Bosses={(snapshot.BossesEnabled ? snapshot.BossCompletedCount + "/" + WorldOptions.BossGoalAmount : "off")}, " +
                $"PuppyGifts={(snapshot.PuppyGiftsEnabled ? snapshot.PuppyGiftCompletedCount + "/" + WorldOptions.PuppyGiftGoalAmount : "off")}, " +
                $"Wastelanders={(snapshot.WastelandersEnabled ? snapshot.WastelanderCompletedCount + "/" + WorldOptions.WastelanderGoalAmount : "off")}"
            );

            if (snapshot.CompletedCategoryCount < snapshot.GoalAmount)
                return;

            if (ArchipelagoClientManager.Instance == null)
            {
                LogWarning($"{sourceTag}: GOAL EVAL complete, but ArchipelagoClientManager.Instance is null.");
                return;
            }

            ArchipelagoClientManager.Instance.SendGoalCompletionStatus(
                sourceTag + "/ConfiguredGoal"
            );
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: EvaluateAPGoalCompletion failed:\n{ex}");
        }
    }

    // AP location check and vanilla reward suppression helpers.
    // This file turns one-time vanilla pickups, quest rewards, cassettes, and gifts
    // into Archipelago location checks while preventing duplicate or fake checks.
    internal static bool HasReceivedOrConsumedAPQuestItem(ItemKind kind, string itemId)
    {
        try
        {
            if (string.IsNullOrEmpty(itemId))
                return false;

            if (HasReceivedAPItem(kind, itemId))
                return true;

            if (WasVanillaConsumedAPItem(kind, itemId))
                return true;

            return false;
        }
        catch (Exception ex)
        {
            LogWarning($"HasReceivedOrConsumedAPQuestItem failed for {kind}:{itemId}\n{ex}");
            return false;
        }
    }

    internal static void TrySendFetchItemLocationIfAPTurnedIn(
        string questId,
        string itemId,
        ItemKind itemKind,
        string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (string.IsNullOrEmpty(questId) || string.IsNullOrEmpty(itemId))
                return;

            bool isHarpoonTurnIn =
                questId == "Q_D_2_Lighthouse" &&
                (itemId == "I_HARPOON_PIECE_1" ||
                 itemId == "I_HARPOON_PIECE_2");

            if (isHarpoonTurnIn)
            {
                // FixHarpoon can also be completed by the Dash bypass.
                // Only claim a turn-in location when vanilla actually consumed
                // this AP-owned part.
                if (!WasVanillaConsumedAPItem(itemKind, itemId))
                    return;
            }
            else if (!HasReceivedOrConsumedAPQuestItem(itemKind, itemId))
            {
                return;
            }

            APLocationDefinition itemLocation;
            if (!TryGetLocationDefinition(itemId, out itemLocation))
            {
                LogWarning($"{sourceTag}: no AP location definition for fetch item {itemId} tied to quest {questId}.");
                return;
            }

            if (HasLocationBeenSent(itemLocation.LocationId))
            {
                LogInfo($"{sourceTag}: fetch-item location already sent for {itemId} tied to quest {questId}.");
                return;
            }

            TrySendLocationCheck(
                itemLocation,
                $"{sourceTag}/FetchItemAutoCheck/{questId}",
                false
            );

            LogInfo(
                $"{sourceTag}: auto-sent fetch-item location {itemLocation.DisplayName} because AP-provided item {itemId} was turned in for quest {questId}."
            );
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: TrySendFetchItemLocationIfAPTurnedIn failed for quest={questId}, item={itemId}\n{ex}");
        }
    }

    internal static void TrySendFetchItemLocationsForCompletedQuest(string questId, string sourceTag)
    {
        if (string.IsNullOrEmpty(questId))
            return;

        switch (questId)
        {
            case "Q_D_S_TutorialHook":
                // Finishing Bonehead's Hook proves the Hook Head portion of this quest
                // is no longer obtainable. Normally the physical pickup already sent
                // its AP check. If AP Hook caused vanilla to skip/despawn that pickup,
                // claim the location here instead.
                TrySendAutoClaimLocation(
                    "I_HOOK_HEAD",
                    sourceTag + "/BoneheadsHook"
                );
                break;

            case "Q_D_A_MusiciansErhu":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ERHU_STRINGS", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_MusiciansVoice":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_COUGHING_SYRUP", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_MusiciansFlute":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_FLUTE_REED", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_MusiciansGuitar":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_GUITAR_STRINGS", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_MusiciansDrums":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_DRUMSTICKS", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_MusiciansPiano":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_PARTITURES", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_NewSheriff":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ALFREDO_COMIC", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Tombstone":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_TOMB_FLOWER", ItemKind.KeyItem, sourceTag);
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_TOMB_PEBBLE", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_CamillasJoint":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_CAMILLA_HERBS", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_BoneFlour":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_SPECIAL_BONES", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_EntomBrother":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ENTOM_INVITATION", ItemKind.KeyItem, sourceTag);
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_BUGS_JAR", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_FirstPeriod":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_NAPKINS", ItemKind.KeyItem, sourceTag);
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_PERIOD_MUSHROOM", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_LastMeal":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_SUICIDE_BERRIES", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Prophecy":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_PETEY_PROPHECY", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_OldCamp":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_HILDA_BRAID", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Seashell":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_HILDA_SEASHELL", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_NightmaresOne":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ANTI_NIGHTMARE", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_NightmaresTwo":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ANTI_URTICARIA", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_NightmaresThree":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_ANTI_DIARRHOEA", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Rapist":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_GIRL_DIARY", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Gasoline":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_GASOLINE", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_A_Dictionary":
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_DICTIONARY", ItemKind.KeyItem, sourceTag);
                break;

            case "Q_D_S_Flower":
                // Backup only. Normal Heartglaze handling should still happen at the physical flower.
                TrySendFetchItemLocationIfAPTurnedIn(questId, "I_PUPPY_FLOWER", ItemKind.KeyItem, sourceTag);
                break;
            case "Q_D_2_Lighthouse":
                // Radio Silence can despawn the physical harpoon-part sources if AP
                // supplied the parts first. If vanilla successfully used those AP
                // items, make sure their corresponding AP locations are not lost.
                TrySendFetchItemLocationIfAPTurnedIn(
                    questId,
                    "I_HARPOON_PIECE_1",
                    ItemKind.KeyItem,
                    sourceTag
                );

                TrySendFetchItemLocationIfAPTurnedIn(
                    questId,
                    "I_HARPOON_PIECE_2",
                    ItemKind.KeyItem,
                    sourceTag
                );
                break;
        }
    }

    // Used when vanilla progression can permanently remove a physical AP location.
    //
    // Unlike TrySendFetchItemLocationIfAPTurnedIn(), this does NOT require the
    // location's vanilla item itself to have been received from AP. The caller
    // must only use this when game state definitively proves the player passed
    // the corresponding one-time vanilla step.
    internal static void TrySendAutoClaimLocation(
        string locationKey,
        string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            if (string.IsNullOrEmpty(locationKey))
                return;

            APLocationDefinition definition;
            if (!TryGetLocationDefinition(locationKey, out definition))
            {
                LogWarning(
                    $"{sourceTag}: no AP location definition found for auto-claim key {locationKey}."
                );
                return;
            }

            if (HasLocationBeenSent(definition.LocationId))
            {
                LogInfo(
                    $"{sourceTag}: auto-claim location already sent -> {definition.DisplayName}"
                );
                return;
            }

            TrySendLocationCheck(
                definition,
                $"{sourceTag}/AutoClaim",
                false
            );

            LogInfo(
                $"{sourceTag}: auto-claimed {definition.DisplayName} because " +
                "vanilla progression skipped or removed its physical source."
            );
        }
        catch (Exception ex)
        {
            LogWarning(
                $"{sourceTag}: TrySendAutoClaimLocation failed for {locationKey}\n{ex}"
            );
        }
    }

    // Centralized check-send path for all AP-style locations.
    // This is the one place that should handle duplicate suppression and persistent sent-state.
    internal static void TrySendLocationCheck(
        APLocationDefinition definition,
        string sourceTag,
        bool consumeVanillaReward = true)
    {
        if (definition == null)
        {
            LogWarning($"{sourceTag}: TrySendLocationCheck called with null definition.");
            return;
        }

        if (HasLocationBeenSent(definition.LocationId))
        {
            LogInfo($"{sourceTag}: check already sent -> {definition.DisplayName} ({definition.LocationId})");
            return;
        }

        if (ArchipelagoClientManager.Instance != null)
        {
            ArchipelagoClientManager.Instance.SendLocationCheck(definition);

            if (consumeVanillaReward)
                TryConsumeVanillaLocationReward(definition, sourceTag);
        }
        else
        {
            LogWarning($"{sourceTag}: ArchipelagoClientManager missing, check not sent -> {definition.DisplayName}");
        }
    }

    internal static void ReportLaikaGoalComplete(string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            // Make sure the final boss check is sent even if the achievement hook was missed.
            APLocationDefinition finalBossDefinition;
            if (TryGetLocationDefinition("BOSS_04_DEFEATED", out finalBossDefinition))
            {
                TrySendLocationCheck(
                    finalBossDefinition,
                    $"{sourceTag}/FinalBossCheckBackup",
                    false
                );
            }
            else
            {
                LogWarning($"{sourceTag}: could not find final boss AP location definition.");
            }

            // The ending is now only another evaluation point. It must not
            // unconditionally win the seed because the configured goal may
            // require a different category or multiple categories.
            EvaluateAPGoalCompletion(sourceTag + "/EndingSequence");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: ReportLaikaGoalComplete failed:\n{ex}");
        }
    }

    internal static string MakeAPItemKey(ItemKind kind, string itemId)
    {
        return $"{kind}|{itemId}";
    }

    internal static void RememberReceivedAPItem(PendingItem item)
    {
        if (item == null || SessionState == null || string.IsNullOrEmpty(item.Id))
            return;

        if (SessionState.ReceivedAPItemKeys == null)
            SessionState.ReceivedAPItemKeys = new List<string>();

        string key = MakeAPItemKey(item.Kind, item.Id);

        if (!SessionState.ReceivedAPItemKeys.Contains(key))
        {
            SessionState.ReceivedAPItemKeys.Add(key);
            SaveSessionState();

            LogInfo($"AP STATE: remembered received AP item -> {key}");
        }
    }

    internal static bool HasReceivedAPItem(ItemKind kind, string itemId)
    {
        if (SessionState == null || SessionState.ReceivedAPItemKeys == null)
            return false;

        return SessionState.ReceivedAPItemKeys.Contains(MakeAPItemKey(kind, itemId));
    }

    internal static void RememberVanillaConsumedAPItem(ItemKind kind, string itemId)
    {
        if (SessionState == null || string.IsNullOrEmpty(itemId))
            return;

        if (SessionState.VanillaConsumedAPItemKeys == null)
            SessionState.VanillaConsumedAPItemKeys = new List<string>();

        string key = MakeAPItemKey(kind, itemId);

        if (!SessionState.VanillaConsumedAPItemKeys.Contains(key))
        {
            SessionState.VanillaConsumedAPItemKeys.Add(key);
            SaveSessionState();

            LogInfo($"AP STATE: vanilla consumed AP item, reconcile will not restore it -> {key}");
        }
    }

    internal static bool WasVanillaConsumedAPItem(ItemKind kind, string itemId)
    {
        if (SessionState == null || SessionState.VanillaConsumedAPItemKeys == null)
            return false;

        return SessionState.VanillaConsumedAPItemKeys.Contains(MakeAPItemKey(kind, itemId));
    }

    internal static bool TryGetItemKindForInventoryLocation(string itemId, out ItemKind kind)
    {
        kind = ItemKind.Unknown;

        APLocationDefinition definition;
        if (!TryGetLocationDefinition(itemId, out definition))
            return false;

        if (definition.Category == "KeyItem")
            kind = ItemKind.KeyItem;
        else if (definition.Category == "Material")
            kind = ItemKind.Material;
        else if (definition.Category == "PuppyGift")
            kind = ItemKind.PuppyTreat;
        else
            return false;

        return true;
    }

    // Attempts to remove vanilla item rewards from the player.
    internal static void TryConsumeVanillaLocationReward(APLocationDefinition definition, string sourceTag)
    {
        if (definition == null)
            return;

        if (definition.Category == "Cassette")
        {
            TryRemoveCassetteReward(definition.InternalId, sourceTag);
            return;
        }

        if (definition.Category == "PuppyGift" ||
            definition.Category == "Material" ||
            definition.Category == "Ingredient")
        {
            TryRemoveInventoryReward(definition.InternalId, 1, sourceTag);
            return;
        }

        if (definition.Category == "KeyItem")
        {
            // Key items often drive vanilla quest/dialogue/exit flow.
            // Remove them from the AddKeyItem postfix instead, where we can special-case timing.
            return;
        }

        // Do not consume quest-completion, boss, or map-unlock rewards here.
        // Those are progression/check states, not the randomized item reward itself.
    }

    internal static bool TryRemoveInventoryReward(string itemId, int amount, string sourceTag)
    {
        if (string.IsNullOrEmpty(itemId))
            return false;

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: could not remove vanilla reward {itemId}; InventoryManager is null.");
            return false;
        }

        try
        {
            bool previousSuppressState = SuppressVanillaConsumeTracking;
            SuppressVanillaConsumeTracking = true;

            bool removed;

            try
            {
                removed = inventory.RemoveItem(itemId, amount, true);
            }
            finally
            {
                SuppressVanillaConsumeTracking = previousSuppressState;
            }

            LogInfo($"{sourceTag}: remove vanilla inventory reward {itemId} x{amount} -> {removed}");

            return removed;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception removing vanilla inventory reward {itemId}:\n{ex}");
            return false;
        }
    }

    internal static void TryRemoveCassetteReward(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return;

        var manager = Singleton<CassettesManager>.Instance;
        var loader = Singleton<CassettesDataLoader>.Instance;

        if (manager == null || loader == null)
        {
            LogWarning($"{sourceTag}: could not remove vanilla cassette {cassetteId}; cassette manager/loader is null.");
            return;
        }

        try
        {
            CassetteData cassette = loader.FindCassette(cassetteId);
            if (cassette == null)
            {
                LogWarning($"{sourceTag}: could not remove vanilla cassette {cassetteId}; FindCassette returned null.");
                return;
            }

            bool removed = manager.CassettesInventory != null && manager.CassettesInventory.Remove(cassette);
            LogInfo($"{sourceTag}: remove vanilla cassette reward {cassetteId} -> {removed}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception removing vanilla cassette reward {cassetteId}:\n{ex}");
        }
    }

    internal static void ArmCassetteLocationCheck(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return;

        ArmedCassetteLocationChecks.Add(cassetteId);
        LogInfo($"{sourceTag}: armed cassette location check for {cassetteId}.");
    }

    internal static bool ConsumeArmedCassetteLocationCheck(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
            return false;

        if (!ArmedCassetteLocationChecks.Remove(cassetteId))
            return false;

        LogInfo($"{sourceTag}: consumed armed cassette location check for {cassetteId}.");
        return true;
    }

    internal static bool IsJakobCollectionCassetteId(string cassetteId)
    {
        return
            cassetteId == "I_CASSETTE_1" ||
            cassetteId == "I_CASSETTE_2" ||
            cassetteId == "I_CASSETTE_3" ||
            cassetteId == "I_CASSETTE_4" ||
            cassetteId == "I_CASSETTE_5";
    }

    internal static bool IsQuestRewardCassetteId(string cassetteId)
    {
        return
            cassetteId == "I_CASSETTE_D01" ||          // Trust Them, Diplomacy reward
            cassetteId == "I_CASSETTE_D02" ||          // My Destiny, Radio Silence reward
            cassetteId == "I_CASSETTE_D03" ||          // The End of the Road, Big Tree reward
            cassetteId == "I_CASSETTE_KIDNAPPING";     // Mother, Floating reward
    }

    internal static bool TryGetQuestRewardCassetteQuestId(string cassetteId, out string questId)
    {
        questId = null;

        switch (cassetteId)
        {
            case "I_CASSETTE_D01": // Trust Them
                questId = "Q_D_1_Mines"; // Diplomacy
                return true;

            case "I_CASSETTE_D02": // My Destiny
                questId = "Q_D_2_Lighthouse"; // Radio Silence
                return true;

            case "I_CASSETTE_D03": // The End of the Road
                questId = "Q_D_3_TheBigTree"; // The Big Tree
                return true;

            case "I_CASSETTE_KIDNAPPING": // Mother
                questId = "Q_D_S_TutorialDash"; // Floating
                return true;

            default:
                return false;
        }
    }

    internal static bool IsQuestRewardCassetteLocationAllowed(string cassetteId)
    {
        string questId;
        if (!TryGetQuestRewardCassetteQuestId(cassetteId, out questId))
            return true;

        APLocationDefinition questDefinition;
        if (!TryGetLocationDefinition(questId, out questDefinition))
            return false;

        return HasLocationBeenSent(questDefinition.LocationId);
    }

    internal static void TrySendQuestRewardCassetteLocationForCompletedQuest(string questId, string sourceTag)
    {
        if (string.IsNullOrEmpty(questId))
            return;

        string cassetteId = null;

        switch (questId)
        {
            case "Q_D_1_Mines":
                cassetteId = "I_CASSETTE_D01"; // Trust Them
                break;

            case "Q_D_2_Lighthouse":
                cassetteId = "I_CASSETTE_D02"; // My Destiny
                break;

            case "Q_D_3_TheBigTree":
                cassetteId = "I_CASSETTE_D03"; // The End of the Road
                break;

            case "Q_D_S_TutorialDash":
                cassetteId = "I_CASSETTE_KIDNAPPING"; // Mother
                break;
        }

        if (string.IsNullOrEmpty(cassetteId))
            return;

        APLocationDefinition cassetteDefinition;
        if (!TryGetLocationDefinition(cassetteId, out cassetteDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition for quest reward cassette {cassetteId} from quest {questId}.");
            return;
        }

        if (HasLocationBeenSent(cassetteDefinition.LocationId))
        {
            LogInfo($"{sourceTag}: quest reward cassette already sent -> {cassetteDefinition.DisplayName}");
            return;
        }

        TrySendLocationCheck(
            cassetteDefinition,
            $"{sourceTag}/QuestRewardCassette/{questId}",
            false
        );

        LogInfo(
            $"{sourceTag}: sent quest reward cassette check {cassetteDefinition.DisplayName} because quest {questId} completed."
        );
    }

    // Centralized cassette check handler.
    // This consumes one-shot suppression for AP-granted cassette items so that receiving
    // a cassette does not accidentally count as finding its real in-world location.
    internal static void TryHandleCassetteLocationCheck(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
        {
            LogWarning($"{sourceTag}: cassetteId was null or empty.");
            return;
        }

        if (SuppressedCassetteChecks.Remove(cassetteId))
        {
            LogInfo($"{sourceTag}: suppressed cassette check for AP-granted cassette {cassetteId}.");
            return;
        }

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(cassetteId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for cassetteId={cassetteId}");
            return;
        }

        string requiredQuestId;
        if (TryGetQuestRewardCassetteQuestId(cassetteId, out requiredQuestId) &&
            !IsQuestRewardCassetteLocationAllowed(cassetteId))
        {
            LogWarning(
                $"{sourceTag}: blocked quest reward cassette check for {cassetteId} / {locationDefinition.DisplayName} " +
                $"because required quest {requiredQuestId} is not completed yet."
            );

            // If vanilla/camp/night tried to add this early, rip it back out.
            TryRemoveCassetteReward(
                cassetteId,
                $"{sourceTag}/BlockedQuestRewardCassette"
            );

            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag);

        // Keep this here so legitimate non-AP cassette rewards still do not become free vanilla inventory.
        TryConsumeVanillaLocationReward(locationDefinition, sourceTag);
    }

    internal static bool IsPuppyGiftPlacedInHouse(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            return false;

        string achievementId = null;

        switch (itemId)
        {
            case "I_UKULELE":
                achievementId = "D_A_GiftsPuppy_Ukulele_Intro";
                break;

            case "I_DREAMCATCHER":
                achievementId = "D_A_GiftsPuppy_Dreamcatcher";
                break;

            case "I_TOY_BIKE":
                achievementId = "D_A_GiftsPuppy_Bike_Intro";
                break;

            case "I_BOOK_MOTHER":
                achievementId = "D_A_GiftsPuppy_BookMother_Intro";
                break;

            case "I_TOY_ANIMAL":
                achievementId = "D_A_GiftsPuppy_ToyAnimal";
                break;

            case "I_GAMEBOY":
                achievementId = "D_A_GiftsPuppy_GameBoy_Intro";
                break;

            case "I_PLANT_PUPPY":
                achievementId = "D_A_GiftsPuppy_Plant_Intro";
                break;
        }

        if (string.IsNullOrEmpty(achievementId))
            return false;

        return HasProgressionFlag(achievementId);
    }

    internal static string NormalizePuppyGiftId(string giftId)
    {
        if (string.IsNullOrEmpty(giftId))
            return giftId;

        string normalized = giftId.Trim();

        // Keep this here in case the game ever gives us something like I_TOY_BIKEgift.
        if (normalized.EndsWith("gift", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 4);
        }

        return normalized;
    }

    internal static bool ConsumeSuppressedPuppyGiftCheck(string giftId, string sourceTag)
    {
        if (string.IsNullOrEmpty(giftId))
            return false;

        if (SuppressedPuppyGiftChecks.Remove(giftId))
        {
            LogInfo($"{sourceTag}: suppressed puppy gift check for AP-granted item {giftId}.");
            return true;
        }

        return false;
    }

    internal static bool ShouldSuppressVanillaInventoryReward(APLocationDefinition definition)
    {
        if (definition == null)
            return false;

        // Cassettes and maps have their own specialized handlers.
        if (definition.Category == "Cassette" || definition.Category == "MapUnlock")
            return false;

        // Do not block progression-state-only checks here.
        if (definition.Category == "Quest" || definition.Category == "Boss")
            return false;

        // Bonehead hook parts are location checks only.
        // Head can be consumed. Body should stay because it is the final vanilla hook-construction pickup.
        if (IsBoneheadHookPartLocationId(definition.InternalId))
        {
            return !ShouldKeepBoneheadHookPartAfterLocationCheck(definition.InternalId);
        }

        return (
            definition.Category == "PuppyGift" ||
            definition.Category == "KeyItem" ||
            definition.Category == "Material" ||
            definition.Category == "Ingredient"
        );
    }

    // Handles a real Puppy gift source and turns it into an AP location check.
    // If the gift came from AP instead of being found naturally, suppression eats it here
    // so the player does not get a fake local check for an item they were only sent.
    internal static void TryHandlePuppyGiftLocationCheck(string giftId, string sourceTag)
    {
        TryHandlePuppyGiftLocationCheck(giftId, sourceTag, true);
    }

    internal static void TryHandlePuppyGiftLocationCheck(string giftId, string sourceTag, bool consumeVanillaReward)
    {
        if (string.IsNullOrEmpty(giftId))
        {
            LogWarning($"{sourceTag}: puppy gift id was null or empty.");
            return;
        }

        string normalizedGiftId = NormalizePuppyGiftId(giftId);

        LogInfo($"{sourceTag}: puppy gift rawId={giftId}, normalizedId={normalizedGiftId}");

        if (ConsumeSuppressedPuppyGiftCheck(normalizedGiftId, sourceTag))
            return;

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(normalizedGiftId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for puppy gift id={normalizedGiftId}");
            return;
        }

        if (locationDefinition.Category != "PuppyGift")
        {
            LogWarning(
                $"{sourceTag}: resolved id {normalizedGiftId} but category was {locationDefinition.Category}, not PuppyGift."
            );
            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag, consumeVanillaReward);
    }
}
