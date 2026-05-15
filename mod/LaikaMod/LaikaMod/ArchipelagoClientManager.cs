using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
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
            ImportServerCheckedLocations();

            LaikaMod.SessionState.APEnabled = true;
            LaikaMod.HasReconciledReceivedItemsThisConnection = false;
            LaikaMod.SaveSessionState();
            LaikaMod.RefreshDevOverlay();

            SubscribeGameplayHooks();

            LaikaMod.LogInfo("AP: Connected successfully!");
            LaikaMod.ResetUniversalTrackerRegionCache("AP connected");
            LaikaMod.AnnounceAPSuccess("[AP] Connected to server.");

            TryApplyLiveSlotData(loginResult);


            try
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid())
                {
                    LaikaMod.TryUpdateUniversalTrackerRegionFromScene(
                        UnityEngine.SceneManagement.SceneManager.GetActiveScene(),
                        "ArchipelagoClientManager.Connect.Success"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"UT MAP: failed immediate region sync after AP connect.\n{ex}");
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


    private void ImportServerCheckedLocations()
    {
        try
        {
            if (session == null || LaikaMod.SessionState == null)
                return;

            if (LaikaMod.SessionState.SentLocationIds == null)
                LaikaMod.SessionState.SentLocationIds = new List<long>();

            object locationsHelper = TryReadObjectProperty(session, "Locations");

            if (locationsHelper == null)
            {
                LaikaMod.LogWarning("AP CHECKS: session.Locations was null, could not import server checked locations.");
                return;
            }

            object checkedLocationsObject = TryReadObjectPropertyOrField(
                locationsHelper,
                "CheckedLocations",
                "AllLocationsChecked",
                "CheckedLocationIds",
                "Checked"
            );

            IEnumerable checkedLocations = checkedLocationsObject as IEnumerable;

            if (checkedLocations == null)
            {
                LaikaMod.LogWarning(
                    $"AP CHECKS: could not import server checked locations. Locations helper type={locationsHelper.GetType().FullName}"
                );
                return;
            }

            int imported = 0;

            foreach (object rawLocationId in checkedLocations)
            {
                if (rawLocationId == null)
                    continue;

                long locationId = Convert.ToInt64(rawLocationId);

                if (!LaikaMod.SessionState.SentLocationIds.Contains(locationId))
                {
                    LaikaMod.SessionState.SentLocationIds.Add(locationId);
                    imported++;
                }
            }

            if (imported > 0)
                LaikaMod.SaveSessionState();

            LaikaMod.LogInfo(
                $"AP CHECKS: imported {imported} already-checked server locations. " +
                $"Local sent cache now has {LaikaMod.SessionState.SentLocationIds.Count} checks."
            );
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"AP CHECKS: failed to import server checked locations:\n{ex}");
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

    private const string SuppressPrintJsonLine = "\u0001SUPPRESS_AP_PRINTJSON_LINE";

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

    private object TryReadObjectPropertyOrField(object instance, params string[] names)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string name in names)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (property != null)
            {
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

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field != null)
            {
                try
                {
                    object value = field.GetValue(instance);
                    if (value != null)
                        return value;
                }
                catch
                {
                }
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
                    pendingItem.SetApItemId(apItemId);

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

    public void ForceReconcileReceivedItems(string sourceTag)
    {
        try
        {
            if (session == null || session.Items == null)
            {
                LaikaMod.LogInfo($"{sourceTag}: AP reconcile skipped because session/items are not ready.");
                return;
            }

            var allItems = session.Items.AllItemsReceived;

            if (allItems == null)
            {
                LaikaMod.LogInfo($"{sourceTag}: AP reconcile skipped because AllItemsReceived is null.");
                return;
            }

            LaikaMod.LogInfo($"{sourceTag}: forcing AP received-item reconciliation after scene/inventory reload.");

            ReconcileImportantReceivedItems(allItems);
            LaikaMod.ProcessPendingItemQueue(sourceTag + "/APSceneReconcile");
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"{sourceTag}: ForceReconcileReceivedItems failed:\n{ex}");
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
        object value = ReadObjectProperty(instance, propertyNames);

        if (value == null)
            return string.Empty;

        return value.ToString();
    }

    private object ReadObjectProperty(object instance, params string[] propertyNames)
    {
        if (instance == null)
            return null;

        Type type = instance.GetType();

        foreach (string propertyName in propertyNames)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                continue;

            var property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return property.GetValue(instance, null);
                }
                catch
                {
                }
            }

            var field = type.GetField(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            if (field != null)
            {
                try
                {
                    return field.GetValue(instance);
                }
                catch
                {
                }
            }
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
                itemId = TryParseLongText(text);
                itemOwnerSlot = part.Value<int?>("player") ?? -1;
                itemColorHex = ResolveOverlayItemColorHex(itemId, color);
                itemTextFromPacket = text;
            }
            else if (normalizedType == "locationid")
            {
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

    private void OnPacketReceived(ArchipelagoPacketBase packet)
    {
        if (packet == null)
            return;

        try
        {
            string cmd = ReadStringProperty(packet, "Cmd", "cmd");
            string packetType = ReadStringProperty(packet, "PacketType");
            string messageType = ReadStringProperty(packet, "MessageType");

            LaikaMod.LogInfo(
                $"AP PACKET DEBUG: received packet type={packet.GetType().FullName}, " +
                $"cmd='{cmd}', packetType='{packetType}', messageType='{messageType}'"
            );

            bool isSetReply =
                string.Equals(cmd, "SetReply", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(packetType, "SetReply", StringComparison.OrdinalIgnoreCase) ||
                packet.GetType().Name.IndexOf("SetReply", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isSetReply)
            {
                object keyObject = ReadObjectProperty(packet, "Key", "key");
                object valueObject = ReadObjectProperty(packet, "Value", "value");

                LaikaMod.LogInfo(
                    $"UT MAP SETREPLY: key={keyObject}, value={valueObject}, packetType={packet.GetType().FullName}"
                );

                return;
            }

            bool isPrintJson =
                string.Equals(cmd, "PrintJSON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(packetType, "PrintJSON", StringComparison.OrdinalIgnoreCase) ||
                packet.GetType().Name.IndexOf("PrintJson", StringComparison.OrdinalIgnoreCase) >= 0 ||
                packet.GetType().Name.IndexOf("PrintJSON", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isPrintJson)
            {
                HandlePrintJsonPacket(packet);
                return;
            }

            bool isReceivedItems =
                string.Equals(cmd, "ReceivedItems", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(packetType, "ReceivedItems", StringComparison.OrdinalIgnoreCase) ||
                packet.GetType().Name.IndexOf("ReceivedItems", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isReceivedItems)
            {
                LaikaMod.RequestReceivedItemPump("ReceivedItems packet");
                return;
            }

            if (string.IsNullOrWhiteSpace(cmd))
            {
                DumpObjectShape("AP PACKET DEBUG: packet with blank cmd", packet);
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

    private object TryInvokeAny(object target, string[] methodNames, params object[] args)
    {
        if (target == null || methodNames == null)
            return null;

        Type type = target.GetType();

        foreach (string methodName in methodNames)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length != args.Length)
                    continue;

                try
                {
                    object[] convertedArgs = new object[args.Length];

                    for (int i = 0; i < args.Length; i++)
                        convertedArgs[i] = ConvertValueForMember(args[i], parameters[i].ParameterType);

                    object result = method.Invoke(target, convertedArgs);

                    if (result != null)
                        return result;
                }
                catch
                {
                    // Try another overload.
                }
            }
        }

        return null;
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

    public bool SendUniversalTrackerRegion(string regionName, int mapIndex)
    {
        if (session == null)
        {
            LaikaMod.LogWarning($"UT MAP: cannot send current region={regionName}; AP session is null.");
            return false;
        }

        if (LaikaMod.SessionState == null ||
            LaikaMod.SessionState.Connection == null ||
            !LaikaMod.SessionState.Connection.IsAuthenticated)
        {
            LaikaMod.LogWarning($"UT MAP: cannot send current region={regionName}; AP connection is not authenticated.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(regionName))
            return false;

        try
        {
            int protocolTeam = LaikaMod.SessionState.Connection.Team; // Usually 0 for Team #1
            int displayTeam = protocolTeam + 1;                       // Usually 1 for Team #1
            int slot = LaikaMod.SessionState.Connection.Slot;

            JObject mapPayload = new JObject
            {
                ["index"] = mapIndex,
                ["region"] = regionName,
                ["nonce"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            string protocolKey = $"laika_current_region_{protocolTeam}_{slot}";
            string displayKey = $"laika_current_region_{displayTeam}_{slot}";

            bool protocolSent = SendDataStorageReplaceValue(protocolKey, mapPayload);
            bool displaySent = true;

            if (!string.Equals(protocolKey, displayKey, StringComparison.OrdinalIgnoreCase))
                displaySent = SendDataStorageReplaceValue(displayKey, mapPayload);

            if (!protocolSent && !displaySent)
            {
                LaikaMod.LogWarning(
                    $"UT MAP: data storage Set packet was not sent. keys={protocolKey}, {displayKey}, value={mapPayload.ToString(Newtonsoft.Json.Formatting.None)}"
                );

                return false;
            }

            LaikaMod.LogInfo(
                $"UT MAP: sent data storage Set packet keys {protocolKey} and {displayKey} = " +
                $"{mapPayload.ToString(Newtonsoft.Json.Formatting.None)} " +
                $"(AP protocol team={protocolTeam}, display team={displayTeam}, slot={slot})"
            );

            return true;
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"UT MAP: failed to send current region={regionName}\n{ex}");
            return false;
        }
    }

    private bool SendDataStorageReplaceValue(string key, object value)
    {
        // Archipelago.MultiClient.Net has changed API shapes across versions.
        // This reflection path avoids hard-binding us to one exact helper API.
        Type setPacketType = Type.GetType(
            "Archipelago.MultiClient.Net.Packets.SetPacket, Archipelago.MultiClient.Net"
        );

        if (setPacketType == null)
        {
            LaikaMod.LogWarning("UT MAP: SetPacket type was not found in Archipelago.MultiClient.Net.");
            return false;
        }

        object packet = Activator.CreateInstance(setPacketType);

        TrySetPacketMember(packet, "Key", key);
        TrySetPacketMember(packet, "key", key);

        JObject defaultPayload = new JObject
        {
            ["index"] = 0,
            ["region"] = "Start / Tutorial Area",
            ["nonce"] = 0
        };

        TrySetPacketMember(packet, "DefaultValue", defaultPayload);
        TrySetPacketMember(packet, "Default", defaultPayload);
        TrySetPacketMember(packet, "default", defaultPayload);

        TrySetPacketMember(packet, "WantReply", true);
        TrySetPacketMember(packet, "want_reply", true);

        object operation = CreateReplaceOperation(value, setPacketType);

        if (operation == null)
        {
            LaikaMod.LogWarning("UT MAP: failed to create SetPacket replace operation.");
            return false;
        }

        bool operationsSet = SetOperationsMember(packet, operation);

        if (!operationsSet)
        {
            LaikaMod.LogWarning("UT MAP: failed to attach SetPacket operations.");
            return false;
        }

        // DumpObjectShape("UT MAP DEBUG: SetPacket after setup", packet);
        // DumpObjectShape("UT MAP DEBUG: SetPacket operation after setup", operation);

        ArchipelagoPacketBase typedPacket = packet as ArchipelagoPacketBase;

        if (typedPacket == null)
        {
            LaikaMod.LogWarning("UT MAP: SetPacket was not an ArchipelagoPacketBase.");
            return false;
        }

        session.Socket.SendPacket(typedPacket);
        return true;
    }

    private object CreateReplaceOperation(object value, Type setPacketType)
    {
        Type operationType = GetSetPacketOperationElementType(setPacketType);

        if (operationType == null)
        {
            LaikaMod.LogWarning("UT MAP: could not determine SetPacket operation element type.");
            return null;
        }

        object operation = TryCreateReplaceOperationByConstructor(operationType, value);

        if (operation != null)
        {
            LaikaMod.LogInfo($"UT MAP DEBUG: created replace operation by constructor. Type={operationType.FullName}");
            return operation;
        }

        operation = TryCreateReplaceOperationByMembers(operationType, value);

        if (operation != null)
        {
            //LaikaMod.LogInfo($"UT MAP DEBUG: created replace operation by member assignment. Type={operationType.FullName}");
            return operation;
        }

        DumpOperationTypeShape(operationType);

        LaikaMod.LogWarning(
            $"UT MAP: failed to create replace operation. OperationType={operationType.FullName}"
        );

        return null;
    }

    private Type GetSetPacketOperationElementType(Type setPacketType)
    {
        if (setPacketType == null)
            return null;

        var operationsMember =
            setPacketType.GetProperty("Operations") ??
            setPacketType.GetProperty("operations");

        if (operationsMember != null)
        {
            Type operationsType = operationsMember.PropertyType;

            if (operationsType.IsArray)
                return operationsType.GetElementType();

            if (operationsType.IsGenericType)
            {
                Type[] args = operationsType.GetGenericArguments();

                if (args.Length == 1)
                    return args[0];
            }
        }

        var operationsField =
            setPacketType.GetField("Operations") ??
            setPacketType.GetField("operations");

        if (operationsField != null)
        {
            Type operationsType = operationsField.FieldType;

            if (operationsType.IsArray)
                return operationsType.GetElementType();

            if (operationsType.IsGenericType)
            {
                Type[] args = operationsType.GetGenericArguments();

                if (args.Length == 1)
                    return args[0];
            }
        }

        return Type.GetType(
            "Archipelago.MultiClient.Net.Models.OperationSpecification, Archipelago.MultiClient.Net"
        );
    }

    private object TryCreateReplaceOperationByConstructor(Type operationType, object value)
    {
        if (operationType == null)
            return null;

        foreach (ConstructorInfo constructor in operationType.GetConstructors())
        {
            ParameterInfo[] parameters = constructor.GetParameters();

            if (parameters.Length != 2)
                continue;

            try
            {
                object firstArg = BuildReplaceOperationValue(parameters[0].ParameterType);
                object secondArg = ConvertValueForMember(value, parameters[1].ParameterType);

                if (firstArg == null)
                    continue;

                object operation = constructor.Invoke(new object[] { firstArg, secondArg });

                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: OperationSpecification constructor matched: " +
                    $"{parameters[0].ParameterType.FullName}, {parameters[1].ParameterType.FullName}"
                );

                return operation;
            }
            catch
            {
                // Try next constructor shape.
            }

            try
            {
                object firstArg = ConvertValueForMember(value, parameters[0].ParameterType);
                object secondArg = BuildReplaceOperationValue(parameters[1].ParameterType);

                if (secondArg == null)
                    continue;

                object operation = constructor.Invoke(new object[] { firstArg, secondArg });

                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: OperationSpecification constructor matched reversed: " +
                    $"{parameters[0].ParameterType.FullName}, {parameters[1].ParameterType.FullName}"
                );

                return operation;
            }
            catch
            {
                // Try next constructor shape.
            }
        }

        return null;
    }

    private object TryCreateReplaceOperationByMembers(Type operationType, object value)
    {
        if (operationType == null)
            return null;

        object operation = Activator.CreateInstance(operationType);

        bool operationSet =
            TrySetOperationMember(operation, "Operation", "replace") ||
            TrySetOperationMember(operation, "operation", "replace") ||
            TrySetOperationMember(operation, "OperationType", "replace") ||
            TrySetOperationMember(operation, "operationType", "replace") ||
            TrySetOperationMember(operation, "Type", "replace") ||
            TrySetOperationMember(operation, "type", "replace");

        if (!operationSet)
            return null;

        bool valueSet =
            TrySetPacketMember(operation, "Value", value) ||
            TrySetPacketMember(operation, "value", value);

        if (!valueSet)
            return null;

        return operation;
    }

    private bool TrySetOperationMember(object target, string name, string operationName)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
            return false;

        Type type = target.GetType();

        var property = type.GetProperty(name);
        if (property != null && property.CanWrite)
        {
            object converted = BuildReplaceOperationValue(property.PropertyType);

            if (converted == null)
                return false;

            property.SetValue(target, converted, null);
            return true;
        }

        var field = type.GetField(name);
        if (field != null)
        {
            object converted = BuildReplaceOperationValue(field.FieldType);

            if (converted == null)
                return false;

            field.SetValue(target, converted);
            return true;
        }

        return false;
    }

    private bool TrySetDataStorageOperationReplace(object operation)
    {
        if (operation == null)
            return false;

        Type operationObjectType = operation.GetType();

        var operationProperty =
            operationObjectType.GetProperty("Operation") ??
            operationObjectType.GetProperty("operation");

        if (operationProperty != null && operationProperty.CanWrite)
        {
            object replaceValue = BuildReplaceOperationValue(operationProperty.PropertyType);

            if (replaceValue != null)
            {
                operationProperty.SetValue(operation, replaceValue, null);
                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: set operation property {operationProperty.Name} " +
                    $"to {replaceValue} ({operationProperty.PropertyType.FullName})"
                );
                return true;
            }
        }

        var operationField =
            operationObjectType.GetField("Operation") ??
            operationObjectType.GetField("operation");

        if (operationField != null)
        {
            object replaceValue = BuildReplaceOperationValue(operationField.FieldType);

            if (replaceValue != null)
            {
                operationField.SetValue(operation, replaceValue);
                LaikaMod.LogInfo(
                    $"UT MAP DEBUG: set operation field {operationField.Name} " +
                    $"to {replaceValue} ({operationField.FieldType.FullName})"
                );
                return true;
            }
        }

        return false;
    }

    private object BuildReplaceOperationValue(Type targetType)
    {
        if (targetType == null)
            return null;

        if (targetType == typeof(string))
            return "replace";

        if (typeof(Newtonsoft.Json.Linq.JToken).IsAssignableFrom(targetType))
            return Newtonsoft.Json.Linq.JToken.FromObject("replace");

        if (targetType.IsEnum)
        {
            foreach (string enumName in Enum.GetNames(targetType))
            {
                if (string.Equals(enumName, "Replace", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(enumName, "DataStorageOperationType.Replace", StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.Parse(targetType, enumName);
                }
            }

            foreach (string enumName in Enum.GetNames(targetType))
            {
                if (enumName.IndexOf("Replace", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    enumName.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return Enum.Parse(targetType, enumName);
                }
            }

            LaikaMod.LogWarning(
                "UT MAP: could not find Replace enum value. Available operation enum values: " +
                string.Join(", ", Enum.GetNames(targetType))
            );

            return null;
        }

        return null;
    }

    private void DumpOperationTypeShape(Type operationType)
    {
        if (operationType == null)
            return;

        try
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"UT MAP DEBUG: operation type shape for {operationType.FullName}");

            sb.AppendLine("Constructors:");
            foreach (ConstructorInfo constructor in operationType.GetConstructors())
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                List<string> parts = new List<string>();

                foreach (ParameterInfo parameter in parameters)
                {
                    parts.Add($"{parameter.ParameterType.FullName} {parameter.Name}");
                }

                sb.AppendLine("  .ctor(" + string.Join(", ", parts.ToArray()) + ")");
            }

            sb.AppendLine("Properties:");
            foreach (PropertyInfo property in operationType.GetProperties())
            {
                sb.AppendLine(
                    $"  {property.PropertyType.FullName} {property.Name} CanWrite={property.CanWrite}"
                );
            }

            sb.AppendLine("Fields:");
            foreach (FieldInfo field in operationType.GetFields())
            {
                sb.AppendLine($"  {field.FieldType.FullName} {field.Name}");
            }

            LaikaMod.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"UT MAP DEBUG: failed to dump operation type shape.\n{ex}");
        }
    }

    private bool SetOperationsMember(object packet, object operation)
    {
        if (packet == null || operation == null)
            return false;

        Type packetType = packet.GetType();

        var operationsProperty =
            packetType.GetProperty("Operations") ??
            packetType.GetProperty("operations");

        if (operationsProperty != null && operationsProperty.CanWrite)
        {
            Type operationsType = operationsProperty.PropertyType;

            if (operationsType.IsArray)
            {
                Array array = Array.CreateInstance(operation.GetType(), 1);
                array.SetValue(operation, 0);
                operationsProperty.SetValue(packet, array, null);
                return true;
            }

            if (operationsType.IsGenericType)
            {
                object list = Activator.CreateInstance(operationsType);
                var addMethod = operationsType.GetMethod("Add");

                if (addMethod != null)
                {
                    addMethod.Invoke(list, new object[] { operation });
                    operationsProperty.SetValue(packet, list, null);
                    return true;
                }
            }
        }

        var operationsField =
            packetType.GetField("Operations") ??
            packetType.GetField("operations");

        if (operationsField != null)
        {
            Type operationsType = operationsField.FieldType;

            if (operationsType.IsArray)
            {
                Array array = Array.CreateInstance(operation.GetType(), 1);
                array.SetValue(operation, 0);
                operationsField.SetValue(packet, array);
                return true;
            }

            if (operationsType.IsGenericType)
            {
                object list = Activator.CreateInstance(operationsType);
                var addMethod = operationsType.GetMethod("Add");

                if (addMethod != null)
                {
                    addMethod.Invoke(list, new object[] { operation });
                    operationsField.SetValue(packet, list);
                    return true;
                }
            }
        }

        LaikaMod.LogWarning(
            $"UT MAP: failed to attach operation to SetPacket. PacketType={packetType.FullName}, OperationType={operation.GetType().FullName}"
        );

        return false;
    }

    private bool TrySetPacketMember(object target, string name, object value)
    {
        if (target == null || string.IsNullOrWhiteSpace(name))
            return false;

        Type type = target.GetType();

        var property = type.GetProperty(name);
        if (property != null && property.CanWrite)
        {
            object convertedValue = ConvertValueForMember(value, property.PropertyType);
            property.SetValue(target, convertedValue, null);
            return true;
        }

        var field = type.GetField(name);
        if (field != null)
        {
            object convertedValue = ConvertValueForMember(value, field.FieldType);
            field.SetValue(target, convertedValue);
            return true;
        }

        return false;
    }

    private object ConvertValueForMember(object value, Type targetType)
    {
        if (targetType == null)
            return value;

        if (value == null)
            return null;

        if (targetType.IsInstanceOfType(value))
            return value;

        // Archipelago.MultiClient.Net's SetPacket operations use JToken for JSON values.
        if (typeof(Newtonsoft.Json.Linq.JToken).IsAssignableFrom(targetType))
        {
            return Newtonsoft.Json.Linq.JToken.FromObject(value);
        }

        // Nullable<T> support, just in case one packet shape uses nullable fields.
        Type nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType != null)
        {
            return Convert.ChangeType(value, nullableType);
        }

        // Basic primitive conversion fallback.
        if (targetType.IsEnum)
        {
            return Enum.Parse(targetType, value.ToString());
        }

        return Convert.ChangeType(value, targetType);
    }

    private void DumpObjectShape(string label, object target)
    {
        if (target == null)
        {
            LaikaMod.LogInfo($"{label}: <null>");
            return;
        }

        try
        {
            Type type = target.GetType();
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"{label}: type={type.FullName}");

            sb.AppendLine("Properties:");
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = null;
                string valueText = "<unread>";

                try
                {
                    if (property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(target, null);
                        valueText = value == null ? "<null>" : value.ToString();
                    }
                }
                catch
                {
                }

                sb.AppendLine(
                    $"  {property.PropertyType.FullName} {property.Name} CanWrite={property.CanWrite} Value={valueText}"
                );
            }

            sb.AppendLine("Fields:");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value = null;
                string valueText = "<unread>";

                try
                {
                    value = field.GetValue(target);
                    valueText = value == null ? "<null>" : value.ToString();
                }
                catch
                {
                }

                sb.AppendLine(
                    $"  {field.FieldType.FullName} {field.Name} Value={valueText}"
                );
            }

            LaikaMod.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning($"{label}: failed to dump object shape.\n{ex}");
        }
    }
}