using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public partial class LaikaMod
{
    // Local cache for AP slot_data values pushed from the AP server.
    // For now this is just a tiny text file so I can test option flow before full live networking is finished.
    internal static string SlotDataFilePath =
        Path.Combine(Paths.ConfigPath, "laika_ap_slot_data.txt");

    internal static APSaveState PeekSessionStateForSlot(int slotIndex)
    {
        try
        {
            string path = GetAPStatePathForSlot(slotIndex);

            if (!File.Exists(path))
            {
                return new APSaveState
                {
                    SaveSlotIndex = slotIndex,
                    APEnabled = false,
                    Connection = new APConnectionState()
                };
            }

            string json = File.ReadAllText(path);
            APSaveState state = JsonConvert.DeserializeObject<APSaveState>(json) ?? new APSaveState();

            state.SaveSlotIndex = slotIndex;
            if (state.Connection == null)
                state.Connection = new APConnectionState();

            if (state.SentLocationIds == null)
                state.SentLocationIds = new List<long>();

            return state;
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to peek AP session state for slot {slotIndex}:\n{ex}");
            return new APSaveState
            {
                SaveSlotIndex = slotIndex,
                APEnabled = false,
                Connection = new APConnectionState()
            };
        }
    }

    internal static string BuildSaveSlotAPSummary(int slotIndex)
    {
        APSaveState state = PeekSessionStateForSlot(slotIndex);

        if (state == null || !state.APEnabled)
            return "[AP Off]";

        bool ready =
            state.Connection != null &&
            !string.IsNullOrWhiteSpace(state.Connection.Host) &&
            state.Connection.Port > 0 &&
            !string.IsNullOrWhiteSpace(state.Connection.SlotName);

        if (!ready)
            return "[AP ON]";

        return $"[AP Ready: {state.Connection.SlotName}]";
    }

    internal static string BuildSaveSlotAPSummaryShort(int slotIndex)
    {
        APSaveState state = PeekSessionStateForSlot(slotIndex);

        if (state == null || !state.APEnabled)
            return "AP OFF";

        bool ready =
            state.Connection != null &&
            !string.IsNullOrWhiteSpace(state.Connection.Host) &&
            state.Connection.Port > 0 &&
            !string.IsNullOrWhiteSpace(state.Connection.SlotName);

        if (!ready)
            return "ADDITIONAL AP SETUP REQUIRED";

        return "AP Enabled: " + state.Connection.SlotName.ToUpperInvariant();
    }

    internal static Color GetSaveSlotAPColor(int slotIndex)
    {
        APSaveState state = PeekSessionStateForSlot(slotIndex);

        if (state == null || !state.APEnabled)
        {
            return new Color(0.48f, 0.56f, 0.66f, 1f); // stronger cool gray-blue
        }

        bool ready =
            state.Connection != null &&
            !string.IsNullOrWhiteSpace(state.Connection.Host) &&
            state.Connection.Port > 0 &&
            !string.IsNullOrWhiteSpace(state.Connection.SlotName);

        if (!ready)
        {
            return new Color(0.8627452f, 0.07843138f, 0.2352941f, 1f); // crimson
        }

        return new Color(0.25f, 1.00f, 0.52f, 1f); // brighter green
    }

    internal static bool SuppressTitleUIForSlotLoad = false;
    internal static float SuppressTitleUIUntil = 0f;

    internal static void BindToGameSaveSlot(int slotIndex, string reason, bool autoConnectIfConfigured = false)
    {
        try
        {
            if (slotIndex < 0)
            {
                LogWarning($"AP SLOT BIND ignored because slotIndex was invalid: {slotIndex}");
                return;
            }

            bool slotChanged = ActiveSaveSlotIndex != slotIndex;

            LogInfo(
                $"AP SLOT BIND requested. " +
                $"OldSlot={ActiveSaveSlotIndex}, NewSlot={slotIndex}, Reason={reason}, AutoConnect={autoConnectIfConfigured}"
            );

            if (slotChanged && ArchipelagoClientManager.Instance != null)
            {
                if (ArchipelagoClientManager.Instance.IsConnected || ArchipelagoClientManager.Instance.IsConnecting)
                {
                    ArchipelagoClientManager.Instance.Disconnect(
                        $"Switching AP context from slot {ActiveSaveSlotIndex} to slot {slotIndex}"
                    );
                }
            }

            ActiveSaveSlotIndex = slotIndex;

            LoadSessionStateForSlot(slotIndex);

            if (ActiveDevOverlayController != null)
            {
                ActiveDevOverlayController.ReloadConnectionInputFieldsFromSession();
            }

            // Reset per-runtime-only counters so one slot does not leak into another.
            LocalDeathsThisSession = 0;
            DeathsSinceLastDeathLink = 0;
            SuppressedDeathLinksRemaining = 0;

            // Clear any pending runtime-only grants when changing slot context.
            PendingItemQueue.Clear();
            IsProcessingQueue = false;

            if (SessionState != null && SessionState.APEnabled)
            {
                EnqueueRequiredStartingItems();
            }

            // Reset slot_data-derived runtime options until this slot connects and reapplies them.
            WorldOptions = new APWorldOptions();
            HasAppliedLiveSlotData = false;

            // Keep slot index normalized in persisted state.
            SessionState.SaveSlotIndex = slotIndex;
            SaveSessionState();

            RefreshDevOverlay();

            AnnounceAPActivity($"[AP] Bound to Laika save slot {slotIndex + 1}.");

            if (autoConnectIfConfigured)
            {
                SuppressTitleUIForSlotLoad = true;
                SuppressTitleUIUntil = UnityEngine.Time.unscaledTime + 6f;

                if (MainMenuArchipelagoEditionCanvasObject != null)
                    MainMenuArchipelagoEditionCanvasObject.SetActive(false);

                if (TitleAPPanelCanvasObject != null)
                    TitleAPPanelCanvasObject.SetActive(false);

                ConnectActiveSlotIfConfigured();
            }
        }
        catch (Exception ex)
        {
            LogError($"AP SLOT BIND failed for slot {slotIndex}:\n{ex}");
        }
    }

    // Loads APWorld slot_data values from a tiny local cache file.
    // This is a stepping stone until live AP networking fills these values directly after Connect/Connected.
    internal static void LoadWorldOptionsFromLocalSlotData()
    {
        if (HasAppliedLiveSlotData)
        {
            LogInfo("AP local slot_data load skipped because live slot_data is already active.");
            return;
        }

        try
        {
            // Keep the defaults already defined on APWorldOptions unless the file overrides them.
            if (!File.Exists(SlotDataFilePath))
            {
                Log.LogInfo("AP slot_data file not found. Using APWorldOptions defaults.");
                return;
            }

            string[] lines = File.ReadAllLines(SlotDataFilePath);

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();

                if (line.StartsWith("weapon_mode="))
                {
                    string value = line.Substring("weapon_mode=".Length).Trim().ToLowerInvariant();

                    if (value == "direct")
                    {
                        WorldOptions.WeaponMode = WeaponGrantMode.Direct;
                    }
                    else if (value == "crafting")
                    {
                        WorldOptions.WeaponMode = WeaponGrantMode.Crafting;
                    }
                    else
                    {
                        LogWarning($"Unknown weapon_mode in slot_data: {value}");
                    }
                }
                else if (line.StartsWith("death_link="))
                {
                    bool parsedValue;
                    if (bool.TryParse(line.Substring("death_link=".Length), out parsedValue))
                    {
                        WorldOptions.DeathLinkEnabled = parsedValue;
                    }
                }
                else if (line.StartsWith("death_amnesty="))
                {
                    bool parsedValue;
                    if (bool.TryParse(line.Substring("death_amnesty=".Length), out parsedValue))
                    {
                        WorldOptions.DeathAmnestyEnabled = parsedValue;
                    }
                }
                else if (line.StartsWith("death_amnesty_count="))
                {
                    int parsedValue;
                    if (int.TryParse(line.Substring("death_amnesty_count=".Length), out parsedValue))
                    {
                        WorldOptions.DeathAmnestyCount = Math.Max(1, parsedValue);
                    }
                }
            }

            Log.LogInfo(
                "AP slot_data applied. " +
                $"WeaponMode={WorldOptions.WeaponMode}, " +
                $"DeathLink={WorldOptions.DeathLinkEnabled}, " +
                $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled}, " +
                $"DeathAmnestyCount={WorldOptions.DeathAmnestyCount}"
            );
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to load AP slot_data:\n{ex}");
        }
    }

    internal static void LoadSessionStateForSlot(int slotIndex)
    {
        try
        {
            string path = GetAPStatePathForSlot(slotIndex);

            if (!File.Exists(path))
            {
                SessionState = new APSaveState
                {
                    SaveSlotIndex = slotIndex,
                    APEnabled = false
                };

                LogInfo($"AP session state file not found for slot {slotIndex}. Created fresh state.");
                return;
            }

            string json = File.ReadAllText(path);
            SessionState = JsonConvert.DeserializeObject<APSaveState>(json);

            if (SessionState == null)
            {
                SessionState = new APSaveState();
            }

            SessionState.SaveSlotIndex = slotIndex;

            if (SessionState.Connection == null)
            {
                SessionState.Connection = new APConnectionState();
            }

            if (SessionState.Options == null)
            {
                SessionState.Options = new APWorldOptions();
            }

            if (SessionState.SentLocationIds == null)
            {
                SessionState.SentLocationIds = new List<long>();
            }

            LogInfo(
                $"AP session state loaded for slot {slotIndex}. " +
                $"LastReceivedIndex={SessionState.LastProcessedReceivedItemIndex}, " +
                $"SentChecks={SessionState.SentLocationIds.Count}, " +
                $"GoalReported={SessionState.GoalReported}, " +
                $"APEnabled={SessionState.APEnabled}"
            );
        }
        catch (Exception ex)
        {
            LogError($"Failed to load AP session state for slot {slotIndex}:\n{ex}");

            SessionState = new APSaveState
            {
                SaveSlotIndex = slotIndex,
                APEnabled = false
            };
        }
    }

    internal static void SaveSessionStateForSlot(int slotIndex)
    {
        try
        {
            if (SessionState == null)
            {
                SessionState = new APSaveState();
            }

            SessionState.SaveSlotIndex = slotIndex;

            string path = GetAPStatePathForSlot(slotIndex);
            string json = JsonConvert.SerializeObject(SessionState, Formatting.Indented);

            File.WriteAllText(path, json);

            LogInfo(
                $"AP session state saved for slot {slotIndex}. " +
                $"LastReceivedIndex={SessionState.LastProcessedReceivedItemIndex}, " +
                $"SentChecks={SessionState.SentLocationIds.Count}, " +
                $"GoalReported={SessionState.GoalReported}"
            );
        }
        catch (Exception ex)
        {
            LogError($"Failed to save AP session state for slot {slotIndex}:\n{ex}");
        }
    }

    internal static void LoadSessionState()
    {
        LoadSessionStateForSlot(ActiveSaveSlotIndex);
    }

    internal static void SaveSessionState()
    {
        SaveSessionStateForSlot(ActiveSaveSlotIndex);
    }

    internal static string GetAPStateFolder()
    {
        string folder = Path.Combine(Paths.ConfigPath, "LaikaAP");
        Directory.CreateDirectory(folder);
        return folder;
    }

    internal static string GetAPStatePathForSlot(int slotIndex)
    {
        return Path.Combine(GetAPStateFolder(), $"slot{slotIndex}.json");
    }

    internal static void ConnectActiveSlotIfConfigured()
    {
        if (ArchipelagoClientManager.Instance == null)
        {
            LogWarning("AP connect requested, but ArchipelagoClientManager is missing.");
            return;
        }

        if (SessionState == null)
        {
            LogWarning("AP connect requested, but SessionState is null.");
            return;
        }

        if (!SessionState.APEnabled)
        {
            LogWarning($"AP connect requested for slot {ActiveSaveSlotIndex}, but AP is not enabled for this slot.");
            AnnounceAPActivity("[AP] Active slot is not AP-enabled.");
            return;
        }

        if (SessionState.Connection == null)
        {
            LogWarning($"AP connect requested for slot {ActiveSaveSlotIndex}, but Connection is null.");
            AnnounceAPActivity("[AP] Active slot has no connection data.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SessionState.Connection.Host) ||
            string.IsNullOrWhiteSpace(SessionState.Connection.SlotName) ||
            SessionState.Connection.Port <= 0)
        {
            LogWarning($"AP connect requested for slot {ActiveSaveSlotIndex}, but connection settings are incomplete.");
            AnnounceAPActivity("[AP] Active slot connection settings are incomplete.");
            return;
        }

        LogInfo(
            $"AP connect requested for slot {ActiveSaveSlotIndex}: " +
            $"{SessionState.Connection.Host}:{SessionState.Connection.Port} as {SessionState.Connection.SlotName}"
        );

        AnnounceAPActivity(
            $"[AP] Connecting slot {ActiveSaveSlotIndex}: " +
            $"{SessionState.Connection.Host}:{SessionState.Connection.Port}"
        );

        ArchipelagoClientManager.Instance.Connect(
            SessionState.Connection.Host,
            SessionState.Connection.Port,
            SessionState.Connection.SlotName,
            SessionState.Connection.Password
        );
    }

    internal static void MarkLocationCheckedAndSent(long locationId)
    {
        if (!SessionState.SentLocationIds.Contains(locationId))
        {
            SessionState.SentLocationIds.Add(locationId);
            SaveSessionState();
            LogInfo($"AP STATE: marked location as sent -> {locationId}");
        }
    }

    internal static bool HasLocationBeenSent(long locationId)
    {
        return SessionState.SentLocationIds.Contains(locationId);
    }

    internal static void UpdateLastProcessedReceivedItemIndex(int index)
    {
        if (index < 0)
            return;

        SessionState.LastProcessedReceivedItemIndex = index;
        SaveSessionState();

        LogInfo($"AP STATE: updated last processed received item index -> {index}");
    }

    internal static void MarkGoalReported()
    {
        if (SessionState.GoalReported)
            return;

        SessionState.GoalReported = true;
        SaveSessionState();

        LogInfo("AP STATE: goal marked as reported.");
    }

    internal static void ResetRuntimeConnectionState()
    {
        SessionState.Connection = new APConnectionState();

        LogInfo("AP runtime connection state reset.");
    }
}