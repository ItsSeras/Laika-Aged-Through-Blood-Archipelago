using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

public partial class ArchipelagoClientManager
{
    // Archipelago connection lifecycle.
    // Handles login, session identity validation, connection tags, and disconnect cleanup.
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

    public void SendGoalCompletionStatus(string sourceTag)
    {
        try
        {
            if (!IsConnected || session == null || session.Socket == null)
            {
                LaikaMod.LogWarning($"{sourceTag}: cannot report AP goal because the client is not connected.");
                return;
            }

            if (LaikaMod.SessionState != null && LaikaMod.SessionState.GoalReported)
            {
                LaikaMod.LogInfo($"{sourceTag}: AP goal already reported, skipping duplicate StatusUpdate.");
                return;
            }

            Type packetType = Type.GetType(
                "Archipelago.MultiClient.Net.Packets.StatusUpdatePacket, Archipelago.MultiClient.Net"
            );

            if (packetType == null)
            {
                LaikaMod.LogError($"{sourceTag}: could not find StatusUpdatePacket type.");
                return;
            }

            object packet = null;

            // Prefer a one-argument constructor if the library version exposes one.
            foreach (ConstructorInfo constructor in packetType.GetConstructors())
            {
                ParameterInfo[] parameters = constructor.GetParameters();

                if (parameters.Length != 1)
                    continue;

                try
                {
                    Type parameterType = parameters[0].ParameterType;
                    object statusValue = parameterType.IsEnum
                        ? Enum.ToObject(parameterType, 30)
                        : Convert.ChangeType(30, parameterType);

                    packet = constructor.Invoke(new object[] { statusValue });
                    break;
                }
                catch
                {
                    // Try another constructor.
                }
            }

            // Fallback for versions with a default constructor + Status property/field.
            if (packet == null)
            {
                packet = Activator.CreateInstance(packetType);

                bool statusWasSet = false;

                PropertyInfo statusProperty = packetType.GetProperty(
                    "Status",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                if (statusProperty != null && statusProperty.CanWrite)
                {
                    Type statusType = statusProperty.PropertyType;
                    object statusValue = statusType.IsEnum
                        ? Enum.ToObject(statusType, 30)
                        : Convert.ChangeType(30, statusType);

                    statusProperty.SetValue(packet, statusValue, null);
                    statusWasSet = true;
                }

                if (!statusWasSet)
                {
                    FieldInfo statusField = packetType.GetField(
                        "Status",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );

                    if (statusField == null)
                    {
                        statusField = packetType.GetField(
                            "status",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        );
                    }

                    if (statusField != null)
                    {
                        Type statusType = statusField.FieldType;
                        object statusValue = statusType.IsEnum
                            ? Enum.ToObject(statusType, 30)
                            : Convert.ChangeType(30, statusType);

                        statusField.SetValue(packet, statusValue);
                        statusWasSet = true;
                    }
                }

                if (!statusWasSet)
                {
                    LaikaMod.LogError($"{sourceTag}: could not set StatusUpdatePacket status.");
                    return;
                }
            }

            object socket = session.Socket;
            MethodInfo sendPacketMethod = null;

            foreach (MethodInfo method in socket.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, "SendPacket", StringComparison.OrdinalIgnoreCase))
                    continue;

                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length != 1)
                    continue;

                if (!parameters[0].ParameterType.IsAssignableFrom(packetType))
                    continue;

                sendPacketMethod = method;
                break;
            }

            if (sendPacketMethod == null)
            {
                LaikaMod.LogError($"{sourceTag}: could not find Socket.SendPacket(StatusUpdatePacket).");
                return;
            }

            sendPacketMethod.Invoke(socket, new object[] { packet });

            LaikaMod.MarkGoalReported();
            LaikaMod.AnnounceAPSuccess("[AP] Goal complete! Reporting completion to Archipelago.");
            LaikaMod.LogInfo($"{sourceTag}: sent AP StatusUpdate CLIENT_GOAL.");
        }
        catch (Exception ex)
        {
            LaikaMod.LogError($"{sourceTag}: failed to report AP goal completion:\n{ex}");
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
}