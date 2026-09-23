using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class ArchipelagoClientManager
{
    // DeathLink packet handling.
    // Sends local DeathLinks and receives remote DeathLinks from Archipelago.
    private void ApplyIncomingDeathLink(Dictionary<string, JToken> dataObject)
    {
        try
        {
            string source = "<unknown>";
            string cause = "DeathLink";

            if (dataObject != null)
            {
                if (dataObject.ContainsKey("source") &&
                    dataObject["source"] != null)
                {
                    source = dataObject["source"].ToString();
                }

                if (dataObject.ContainsKey("cause") &&
                    dataObject["cause"] != null)
                {
                    cause = dataObject["cause"].ToString();
                }
            }

            // Tell the player immediately what was received.
            // Remote DeathLinks use the danger/red presentation, while the
            // originating player's name keeps the standard AP player color.
            LaikaMod.AnnounceIncomingAPDeathLink(
                source,
                cause
            );

            // Do NOT increment SuppressedDeathLinksRemaining here.
            // The actual vanilla death has not happened yet.
            //
            // Queue the kill so LaikaMod.Update can perform it safely
            // on Unity's main thread.
            LaikaMod.QueueIncomingDeathLink(
                source,
                cause
            );

            LaikaMod.LogInfo(
                $"AP DEATHLINK: incoming DeathLink queued. " +
                $"Source={source}, Cause={cause}"
            );
        }
        catch (Exception ex)
        {
            LaikaMod.LogError(
                $"AP DEATHLINK: failed to apply incoming death:\n{ex}"
            );
        }
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