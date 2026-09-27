using System.Collections.Generic;
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
        }
        catch (Exception ex)
        {
            LaikaMod.LogError(
                $"AP CHECKS: failed to send location check -> " +
                $"{definition.DisplayName} ({definition.LocationId})\n{ex}"
            );
        }
    }

    // Renato AP item previews.

    private ArchipelagoSession renatoPreviewSession;
    private readonly Dictionary<long, string> renatoPreviewCache =
        new Dictionary<long, string>();
    private Task<string> renatoPreviewRequest;
    private long renatoPreviewLocation;
    private DateTime renatoPreviewRetryAfter;

    // Called only by Unity's main thread. Async work returns plain text only.
    internal string GetRenatoPreview(long locationId)
    {
        if (!IsConnected)
            return null;

        if (!ReferenceEquals(renatoPreviewSession, session))
        {
            renatoPreviewSession = session;
            renatoPreviewCache.Clear();
            renatoPreviewRequest = null;
            renatoPreviewRetryAfter = DateTime.MinValue;
        }

        if (renatoPreviewRequest != null && renatoPreviewRequest.IsCompleted)
        {
            // FetchRenatoPreview catches failures, including abandoned-session failures.
            string result = renatoPreviewRequest.GetAwaiter().GetResult();
            renatoPreviewRequest = null;

            if (result != null)
                renatoPreviewCache[renatoPreviewLocation] = result;
            else
                renatoPreviewRetryAfter = DateTime.UtcNow.AddSeconds(10);
        }

        string cached;
        if (renatoPreviewCache.TryGetValue(locationId, out cached))
            return cached;

        // The bundled client warns against simultaneous ScoutLocationsAsync calls.
        // Keep one outstanding request even if the player closes/reopens the popup.
        if (renatoPreviewRequest == null &&
            DateTime.UtcNow >= renatoPreviewRetryAfter)
        {
            renatoPreviewLocation = locationId;

            HintCreationPolicy hintPolicy =
                LaikaMod.WorldOptions.AutomaticPurchaseHints
                    ? HintCreationPolicy.CreateAndAnnounceOnce
                    : HintCreationPolicy.None;

            LaikaMod.LogInfo(
                $"RENATO AP PREVIEW: scouting location={locationId}, " +
                $"hintPolicy={hintPolicy}"
            );

            renatoPreviewRequest =
                FetchRenatoPreview(session, locationId, hintPolicy);
        }

        return null;
    }

    private static async Task<string> FetchRenatoPreview(
        ArchipelagoSession sourceSession,
        long locationId,
        HintCreationPolicy hintPolicy)
    {
        DateTime startedAtUtc = DateTime.UtcNow;

        try
        {
            var results = await sourceSession.Locations.ScoutLocationsAsync(
                hintPolicy,
                new long[] { locationId }
            ).ConfigureAwait(false);

            double elapsedMs = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            LaikaMod.LogInfo(
                $"RENATO AP PREVIEW: scout completed for {locationId} " +
                $"in {elapsedMs:F0} ms."
            );

            var item = results[locationId];

            if (item == null ||
                item.Player == null ||
                string.IsNullOrWhiteSpace(item.ItemName))
            {
                return null;
            }

            return RenatoPlainText(item.ItemName) + " for " +
                RenatoPlainText(item.Player.Alias);
        }
        catch (Exception ex)
        {
            double elapsedMs = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            LaikaMod.LogWarning(
                $"RENATO AP PREVIEW: scout failed for {locationId} " +
                $"after {elapsedMs:F0} ms.\n{ex}"
            );

            // No Unity access from this continuation. The popup remains usable.
            return null;
        }
    }

    private static string RenatoPlainText(string value)
    {
        // Prevent item/player names from injecting TextMeshPro formatting.
        return (value ?? "Unknown player")
            .Replace('<', '‹')
            .Replace('>', '›')
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }
}
