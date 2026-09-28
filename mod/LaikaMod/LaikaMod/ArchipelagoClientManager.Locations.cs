using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using System;

public partial class ArchipelagoClientManager
{
    // Location check sending.
    // Sends Laika AP location checks to the connected Archipelago session.
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

            // Presentation is deliberately separate from the actual location check.
            // If this location sent an item to another player, show that later when
            // the normal in-game UI is no longer busy.
            LaikaMod.QueueSentLocationPresentation(definition);
        }
        catch (Exception ex)
        {
            LaikaMod.LogError(
                $"AP CHECKS: failed to send location check -> " +
                $"{definition.DisplayName} ({definition.LocationId})\n{ex}"
            );
        }
    }

    // ===== Shared AP location preview/scouting cache =====
    // Renato, normal shops, the travelling merchant, and sent-item presentation
    // all share this one serialized scout pipeline. Archipelago.MultiClient.Net
    // warns that overlapping ScoutLocationsAsync calls can overwrite callbacks,
    // so only one request may be in flight at a time.

    private ArchipelagoSession locationPreviewSession;
    private readonly Dictionary<long, APLocationPreview> locationPreviewCache =
        new Dictionary<long, APLocationPreview>();

    private Task<Dictionary<long, APLocationPreview>> locationPreviewRequest;
    private long[] locationPreviewRequestIds;
    private DateTime locationPreviewRetryAfter;
    private string locationPreviewRequestSource;

    private void ResetLocationPreviewCacheIfSessionChanged()
    {
        if (ReferenceEquals(locationPreviewSession, session))
            return;

        locationPreviewSession = session;
        locationPreviewCache.Clear();
        locationPreviewRequest = null;
        locationPreviewRequestIds = null;
        locationPreviewRetryAfter = DateTime.MinValue;
        locationPreviewRequestSource = null;
    }

    private void PumpLocationPreviewRequest()
    {
        ResetLocationPreviewCacheIfSessionChanged();

        if (locationPreviewRequest == null || !locationPreviewRequest.IsCompleted)
            return;

        Dictionary<long, APLocationPreview> result =
            locationPreviewRequest.GetAwaiter().GetResult();

        string completedSource = locationPreviewRequestSource;
        long[] completedIds = locationPreviewRequestIds;

        locationPreviewRequest = null;
        locationPreviewRequestIds = null;
        locationPreviewRequestSource = null;

        if (result == null)
        {
            locationPreviewRetryAfter = DateTime.UtcNow.AddSeconds(5);
            return;
        }

        foreach (KeyValuePair<long, APLocationPreview> kvp in result)
        {
            if (kvp.Value != null)
                locationPreviewCache[kvp.Key] = kvp.Value;
        }

        if (completedIds != null)
        {
            int missing = 0;

            foreach (long locationId in completedIds)
            {
                if (!locationPreviewCache.ContainsKey(locationId))
                    missing++;
            }

            if (missing > 0)
            {
                LaikaMod.LogWarning(
                    $"{completedSource ?? "AP PREVIEW"}: scout completed but " +
                    $"{missing}/{completedIds.Length} requested location(s) had no preview data."
                );
            }
        }
    }

    internal bool TryGetCachedLocationPreview(
        long locationId,
        out APLocationPreview preview)
    {
        preview = null;

        if (!IsConnected)
            return false;

        PumpLocationPreviewRequest();
        return locationPreviewCache.TryGetValue(locationId, out preview);
    }

    // Starts a scout for one location only if it is not already cached.
    // allowAutomaticHint=false is used for cosmetic follow-up presentation after
    // a location has already been checked, so those scouts never create hints.
    internal APLocationPreview GetLocationPreview(
        long locationId,
        bool allowAutomaticHint,
        string sourceTag)
    {
        if (!IsConnected || locationId <= 0)
            return null;

        PumpLocationPreviewRequest();

        APLocationPreview cached;
        if (locationPreviewCache.TryGetValue(locationId, out cached))
            return cached;

        if (locationPreviewRequest == null &&
            DateTime.UtcNow >= locationPreviewRetryAfter)
        {
            HintCreationPolicy policy =
                allowAutomaticHint && LaikaMod.WorldOptions.AutomaticPurchaseHints
                    ? HintCreationPolicy.CreateAndAnnounceOnce
                    : HintCreationPolicy.None;

            StartLocationPreviewRequest(
                new long[] { locationId },
                policy,
                sourceTag
            );
        }

        return null;
    }

    // Prime all currently-visible/currently-stocked shop locations in one request.
    // The caller decides which locations are actually visible before calling this,
    // which prevents locked future shop stock from being spoiled or auto-hinted.
    internal void PrimeLocationPreviews(
        IEnumerable<long> locationIds,
        bool allowAutomaticHints,
        string sourceTag)
    {
        if (!IsConnected || locationIds == null)
            return;

        PumpLocationPreviewRequest();

        if (locationPreviewRequest != null ||
            DateTime.UtcNow < locationPreviewRetryAfter)
        {
            return;
        }

        long[] needed = locationIds
            .Where(id => id > 0 && !locationPreviewCache.ContainsKey(id))
            .Distinct()
            .ToArray();

        if (needed.Length == 0)
            return;

        HintCreationPolicy policy =
            allowAutomaticHints && LaikaMod.WorldOptions.AutomaticPurchaseHints
                ? HintCreationPolicy.CreateAndAnnounceOnce
                : HintCreationPolicy.None;

        StartLocationPreviewRequest(needed, policy, sourceTag);
    }

    internal bool AreLocationPreviewsReady(IEnumerable<long> locationIds)
    {
        if (locationIds == null)
            return true;

        if (!IsConnected)
            return false;

        PumpLocationPreviewRequest();

        foreach (long locationId in locationIds)
        {
            if (locationId > 0 && !locationPreviewCache.ContainsKey(locationId))
                return false;
        }

        return true;
    }

    private void StartLocationPreviewRequest(
        long[] locationIds,
        HintCreationPolicy policy,
        string sourceTag)
    {
        if (locationIds == null || locationIds.Length == 0 || session == null)
            return;

        locationPreviewRequestIds = locationIds;
        locationPreviewRequestSource =
            string.IsNullOrWhiteSpace(sourceTag) ? "AP PREVIEW" : sourceTag;

        LaikaMod.LogInfo(
            $"{locationPreviewRequestSource}: scouting {locationIds.Length} location(s), " +
            $"hintPolicy={policy}, ids=[{string.Join(",", locationIds)}]"
        );

        locationPreviewRequest = FetchLocationPreviews(
            session,
            locationIds,
            policy,
            locationPreviewRequestSource
        );
    }

    private static async Task<Dictionary<long, APLocationPreview>> FetchLocationPreviews(
        ArchipelagoSession sourceSession,
        long[] locationIds,
        HintCreationPolicy hintPolicy,
        string sourceTag)
    {
        DateTime startedAtUtc = DateTime.UtcNow;

        try
        {
            var results = await sourceSession.Locations.ScoutLocationsAsync(
                hintPolicy,
                locationIds
            ).ConfigureAwait(false);

            double elapsedMs =
                (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            LaikaMod.LogInfo(
                $"{sourceTag}: scout completed for {locationIds.Length} location(s) " +
                $"in {elapsedMs:F0} ms."
            );

            var previews = new Dictionary<long, APLocationPreview>();

            foreach (long locationId in locationIds)
            {
                if (!results.ContainsKey(locationId))
                    continue;

                var item = results[locationId];

                if (item == null ||
                    item.Player == null ||
                    string.IsNullOrWhiteSpace(item.ItemName))
                {
                    continue;
                }

                previews[locationId] = new APLocationPreview(
                    locationId,
                    item.ItemId,
                    PreviewPlainText(item.ItemName),
                    item.Player.Slot,
                    PreviewPlainText(item.Player.Alias)
                );
            }

            return previews;
        }
        catch (Exception ex)
        {
            double elapsedMs =
                (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            LaikaMod.LogWarning(
                $"{sourceTag}: scout failed for {locationIds.Length} location(s) " +
                $"after {elapsedMs:F0} ms.\n{ex}"
            );

            // No Unity access from this continuation.
            return null;
        }
    }

    private static string PreviewPlainText(string value)
    {
        // Prevent item/player names from injecting TextMeshPro formatting.
        return (value ?? "Unknown player")
            .Replace('<', '‹')
            .Replace('>', '›')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    // Backwards-compatible Renato wrapper so the existing popup patch remains small.
    internal string GetRenatoPreview(long locationId)
    {
        APLocationPreview preview = GetLocationPreview(
            locationId,
            true,
            "RENATO AP PREVIEW"
        );

        return preview != null ? preview.DisplayText : null;
    }
}
