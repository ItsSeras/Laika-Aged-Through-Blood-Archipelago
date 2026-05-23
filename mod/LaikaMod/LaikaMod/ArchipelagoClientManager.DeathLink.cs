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
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"AP DEATHLINK: failed to apply incoming death:\n{ex}");
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