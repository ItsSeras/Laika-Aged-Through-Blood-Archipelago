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
}