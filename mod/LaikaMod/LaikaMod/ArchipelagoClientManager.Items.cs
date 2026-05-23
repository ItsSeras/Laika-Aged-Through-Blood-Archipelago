using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public partial class ArchipelagoClientManager
{
    // Received item handling and reconciliation.
    // Processes AP ReceivedItems packets and restores important AP state after reconnects or scene loads.
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
}