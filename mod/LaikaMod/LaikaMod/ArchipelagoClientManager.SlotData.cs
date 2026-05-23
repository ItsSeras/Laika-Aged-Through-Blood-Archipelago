using System;
using System.Collections;


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

            ApplySlotDataValue(slotDataDictionary, "weapon_mode");
            ApplySlotDataValue(slotDataDictionary, "death_link");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty_count");

            LaikaMod.LogInfo(
                "AP: Live slot_data read. " +
                $"HadWeaponMode={hadWeaponMode}, " +
                $"HadDeathLink={hadDeathLink}, " +
                $"HadDeathAmnesty={hadDeathAmnesty}, " +
                $"HadDeathAmnestyCount={hadDeathAmnestyCount}, " +
                $"SlotDataWeaponMode={LaikaMod.WorldOptions.WeaponMode}, " +
                $"SlotDataDeathLink={LaikaMod.WorldOptions.DeathLinkEnabled}, " +
                $"SlotDataDeathAmnesty={LaikaMod.WorldOptions.DeathAmnestyEnabled}, " +
                $"SlotDataDeathAmnestyCount={LaikaMod.WorldOptions.DeathAmnestyCount}"
            );

            if (LaikaMod.SessionState != null)
            {
                if (LaikaMod.SessionState.Options == null)
                    LaikaMod.SessionState.Options = new APWorldOptions();

                APWorldOptions options = LaikaMod.SessionState.Options;

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
                    $"DeathAmnestyCountOverride={options.DeathAmnestyCountLocalOverrideEnabled}"
                );
            }

            LaikaMod.HasAppliedLiveSlotData = true;
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: Failed to apply live slot_data:\n{ex}");
        }
    }

    private string ResolveOverlayItemColorHex(long itemId, string packetColorName)
    {
        string laikaColor = LaikaMod.GetOverlayItemColorHex(itemId, null);

        if (!string.IsNullOrWhiteSpace(laikaColor))
            return laikaColor;

        string packetColor = MapAPColorToUnityRichText(packetColorName);

        if (!string.IsNullOrWhiteSpace(packetColor))
            return packetColor;

        return "#FFFFFF";
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