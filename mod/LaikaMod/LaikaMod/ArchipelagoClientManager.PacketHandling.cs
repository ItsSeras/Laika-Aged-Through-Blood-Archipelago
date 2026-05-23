using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class ArchipelagoClientManager
{
    // Low-level AP packet routing.
    // Routes received Archipelago packets into item pumping, PrintJSON display, DeathLink, and data storage handling.
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
}