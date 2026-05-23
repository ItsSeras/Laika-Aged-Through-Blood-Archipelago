using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

public partial class ArchipelagoClientManager
{
    // AP PrintJSON formatting.
    // Converts Archipelago chat/item/location messages into readable in-game overlay lines.
    private string NormalizePrintJsonPartType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "";

        return type
            .Trim()
            .Replace("_", "")
            .Replace("-", "")
            .Replace(" ", "")
            .ToLowerInvariant();
    }

    private string MapAPColorToUnityRichText(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName))
            return null;

        switch (colorName.Trim().ToLowerInvariant())
        {
            case "red": return "#FF6B6B";

            // AP progression / advancement-style colors.
            case "magenta":
            case "plum":
                return "#C792EA";

            // AP useful-style colors.
            case "cyan":
            case "blue":
                return "#00D9FF";

            // AP filler / neutral.
            case "green":
                return "#5F7FFF";

            case "yellow": return "#FFD166";
            case "orange": return "#FFA94D";
            case "salmon": return "#FA8072";
            case "white": return "#FFFFFF";

            default: return null;
        }
    }

    private string BuildPrintJsonOverlayLine(object dataObject)
    {
        if (dataObject == null)
            return string.Empty;

        if (dataObject is string plainText)
            return plainText;

        Newtonsoft.Json.Linq.JArray jsonParts = dataObject as Newtonsoft.Json.Linq.JArray;
        if (jsonParts != null)
        {
            string styledJsonLine = TryBuildStyledAPLineFromJsonParts(jsonParts);

            if (styledJsonLine == SuppressPrintJsonLine)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(styledJsonLine))
                return styledJsonLine;
        }

        Newtonsoft.Json.Linq.JToken jsonToken = dataObject as Newtonsoft.Json.Linq.JToken;
        if (jsonToken != null && jsonToken.Type == Newtonsoft.Json.Linq.JTokenType.Array)
        {
            string styledJsonLine = TryBuildStyledAPLineFromJsonParts((Newtonsoft.Json.Linq.JArray)jsonToken);

            if (styledJsonLine == SuppressPrintJsonLine)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(styledJsonLine))
                return styledJsonLine;
        }

        var enumerable = dataObject as IEnumerable;
        if (enumerable == null)
            return dataObject.ToString();

        List<object> parts = new List<object>();
        foreach (object part in enumerable)
        {
            if (part != null)
                parts.Add(part);
        }

        string styledApLine = TryBuildStyledAPLine(parts);

        if (styledApLine == SuppressPrintJsonLine)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(styledApLine))
            return styledApLine;

        StringBuilder sb = new StringBuilder();

        foreach (object part in parts)
        {
            string text = ReadStringProperty(part, "Text", "text");
            string color = ReadStringProperty(part, "Color", "color");

            if (string.IsNullOrEmpty(text))
                continue;

            string mappedColor = MapAPColorToUnityRichText(color);

            if (!string.IsNullOrEmpty(mappedColor))
                sb.Append($"<color={mappedColor}>{text}</color>");
            else
                sb.Append(text);
        }

        return sb.ToString();
    }

    private string TryBuildStyledAPLine(List<object> parts)
    {
        if (parts == null || parts.Count == 0)
            return null;

        string localPlayerName =
            LaikaMod.SessionState != null &&
            LaikaMod.SessionState.Connection != null &&
            !string.IsNullOrWhiteSpace(LaikaMod.SessionState.Connection.SlotName)
                ? LaikaMod.SessionState.Connection.SlotName
                : null;

        List<int> playerSlots = new List<int>();
        List<string> playerNames = new List<string>();

        string itemName = null;
        long itemId = -1;

        string locationName = null;
        long locationId = -1;

        string itemColorHex = null;

        StringBuilder plainTextBuilder = new StringBuilder();

        foreach (object part in parts)
        {
            if (part == null)
                continue;

            string partType = ReadStringProperty(part, "Type", "type");
            string text = ReadStringProperty(part, "Text", "text");
            string color = ReadStringProperty(part, "Color", "color");

            if (!string.IsNullOrWhiteSpace(text))
                plainTextBuilder.Append(text);

            if (string.IsNullOrWhiteSpace(partType) || string.IsNullOrWhiteSpace(text))
                continue;

            string normalizedType = NormalizePrintJsonPartType(partType);

            switch (normalizedType)
            {
                case "playerid":
                    {
                        int slot = TryParseIntText(text);
                        playerSlots.Add(slot);

                        if (slot > 0)
                            playerNames.Add(ResolveApPlayerNameFromSlot(slot));
                        else
                            playerNames.Add(text);

                        break;
                    }

                case "itemid":
                    {
                        itemId = ReadLongProperty(part, "ItemId", "Item", "item", "Id", "id");

                        if (itemId <= 0)
                            itemId = TryParseLongText(text);

                        itemColorHex = ResolveOverlayItemColorHex(itemId, color);

                        // We resolve the item name after we know whether this is self-find or send-to-other.
                        itemName = text;
                        break;
                    }

                case "locationid":
                    {
                        locationId = ReadLongProperty(part, "LocationId", "Location", "location", "Id", "id");

                        if (locationId <= 0)
                            locationId = TryParseLongText(text);

                        // We resolve the location name after we know the owner slot.
                        locationName = text;
                        break;
                    }
            }
        }

        string plainText = plainTextBuilder.ToString();

        // Resolve names after seeing the whole sentence.
        // In AP text:
        // - "found their" means item owner is usually the finder.
        // - "sent X to Y" means item owner is usually the receiver.
        // - location owner is usually the sender/finder.
        if (plainText.Contains(" sent ") && plainText.Contains(" to ") && playerSlots.Count >= 2)
        {
            int senderSlot = playerSlots[0];
            int receiverSlot = playerSlots[1];

            itemName = ResolveApItemNameFromId(itemId, receiverSlot);
            locationName = ResolveApLocationNameFromId(locationId, senderSlot);
        }
        else if (plainText.Contains(" found their ") && playerSlots.Count >= 1)
        {
            int finderSlot = playerSlots[0];

            itemName = ResolveApItemNameFromId(itemId, finderSlot);
            locationName = ResolveApLocationNameFromId(locationId, finderSlot);
        }
        else
        {
            int fallbackSlot =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Connection != null
                    ? LaikaMod.SessionState.Connection.Slot
                    : 0;

            itemName = ResolveApItemNameFromId(itemId, fallbackSlot);
            locationName = ResolveApLocationNameFromId(locationId, fallbackSlot);
        }

        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(locationName))
            return null;


        if (plainText.IndexOf("hint", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Real AP hint packets include item/location IDs. Chat echoes from UT/text
            // clients can contain the word "hint" without carrying those IDs, which
            // would otherwise render as Item -1 / Location -1.
            if (itemId <= 0 || locationId <= 0)
                return SuppressPrintJsonLine;

            string hinterName = playerNames.Count >= 1 ? playerNames[0] : "Someone";
            string ownerName = playerNames.Count >= 2 ? playerNames[1] : localPlayerName;

            if (string.IsNullOrWhiteSpace(ownerName))
                ownerName = "Unknown player";

            string prefixPart = LaikaMod.OverlayColor("#FFFFFF", "[Hint] ");
            string hinterPart = LaikaMod.OverlayColor("#C792EA", hinterName);
            string hintedPart = LaikaMod.OverlayColor("#FFFFFF", " hinted ");
            string ownerPart = LaikaMod.OverlayColor("#C792EA", ownerName);
            string possessivePart = LaikaMod.OverlayColor("#FFFFFF", "'s ");
            string itemPart = LaikaMod.OverlayColor(
                string.IsNullOrWhiteSpace(itemColorHex) ? ResolveOverlayItemColorHex(itemId, null) : itemColorHex,
                itemName
            );
            string atPart = LaikaMod.OverlayColor("#FFFFFF", " at ");
            string locationPart = LaikaMod.OverlayColor("#00E676", locationName + ".");

            return $"{prefixPart}{hinterPart}{hintedPart}{ownerPart}{possessivePart}{itemPart}{atPart}{locationPart}";
        }

        // AP self-find line.
        // Ignore our own self-finds here because ReceivedItems already creates
        // the nice local Laika-styled "[AP] Granted..." entry.
        if (plainText.Contains(" found their ") && playerSlots.Count >= 1)
        {
            int finderSlot = playerSlots[0];

            int localSlot =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Connection != null
                    ? LaikaMod.SessionState.Connection.Slot
                    : -1;

            if (finderSlot == localSlot)
                return SuppressPrintJsonLine;

            return LaikaMod.BuildFoundYourOwnItemOverlayLine(
                itemName,
                itemId,
                locationName,
                itemColorHex
            );
        }

        // AP send-to-other line
        if (plainText.Contains(" sent ") && plainText.Contains(" to ") && playerNames.Count >= 2 && playerSlots.Count >= 2)
        {
            int receiverSlot = playerSlots[1];

            int localSlot =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Connection != null
                    ? LaikaMod.SessionState.Connection.Slot
                    : -1;

            // If the item was sent to us, ReceivedItems will create the nicer local grant line.
            if (receiverSlot == localSlot)
                return SuppressPrintJsonLine;

            string senderName = playerNames[0];
            string receiverName = playerNames[1];

            return LaikaMod.BuildSentToOtherPlayerFromLocationOverlayLine(
                senderName,
                itemName,
                itemId,
                receiverName,
                locationName,
                itemColorHex
            );
        }

        return null;
    }

    private string TryBuildStyledAPLineFromJsonParts(Newtonsoft.Json.Linq.JArray parts)
    {
        if (parts == null)
            return null;

        string itemTextFromPacket = "";
        string locationTextFromPacket = "";

        List<int> playerSlots = new List<int>();
        List<string> playerNames = new List<string>();

        long itemId = -1;
        long locationId = -1;

        int itemOwnerSlot = -1;
        int locationOwnerSlot = -1;

        string itemColorHex = null;

        StringBuilder plainBuilder = new StringBuilder();

        foreach (Newtonsoft.Json.Linq.JToken token in parts)
        {
            Newtonsoft.Json.Linq.JObject part = token as Newtonsoft.Json.Linq.JObject;

            if (part == null)
                continue;

            string type = part.Value<string>("type") ?? "";
            string text = part.Value<string>("text") ?? "";
            string color = part.Value<string>("color") ?? "";

            plainBuilder.Append(text);

            string normalizedType = NormalizePrintJsonPartType(type);

            if (normalizedType == "playerid")
            {
                int slot = TryParseIntText(text);
                playerSlots.Add(slot);
                playerNames.Add(ResolveApPlayerNameFromSlot(slot));
            }
            else if (normalizedType == "itemid")
            {
                itemId = ReadLongProperty(part, "ItemId", "Item", "item", "Id", "id");

                if (itemId <= 0)
                    itemId = TryParseLongText(text);

                itemOwnerSlot = part.Value<int?>("player") ?? -1;
                itemColorHex = ResolveOverlayItemColorHex(itemId, color);
                itemTextFromPacket = text;
            }
            else if (normalizedType == "locationid")
            {
                locationId = ReadLongProperty(part, "LocationId", "Location", "location", "Id", "id");

                if (locationId <= 0)
                    locationId = TryParseLongText(text);

                locationOwnerSlot = part.Value<int?>("player") ?? -1;
                locationTextFromPacket = text;
            }
        }

        string plainText = plainBuilder.ToString();

        int localSlot =
            LaikaMod.SessionState != null &&
            LaikaMod.SessionState.Connection != null
                ? LaikaMod.SessionState.Connection.Slot
                : -1;

        if (plainText.Contains(" found their ") && playerSlots.Count >= 1)
        {
            int finderSlot = playerSlots[0];

            if (finderSlot == localSlot)
                return SuppressPrintJsonLine;

            string itemName = itemTextFromPacket;

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = ResolveApItemNameFromId(
                    itemId,
                    itemOwnerSlot > 0 ? itemOwnerSlot : finderSlot
                );
            }

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = ExtractFirstJsonTextPartByType(parts, "item_id");
            }

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = $"Item {itemId}";
            }

            string locationName = locationTextFromPacket;

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = ResolveApLocationNameFromId(
                    locationId,
                    locationOwnerSlot > 0 ? locationOwnerSlot : finderSlot
                );
            }

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = ExtractFirstJsonTextPartByType(parts, "location_id");
            }

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = $"Location {locationId}";
            }

            return LaikaMod.BuildFoundYourOwnItemOverlayLine(
                itemName,
                itemId,
                locationName,
                itemColorHex
            );
        }

        if (plainText.Contains(" sent ") && plainText.Contains(" to ") && playerSlots.Count >= 2)
        {
            int senderSlot = playerSlots[0];
            int receiverSlot = playerSlots[1];

            if (receiverSlot == localSlot)
                return SuppressPrintJsonLine;

            string senderName = ResolveApPlayerNameFromSlot(senderSlot);
            string receiverName = ResolveApPlayerNameFromSlot(receiverSlot);

            string itemName = itemTextFromPacket;

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = ResolveApItemNameFromId(
                    itemId,
                    itemOwnerSlot > 0 ? itemOwnerSlot : receiverSlot
                );
            }

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = ExtractFirstJsonTextPartByType(parts, "item_id");
            }

            if (LooksLikeRawArchipelagoIdLabel(itemName, "Item"))
            {
                itemName = $"Item {itemId}";
            }

            string locationName = locationTextFromPacket;

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = ResolveApLocationNameFromId(
                    locationId,
                    locationOwnerSlot > 0 ? locationOwnerSlot : senderSlot
                );
            }

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = ExtractFirstJsonTextPartByType(parts, "location_id");
            }

            if (LooksLikeRawArchipelagoIdLabel(locationName, "Location"))
            {
                locationName = $"Location {locationId}";
            }

            return LaikaMod.BuildSentToOtherPlayerFromLocationOverlayLine(
                senderName,
                itemName,
                itemId,
                receiverName,
                locationName,
                itemColorHex
            );
        }

        return null;
    }

    private string ResolveApGameNameFromSlot(int slot)
    {
        if (slot <= 0)
            return "";

        try
        {
            if (session != null && session.Players != null)
            {
                object result = TryInvokeAny(
                    session.Players,
                    new string[]
                    {
                    "GetPlayerGame",
                    "GetGame",
                    "GetGameName"
                    },
                    slot
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();

                object playerInfo = TryInvokeAny(
                    session.Players,
                    new string[]
                    {
                    "GetPlayerInfo",
                    "GetPlayer",
                    "GetNetworkPlayer"
                    },
                    slot
                );

                string gameName = ReadStringProperty(
                    playerInfo,
                    "Game",
                    "GameName",
                    "game",
                    "gameName"
                );

                if (!string.IsNullOrWhiteSpace(gameName))
                    return gameName;
            }
        }
        catch
        {
        }

        return "";
    }

    private string ExtractFirstJsonTextPartByType(Newtonsoft.Json.Linq.JArray parts, string wantedType)
    {
        if (parts == null || string.IsNullOrWhiteSpace(wantedType))
            return "";

        string wantedNormalized = NormalizePrintJsonPartType(wantedType);

        foreach (Newtonsoft.Json.Linq.JToken token in parts)
        {
            Newtonsoft.Json.Linq.JObject part = token as Newtonsoft.Json.Linq.JObject;

            if (part == null)
                continue;

            string type = part.Value<string>("type") ?? "";
            string text = part.Value<string>("text") ?? "";

            if (NormalizePrintJsonPartType(type) == wantedNormalized &&
                !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return "";
    }

    private object ExtractPrintJsonDataObject(object packet)
    {
        if (packet == null)
            return null;

        object directData = ReadObjectProperty(packet, "Data", "data");

        if (directData != null)
            return directData;

        try
        {
            FieldInfo jobjectField = packet.GetType().GetField(
                "jobject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (jobjectField != null)
            {
                object jobjectValue = jobjectField.GetValue(packet);

                Newtonsoft.Json.Linq.JObject jobject = jobjectValue as Newtonsoft.Json.Linq.JObject;

                if (jobject != null && jobject["data"] != null)
                    return jobject["data"];
            }
        }
        catch
        {
        }

        return null;
    }

    private void HandlePrintJsonPacket(object packet)
    {
        try
        {
            string messageType = ReadStringProperty(packet, "MessageType", "messageType");

            // Do not spam the Recent AP Activity box with connection/tutorial text.
            bool shouldShowInRecentLog =
                string.Equals(messageType, "ItemSend", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "Hint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "Chat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(messageType, "ServerChat", StringComparison.OrdinalIgnoreCase);

            if (!shouldShowInRecentLog)
            {
                string skippedLine = BuildPrintJsonOverlayLine(ReadObjectProperty(packet, "Data", "data"));
                LaikaMod.LogInfo($"AP PRINTJSON skipped from recent overlay: type={messageType}, text={skippedLine}");
                return;
            }

            object dataObject = ExtractPrintJsonDataObject(packet);
            string line = BuildPrintJsonOverlayLine(dataObject);

            if (string.IsNullOrWhiteSpace(line))
                return;

            LaikaMod.LogInfo($"AP PRINTJSON: {line}");
            LaikaMod.AnnounceAPActivity(line);
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP PRINTJSON: failed to parse PrintJSON packet:\n{ex}");
        }
    }

    private int TryParseIntText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        int parsed;
        if (int.TryParse(text.Trim(), out parsed))
            return parsed;

        return -1;
    }

    private long TryParseLongText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return -1;

        long parsed;
        if (long.TryParse(text.Trim(), out parsed))
            return parsed;

        return -1;
    }

    private bool LooksLikeRawArchipelagoIdLabel(string text, string labelPrefix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        string trimmed = text.Trim();

        long numericOnly;
        if (long.TryParse(trimmed, out numericOnly))
            return true;

        if (!string.IsNullOrWhiteSpace(labelPrefix) &&
            trimmed.StartsWith(labelPrefix + " ", StringComparison.OrdinalIgnoreCase))
        {
            string suffix = trimmed.Substring(labelPrefix.Length + 1).Trim();

            long numericSuffix;
            if (long.TryParse(suffix, out numericSuffix))
                return true;
        }

        return false;
    }

    private string ResolveApPlayerNameFromSlot(int slot)
    {
        if (slot <= 0)
            return "Unknown Player";

        try
        {
            if (session != null && session.Players != null)
            {
                object result = TryInvokeAny(
                    session.Players,
                    new string[]
                    {
                    "GetPlayerAlias",
                    "GetPlayerName",
                    "GetPlayer",
                    "GetName"
                    },
                    slot
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();
            }
        }
        catch
        {
        }

        return $"Player {slot}";
    }

    private string ResolveApItemNameFromId(long itemId, int ownerSlot)
    {
        if (itemId <= 0)
            return $"Item {itemId}";

        try
        {
            string ownerGameName = ResolveApGameNameFromSlot(ownerSlot);

            if (session != null && session.Items != null)
            {
                if (!string.IsNullOrWhiteSpace(ownerGameName))
                {
                    object resultByGame = TryInvokeAny(
                        session.Items,
                        new string[]
                        {
                        "GetItemName",
                        "GetItemNameFromId",
                        "GetItemNameById"
                        },
                        itemId,
                        ownerGameName
                    );

                    if (resultByGame != null && !string.IsNullOrWhiteSpace(resultByGame.ToString()))
                        return resultByGame.ToString();
                }

                object result = TryInvokeAny(
                    session.Items,
                    new string[]
                    {
                    "GetItemName",
                    "GetItemNameFromId",
                    "GetItemNameById"
                    },
                    itemId,
                    ownerSlot
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();

                result = TryInvokeAny(
                    session.Items,
                    new string[]
                    {
                    "GetItemName",
                    "GetItemNameFromId",
                    "GetItemNameById"
                    },
                    itemId
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();
            }
        }
        catch
        {
        }

        PendingItem pendingItem;
        if (LaikaMod.TryCreatePendingItemFromApItemId(itemId, out pendingItem))
            return pendingItem.DisplayName;

        return $"Item {itemId}";
    }

    private string ResolveApLocationNameFromId(long locationId, int ownerSlot)
    {
        if (locationId <= 0)
            return $"Location {locationId}";

        try
        {
            if (session != null && session.Locations != null)
            {
                object result = TryInvokeAny(
                    session.Locations,
                    new string[]
                    {
                    "GetLocationNameFromId",
                    "GetLocationName",
                    "GetLocationNameById"
                    },
                    locationId,
                    ownerSlot
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();

                result = TryInvokeAny(
                    session.Locations,
                    new string[]
                    {
                    "GetLocationNameFromId",
                    "GetLocationName",
                    "GetLocationNameById"
                    },
                    locationId
                );

                if (result != null && !string.IsNullOrWhiteSpace(result.ToString()))
                    return result.ToString();
            }
        }
        catch
        {
        }

        APLocationDefinition localDefinition;
        if (LaikaMod.TryGetLocationDefinition(locationId, out localDefinition))
            return localDefinition.DisplayName;

        return $"Location {locationId}";
    }
}