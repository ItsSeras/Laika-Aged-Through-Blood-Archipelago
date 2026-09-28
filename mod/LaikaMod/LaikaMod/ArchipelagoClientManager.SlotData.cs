using System;
using System.Collections;
using System.Collections.Generic;


public partial class ArchipelagoClientManager
{
    // AP slot_data handling.
    // Converts APWorld options from the connected seed into local runtime options.
    private string TryReadSeedName(object loginResult)
    {
        string seedName = TryReadStringProperty(loginResult, "SeedName", "Seed", "RoomSeed");

        if (!string.IsNullOrWhiteSpace(seedName))
            return seedName;

        try
        {
            if (session != null)
            {
                object roomState = TryReadObjectProperty(session, "RoomState", "RoomInfo", "Room");
                seedName = TryReadStringProperty(roomState, "SeedName", "Seed", "RoomSeed");

                if (!string.IsNullOrWhiteSpace(seedName))
                    return seedName;
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static bool ReadSlotToggle(
    IDictionary slotData,
    string key,
    bool fallback)
    {
        if (!slotData.Contains(key) || slotData[key] == null)
            return fallback;

        string value = slotData[key].ToString().Trim();

        bool parsed;
        if (bool.TryParse(value, out parsed))
            return parsed;

        if (value == "1") return true;
        if (value == "0") return false;

        LaikaMod.LogWarning(
            $"AP: Invalid {key} value '{value}'; using {fallback}."
        );
        return fallback;
    }


    private static int ReadSlotInt(
        IDictionary slotData,
        string key,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!slotData.Contains(key) || slotData[key] == null)
            return fallback;

        int parsed;
        if (!int.TryParse(slotData[key].ToString().Trim(), out parsed))
        {
            LaikaMod.LogWarning(
                $"AP: Invalid {key} value '{slotData[key]}'; using {fallback}."
            );
            return fallback;
        }

        if (parsed < minimum || parsed > maximum)
        {
            LaikaMod.LogWarning(
                $"AP: Out-of-range {key} value {parsed}; using {fallback}."
            );
            return fallback;
        }

        return parsed;
    }

    private static List<string> ReadSlotStringList(
        IDictionary slotData,
        string key,
        IEnumerable<string> fallback)
    {
        List<string> fallbackList = new List<string>();
        if (fallback != null)
            fallbackList.AddRange(fallback);

        if (!slotData.Contains(key) || slotData[key] == null)
            return fallbackList;

        List<string> values = new List<string>();
        object rawValue = slotData[key];

        string rawString = rawValue as string;
        if (rawString != null)
        {
            string cleaned = rawString.Trim();

            if (cleaned.StartsWith("[") && cleaned.EndsWith("]"))
                cleaned = cleaned.Substring(1, cleaned.Length - 2);

            foreach (string part in cleaned.Split(','))
            {
                string value = part.Trim().Trim('\"', '\'');
                if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                    values.Add(value);
            }

            return values;
        }

        IEnumerable enumerable = rawValue as IEnumerable;
        if (enumerable != null)
        {
            foreach (object entry in enumerable)
            {
                if (entry == null)
                    continue;

                string value = entry.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                    values.Add(value);
            }

            return values;
        }

        LaikaMod.LogWarning(
            $"AP: Could not parse list slot_data value for {key}; using compatibility fallback."
        );
        return fallbackList;
    }

    private static VisceraProtectionMode ReadVisceraProtection(
        IDictionary slotData)
    {
        const string key = "viscera_protection";

        if (!slotData.Contains(key) || slotData[key] == null)
            return VisceraProtectionMode.Off;

        string value = slotData[key].ToString().Trim().ToLowerInvariant();

        switch (value)
        {
            case "off":
            case "0":
                return VisceraProtectionMode.Off;

            case "deathlink_only":
            case "1":
                return VisceraProtectionMode.DeathLinkOnly;

            case "all_deaths":
            case "2":
                return VisceraProtectionMode.AllDeaths;

            default:
                LaikaMod.LogWarning(
                    $"AP: Invalid viscera_protection value '{value}'; using off."
                );
                return VisceraProtectionMode.Off;
        }
    }

    private void TryApplyLiveSlotData(object loginResult)
    {
        try
        {
            var loginResultType = loginResult.GetType();
            var slotDataProperty = loginResultType.GetProperty("SlotData");

            if (slotDataProperty == null)
            {
                LaikaMod.LogWarning("AP: Login result did not expose SlotData.");
                return;
            }

            object rawSlotData = slotDataProperty.GetValue(loginResult, null);

            if (rawSlotData == null)
            {
                LaikaMod.LogWarning("AP: SlotData was null.");
                return;
            }

            IDictionary slotDataDictionary = rawSlotData as IDictionary;

            if (slotDataDictionary == null)
            {
                LaikaMod.LogWarning($"AP: SlotData was not dictionary-like. Type={rawSlotData.GetType().FullName}");
                return;
            }

            bool hadWeaponMode = slotDataDictionary.Contains("weapon_mode");
            bool hadDeathLink = slotDataDictionary.Contains("death_link");
            bool hadDeathAmnesty = slotDataDictionary.Contains("death_amnesty");
            bool hadDeathAmnestyCount = slotDataDictionary.Contains("death_amnesty_count");
            bool hadAutomaticPurchaseHints =
                slotDataDictionary.Contains("automatic_purchase_hints");
            bool hadGoalChecklist =
                slotDataDictionary.Contains("show_goal_checklist");
            bool hadGoalOptions = slotDataDictionary.Contains("goals");

            ApplySlotDataValue(slotDataDictionary, "weapon_mode");
            ApplySlotDataValue(slotDataDictionary, "death_link");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty_count");

            // Seed-controlled options. Explicit fallbacks prevent a previous
            // connection's settings from leaking into an older seed.
            LaikaMod.WorldOptions.SkipJakobTransition =
                ReadSlotToggle(slotDataDictionary, "skip_jakob_transition", true);

            LaikaMod.WorldOptions.SkipOrellaTransition =
                ReadSlotToggle(slotDataDictionary, "skip_orella_transition", true);

            LaikaMod.WorldOptions.SkipRoyBoat =
                ReadSlotToggle(slotDataDictionary, "skip_roy_boat", true);

            LaikaMod.WorldOptions.AutomaticPurchaseHints =
                ReadSlotToggle(
                    slotDataDictionary,
                    "automatic_purchase_hints",
                    false
                );

            LaikaMod.WorldOptions.ShowGoalChecklist =
                ReadSlotToggle(
                    slotDataDictionary,
                    "show_goal_checklist",
                    true
                );

            // Old seeds do not contain these fields. Their compatibility
            // fallback remains the original Two-Beak God-only victory.
            LaikaMod.WorldOptions.GoalCategories = ReadSlotStringList(
                slotDataDictionary,
                "goals",
                new string[] { "bosses" }
            );
            LaikaMod.WorldOptions.GoalAmount = ReadSlotInt(
                slotDataDictionary, "goal_amount", 1, 1, 3
            );
            LaikaMod.WorldOptions.BossGoals = ReadSlotStringList(
                slotDataDictionary,
                "bosses",
                new string[] { "Two-Beak God" }
            );
            LaikaMod.WorldOptions.BossGoalAmount = ReadSlotInt(
                slotDataDictionary, "boss_goal_amount", 1, 1, 6
            );
            LaikaMod.WorldOptions.PuppyGiftGoalAmount = ReadSlotInt(
                slotDataDictionary, "puppy_gift_goal_amount", 7, 1, 7
            );
            LaikaMod.WorldOptions.WastelanderGoalAmount = ReadSlotInt(
                slotDataDictionary, "wastelander_goal_amount", 6, 1, 6
            );

            LaikaMod.WorldOptions.VisceraProtection =
                ReadVisceraProtection(slotDataDictionary);

            LaikaMod.LogInfo(
                "AP: Run options applied. " +
                $"SkipJakob={LaikaMod.WorldOptions.SkipJakobTransition}, " +
                $"SkipOrella={LaikaMod.WorldOptions.SkipOrellaTransition}, " +
                $"SkipRoy={LaikaMod.WorldOptions.SkipRoyBoat}, " +
                $"AutoPurchaseHints={LaikaMod.WorldOptions.AutomaticPurchaseHints}, " +
                $"GoalChecklist={LaikaMod.WorldOptions.ShowGoalChecklist}, " +
                $"Goals=[{string.Join(",", LaikaMod.WorldOptions.GoalCategories)}], " +
                $"GoalAmount={LaikaMod.WorldOptions.GoalAmount}, " +
                $"Bosses=[{string.Join(",", LaikaMod.WorldOptions.BossGoals)}], " +
                $"BossGoalAmount={LaikaMod.WorldOptions.BossGoalAmount}, " +
                $"PuppyGiftGoalAmount={LaikaMod.WorldOptions.PuppyGiftGoalAmount}, " +
                $"WastelanderGoalAmount={LaikaMod.WorldOptions.WastelanderGoalAmount}, " +
                $"VisceraProtection={LaikaMod.WorldOptions.VisceraProtection}"
            );

            LaikaMod.LogInfo(
                "AP: Live slot_data read. " +
                $"HadWeaponMode={hadWeaponMode}, " +
                $"HadDeathLink={hadDeathLink}, " +
                $"HadDeathAmnesty={hadDeathAmnesty}, " +
                $"HadDeathAmnestyCount={hadDeathAmnestyCount}, " +
                $"HadAutoPurchaseHints={hadAutomaticPurchaseHints}, " +
                $"HadGoalChecklist={hadGoalChecklist}, " +
                $"HadGoalOptions={hadGoalOptions}, " +
                $"SlotDataWeaponMode={LaikaMod.WorldOptions.WeaponMode}, " +
                $"SlotDataDeathLink={LaikaMod.WorldOptions.DeathLinkEnabled}, " +
                $"SlotDataDeathAmnesty={LaikaMod.WorldOptions.DeathAmnestyEnabled}, " +
                $"SlotDataDeathAmnestyCount={LaikaMod.WorldOptions.DeathAmnestyCount}, " +
                $"SlotDataAutoPurchaseHints={LaikaMod.WorldOptions.AutomaticPurchaseHints}, " +
                $"SlotDataGoalChecklist={LaikaMod.WorldOptions.ShowGoalChecklist}"
            );

            if (LaikaMod.SessionState != null)
            {
                if (LaikaMod.SessionState.Options == null)
                    LaikaMod.SessionState.Options = new APWorldOptions();

                APWorldOptions options = LaikaMod.SessionState.Options;

                // Persist seed options for offline reloads.
                options.SkipJakobTransition =
                    LaikaMod.WorldOptions.SkipJakobTransition;
                options.SkipOrellaTransition =
                    LaikaMod.WorldOptions.SkipOrellaTransition;
                options.SkipRoyBoat =
                    LaikaMod.WorldOptions.SkipRoyBoat;
                options.AutomaticPurchaseHints =
                    LaikaMod.WorldOptions.AutomaticPurchaseHints;
                options.GoalCategories = new List<string>(
                    LaikaMod.WorldOptions.GoalCategories ?? new List<string>()
                );
                options.GoalAmount = LaikaMod.WorldOptions.GoalAmount;
                options.BossGoals = new List<string>(
                    LaikaMod.WorldOptions.BossGoals ?? new List<string>()
                );
                options.BossGoalAmount = LaikaMod.WorldOptions.BossGoalAmount;
                options.PuppyGiftGoalAmount = LaikaMod.WorldOptions.PuppyGiftGoalAmount;
                options.WastelanderGoalAmount = LaikaMod.WorldOptions.WastelanderGoalAmount;
                options.VisceraProtection =
                    LaikaMod.WorldOptions.VisceraProtection;

                // Weapon mode should still come from the AP seed/slot_data.
                if (hadWeaponMode)
                    options.WeaponMode = LaikaMod.WorldOptions.WeaponMode;

                // DeathLink: slot_data is only the default.
                // If the player changed it locally, local menu value wins.
                if (hadDeathLink && !options.DeathLinkLocalOverrideEnabled)
                {
                    options.DeathLinkEnabled = LaikaMod.WorldOptions.DeathLinkEnabled;
                }
                else
                {
                    LaikaMod.WorldOptions.DeathLinkEnabled = options.DeathLinkEnabled;
                }

                // Death Amnesty: slot_data is only the default.
                if (hadDeathAmnesty && !options.DeathAmnestyLocalOverrideEnabled)
                {
                    options.DeathAmnestyEnabled = LaikaMod.WorldOptions.DeathAmnestyEnabled;
                }
                else
                {
                    LaikaMod.WorldOptions.DeathAmnestyEnabled = options.DeathAmnestyEnabled;
                }

                // Death Amnesty Count: slot_data is only the default.
                if (hadDeathAmnestyCount && !options.DeathAmnestyCountLocalOverrideEnabled)
                {
                    options.DeathAmnestyCount = LaikaMod.WorldOptions.DeathAmnestyCount;
                }
                else
                {
                    LaikaMod.WorldOptions.DeathAmnestyCount = Math.Max(1, options.DeathAmnestyCount);
                    options.DeathAmnestyCount = LaikaMod.WorldOptions.DeathAmnestyCount;
                }

                LaikaMod.SaveSessionState();

                LaikaMod.LogInfo(
                    "AP: merged live slot_data with local overrides. " +
                    $"WeaponMode={LaikaMod.WorldOptions.WeaponMode}, " +
                    $"DeathLink={LaikaMod.WorldOptions.DeathLinkEnabled}, " +
                    $"DeathLinkOverride={options.DeathLinkLocalOverrideEnabled}, " +
                    $"DeathAmnesty={LaikaMod.WorldOptions.DeathAmnestyEnabled}, " +
                    $"DeathAmnestyOverride={options.DeathAmnestyLocalOverrideEnabled}, " +
                    $"DeathAmnestyCount={LaikaMod.WorldOptions.DeathAmnestyCount}, " +
                    $"DeathAmnestyCountOverride={options.DeathAmnestyCountLocalOverrideEnabled}, " +
                    $"AutoPurchaseHints={LaikaMod.WorldOptions.AutomaticPurchaseHints}, " +
                    $"Goals=[{string.Join(",", LaikaMod.WorldOptions.GoalCategories)}], " +
                    $"GoalAmount={LaikaMod.WorldOptions.GoalAmount}"
                );
            }

            LaikaMod.HasAppliedLiveSlotData = true;

            // Re-evaluate after reconnect using the live seed configuration plus
            // server-imported checked locations and this save's received items.
            LaikaMod.EvaluateAPGoalCompletion("AP live slot_data applied");
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: Failed to apply live slot_data:\n{ex}");
        }
    }

    private string ResolveOverlayItemColorHex(
        long itemId,
        string packetColorName,
        int itemFlags = 0,
        int ownerSlot = -1)
    {
        int localSlot =
            LaikaMod.SessionState != null &&
            LaikaMod.SessionState.Connection != null
                ? LaikaMod.SessionState.Connection.Slot
                : -1;

        string ownerGameName =
            ownerSlot > 0
                ? ResolveApGameNameFromSlot(ownerSlot)
                : "";

        bool belongsToLaika =
            (ownerSlot > 0 && ownerSlot == localSlot) ||
            string.Equals(
                ownerGameName,
                "Laika: Aged Through Blood",
                StringComparison.OrdinalIgnoreCase
            );

        // If this is actually a Laika item, preserve all of our existing
        // custom colors: progression, useful/key item, ingredient, cassette,
        // map, Puppy Gift, etc.
        //
        // Do NOT classify foreign items purely by numeric ID because
        // Archipelago IDs are only unique inside a game's data package.
        if (belongsToLaika && LaikaMod.IsLaikaApItemId(itemId))
        {
            return LaikaMod.GetItemRarityColorHex(itemId);
        }

        // For items belonging to another game, use Archipelago's item flags.
        //
        // 0x01 = progression
        // 0x02 = useful
        // 0x04 = trap

        if ((itemFlags & 0x04) != 0)
            return "#FF6B6B"; // trap / dangerous

        if ((itemFlags & 0x01) != 0)
            return "#C792EA"; // progression lavender

        if ((itemFlags & 0x02) != 0)
            return "#00D9FF"; // useful cyan

        // Keep support for explicitly colored AP text if supplied.
        string packetColor =
            MapAPColorToUnityRichText(packetColorName);

        if (!string.IsNullOrWhiteSpace(packetColor))
            return packetColor;

        // Normal/filler item.
        return "#5F7FFF";
    }

    private void ApplySlotDataValue(IDictionary slotDataDictionary, string key)
    {
        if (slotDataDictionary == null || !slotDataDictionary.Contains(key))
            return;

        object rawValue = slotDataDictionary[key];

        if (rawValue == null)
            return;

        string valueText = rawValue.ToString().Trim().ToLowerInvariant();

        switch (key)
        {
            case "weapon_mode":
                if (valueText == "direct" || valueText == "0")
                {
                    LaikaMod.WorldOptions.WeaponMode = WeaponGrantMode.Direct;
                }
                else if (valueText == "crafting" || valueText == "1")
                {
                    LaikaMod.WorldOptions.WeaponMode = WeaponGrantMode.Crafting;
                }
                else
                {
                    LaikaMod.LogWarning($"AP: Unknown weapon_mode slot_data value: {rawValue}");
                }
                break;

            case "death_link":
                {
                    bool parsedBool;
                    if (bool.TryParse(valueText, out parsedBool))
                    {
                        LaikaMod.WorldOptions.DeathLinkEnabled = parsedBool;
                    }
                    else if (valueText == "0" || valueText == "1")
                    {
                        LaikaMod.WorldOptions.DeathLinkEnabled = valueText == "1";
                    }
                }
                break;

            case "death_amnesty":
                {
                    bool parsedBool;
                    if (bool.TryParse(valueText, out parsedBool))
                    {
                        LaikaMod.WorldOptions.DeathAmnestyEnabled = parsedBool;
                    }
                    else if (valueText == "0" || valueText == "1")
                    {
                        LaikaMod.WorldOptions.DeathAmnestyEnabled = valueText == "1";
                    }
                }
                break;

            case "death_amnesty_count":
                {
                    int parsedInt;
                    if (int.TryParse(valueText, out parsedInt))
                    {
                        LaikaMod.WorldOptions.DeathAmnestyCount = Math.Max(1, parsedInt);
                    }
                }
                break;
        }
    }
}