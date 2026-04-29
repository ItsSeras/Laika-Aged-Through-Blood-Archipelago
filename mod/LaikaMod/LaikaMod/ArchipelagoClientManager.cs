using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using static PendingItem;

public class ArchipelagoClientManager
{
    public static ArchipelagoClientManager Instance;

    private ArchipelagoSession session;
    private bool isConnecting = false;
    private bool packetHookSubscribed = false;

    public bool IsConnected =>
        session != null &&
        LaikaMod.SessionState != null &&
        LaikaMod.SessionState.Connection != null &&
        LaikaMod.SessionState.Connection.IsConnected &&
        LaikaMod.SessionState.Connection.IsAuthenticated;

    public bool IsConnecting => isConnecting;

    public ArchipelagoClientManager()
    {
        Instance = this;
    }

    public void Connect(string host, int port, string slotName, string password)
    {
        if (isConnecting)
        {
            LaikaMod.LogWarning("AP: Connect ignored because a connection attempt is already in progress.");
            LaikaMod.AnnounceAPWarning("[AP] Connection already in progress.");
            return;
        }

        if (IsConnected)
        {
            LaikaMod.LogWarning("AP: Connect ignored because client is already connected/authenticated.");
            LaikaMod.AnnounceAPWarning("[AP] Already connected.");
            return;
        }

        try
        {
            isConnecting = true;

            LaikaMod.LogInfo($"AP: Connecting to {host}:{port} as {slotName}");
            LaikaMod.AnnounceAPInfo($"[AP] Connecting to {host}:{port}...");

            // Reset any old connection state before starting a fresh session.
            packetHookSubscribed = false;
            session = null;

            LaikaMod.SessionState.Connection.IsConnected = false;
            LaikaMod.SessionState.Connection.IsAuthenticated = false;
            LaikaMod.RefreshDevOverlay();

            session = ArchipelagoSessionFactory.CreateSession(host, port);

            var loginResult = session.TryConnectAndLogin(
                "Laika: Aged Through Blood",
                slotName,
                ItemsHandlingFlags.AllItems,
                version: null,
                password: string.IsNullOrWhiteSpace(password) ? null : password,
                uuid: null,
                requestSlotData: true
            );

            if (!loginResult.Successful)
            {
                LaikaMod.LogError($"AP: Login failed. Result type={loginResult.GetType().FullName}, Result={loginResult}");
                LaikaMod.AnnounceAPError("[AP] Login failed.");

                LaikaMod.SessionState.Connection.IsConnected = false;
                LaikaMod.SessionState.Connection.IsAuthenticated = false;
                session = null;
                LaikaMod.SaveSessionState();
                LaikaMod.RefreshDevOverlay();
                return;
            }

            // Save live connection info into our local AP session state.
            LaikaMod.SessionState.Connection.Host = host;
            LaikaMod.SessionState.Connection.Port = port;
            LaikaMod.SessionState.Connection.SlotName = slotName;
            LaikaMod.SessionState.Connection.Password = password ?? "";
            LaikaMod.SessionState.Connection.IsConnected = true;
            LaikaMod.SessionState.Connection.IsAuthenticated = true;

            TryCaptureSlotMetadata(loginResult);
            ValidateSessionIdentityAndResetCacheIfNeeded(host, port, slotName, loginResult);

            LaikaMod.SessionState.APEnabled = true;
            LaikaMod.HasReconciledReceivedItemsThisConnection = false;
            LaikaMod.SaveSessionState();
            LaikaMod.RefreshDevOverlay();

            SubscribeGameplayHooks();

            LaikaMod.LogInfo("AP: Connected successfully!");
            LaikaMod.AnnounceAPSuccess("[AP] Connected to server.");

            TryApplyLiveSlotData(loginResult);

            LaikaMod.WorldOptions.DeathLinkEnabled =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Options != null &&
                LaikaMod.SessionState.Options.DeathLinkEnabled;

            LaikaMod.WorldOptions.DeathAmnestyEnabled =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Options != null &&
                LaikaMod.SessionState.Options.DeathAmnestyEnabled;

            if (LaikaMod.SessionState != null && LaikaMod.SessionState.Options != null)
            {
                LaikaMod.WorldOptions.DeathAmnestyCount =
                    LaikaMod.SessionState.Options.DeathAmnestyCount;
            }

            RefreshConnectionTags();
            LaikaMod.RequestReceivedItemPump("connect");
        }
        catch (Exception ex)
        {
            LaikaMod.SessionState.Connection.IsConnected = false;
            LaikaMod.SessionState.Connection.IsAuthenticated = false;
            session = null;

            LaikaMod.LogError($"AP: Connection failed:\n{ex}");
            LaikaMod.AnnounceAPError("[AP] Connection failed.");
            LaikaMod.SaveSessionState();
            LaikaMod.RefreshDevOverlay();
        }
        finally
        {
            isConnecting = false;
        }
    }

    private void TryCaptureSlotMetadata(object loginResult)
    {
        try
        {
            // Use reflection here so we do not hard-lock ourselves to one exact library shape yet.
            var loginResultType = loginResult.GetType();

            var teamProperty = loginResultType.GetProperty("Team");
            if (teamProperty != null)
            {
                object teamValue = teamProperty.GetValue(loginResult, null);
                if (teamValue != null)
                {
                    LaikaMod.SessionState.Connection.Team = Convert.ToInt32(teamValue);
                }
            }

            var slotProperty = loginResultType.GetProperty("Slot");
            if (slotProperty != null)
            {
                object slotValue = slotProperty.GetValue(loginResult, null);
                if (slotValue != null)
                {
                    LaikaMod.SessionState.Connection.Slot = Convert.ToInt32(slotValue);
                }
            }

            LaikaMod.LogInfo(
                $"AP: Connection metadata captured. " +
                $"Team={LaikaMod.SessionState.Connection.Team}, " +
                $"Slot={LaikaMod.SessionState.Connection.Slot}"
            );
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: Could not capture slot/team metadata:\n{ex}");
        }
    }

    private void ValidateSessionIdentityAndResetCacheIfNeeded(
    string host,
    int port,
    string slotName,
    object loginResult)
    {
        try
        {
            if (LaikaMod.SessionState == null)
                return;

            if (LaikaMod.SessionState.SentLocationIds == null)
                LaikaMod.SessionState.SentLocationIds = new List<long>();

            string seedName = TryReadSeedName(loginResult);

            string newIdentityKey =
                $"{host}:{port}|slotName={slotName}|team={LaikaMod.SessionState.Connection.Team}|slot={LaikaMod.SessionState.Connection.Slot}|seed={seedName}";

            string oldIdentityKey = LaikaMod.SessionState.SessionIdentityKey ?? "";

            if (string.IsNullOrWhiteSpace(oldIdentityKey))
            {
                LaikaMod.SessionState.SessionIdentityKey = newIdentityKey;
                LaikaMod.SessionState.SessionSeedName = seedName;

                LaikaMod.LogInfo($"AP CACHE: initialized session identity -> {newIdentityKey}");
                return;
            }

            if (oldIdentityKey == newIdentityKey)
            {
                LaikaMod.LogInfo($"AP CACHE: session identity unchanged -> {newIdentityKey}");
                return;
            }

            LaikaMod.LogWarning(
                "AP CACHE: detected different AP session. Resetting received-item/check cache.\n" +
                $"Old={oldIdentityKey}\n" +
                $"New={newIdentityKey}"
            );

            LaikaMod.SessionState.SessionIdentityKey = newIdentityKey;
            LaikaMod.SessionState.SessionSeedName = seedName;

            LaikaMod.SessionState.LastProcessedReceivedItemIndex = 0;
            LaikaMod.SessionState.GoalReported = false;
            LaikaMod.SessionState.SentLocationIds.Clear();

            LaikaMod.PendingItemQueue.Clear();
            LaikaMod.IsProcessingQueue = false;

            LaikaMod.AnnounceAPWarning("[AP] New seed/session detected. Resetting AP cache for this save slot.");
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP CACHE: session identity validation failed:\n{ex}");
        }
    }

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

    private object TryReadObjectProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            try
            {
                object value = property.GetValue(instance, null);
                if (value != null)
                    return value;
            }
            catch
            {
            }
        }

        return null;
    }

    private string TryReadStringProperty(object instance, params string[] propertyNames)
    {
        object value = TryReadObjectProperty(instance, propertyNames);

        if (value == null)
            return "";

        return value.ToString();
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

            // Try to treat SlotData as a dictionary-like object.
            IDictionary slotDataDictionary = rawSlotData as IDictionary;

            if (slotDataDictionary == null)
            {
                LaikaMod.LogWarning($"AP: SlotData was not dictionary-like. Type={rawSlotData.GetType().FullName}");
                return;
            }

            ApplySlotDataValue(slotDataDictionary, "weapon_mode");
            ApplySlotDataValue(slotDataDictionary, "death_link");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty");
            ApplySlotDataValue(slotDataDictionary, "death_amnesty_count");

            LaikaMod.LogInfo(
                "AP: Live slot_data applied. " +
                $"WeaponMode={LaikaMod.WorldOptions.WeaponMode}, " +
                $"DeathLink={LaikaMod.WorldOptions.DeathLinkEnabled}, " +
                $"DeathAmnesty={LaikaMod.WorldOptions.DeathAmnestyEnabled}, " +
                $"DeathAmnestyCount={LaikaMod.WorldOptions.DeathAmnestyCount}"
            );

            LaikaMod.HasAppliedLiveSlotData = true;

        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: Failed to apply live slot_data:\n{ex}");
        }
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
    public void PumpReceivedItems()
    {

        if (session == null)
        {
            LaikaMod.LogInfo("AP ITEMS: skipped because session is null.");
            return;
        }

        if (LaikaMod.SessionState == null || LaikaMod.SessionState.Connection == null)
        {
            LaikaMod.LogWarning("AP ITEMS: skipped because SessionState.Connection is null.");
            return;
        }

        if (!LaikaMod.SessionState.Connection.IsAuthenticated)
        {
            LaikaMod.LogInfo("AP ITEMS: skipped because client is not authenticated.");
            return;
        }

        try
        {
            var allItems = session.Items.AllItemsReceived;
            if (allItems == null)
            {
                LaikaMod.LogWarning("AP ITEMS: AllItemsReceived is null.");
                return;
            }

            int nextIndex = Math.Max(0, LaikaMod.SessionState.LastProcessedReceivedItemIndex);

            if (nextIndex > allItems.Count)
            {
                LaikaMod.LogWarning(
                    $"AP ITEMS: saved received-item index {nextIndex} is greater than server item count {allItems.Count}. Resetting to 0."
                );

                LaikaMod.UpdateLastProcessedReceivedItemIndex(0);
                nextIndex = 0;
            }

            if (nextIndex >= allItems.Count)
            {
                if (!LaikaMod.HasReconciledReceivedItemsThisConnection)
                {
                    LaikaMod.HasReconciledReceivedItemsThisConnection = true;
                    ReconcileImportantReceivedItems(allItems);
                    LaikaMod.ProcessPendingItemQueue("AP Reconcile");
                }
                return;
            }

            for (int i = nextIndex; i < allItems.Count; i++)
            {
                object receivedItem = allItems[i];
                if (receivedItem == null)
                {
                    LaikaMod.LogWarning($"AP ITEMS: received null item at index {i}");
                    LaikaMod.UpdateLastProcessedReceivedItemIndex(i + 1);
                    continue;
                }

                long apItemId = ReadLongProperty(receivedItem, "ItemId", "Item");
                string itemName = ReadStringProperty(receivedItem, "ItemName", "Name");
                string playerName = ReadStringProperty(receivedItem, "PlayerName", "Player");

                LaikaMod.LogInfo(
                    $"AP ITEMS: raw received item -> index={i}, apItemId={apItemId}, itemName={itemName}, player={playerName}"
                );

                PendingItem pendingItem;
                if (LaikaMod.TryCreatePendingItemFromApItemId(apItemId, out pendingItem))
                {
                    LaikaMod.LogInfo($"AP ITEMS: mapped AP item id {apItemId} -> {pendingItem}");

                    LaikaMod.EnqueueItem(pendingItem);

                    string receivedLocationName = ReadStringProperty(receivedItem, "LocationName", "locationName");
                    if (string.IsNullOrWhiteSpace(receivedLocationName))
                    {
                        long receivedLocationId = ReadLongProperty(receivedItem, "Location", "location", "LocationId", "locationId");
                        if (receivedLocationId > 0)
                        {
                            APLocationDefinition locationDefinition;
                            if (LaikaMod.TryGetLocationDefinition(receivedLocationId, out locationDefinition))
                            {
                                receivedLocationName = locationDefinition.DisplayName;
                            }
                        }
                    }

                    string receiveLine = LaikaMod.BuildReceivedFromOtherPlayerOverlayLine(
                        pendingItem.DisplayName,
                        apItemId,
                        playerName,
                        receivedLocationName
                    );

                    LaikaMod.AnnounceAPActivity(receiveLine);
                }
                else
                {
                    LaikaMod.LogWarning(
                        $"AP ITEMS: no mapping found for AP item id {apItemId}, itemName={itemName}, player={playerName}"
                    );
                    LaikaMod.AnnounceAPWarning($"[AP] Unmapped item: {itemName} ({apItemId})");
                }

                LaikaMod.UpdateLastProcessedReceivedItemIndex(i + 1);
            }

            if (!LaikaMod.HasReconciledReceivedItemsThisConnection)
            {
                LaikaMod.HasReconciledReceivedItemsThisConnection = true;
                ReconcileImportantReceivedItems(allItems);
            }

            LaikaMod.LogInfo("AP ITEMS: handing queued items to ProcessPendingItemQueue.");
            LaikaMod.ProcessPendingItemQueue("AP ReceivedItems");
            LaikaMod.LogInfo("AP ITEMS: ProcessPendingItemQueue completed.");
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP ITEMS: PumpReceivedItems failed:\n{ex}");
            LaikaMod.AnnounceAPError("[AP] Error while processing received items.");
        }
    }

    private void ReconcileImportantReceivedItems(System.Collections.IEnumerable allItems)
    {
        try
        {
            if (allItems == null)
                return;

            Dictionary<string, PendingItem> totalsByKindAndId = new Dictionary<string, PendingItem>();

            foreach (object receivedItem in allItems)
            {
                if (receivedItem == null)
                    continue;

                long apItemId = ReadLongProperty(receivedItem, "ItemId", "Item");

                PendingItem pendingItem;
                if (!LaikaMod.TryCreatePendingItemFromApItemId(apItemId, out pendingItem))
                    continue;

                if (!LaikaMod.IsImportantReconcileKind(pendingItem.Kind))
                    continue;

                string key = pendingItem.Kind + "|" + pendingItem.Id;

                PendingItem existing;
                if (totalsByKindAndId.TryGetValue(key, out existing))
                {
                    existing.AddAmount(pendingItem.Amount);
                }
                else
                {
                    totalsByKindAndId[key] = new PendingItem(
                        pendingItem.Kind,
                        pendingItem.Id,
                        pendingItem.Amount,
                        pendingItem.DisplayName
                    );
                }
            }

            foreach (PendingItem expectedItem in totalsByKindAndId.Values)
            {
                PendingItem missingItem;
                if (!LaikaMod.TryBuildMissingImportantReconcileItem(expectedItem, out missingItem))
                    continue;

                LaikaMod.EnqueueItem(missingItem);
                LaikaMod.LogWarning($"AP RECONCILE: expected AP item missing from save, re-queued -> {missingItem}");
            }
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP RECONCILE: failed:\n{ex}");
        }
    }



    public void SendLocationCheck(APLocationDefinition definition)
    {
        if (definition == null)
            return;

        if (session == null || !LaikaMod.SessionState.Connection.IsAuthenticated)
        {
            LaikaMod.LogWarning($"AP CHECKS: not connected, not sending check for {definition.DisplayName}");
            return;
        }

        try
        {
            session.Locations.CompleteLocationChecks(definition.LocationId);

            LaikaMod.RequestReceivedItemPump("after location check");

            LaikaMod.MarkLocationCheckedAndSent(definition.LocationId);

            LaikaMod.LogInfo(
                $"AP CHECKS: sent location check -> " +
                $"{definition.DisplayName} ({definition.LocationId})"
            );
        }
        catch (Exception ex)
        {
            LaikaMod.LogError(
                $"AP CHECKS: failed to send location check -> " +
                $"{definition.DisplayName} ({definition.LocationId})\n{ex}"
            );
        }
    }

    public void SubscribeGameplayHooks()
    {
        if (session == null)
            return;

        if (packetHookSubscribed)
        {
            LaikaMod.LogInfo("AP: PacketReceived hook already subscribed.");
            return;
        }

        try
        {
            session.Socket.PacketReceived += OnPacketReceived;
            packetHookSubscribed = true;
            LaikaMod.LogInfo("AP: PacketReceived hook subscribed.");
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: failed to subscribe packet hook:\n{ex}");
        }
    }

    private long ReadLongProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return -1;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            object value = property.GetValue(instance, null);
            if (value == null)
                continue;

            try
            {
                return Convert.ToInt64(value);
            }
            catch
            {
            }
        }

        return -1;
    }

    private int ReadIntProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return 0;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            object value = property.GetValue(instance, null);
            if (value == null)
                continue;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
            }
        }

        return 0;
    }

    private string ReadStringProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return string.Empty;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            object value = property.GetValue(instance, null);
            if (value == null)
                continue;

            return value.ToString();
        }

        return string.Empty;
    }

    private object ReadObjectProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName);
            if (property == null)
                continue;

            return property.GetValue(instance, null);
        }

        return null;
    }

    private string MapAPColorToUnityRichText(string colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName))
            return null;

        switch (colorName.Trim().ToLowerInvariant())
        {
            case "red": return "#FF6B6B";
            case "green": return "#7CFF7C";
            case "yellow": return "#FFD166";
            case "blue": return "#7FDBFF";
            case "magenta": return "#C792EA";
            case "cyan": return "#7FDBFF";
            case "white": return "#FFFFFF";
            case "orange": return "#FFA94D";
            case "plum": return "#DDA0DD";
            case "salmon": return "#FA8072";
            default: return null;
        }
    }

    private string BuildPrintJsonOverlayLine(object dataObject)
    {
        if (dataObject == null)
            return string.Empty;

        if (dataObject is string plainText)
            return plainText;

        var enumerable = dataObject as IEnumerable;
        if (enumerable == null)
            return dataObject.ToString();

        List<object> parts = new List<object>();
        foreach (object part in enumerable)
        {
            if (part != null)
                parts.Add(part);
        }

        // Try to build a custom Archipelago-style "found their item (location)" line first.
        string styledApLine = TryBuildStyledAPLine(parts);
        if (!string.IsNullOrWhiteSpace(styledApLine))
            return styledApLine;

        // Fallback: use AP-provided colors part-by-part.
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

        List<string> playerNames = new List<string>();
        string itemName = null;
        long itemId = -1;
        string locationName = null;

        StringBuilder plainTextBuilder = new StringBuilder();

        foreach (object part in parts)
        {
            if (part == null)
                continue;

            string partType = ReadStringProperty(part, "Type", "type");
            string text = ReadStringProperty(part, "Text", "text");

            if (!string.IsNullOrWhiteSpace(text))
                plainTextBuilder.Append(text);

            if (string.IsNullOrWhiteSpace(partType) || string.IsNullOrWhiteSpace(text))
                continue;

            switch (partType.Trim().ToLowerInvariant())
            {
                case "player_id":
                    playerNames.Add(text);
                    break;

                case "item_id":
                    itemName = text;
                    itemId = ReadLongProperty(part, "ItemId", "Item", "item", "Id", "id");
                    break;

                case "location_id":
                    locationName = text;
                    break;
            }
        }

        string plainText = plainTextBuilder.ToString();

        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(locationName))
            return null;

        // AP self-find line
        if (plainText.Contains(" found their ") && playerNames.Count >= 1)
        {
            string finderName = playerNames[0];

            if (!string.IsNullOrWhiteSpace(localPlayerName) &&
                string.Equals(finderName, localPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                return LaikaMod.BuildFoundYourOwnItemOverlayLine(
                    itemName,
                    itemId,
                    locationName
                );
            }

            return LaikaMod.BuildFoundYourOwnItemOverlayLine(
                itemName,
                itemId,
                locationName
            );
        }

        // AP send-to-other line
        if (plainText.Contains(" sent ") && plainText.Contains(" to ") && playerNames.Count >= 2)
        {
            string senderName = playerNames[0];
            string receiverName = playerNames[1];

            return LaikaMod.BuildSentToOtherPlayerFromLocationOverlayLine(
                senderName,
                itemName,
                itemId,
                receiverName,
                locationName
            );
        }

        return null;
    }

    private void HandlePrintJsonPacket(object packet)
    {
        try
        {
            object dataObject = ReadObjectProperty(packet, "Data", "data");
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

    private void OnPacketReceived(ArchipelagoPacketBase packet)
    {
        if (packet == null)
            return;

        try
        {
            string cmd = ReadStringProperty(packet, "Cmd", "cmd");

            if (string.Equals(cmd, "PrintJSON", StringComparison.OrdinalIgnoreCase))
            {
                HandlePrintJsonPacket(packet);
                return;
            }

            if (string.Equals(cmd, "ReceivedItems", StringComparison.OrdinalIgnoreCase))
            {
                LaikaMod.RequestReceivedItemPump("ReceivedItems packet");
                return;
            }

            var bounced = packet as BouncedPacket;
            if (bounced == null)
                return;

            if (bounced.Tags == null)
                return;

            bool isDeathLink = false;

            foreach (string tag in bounced.Tags)
            {
                if (string.Equals(tag, "DeathLink", StringComparison.OrdinalIgnoreCase))
                {
                    isDeathLink = true;
                    break;
                }
            }

            if (!isDeathLink)
                return;

            string source = "<unknown>";
            string cause = "DeathLink";

            if (bounced.Data != null)
            {
                if (bounced.Data.ContainsKey("source") && bounced.Data["source"] != null)
                    source = bounced.Data["source"].ToString();

                if (bounced.Data.ContainsKey("cause") && bounced.Data["cause"] != null)
                    cause = bounced.Data["cause"].ToString();
            }

            string localSlot =
                LaikaMod.SessionState != null &&
                LaikaMod.SessionState.Connection != null
                    ? LaikaMod.SessionState.Connection.SlotName
                    : "";

            if (!string.IsNullOrWhiteSpace(localSlot) &&
                string.Equals(source, localSlot, StringComparison.OrdinalIgnoreCase))
            {
                LaikaMod.LogInfo("AP DEATHLINK: self-bounced DeathLink packet received, treating as sent confirmation.");
                LaikaMod.AnnounceAPDeathLink($"[AP] DeathLink sent to other players: {cause}");
                return;
            }

            LaikaMod.LogInfo("AP DEATHLINK: incoming DeathLink packet received.");
            ApplyIncomingDeathLink(bounced.Data);
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP packet handling failed:\n{ex}");
        }
    }
    private void ApplyIncomingDeathLink(Dictionary<string, JToken> dataObject)
    {
        try
        {
            LaikaMod.SuppressedDeathLinksRemaining++;

            string source = "<unknown>";
            string cause = "DeathLink";

            if (dataObject != null)
            {
                if (dataObject.ContainsKey("source") && dataObject["source"] != null)
                    source = dataObject["source"].ToString();

                if (dataObject.ContainsKey("cause") && dataObject["cause"] != null)
                    cause = dataObject["cause"].ToString();
            }

            LaikaMod.AnnounceAPDeathLink($"[AP] DeathLink from {source}: {cause}");
            LaikaMod.LogInfo(
                $"AP DEATHLINK: suppression incremented. " +
                $"RemainingSuppressedDeaths={LaikaMod.SuppressedDeathLinksRemaining}, " +
                $"Source={source}, Cause={cause}"
            );

            // TODO: replace this with your real kill call once you want inbound DeathLink live.
            // Example:
            // var riderHead = UnityEngine.Object.FindObjectOfType<global::RiderHead>();
            // if (riderHead != null) riderHead.Kill();
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP DEATHLINK: failed to apply incoming death:\n{ex}");
        }
    }

    public void RefreshConnectionTags()
    {
        if (session == null || !IsConnected)
            return;

        List<string> tags = new List<string>();

        if (LaikaMod.WorldOptions.DeathLinkEnabled)
            tags.Add("DeathLink");

        try
        {
            var packet = new ConnectUpdatePacket
            {
                Tags = tags.ToArray(),
                ItemsHandling = ItemsHandlingFlags.AllItems
            };

            session.Socket.SendPacket(packet);

            LaikaMod.LogInfo("AP: refreshed connection tags -> " + string.Join(", ", tags.ToArray()));
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning("AP: failed to refresh connection tags:\n" + ex);
        }
    }

    public void Disconnect(string reason = "Manual disconnect")
    {
        try
        {
            if (session != null && packetHookSubscribed)
            {
                session.Socket.PacketReceived -= OnPacketReceived;
                packetHookSubscribed = false;
                LaikaMod.LogInfo("AP: PacketReceived hook unsubscribed.");
            }
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP: failed to unsubscribe packet hook during disconnect:\n{ex}");
        }

        try
        {
            TryCloseTransport();

            session = null;

            if (LaikaMod.SessionState != null && LaikaMod.SessionState.Connection != null)
            {
                LaikaMod.SessionState.Connection.IsConnected = false;
                LaikaMod.SessionState.Connection.IsAuthenticated = false;
                LaikaMod.SessionState.Connection.Team = 0;
                LaikaMod.SessionState.Connection.Slot = 0;
            }

            isConnecting = false;

            LaikaMod.SaveSessionState();
            LaikaMod.RefreshDevOverlay();

            LaikaMod.LogInfo($"AP: Disconnected. Reason={reason}");
            LaikaMod.AnnounceAPWarning($"[AP] Disconnected: {reason}");
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP: Disconnect failed:\n{ex}");
        }
    }

    private void TryCloseTransport()
    {
        if (session == null)
            return;

        object[] closeTargets = new object[]
        {
        session.Socket,
        session
        };

        string[] methodNames = new string[]
        {
        "DisconnectAsync",
        "Disconnect",
        "CloseAsync",
        "Close",
        "Dispose"
        };

        foreach (object target in closeTargets)
        {
            if (target == null)
                continue;

            Type type = target.GetType();

            foreach (string methodName in methodNames)
            {
                try
                {
                    var method = type.GetMethod(methodName, Type.EmptyTypes);
                    if (method == null)
                        continue;

                    object result = method.Invoke(target, null);
                    LaikaMod.LogInfo($"AP: transport close invoked -> {type.Name}.{methodName}()");
                    return;
                }
                catch (Exception ex)
                {
                    LaikaMod.LogWarning($"AP: close attempt failed -> {type.Name}.{methodName}(): {ex.Message}");
                }
            }
        }

        LaikaMod.LogWarning("AP: no supported close/disconnect method was found on session/socket.");
    }

    public void SendDeathLink(string cause)
    {
        if (session == null || !LaikaMod.SessionState.Connection.IsAuthenticated)
            return;

        try
        {
            var payload = new Dictionary<string, JToken>()
        {
            { "time", JToken.FromObject(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) },
            { "source", JToken.FromObject(LaikaMod.SessionState.Connection.SlotName ?? "Laika") },
            { "cause", JToken.FromObject(cause ?? "Laika death") }
        };

            var packet = new BouncePacket
            {
                Tags = new List<string> { "DeathLink" },
                Data = payload
            };

            session.Socket.SendPacket(packet);

            LaikaMod.LogInfo("AP DEATHLINK: outbound DeathLink sent.");
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP DEATHLINK: send failed:\n{ex}");
        }
    }
}