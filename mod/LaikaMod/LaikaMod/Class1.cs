using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.Quests.Goals;
using Laika.UI.InGame.Inventory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class APConnectionState
{
    public string Host = "archipelago.gg";
    public int Port = 38281;
    public string SlotName = "";
    public string Password = "";

    public bool IsConnected = false;
    public bool IsAuthenticated = false;

    public int Team = 0;
    public int Slot = 0;
}

[Serializable]
public class APSaveState
{
    public int SaveSlotIndex = 1;
    public bool APEnabled = false;

    public APConnectionState Connection = new APConnectionState();

    public int LastProcessedReceivedItemIndex = 0;
    public bool GoalReported = false;

    public List<long> SentLocationIds = new List<long>();

    public int SessionDeaths = 0;
    public int DeathsSinceLastDeathLink = 0;
}

[BepInPlugin("com.seras.laikaapprototype", "Laika AP Alpha", "0.01")]
public class LaikaMod : BaseUnityPlugin
{
    // Shared logger for Harmony patches.
    internal static ManualLogSource Log;

    // Queue of pending Archipelago-style received items.
    internal static Queue<PendingItem> PendingItemQueue = new Queue<PendingItem>();

    // Prevent nested queue processing when UI refreshes trigger more hooks.
    internal static bool IsProcessingQueue = false;

    // NOTE: Ingredient & Cassette logging is currently inactive, but kept for future item-id discovery logging.
    // Prevents ingredient IDs from being logged more than once.
    // Without this, every UI refresh would spam the console repeatedly.
    internal static bool IngredientIdsLogged = false;

    // Prevents cassette IDs from logging repeatedly.
    internal static bool CassetteIdsLogged = false;

    // Tracks deaths during the current AP session only.
    internal static int LocalDeathsThisSession = 0;

    // Tracks deaths since the last DeathLink would send.
    internal static int DeathsSinceLastDeathLink = 0;

    // One-shot suppression counter for inbound DeathLink kills.
    // When set to 1, the next detected death will not count toward outbound DeathLink logic.
    internal static int SuppressedDeathLinksRemaining = 0;

    // Runtime AP world options loaded from slot_data cache.
    // Later this can be filled directly from a live AP Connected packet.
    internal static APWorldOptions WorldOptions = new APWorldOptions();

    // Development stress test toggle.
    // I turn this on when I want to force a batch of received items through the queue without needing a live AP send.
    internal static bool EnableDevelopmentStressTest = false;

    // Canvas-based dev overlay objects.
    internal static GameObject DevOverlayCanvasObject;
    internal static Text DevOverlayStatusText;
    internal static Text DevOverlayRecentLogText;

    internal static GameObject DevOverlayControllerObject;
    internal static DevOverlayController ActiveDevOverlayController;

    internal static bool HasAppliedLiveSlotData = false;

    internal static int ActiveSaveSlotIndex = 1;
    internal static APSaveState SessionState = new APSaveState();

    // ===== Startup =====
    private void Awake()
    {
        // Save logger for static patches.
        Log = Logger;

        ActiveSaveSlotIndex = 1;
        LoadSessionState();

        new ArchipelagoClientManager();

        // Try to load APWorld options from local slot-data cache.
        // If nothing exists yet, the defaults already defined on APWorldOptions stay in place.
        // Only fall back to local cached slot_data if live AP slot_data was not applied.
        if (!HasAppliedLiveSlotData)
        {
            LoadWorldOptionsFromLocalSlotData();
        }

        // Make absolutely sure the plugin component is enabled for Update() / OnGUI().
        enabled = true;

        // Confirm plugin loaded.
        Log.LogInfo(
            $"Laika AP Prototype loaded. " +
            $"WeaponMode={WorldOptions.WeaponMode}, " +
            $"DevStress={EnableDevelopmentStressTest}, " +
            $"DeathLink={WorldOptions.DeathLinkEnabled}, " +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled}, " +
            $"DeathAmnestyCount={WorldOptions.DeathAmnestyCount}"
        );

        // Queue any items the player must start with for AP to make sense.
        // Right now that mainly covers cases where vanilla assumes the player always has something.
        EnqueueRequiredStartingItems();

        // Development stress test items.
        if (EnableDevelopmentStressTest)
        {
            EnqueueDevelopmentStressTestItems();
        }

        // Apply all Harmony patches in this file.
        Harmony harmony = new Harmony("com.seras.laikaapprototype");
        harmony.PatchAll();

        Log.LogInfo("Harmony patches applied.");
    }

    // Loads persistent AP session state from disk.
    // If no file exists yet, defaults are kept.
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
            SessionState.Connection.SlotName
        );
    }

    // Local cache for AP slot_data values pushed from the AP server.
    // For now this is just a tiny text file so I can test option flow before full live networking is finished.
    internal static string SlotDataFilePath =
        Path.Combine(Paths.ConfigPath, "laika_ap_slot_data.txt");

    // Canonical AP location registry.
    // Map unlock and quest names should be maintained here as the single source of truth.
    internal static Dictionary<string, APLocationDefinition> LocationDefinitionsByInternalId =
        new Dictionary<string, APLocationDefinition>()
        {
            {
                "M_A_W06",
                new APLocationDefinition(
                    100002L,
                    "Map Piece: Where Our Bikes Growl",
                    "M_A_W06",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W01_BOTTOM",
                new APLocationDefinition(
                    100003L,
                    "Map Piece: Where All Was Lost (Bottom)",
                    "M_A_W01_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W01_TOP",
                new APLocationDefinition(
                    100004L,
                    "Map Piece: Where All Was Lost (Top)",
                    "M_A_W01_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W02",
                new APLocationDefinition(
                    100005L,
                    "Map Piece: Where Doom Fell",
                    "M_A_W02",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_LEFT",
                new APLocationDefinition(
                    100006L,
                    "Map Piece: Where Rust Weaves (Left)",
                    "M_A_W03_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_CENTER",
                new APLocationDefinition(
                    100007L,
                    "Map Piece: Where Rust Weaves (Center)",
                    "M_A_W03_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_RIGHT",
                new APLocationDefinition(
                    100008L,
                    "Map Piece: Where Rust Weaves (Right)",
                    "M_A_W03_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W04_BOTTOM",
                new APLocationDefinition(
                    100009L,
                    "Map Piece: Where Iron Caresses the Sky (Bottom)",
                    "M_A_W04_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W04_TOP",
                new APLocationDefinition(
                    100010L,
                    "Map Piece: Where Iron Caresses the Sky (Top)",
                    "M_A_W04_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W05_LEFT",
                new APLocationDefinition(
                    100011L,
                    "Map Piece: Where the Waves Die (Left)",
                    "M_A_W05_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W05_RIGHT",
                new APLocationDefinition(
                    100012L,
                    "Map Piece: Where the Waves Die (Right)",
                    "M_A_W05_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W07_BOTTOM",
                new APLocationDefinition(
                    100013L,
                    "Map Piece: Where Our Ancestors Rest (Bottom)",
                    "M_A_W07_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W07_TOP",
                new APLocationDefinition(
                    100014L,
                    "Map Piece: Where Our Ancestors Rest (Top)",
                    "M_A_W07_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W08_LEFT",
                new APLocationDefinition(
                    100015L,
                    "Map Piece: Where Birds Came From (Left/Bottom)",
                    "M_A_W08_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W08_RIGHT",
                new APLocationDefinition(
                    100016L,
                    "Map Piece: Where Birds Came From (Right/Top)",
                    "M_A_W08_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D00_LEFT",
                new APLocationDefinition(
                    100017L,
                    "Map Piece: Where Birds Lurk (Left)",
                    "M_A_D00_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D00_RIGHT",
                new APLocationDefinition(
                    100018L,
                    "Map Piece: Where Birds Lurk (Right)",
                    "M_A_D00_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_LEFT",
                new APLocationDefinition(
                    100019L,
                    "Map Piece: Where Rock Bleeds (Left)",
                    "M_A_D01_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_CENTER",
                new APLocationDefinition(
                    100020L,
                    "Map Piece: Where Rock Bleeds (Center)",
                    "M_A_D01_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_RIGHT",
                new APLocationDefinition(
                    100021L,
                    "Map Piece: Where Rock Bleeds (Right)",
                    "M_A_D01_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_BORDERS",
                new APLocationDefinition(
                    100022L,
                    "Map Piece: Where Water Glistened (Borders)",
                    "M_A_D02_BORDERS",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP1",
                new APLocationDefinition(
                    100023L,
                    "Map Piece: Where Water Glistened (1st Ship)",
                    "M_A_D02_SHIP1",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP2",
                new APLocationDefinition(
                    100024L,
                    "Map Piece: Where Water Glistened (2nd Ship)",
                    "M_A_D02_SHIP2",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP3",
                new APLocationDefinition(
                    100025L,
                    "Map Piece: Where Water Glistened (3rd Ship)",
                    "M_A_D02_SHIP3",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP4",
                new APLocationDefinition(
                    100026L,
                    "Map Piece: Where Water Glistened (4th Ship)",
                    "M_A_D02_SHIP4",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D03",
                new APLocationDefinition(
                    100027L,
                    "Map Piece: The Big Tree",
                    "M_A_D03",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_CENTER",
                new APLocationDefinition(
                    100028L,
                    "Map Piece: Floating City (Control Area)",
                    "M_A_D04_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_TOWN",
                new APLocationDefinition(
                    100029L,
                    "Map Piece: Floating City (Old Town)",
                    "M_A_D04_TOWN",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_ZEPPELIN",
                new APLocationDefinition(
                    100030L,
                    "Map Piece: Floating City (Hangar)",
                    "M_A_D04_ZEPPELIN",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_FACTORY",
                new APLocationDefinition(
                    100031L,
                    "Map Piece: Floating City (Factory)",
                    "M_A_D04_FACTORY",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_FACILITIES",
                new APLocationDefinition(
                    100032L,
                    "Map Piece: Floating City (City Facilities)",
                    "M_A_D04_FACILITIES",
                    "MapUnlock",
                    true
                )
            },
            {
                "Q_D_0_Tutorial",
                new APLocationDefinition(
                    110001L,
                    "Quest Complete: Rage and Sorrow",
                    "Q_D_0_Tutorial",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_OldWarfare",
                new APLocationDefinition(
                    110002L,
                    "Quest Complete: Old Warfare",
                    "Q_D_S_OldWarfare",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_TutorialHook",
                new APLocationDefinition(
                    110003L,
                    "Quest Complete: The Bonehead's Hook",
                    "Q_D_S_TutorialHook",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_TutorialDash",
                new APLocationDefinition(
                    110004L,
                    "Quest Complete: Floating",
                    "Q_D_S_TutorialDash",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Flower",
                new APLocationDefinition(
                    110005L,
                    "Quest Complete: A Heart for Poochie",
                    "Q_D_S_Flower",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_1_Mines",
                new APLocationDefinition(
                    110006L,
                    "Quest Complete: Diplomacy",
                    "Q_D_1_Mines",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_2_Lighthouse",
                new APLocationDefinition(
                    110007L,
                    "Quest Complete: Radio Silence",
                    "Q_D_2_Lighthouse",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_3_TheBigTree",
                new APLocationDefinition(
                    110008L,
                    "Quest Complete: The Big Tree",
                    "Q_D_3_TheBigTree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_K_Kidnapping",
                new APLocationDefinition(
                    110009L,
                    "Quest Complete: Childless",
                    "Q_D_K_Kidnapping",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_PoochiesCorpse",
                new APLocationDefinition(
                    110010L,
                    "Quest Complete: Closure",
                    "Q_D_S_PoochiesCorpse",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_4_FloatingCity",
                new APLocationDefinition(
                    110011L,
                    "Quest Complete: Hell High",
                    "Q_D_4_FloatingCity",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_SacredPlace",
                new APLocationDefinition(
                    110012L,
                    "Quest Complete: Shake Off the Dead Leaves",
                    "Q_D_S_SacredPlace",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Lights",
                new APLocationDefinition(
                    110013L,
                    "Quest Complete: Stargazing",
                    "Q_D_S_Lights",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Remnants",
                new APLocationDefinition(
                    110014L,
                    "Quest Complete: The Remnants",
                    "Q_D_S_Remnants",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NewSheriff",
                new APLocationDefinition(
                    110015L,
                    "Quest Complete: A New Sheriff in Town",
                    "Q_D_S_NewSheriff",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Tombstone",
                new APLocationDefinition(
                    110016L,
                    "Quest Complete: A Little Tomb Stone",
                    "Q_D_S_Tombstone",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_FirstPeriod",
                new APLocationDefinition(
                    110017L,
                    "Quest Complete: First Blood",
                    "Q_D_S_FirstPeriod",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_EntomBrother",
                new APLocationDefinition(
                    110018L,
                    "Quest Complete: Life of the Party",
                    "Q_D_S_EntomBrother",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_BoneFlour",
                new APLocationDefinition(
                    110019L,
                    "Quest Complete: Bone Flour",
                    "Q_D_S_BoneFlour",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_CamillasJoint",
                new APLocationDefinition(
                    110020L,
                    "Quest Complete: A Break for Camilla",
                    "Q_D_S_CamillasJoint",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_OldCamp",
                new APLocationDefinition(
                    110021L,
                    "Quest Complete: From Mother to Daughter",
                    "Q_D_S_OldCamp",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Prophecy",
                new APLocationDefinition(
                    110022L,
                    "Quest Complete: The Prophecy",
                    "Q_D_S_Prophecy",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_LastMeal",
                new APLocationDefinition(
                    110023L,
                    "Quest Complete: Last Meal",
                    "Q_D_S_LastMeal",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_DyingBird",
                new APLocationDefinition(
                    110024L,
                    "Quest Complete: For the Cash",
                    "Q_D_S_DyingBird",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Immortal",
                new APLocationDefinition(
                    110025L,
                    "Quest Complete: Death on Demand",
                    "Q_D_S_Immortal",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_DeadTree",
                new APLocationDefinition(
                    110026L,
                    "Quest Complete: Family Tree",
                    "Q_D_S_DeadTree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_GhostRevenge",
                new APLocationDefinition(
                    110027L,
                    "Quest Complete: Fade Out",
                    "Q_D_S_GhostRevenge",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Rapist",
                new APLocationDefinition(
                    110028L,
                    "Quest Complete: Just a little girl",
                    "Q_D_S_Rapist",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_PlantSeed",
                new APLocationDefinition(
                    110029L,
                    "Quest Complete: We'll Never Know",
                    "Q_D_S_PlantSeed",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Gasoline",
                new APLocationDefinition(
                    110030L,
                    "Quest Complete: High Spirits",
                    "Q_D_S_Gasoline",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Seashell",
                new APLocationDefinition(
                    110031L,
                    "Quest Complete: Water Whispers",
                    "Q_D_S_Seashell",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresOne",
                new APLocationDefinition(
                    110032L,
                    "Quest Complete: Worse than Nightmares",
                    "Q_D_S_NightmaresOne",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresTwo",
                new APLocationDefinition(
                    110033L,
                    "Quest Complete: Worse than Hives",
                    "Q_D_S_NightmaresTwo",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresThree",
                new APLocationDefinition(
                    110034L,
                    "Quest Complete: Worse than Stomach Flu",
                    "Q_D_S_NightmaresThree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansDrums",
                new APLocationDefinition(
                    110035L,
                    "Quest Complete: Fogg's Only Wish",
                    "Q_D_A_MusiciansDrums",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansErhu",
                new APLocationDefinition(
                    110036L,
                    "Quest Complete: The Last Erhu",
                    "Q_D_A_MusiciansErhu",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansFlute",
                new APLocationDefinition(
                    110037L,
                    "Quest Complete: Clean Your Beak",
                    "Q_D_A_MusiciansFlute",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansGuitar",
                new APLocationDefinition(
                    110038L,
                    "Quest Complete: Desperately in Need of Music",
                    "Q_D_A_MusiciansGuitar",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansPiano",
                new APLocationDefinition(
                    110039L,
                    "Quest Complete: Sober Up",
                    "Q_D_A_MusiciansPiano",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansVoice",
                new APLocationDefinition(
                    110040L,
                    "Quest Complete: Oooo Ooo Oo O Ooo",
                    "Q_D_A_MusiciansVoice",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_PuppysBirth",
                new APLocationDefinition(
                    110041L,
                    "Quest Complete: Where We Used to Live",
                    "Q_D_F_PuppysBirth",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_YoungLaika",
                new APLocationDefinition(
                    110042L,
                    "Quest Complete: Target Practice",
                    "Q_D_F_YoungLaika",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_DaughterDies",
                new APLocationDefinition(
                    110043L,
                    "Quest Complete: Ava",
                    "Q_D_F_DaughterDies",
                    "Quest",
                    true
                )
            },
            {
                "B_BOSS_00_DEFEATED",
                new APLocationDefinition(
                    120001L,
                    "Boss Defeated: A Hundred Hungry Beaks",
                    "B_BOSS_00_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_01_DEFEATED",
                new APLocationDefinition(
                    120002L,
                    "Boss Defeated: A Long Lost Woodcrawler",
                    "B_BOSS_01_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_ROSCO_DEFEATED",
                new APLocationDefinition(
                    120003L,
                    "Boss Defeated: A Caterpiller Made of Sadness",
                    "B_BOSS_ROSCO_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_02_DEFEATED",
                new APLocationDefinition(
                    120004L,
                    "Boss Defeated: A Gargantuan Swimcrab",
                    "B_BOSS_02_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_03_DEFEATED",
                new APLocationDefinition(
                    120005L,
                    "Boss Defeated: Pope Melva VIII",
                    "B_BOSS_03_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "BOSS_04_DEFEATED",
                new APLocationDefinition(
                    120006L,
                    "Boss Defeated: Two-Beak God",
                    "BOSS_04_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "I_CASSETTE_1",
                new APLocationDefinition(
                    130001L,
                    "Cassette Tape: Bloody Sunset",
                    "I_CASSETTE_1",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_2",
                new APLocationDefinition(
                    130002L,
                    "Cassette Tape: Playing in the Sun",
                    "I_CASSETTE_2",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_3",
                new APLocationDefinition(
                    130003L,
                    "Cassette Tape: Lullaby of the Dead",
                    "I_CASSETTE_3",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_4",
                new APLocationDefinition(
                    130004L,
                    "Cassette Tape: Blue Limbo",
                    "I_CASSETTE_4",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D01",
                new APLocationDefinition(
                    130005L,
                    "Cassette Tape: Trust Them",
                    "I_CASSETTE_D01",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D02",
                new APLocationDefinition(
                    130006L,
                    "Cassette Tape: My Destiny",
                    "I_CASSETTE_D02",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D03",
                new APLocationDefinition(
                    130007L,
                    "Cassette Tape: The End of the Road",
                    "I_CASSETTE_D03",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_5",
                new APLocationDefinition(
                    130008L,
                    "Cassette Tape: The Whisper",
                    "I_CASSETTE_5",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_6",
                new APLocationDefinition(
                    130009L,
                    "Cassette Tape: Heartglaze Hope",
                    "I_CASSETTE_6",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_7",
                new APLocationDefinition(
                    130010L,
                    "Cassette Tape: The Hero",
                    "I_CASSETTE_7",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_8",
                new APLocationDefinition(
                    130011L,
                    "Cassette Tape: Visions of Red",
                    "I_CASSETTE_8",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_9",
                new APLocationDefinition(
                    130012L,
                    "Cassette Tape: Through the Wind",
                    "I_CASSETTE_9",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_10",
                new APLocationDefinition(
                    130013L,
                    "Cassette Tape: Heartbeat from the Last Century",
                    "I_CASSETTE_10",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_11",
                new APLocationDefinition(
                    130014L,
                    "Cassette Tape: Coming Home",
                    "I_CASSETTE_11",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_KIDNAPPING",
                new APLocationDefinition(
                    130015L,
                    "Cassette Tape: Mother",
                    "I_CASSETTE_KIDNAPPING",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_12",
                new APLocationDefinition(
                    130016L,
                    "Cassette Tape: The Last Tear",
                    "I_CASSETTE_12",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_13",
                new APLocationDefinition(
                    130017L,
                    "Cassette Tape: The Final Hours",
                    "I_CASSETTE_13",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_14",
                new APLocationDefinition(
                    130018L,
                    "Cassette Tape: Overthinker",
                    "I_CASSETTE_14",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_15",
                new APLocationDefinition(
                    130019L,
                    "Cassette Tape: Recurring Dream",
                    "I_CASSETTE_15",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_16",
                new APLocationDefinition(
                    130020L,
                    "Cassette Tape: Lonely Mountain",
                    "I_CASSETTE_16",
                    "Cassette",
                    true
                )
            },
            {
                "I_TOY_BIKE",
                new APLocationDefinition(
                    140001L,
                    "Puppy Gift: Toy Bike",
                    "I_TOY_BIKE",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_GAMEBOY",
                new APLocationDefinition(
                    140002L,
                    "Puppy Gift: Handheld Console",
                    "I_GAMEBOY",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_PLANT_PUPPY",
                new APLocationDefinition(
                    140003L,
                    "Puppy Gift: Tangerine Tree",
                    "I_PLANT_PUPPY",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_TOY_ANIMAL",
                new APLocationDefinition(
                    140004L,
                    "Puppy Gift: Toy Animal",
                    "I_TOY_ANIMAL",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_BOOK_MOTHER",
                new APLocationDefinition(
                    140005L,
                    "Puppy Gift: Great-Great-Grandma's Novella",
                    "I_BOOK_MOTHER",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_DREAMCATCHER",
                new APLocationDefinition(
                    140006L,
                    "Puppy Gift: Dreamcatcher",
                    "I_DREAMCATCHER",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_UKULELE",
                new APLocationDefinition(
                    140007L,
                    "Puppy Gift: Ukulele",
                    "I_UKULELE",
                    "PuppyGift",
                    true
                )
            },


        };

    internal static Dictionary<long, Func<PendingItem>> ItemFactoriesByApId =
        new Dictionary<long, Func<PendingItem>>()
        {
        // ===== Key items =====
        { 1000L, () => new PendingItem(ItemKind.KeyItem, "I_DASH", 1, "Dash (Bike Upgrade)") },
        { 1001L, () => new PendingItem(ItemKind.KeyItem, "I_E_HOOK", 1, "Hook (Bike Upgrade)") },
        { 1002L, () => new PendingItem(ItemKind.KeyItem, "I_MAYA_PENDANT", 1, "Maya's Pendant (Bike Upgrade)") },

        // ===== Weapons / weapon-mode-aware unlocks =====
        { 1100L, () => GetShotgunUnlockItem() },
        { 1101L, () => GetSniperUnlockItem() },
        { 1102L, () => GetMachineGunUnlockItem() },
        { 1103L, () => GetRocketLauncherUnlockItem() },

        // Crossbow is still direct for now
        { 1104L, () => new PendingItem(ItemKind.Weapon, "I_W_CROSSBOW", 1, "Crossbow (Weapon)") },

        // ===== Crafting-mode weapon materials =====
        { 1150L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_SHOTGUN", 1, "Rusty Spring (Shotgun Material)") },
        { 1151L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_SNIPER", 1, "Magnifying Glass (Sniper Rifle Material)") },
        { 1152L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_UZI", 1, "Titanium Plates (Machine Gun Material)") },
        { 1153L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_ROCKETLAUNCHER", 1, "Missile (Rocket Launcher Material)") },

        // ===== Puppy gifts =====
        { 1900L, () => new PendingItem(ItemKind.PuppyTreat, "I_TOY_BIKE", 1, "Toy Bike") },
        { 1901L, () => new PendingItem(ItemKind.PuppyTreat, "I_GAMEBOY", 1, "Handheld Console") },
        { 1902L, () => new PendingItem(ItemKind.PuppyTreat, "I_PLANT_PUPPY", 1, "Tangerine Tree") },
        { 1903L, () => new PendingItem(ItemKind.PuppyTreat, "I_TOY_ANIMAL", 1, "Toy Animal") },
        { 1904L, () => new PendingItem(ItemKind.PuppyTreat, "I_BOOK_MOTHER", 1, "Great-Great-Grandma's Novella") },
        { 1905L, () => new PendingItem(ItemKind.PuppyTreat, "I_DREAMCATCHER", 1, "Dreamcatcher") },
        { 1906L, () => new PendingItem(ItemKind.PuppyTreat, "I_UKULELE", 1, "Ukulele") },

        // ===== Currency =====
        { 1907L, () => new PendingItem(ItemKind.Currency, "VISCERA_100", 100, "Viscera x100") },

        // ===== Ingredients =====
        { 1950L, () => new PendingItem(ItemKind.Ingredient, "I_C_BEANS", 1, "Beans") },
        { 1951L, () => new PendingItem(ItemKind.Ingredient, "I_C_CORN", 1, "Corn") },
        { 1952L, () => new PendingItem(ItemKind.Ingredient, "I_C_WORMS", 1, "Worms") },
        { 1953L, () => new PendingItem(ItemKind.Ingredient, "I_C_ONION", 1, "Onion") },
        { 1954L, () => new PendingItem(ItemKind.Ingredient, "I_C_CHILLY", 1, "Chilly Pepper") },
        { 1955L, () => new PendingItem(ItemKind.Ingredient, "I_C_GHOSTPEPPER", 1, "Ghost Pepper") },
        { 1956L, () => new PendingItem(ItemKind.Ingredient, "I_C_LEMON", 1, "Lemon") },
        { 1957L, () => new PendingItem(ItemKind.Ingredient, "I_C_GARLIC", 1, "Garlic") },
        { 1958L, () => new PendingItem(ItemKind.Ingredient, "I_C_MEAT", 1, "Meat") },
        { 1959L, () => new PendingItem(ItemKind.Ingredient, "I_C_JACKFRUIT", 1, "Jackfruit") },
        { 1960L, () => new PendingItem(ItemKind.Ingredient, "I_C_SARDINE", 1, "Sardine") },
        { 1961L, () => new PendingItem(ItemKind.Ingredient, "I_C_COCO", 1, "Coco") },
        { 1962L, () => new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 1, "Coffee Beans") },
        { 1963L, () => new PendingItem(ItemKind.Ingredient, "I_C_WHISKEY", 1, "Whiskey") },
        { 1964L, () => new PendingItem(ItemKind.Ingredient, "I_C_TOMATO", 1, "Tomato") },

        // ===== Cassettes =====
        { 1970L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_1", 1, "Cassette: Bloody Sunset") },
        { 1971L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_2", 1, "Cassette: Playing in the Sun") },
        { 1972L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_3", 1, "Cassette: Lullaby of the Dead") },
        { 1973L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_4", 1, "Cassette: Blue Limbo") },
        { 1974L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D01", 1, "Cassette: Trust Them") },
        { 1975L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D02", 1, "Cassette: My Destiny") },
        { 1976L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D03", 1, "Cassette: The End of the Road") },
        { 1977L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_5", 1, "Cassette: The Whisper") },
        { 1978L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_6", 1, "Cassette: Heartglaze Hope") },
        { 1979L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_7", 1, "Cassette: The Hero") },
        { 1980L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_8", 1, "Cassette: Visions of Red") },
        { 1981L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_9", 1, "Cassette: Through the Wind") },
        { 1982L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_10", 1, "Cassette: Heartbeat from the Last Century") },
        { 1983L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_11", 1, "Cassette: Coming Home") },
        { 1984L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_KIDNAPPING", 1, "Cassette: Mother") },
        { 1985L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_12", 1, "Cassette: The Last Tear") },
        { 1986L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_13", 1, "Cassette: The Final Hours") },
        { 1987L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_14", 1, "Cassette: Overthinker") },
        { 1988L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_15", 1, "Cassette: Recurring Dream") },
        { 1989L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_16", 1, "Cassette: Lonely Mountain") },

        // ===== Map unlocks =====
        { 2000L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map: Where Our Bikes Growl") },
        { 2001L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W01_BOTTOM", 1, "Map: Where All Was Lost (Bottom)") },
        { 2002L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W01_TOP", 1, "Map: Where All Was Lost (Top)") },
        { 2003L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W02", 1, "Map: Where Doom Fell") },
        { 2004L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_LEFT", 1, "Map: Where Rust Weaves (Left)") },
        { 2005L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_CENTER", 1, "Map: Where Rust Weaves (Center)") },
        { 2006L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_RIGHT", 1, "Map: Where Rust Weaves (Right)") },
        { 2007L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W04_BOTTOM", 1, "Map: Where Iron Caresses the Sky (Bottom)") },
        { 2008L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W04_TOP", 1, "Map: Where Iron Caresses the Sky (Top)") },
        { 2009L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W05_LEFT", 1, "Map: Where the Waves Die (Left)") },
        { 2010L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W05_RIGHT", 1, "Map: Where the Waves Die (Right)") },
        { 2011L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W07_BOTTOM", 1, "Map: Where Our Ancestors Rest (Bottom)") },
        { 2012L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W07_TOP", 1, "Map: Where Our Ancestors Rest (Top)") },
        { 2013L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W08_LEFT", 1, "Map: Where Birds Came From (Left/Bottom)") },
        { 2014L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W08_RIGHT", 1, "Map: Where Birds Came From (Right/Top)") },
        { 2015L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D00_LEFT", 1, "Map: Where Birds Lurk (Left)") },
        { 2016L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D00_RIGHT", 1, "Map: Where Birds Lurk (Right)") },
        { 2017L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_LEFT", 1, "Map: Where Rock Bleeds (Left)") },
        { 2018L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_CENTER", 1, "Map: Where Rock Bleeds (Center)") },
        { 2019L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_RIGHT", 1, "Map: Where Rock Bleeds (Right)") },
        { 2020L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_BORDERS", 1, "Map: Where Water Glistened (Borders)") },
        { 2021L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP1", 1, "Map: Where Water Glistened (1st Ship)") },
        { 2022L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP2", 1, "Map: Where Water Glistened (2nd Ship)") },
        { 2023L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP3", 1, "Map: Where Water Glistened (3rd Ship)") },
        { 2024L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP4", 1, "Map: Where Water Glistened (4th Ship)") },
        { 2025L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D03", 1, "Map: The Big Tree") },
        { 2026L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_CENTER", 1, "Map: Floating City (Control Area)") },
        { 2027L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_TOWN", 1, "Map: Floating City (Old Town)") },
        { 2028L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_ZEPPELIN", 1, "Map: Floating City (Hangar)") },
        { 2029L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_FACTORY", 1, "Map: Floating City (Factory)") },
        { 2030L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_FACILITIES", 1, "Map: Floating City (City Facilities)") },
        };

    internal static bool TryCreatePendingItemFromApItemId(long apItemId, out PendingItem pendingItem)
    {
        pendingItem = null;

        Func<PendingItem> factory;
        if (!ItemFactoriesByApId.TryGetValue(apItemId, out factory))
        {
            LogWarning($"AP ITEM: no C# mapping exists yet for AP item id {apItemId}");
            return false;
        }

        pendingItem = factory();
        return pendingItem != null;
    }

    // Canonical collectible item registry.
    // Use this for AP received-item display names.
    // Do not use this as a location/check registry unless cassette checks are later
    // sourced from boombox destruction, shop purchase, quest reward, or Jakob bundle logic.
    internal static Dictionary<string, string> CollectibleDisplayNamesByInternalId =
        new Dictionary<string, string>()
        {
            { "I_CASSETTE_1", "Cassette Tape: Bloody Sunset" },
            { "I_CASSETTE_2", "Cassette Tape: Playing in the Sun" },
            { "I_CASSETTE_3", "Cassette Tape: Lullaby of the Dead" },
            { "I_CASSETTE_4", "Cassette Tape: Blue Limbo" },
            { "I_CASSETTE_D01", "Cassette Tape: Trust Them" },
            { "I_CASSETTE_D02", "Cassette Tape: My Destiny" },
            { "I_CASSETTE_D03", "Cassette Tape: The End of the Road" },
            { "I_CASSETTE_5", "Cassette Tape: The Whisper" },
            { "I_CASSETTE_6", "Cassette Tape: Heartglaze Hope" },
            { "I_CASSETTE_7", "Cassette Tape: The Hero" },
            { "I_CASSETTE_8", "Cassette Tape: Visions of Red" },
            { "I_CASSETTE_9", "Cassette Tape: Through the Wind" },
            { "I_CASSETTE_10", "Cassette Tape: Heartbeat from the Last Century" },
            { "I_CASSETTE_11", "Cassette Tape: Coming Home" },
            { "I_CASSETTE_KIDNAPPING", "Cassette Tape: Mother" },
            { "I_CASSETTE_12", "Cassette Tape: The Last Tear" },
            { "I_CASSETTE_13", "Cassette Tape: The Final Hours" },
            { "I_CASSETTE_14", "Cassette Tape: Overthinker" },
            { "I_CASSETTE_15", "Cassette Tape: Recurring Dream" },
            { "I_CASSETTE_16", "Cassette Tape: Lonely Mountain" },
            { "I_COLLECTION_JAKOB", "Cassette Tape: Jakob's Music Collection" }
        };

    // Marks a location as sent locally so reconnect/resync logic can reuse this data later.
    internal static void MarkLocationCheckedAndSent(long locationId)
    {
        if (!SessionState.SentLocationIds.Contains(locationId))
        {
            SessionState.SentLocationIds.Add(locationId);
            SaveSessionState();
            LogInfo($"AP STATE: marked location as sent -> {locationId}");
        }
    }

    // Returns true if the location was already marked as sent locally.
    internal static bool HasLocationBeenSent(long locationId)
    {
        return SessionState.SentLocationIds.Contains(locationId);
    }

    // Stores the last processed ReceivedItems index from Archipelago.
    internal static void UpdateLastProcessedReceivedItemIndex(int index)
    {
        if (index < 0)
            return;

        SessionState.LastProcessedReceivedItemIndex = index;
        SaveSessionState();

        LogInfo($"AP STATE: updated last processed received item index -> {index}");
    }

    // Marks the AP goal as reported locally so the client does not spam StatusUpdate later.
    internal static void MarkGoalReported()
    {
        if (SessionState.GoalReported)
            return;

        SessionState.GoalReported = true;
        SaveSessionState();

        LogInfo("AP STATE: goal marked as reported.");
    }

    // Clears connection-only state without wiping persistent progression tracking.
    internal static void ResetRuntimeConnectionState()
    {
        SessionState.Connection = new APConnectionState();

        LogInfo("AP runtime connection state reset.");
    }

    public class APLocationDefinition
    {
        // Stable Archipelago location id for this check.
        public long LocationId { get; private set; }

        // Player-facing AP location/check name.
        public string DisplayName { get; private set; }

        // Internal Laika identifier used to match runtime events or save flags.
        public string InternalId { get; private set; }

        // What kind of in-game thing this location represents.
        // Example: map unlock, quest completion, boss clear, shop purchase.
        public string Category { get; private set; }

        // Whether this check should be recoverable later from save/progression state.
        public bool RecoverableFromSave { get; private set; }

        public APLocationDefinition(
            long locationId,
            string displayName,
            string internalId,
            string category,
            bool recoverableFromSave)
        {
            LocationId = locationId;
            DisplayName = displayName;
            InternalId = internalId;
            Category = category;
            RecoverableFromSave = recoverableFromSave;
        }

        public override string ToString()
        {
            return
                $"LocationId={LocationId}, " +
                $"DisplayName={DisplayName}, " +
                $"InternalId={InternalId}, " +
                $"Category={Category}, " +
                $"RecoverableFromSave={RecoverableFromSave}";
        }
    }

    // ===== Logging helpers =====
    // Logs a message to both BepInEx and the in-game developer overlay.
    internal static void LogInfo(string message)
    {
        Log.LogInfo(message);
    }

    internal static void LogWarning(string message)
    {
        Log.LogWarning(message);
    }

    internal static void LogError(string message)
    {
        Log.LogError(message);
    }

    // Adds a player-facing AP activity line to the recent overlay.
    // This is intentionally separate from the normal debug logger so the recent overlay
    // only shows useful AP gameplay events instead of internal spam.
    internal static void AnnounceAPActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        OverlayLines.Enqueue(message);

        while (OverlayLines.Count > MaxOverlayLines)
        {
            OverlayLines.Dequeue();
        }

        ShowRecentLogOverlay = true;

        if (ActiveDevOverlayController != null)
        {
            ActiveDevOverlayController.ResetRecentLogAutoHideTimer();
        }

        RefreshDevOverlay();
    }

    // Logs every ingredient ID the game has loaded.
    // This helps us discover real ingredient IDs for testing.
    internal static void LogAllIngredientIds()
    {
        // Grab the game's master item loader singleton.
        var loader = Singleton<ItemDataLoader>.Instance;

        // Safety check in case loader somehow isn't ready yet.
        if (loader == null)
        {
            LogWarning("ItemDataLoader is null.");
            return;
        }

        // Ask the game for every ingredient definition.
        var ingredients = loader.GetAllIngredientDatas();

        // Another safety check.
        if (ingredients == null)
        {
            LogWarning("GetAllIngredientDatas returned null.");
            return;
        }

        // Print how many ingredients exist total.
        LogInfo($"INGREDIENT LIST START: count={ingredients.Count}");

        // Loop through every ingredient.
        foreach (var ingredient in ingredients)
        {
            // Skip broken/null entries just in case.
            if (ingredient == null)
                continue;

            // Print the ingredient's internal ID.
            LogInfo($"INGREDIENT ID: {ingredient.id}");
        }

        LogInfo("INGREDIENT LIST END");
    }

    // Logs every cassette ID the game has loaded.
    // This helps us discover real cassette IDs for testing.
    internal static void LogAllCassetteIds()
    {
        // Grab the game's cassette data loader singleton.
        var loader = Singleton<CassettesDataLoader>.Instance;

        // Safety check in case loader is not ready yet.
        if (loader == null)
        {
            LogWarning("CassettesDataLoader is null.");
            return;
        }

        // Ask the game for all cassette IDs.
        var cassetteIds = loader.GetCassettesIds();

        // Another safety check.
        if (cassetteIds == null)
        {
            LogWarning("GetCassettesIds returned null.");
            return;
        }

        // Print how many cassettes exist total.
        LogInfo($"CASSETTE LIST START: count={cassetteIds.Count}");

        // Loop through every cassette ID.
        foreach (var cassetteId in cassetteIds)
        {
            if (string.IsNullOrEmpty(cassetteId))
                continue;

            LogInfo($"CASSETTE ID: {cassetteId}");
        }

        LogInfo("CASSETTE LIST END");
    }

    // Logs the current visible weapon inventory for debugging before/after queue processing.
    internal static void LogWeaponInventorySnapshot(string label)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            LogWarning($"{label}: WeaponsInventory is null.");
            return;
        }

        if (inventory.Weapons == null)
        {
            LogWarning($"{label}: Weapons list is null.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"WEAPON INVENTORY SNAPSHOT {label}: count={inventory.Weapons.Count}");

        foreach (var weapon in inventory.Weapons)
        {
            if (weapon == null)
                continue;

            sb.Append($" | {weapon.Id}");
        }

        LogInfo(sb.ToString());
    }

    // ===== Dev overlay state =====
    // Stores recent player-facing AP activity lines for the recent overlay.
    internal static Queue<string> OverlayLines = new Queue<string>();

    // Maximum number of lines to keep in the overlay at once.
    internal static int MaxOverlayLines = 8;

    // Recent-log box visibility is separate from the always-visible status HUD.
    internal static bool ShowRecentLogOverlay = false;
    internal static float RecentLogAutoHideDelaySeconds = 14f;

    // Creates the developer overlay canvas if it does not already exist.
    internal static void EnsureDevOverlayCanvas()
    {
        if (DevOverlayCanvasObject != null)
            return;

        Font builtInFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        Log.LogInfo($"Dev overlay font loaded: {(builtInFont != null)}");

        DevOverlayCanvasObject = new GameObject("LaikaAPDevOverlayCanvas");
        DontDestroyOnLoad(DevOverlayCanvasObject);

        Canvas canvas = DevOverlayCanvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        canvas.pixelPerfect = false;
        canvas.targetDisplay = 0;

        CanvasScaler scaler = DevOverlayCanvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        DevOverlayCanvasObject.AddComponent<GraphicRaycaster>();

        // ===== Permanent status panel =====
        GameObject statusPanel = new GameObject("OverlayStatusPanel");
        statusPanel.transform.SetParent(DevOverlayCanvasObject.transform, false);

        Image statusBg = statusPanel.AddComponent<Image>();
        statusBg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform statusPanelRect = statusPanel.GetComponent<RectTransform>();
        statusPanelRect.anchorMin = new Vector2(0f, 0f);
        statusPanelRect.anchorMax = new Vector2(0f, 0f);
        statusPanelRect.pivot = new Vector2(0f, 0f);
        statusPanelRect.anchoredPosition = new Vector2(20f, 300f);
        statusPanelRect.sizeDelta = new Vector2(360f, 190f);

        GameObject statusTextObj = new GameObject("OverlayStatusText");
        statusTextObj.transform.SetParent(statusPanel.transform, false);

        DevOverlayStatusText = statusTextObj.AddComponent<Text>();
        DevOverlayStatusText.font = builtInFont;
        DevOverlayStatusText.fontSize = 16;
        DevOverlayStatusText.color = Color.white;
        DevOverlayStatusText.alignment = TextAnchor.UpperLeft;
        DevOverlayStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        DevOverlayStatusText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform statusTextRect = statusTextObj.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0f, 0f);
        statusTextRect.anchorMax = new Vector2(1f, 1f);
        statusTextRect.offsetMin = new Vector2(10f, 10f);
        statusTextRect.offsetMax = new Vector2(-10f, -10f);

        // ===== Recent log panel =====
        GameObject logPanel = new GameObject("OverlayRecentLogPanel");
        logPanel.transform.SetParent(DevOverlayCanvasObject.transform, false);

        Image logBg = logPanel.AddComponent<Image>();
        logBg.color = new Color(0f, 0f, 0f, 0.65f);

        RectTransform logPanelRect = logPanel.GetComponent<RectTransform>();
        logPanelRect.anchorMin = new Vector2(0f, 0f);
        logPanelRect.anchorMax = new Vector2(0f, 0f);
        logPanelRect.pivot = new Vector2(0f, 0f);
        logPanelRect.anchoredPosition = new Vector2(20f, 20f);
        logPanelRect.sizeDelta = new Vector2(700f, 240f);

        GameObject logTextObj = new GameObject("OverlayRecentLogText");
        logTextObj.transform.SetParent(logPanel.transform, false);

        DevOverlayRecentLogText = logTextObj.AddComponent<Text>();
        DevOverlayRecentLogText.font = builtInFont;
        DevOverlayRecentLogText.fontSize = 12;
        DevOverlayRecentLogText.color = Color.white;
        DevOverlayRecentLogText.alignment = TextAnchor.UpperLeft;
        DevOverlayRecentLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
        DevOverlayRecentLogText.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform logTextRect = logTextObj.GetComponent<RectTransform>();
        logTextRect.anchorMin = new Vector2(0f, 0f);
        logTextRect.anchorMax = new Vector2(1f, 1f);
        logTextRect.offsetMin = new Vector2(12f, 12f);
        logTextRect.offsetMax = new Vector2(-12f, -12f);

        Canvas.ForceUpdateCanvases();
        Log.LogInfo("Dev overlay canvas created.");
    }

    // Redraws both overlay panels based on the current shared overlay state.
    // This method does not own timing; it only reflects the latest state to the UI.
    // Refreshes the overlay text and visibility.
    internal static void RefreshDevOverlay()
    {
        if (DevOverlayCanvasObject == null || DevOverlayStatusText == null || DevOverlayRecentLogText == null)
            return;

        DevOverlayStatusText.text =
            "Laika AP Status\n\n" +
            $"Queue={PendingItemQueue.Count}\n" +
            $"WeaponMode={WorldOptions.WeaponMode}\n" +
            $"SessionDeaths={LocalDeathsThisSession}\n" +
            $"DeathsSinceLastLink={DeathsSinceLastDeathLink}\n" +
            $"DeathLink={WorldOptions.DeathLinkEnabled}\n" +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled} ({WorldOptions.DeathAmnestyCount})";

        DevOverlayRecentLogText.transform.parent.gameObject.SetActive(ShowRecentLogOverlay);

        if (ShowRecentLogOverlay)
        {
            DevOverlayRecentLogText.text =
                "Recent AP Activity:\n\n" +
                string.Join("\n", OverlayLines.ToArray());
        }
        else
        {
            DevOverlayRecentLogText.text = string.Empty;
        }
    }

    // This parameter is only used as a safe creation hook.
    // The controller itself is created on its own persistent object.
    internal static void EnsureRuntimeDevOverlay(WeaponsOverlay weaponsOverlay)
    {
        if (weaponsOverlay == null)
        {
            LogWarning("EnsureRuntimeDevOverlay: WeaponsOverlay was null.");
            return;
        }

        if (ActiveDevOverlayController != null)
            return;

        DevOverlayControllerObject = new GameObject("LaikaAPDevOverlayController");
        DontDestroyOnLoad(DevOverlayControllerObject);

        ActiveDevOverlayController = DevOverlayControllerObject.AddComponent<DevOverlayController>();

        Log.LogInfo("Created persistent DevOverlayController object.");
    }

    // ===== Queue processing =====
    // Development-only queue entries go here when I want to force-test item grants.
    // These are commented examples for every ItemKind currently supported by the grant handler.
    // I can uncomment one or more lines as needed instead of trying to remember the right format.
    internal static void EnqueueDevelopmentStressTestItems()
    {
        // ===== Currency =====
        // Raw money/viscera grant.
        // The Id is mostly just a label here since Currency routes through TryGrantCurrency(...).
        // Amount is what really matters.
        // EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA", 50000, "Viscera"));


        // ===== Weapon =====
        // Direct weapon ownership grant.
        // Use this when I want the player to immediately own a full weapon.
        // Example weapon ids I have confirmed:
        // I_W_PISTOL, I_W_UZI, I_W_SHOTGUN, I_W_SNIPER, I_W_CROSSBOW, I_W_ROCKETLAUNCHER
        // EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_UZI", 1, "Machine Gun"));

        // ===== WeaponUpgrade =====
        // Adds weapon upgrade steps from the weapon's current level.
        // Example: a fresh weapon at displayed level 1 plus amount 3 should end at displayed level 4.
        // Use the base weapon id here, not a separate "upgrade item" id.
        // EnqueueItem(new PendingItem(ItemKind.WeaponUpgrade, "I_W_PISTOL", 3, "Pistol Upgrade 3"));

        // ===== Ingredient =====
        // Adds normal recipe/cooking ingredients through InventoryManager.
        // Use this for consumable crafting/cooking inputs that stack by amount.
        // Example confirmed ids:
        // I_C_BEANS, I_C_CORN, I_C_WORMS, I_C_ONION, I_C_CHILLY, I_C_GHOSTPEPPER, I_C_LEMON, I_C_GARLIC
        // I_C_MEAT, I_C_JACKFRUIT, I_C_SARDINE, I_C_COCO, I_C_COFFEE, I_C_WHISKEY, I_C_TOMATO
        // EnqueueItem(new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 5, "Coffee Can"));

        // ===== Material =====
        // Adds crafting materials/resources through InventoryManager.
        // Use this for stackable build/upgrade materials instead of unique progression items.
        // Example confirmed ids:
        // Common: I_BASALT, I_BONE, I_CALCIUM, I_METAL_BAD, I_SHALE, I_LEATHER_BAD, I_WOOD
        // Rare: I_METAL_GOOD, I_SCRAPS_UPCYCLE, I_SCRAPS_RUSTY, I_CABLE, I_LEATHER_GOOD
        // Unique: I_MATERIAL_SHOTGUN, I_MATERIAL_ROCKETLAUNCHER, I_MATERIAL_UZI, I_MATERIAL_SNIPER
        // EnqueueItem(new PendingItem(ItemKind.Material, "I_METAL_GOOD", 10, "Refined Metal"));

        // ===== Collectible =====
        // Grants a cassette/collectible directly through the cassette manager.
        // Use the cassette's internal id here.
        // Example confirmed ids:
        // I_CASSETTE_1 through I_CASSETTE_16, plus things like I_CASSETTE_D01, I_CASSETTE_D02, etc.
        // EnqueueItem(new PendingItem(ItemKind.Collectible, "I_CASSETTE_7", 1, "Cassette Tape: The Hero"));

        // ===== PuppyTreat =====
        // Grants one of Puppy's gifts through the Puppy gift path.
        // This path also uses suppression so AP-received Puppy gifts do not falsely send local checks.
        // Example confirmed ids:
        // I_TOY_BIKE, I_GAMEBOY, I_PLANT_PUPPY, I_TOY_ANIMAL, I_BOOK_MOTHER, I_DREAMCATCHER, I_UKULELE
        // EnqueueItem(new PendingItem(ItemKind.PuppyTreat, "I_DREAMCATCHER", 1, "Dreamcatcher"));

        // ===== KeyItem =====
        // Grants a unique progression/key item through InventoryManager.
        // Some of these also need progression flags after grant, which ApplyKeyItemProgressionFlags(...) handles.
        // Example confirmed ids:
        // I_DASH, I_E_HOOK, I_MAYA_PENDANT
        // EnqueueItem(new PendingItem(ItemKind.KeyItem, "I_DASH", 1, "Dash"));

        // ===== MapUnlock =====
        // Unlocks a map piece/area directly through ProgressionData.
        // Use the internal map area id here.
        // Example confirmed ids:
        // M_A_W06, M_A_W07_TOP, M_A_W07_BOTTOM, etc.
        // EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map Piece: Where Our Bikes Growl"));

        EnqueueItem(new PendingItem(ItemKind.Currency, "VISCERA_100", 0, "Viscera x100"));
        EnqueueItem(new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 1, "Coffee Beans"));
        EnqueueItem(new PendingItem(ItemKind.Collectible, "I_CASSETTE_1", 1, "Cassette: Bloody Sunset"));
        EnqueueItem(new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map: Where Our Bikes Growl"));
    }

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Main queue processor.
    // This runs when the game is in a safe enough state to grant AP items and also acts as a fallback
    // recovery point for quest softlocks on older saves.
    internal static void ProcessPendingItemQueue(string sourceTag)
    {
        if (IsProcessingQueue)
        {
            LogInfo($"{sourceTag}: queue processing already in progress, skipping nested call.");
            return;
        }

        // Some AP softlocks are not item grant failures.
        // They happen because the player already owns the required item before the vanilla quest reaches
        // the step that expects it. I only fix the blocked step here so the rest of the quest can continue normally.
        TryReconcileKnownQuestSoftlocks(sourceTag);

        if (PendingItemQueue.Count == 0)
        {
            Log.LogInfo($"{sourceTag}: no pending items to process.");
            return;
        }

        // Make sure parry/reflect is unlocked once the game managers are alive.
        // Awake() is too early because ProgressionManager is still null there.
        TryEnsureParryUnlockedOnce(sourceTag);

        IsProcessingQueue = true;

        try
        {
            LogInfo($"{sourceTag}: starting queue processing. Count={PendingItemQueue.Count}");
            LogWeaponInventorySnapshot($"{sourceTag} BEFORE");

            Queue<PendingItem> remainingQueue = new Queue<PendingItem>();

            while (PendingItemQueue.Count > 0)
            {
                PendingItem item = PendingItemQueue.Dequeue();

                try
                {
                    bool granted = TryGrantPendingItem(item, sourceTag);

                    if (granted)
                    {
                        AnnounceAPActivity(BuildReceivedItemAnnouncement(item));
                    }
                    else
                    {
                        LogWarning($"{sourceTag}: item not granted, re-queueing -> {item}");
                        remainingQueue.Enqueue(item);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"{sourceTag}: exception while processing {item}:\n{ex}");
                    remainingQueue.Enqueue(item);
                }
            }

            PendingItemQueue = remainingQueue;

            LogWeaponInventorySnapshot($"{sourceTag} AFTER");
            LogInfo($"{sourceTag}: queue processing finished. Remaining={PendingItemQueue.Count}");
        }
        finally
        {
            IsProcessingQueue = false;
        }
    }

    // ===== Grant handlers =====
    // This is the main router for received items.
    // Each ItemKind goes through its own grant path so I can keep the weird edge cases isolated.
    internal static bool TryGrantPendingItem(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: processing {item}");

        switch (item.Kind)
        {
            case ItemKind.Currency:
                return TryGrantCurrency(item, sourceTag);

            case ItemKind.Weapon:
                return TryGrantWeapon(item, sourceTag);

            case ItemKind.WeaponUpgrade:
                return TryGrantWeaponUpgrade(item, sourceTag);

            case ItemKind.Ingredient:
                return TryGrantIngredient(item, sourceTag);

            case ItemKind.Material:
                return TryGrantMaterial(item, sourceTag);

            case ItemKind.Collectible:
                return TryGrantCollectible(item, sourceTag);
           
            case ItemKind.PuppyTreat:
                return TryGrantPuppyTreat(item, sourceTag);

            case ItemKind.KeyItem:
                return TryGrantKeyItem(item, sourceTag);

            case ItemKind.MapUnlock:
                return TryGrantMapUnlock(item, sourceTag);

            default:
                LogWarning($"{sourceTag}: unsupported item kind -> {item.Kind}");
                return false;
        }
    }

    // Grants Viscera through EconomyManager.
    // If the AP item has a positive Amount, use that directly.
    // Otherwise, infer a fixed amount from the item's internal id.
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var economy = Singleton<EconomyManager>.Instance;

        if (economy == null)
        {
            LogWarning($"{sourceTag}: currency grant failed, EconomyManager is null.");
            return false;
        }

        try
        {
            int amountToGrant = ResolveCurrencyAmount(item);

            if (amountToGrant <= 0)
            {
                LogWarning($"{sourceTag}: currency grant failed, resolved amount was {amountToGrant} for item {item.Id}");
                return false;
            }

            int before = economy.Money;
            LogInfo($"{sourceTag}: currency before grant = {before}");

            economy.AddMoney(amountToGrant);

            int after = economy.Money;
            LogInfo($"{sourceTag}: currency after grant = {after}");

            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
        }
    }

    // Resolves how much money a currency AP item should award.
    // I let PendingItem.Amount win if it is already populated,
    // otherwise I map known AP currency ids here.
    internal static int ResolveCurrencyAmount(PendingItem item)
    {
        if (item == null)
            return 0;

        if (item.Amount > 0)
            return item.Amount;

        switch (item.Id)
        {
            case "VISCERA_10":
                return 10;

            case "VISCERA_25":
                return 25;

            case "VISCERA_50":
                return 50;

            case "VISCERA_100":
                return 100;

            default:
                // Keep old generic VISCERA as a fallback while transitioning.
                if (item.Id == "VISCERA")
                    return 100;

                return 0;
        }
    }

    // Ownership-based grant handlers.
    // These usually treat already-owned items as success so they do not remain stuck in the queue.
    internal static bool TryGrantWeapon(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<WeaponsInventory>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: weapon grant failed, WeaponsInventory is null.");
            return false;
        }

        if (inventory.Weapons == null)
        {
            LogWarning($"{sourceTag}: weapon grant failed, Weapons list is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasWeapon(item.Id);
        LogInfo($"{sourceTag}: weapon {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
        {
            LogInfo($"{sourceTag}: skipping weapon {item.Id} because player already owns it.");
            return true;
        }

        bool addResult = inventory.AddWeapon(item.Id);
        LogInfo($"{sourceTag}: AddWeapon({item.Id}) returned {addResult}");

        bool ownedAfter = inventory.HasWeapon(item.Id);
        LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for weapon {item.Id}");

        if (ownedAfter && item.Id == "I_W_SHOTGUN")
        {
            LogImportantQuestSnapshots($"{sourceTag}: shotgun grant follow-up");
        }

        return ownedAfter;
    }

    // Grants weapon upgrades as additional upgrade steps from the weapon's current level.
    // Internal weapon levels are zero-based in Laika:
    // internal 0 = displayed level 1
    // internal 1 = displayed level 2
    // internal 2 = displayed level 3
    // internal 3 = displayed level 4
    //
    // That means if a fresh weapon is newly granted and starts at internal level 0,
    // an AP upgrade amount of 3 should move it to internal level 3, which is displayed level 4.
    internal static bool TryGrantWeaponUpgrade(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var weaponsInventory = Singleton<WeaponsInventory>.Instance;
        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, WeaponsInventory is null.");
            return false;
        }

        var itemLoader = Singleton<ItemDataLoader>.Instance;
        if (itemLoader == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, ItemDataLoader is null.");
            return false;
        }

        try
        {
            bool alreadyOwned = weaponsInventory.HasWeapon(item.Id);
            LogInfo($"{sourceTag}: weapon upgrade target {item.Id}, alreadyOwned={alreadyOwned}");

            if (!alreadyOwned)
            {
                LogWarning($"{sourceTag}: cannot upgrade weapon {item.Id} because player does not own it yet.");
                return false;
            }

            ItemDataWeapon weaponData = itemLoader.FindWeapon(item.Id);
            if (weaponData == null)
            {
                LogWarning($"{sourceTag}: weapon upgrade grant failed, FindWeapon({item.Id}) returned null.");
                return false;
            }

            WeaponInstance weaponInstance = weaponsInventory.GetWeaponInstance(weaponData);
            if (weaponInstance == null)
            {
                LogWarning($"{sourceTag}: weapon upgrade grant failed, GetWeaponInstance({item.Id}) returned null.");
                return false;
            }

            // Laika weapon levels are zero-based internally.
            int currentInternalLevel = weaponInstance.Level;

            // item.Amount means "how many upgrade steps to add from where the weapon is now".
            int targetInternalLevel = currentInternalLevel + item.Amount;

            // Laika weapons cap at displayed level 4, which is internal level 3.
            // Clamp here so I never push a weapon past the game's valid level data.
            int maxInternalLevel = 3;
            if (targetInternalLevel > maxInternalLevel)
            {
                targetInternalLevel = maxInternalLevel;
            }

            LogInfo(
                $"{sourceTag}: weapon {item.Id} currentInternalLevel={currentInternalLevel}, " +
                $"targetInternalLevel={targetInternalLevel}, " +
                $"currentDisplayedLevel={currentInternalLevel + 1}, " +
                $"targetDisplayedLevel={targetInternalLevel + 1}"
            );

            if (currentInternalLevel >= targetInternalLevel)
            {
                LogInfo($"{sourceTag}: weapon {item.Id} is already at or above target level.");
                return true;
            }

            while (currentInternalLevel < targetInternalLevel)
            {
                bool upgradeResult = weaponsInventory.UpgradeWeapon(item.Id);
                LogInfo($"{sourceTag}: UpgradeWeapon({item.Id}) returned {upgradeResult}");

                weaponInstance = weaponsInventory.GetWeaponInstance(weaponData);
                if (weaponInstance == null)
                {
                    LogWarning($"{sourceTag}: weapon upgrade grant failed, weapon instance disappeared after upgrade.");
                    return false;
                }

                int newInternalLevel = weaponInstance.Level;
                LogInfo(
                    $"{sourceTag}: weapon {item.Id} level after upgrade attempt = " +
                    $"internal {newInternalLevel} / displayed {newInternalLevel + 1}"
                );

                if (newInternalLevel <= currentInternalLevel)
                {
                    LogWarning(
                        $"{sourceTag}: weapon {item.Id} stopped upgrading before target level. " +
                        $"CurrentInternal={newInternalLevel}, TargetInternal={targetInternalLevel}"
                    );
                    break;
                }

                currentInternalLevel = newInternalLevel;
            }

            bool reachedTarget = currentInternalLevel >= targetInternalLevel;
            LogInfo(
                $"{sourceTag}: weapon {item.Id} reachedTarget={reachedTarget}, " +
                $"finalInternalLevel={currentInternalLevel}, " +
                $"finalDisplayedLevel={currentInternalLevel + 1}"
            );

            return reachedTarget;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while upgrading weapon {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants ingredients through InventoryManager using the item's internal ID.
    internal static bool TryGrantIngredient(PendingItem item, string sourceTag)
    {
        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: ingredient grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Read amount before grant.
            int before = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: ingredient {item.Id} before grant = {before}");

            // Try to add the ingredient by item id.
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Read amount after grant.
            int after = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: ingredient {item.Id} after grant = {after}");

            // Success if amount increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting ingredient {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants crafting materials through InventoryManager using the item's internal ID.
    internal static bool TryGrantMaterial(PendingItem item, string sourceTag)
    {
        // Friendly log line for readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: material grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Read amount before grant.
            int before = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: material {item.Id} before grant = {before}");

            // Try to add the material by item id.
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            // Read amount after grant.
            int after = inventory.GetItemAmount(item.Id);
            LogInfo($"{sourceTag}: material {item.Id} after grant = {after}");

            // Success if amount increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting material {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a cassette / collectible using the game's cassette manager.
    internal static bool TryGrantCollectible(PendingItem item, string sourceTag)
    {
        // Grab the cassette manager singleton that owns cassette inventory.
        var cassettesManager = Singleton<CassettesManager>.Instance;

        // Safety check in case the manager is not ready yet.
        if (cassettesManager == null)
        {
            LogWarning($"{sourceTag}: collectible grant failed, CassettesManager is null.");
            return false;
        }

        if (item.Id == "I_COLLECTION_JAKOB")
        {
            LogWarning($"{sourceTag}: collectible {item.Id} is a bundle/progression reward, not a directly grantable cassette.");
            return true;
        }

        try
        {
            // Check whether the player already owns this cassette.
            bool alreadyOwned = cassettesManager.HasCassette(item.Id);
            LogInfo($"{sourceTag}: cassette {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it doesn't stay in the queue.
            if (alreadyOwned)
            {
                LogInfo($"{sourceTag}: skipping cassette {item.Id} because player already owns it.");
                return true;
            }

            // Try to add the cassette by its internal ID.
            bool addResult = cassettesManager.AddCassetteToInventory(item.Id, null, false);
            LogInfo($"{sourceTag}: AddCassetteToInventory({item.Id}) returned {addResult}");

            // Check again after trying to add it.
            bool ownedAfter = cassettesManager.HasCassette(item.Id);
            LogInfo($"{sourceTag}: ownedAfter={ownedAfter} for cassette {item.Id}");

            if (ownedAfter)
            {
                SuppressedCassetteChecks.Add(item.Id);
                LogInfo($"{sourceTag}: cassette {item.Id} marked for one-shot check suppression.");
            }

            // Success if the player now owns it.
            return ownedAfter;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting cassette {item.Id}:\n{ex}");
            return false;
        }
    }

    // Puppy gifts are a little weird compared to normal inventory items.
    // I resolve the ItemData first and try the ItemData overload, then fall back to the string overload.
    // Suppression gets added first so AP-received gifts do not come back through the real source patch
    // and count as locally found checks.
    internal static bool TryGrantPuppyTreat(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting puppy treat {item.DisplayName}");

        // Grab the game's runtime inventory manager.
        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, InventoryManager is null.");
            return false;
        }

        // Grab the game's runtime item database so we can resolve the real ItemData object.
        var itemLoader = Singleton<ItemDataLoader>.Instance;

        if (itemLoader == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, ItemDataLoader is null.");
            return false;
        }

        // Normalize once so suppression and location handling always use the same ID format.
        string normalizedGiftId = NormalizePuppyGiftId(item.Id);

        // If the player already owns the Puppy gift, treat that as success so it does not stay stuck in the queue.
        bool alreadyOwned = inventory.HasItem(item.Id);
        LogInfo($"{sourceTag}: puppy treat {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
        {
            LogInfo($"{sourceTag}: skipping puppy gift {item.Id} because player already owns it.");
            return true;
        }

        // Add suppression BEFORE any grant attempt so our Puppy source patch sees it in time.
        SuppressedPuppyGiftChecks.Add(normalizedGiftId);
        LogInfo($"{sourceTag}: puppy gift {normalizedGiftId} pre-marked for one-shot check suppression.");

        bool grantSucceeded = false;

        try
        {
            // Resolve the game's real ItemData for this Puppy gift.
            ItemData itemData = itemLoader.Find(item.Id);

            if (itemData == null)
            {
                LogWarning($"{sourceTag}: puppy treat grant failed, ItemDataLoader.Find({item.Id}) returned null.");
            }
            else
            {
                // First try the generic AddItem(...) path using the resolved ItemData.
                // Even though we cannot directly call AddKeyItem(...) from here,
                // InventoryManager.AddItem(...) may still internally route certain Puppy gifts
                // through the game's own key-item logic.
                bool addItemFromDataResult = inventory.AddItem(itemData, item.Amount, null, false);
                LogInfo($"{sourceTag}: AddItem(ItemData:{item.Id}, {item.Amount}) returned {addItemFromDataResult}");

                grantSucceeded = addItemFromDataResult;
            }

            // If AddItem(ItemData, ...) did not work, fall back to the string-id version.
            // Some items in Laika appear to behave differently depending on which overload is used.
            if (!grantSucceeded)
            {
                bool addItemResult = inventory.AddItem(item.Id, item.Amount, null, false);
                LogInfo($"{sourceTag}: fallback AddItem(string:{item.Id}, {item.Amount}) returned {addItemResult}");

                grantSucceeded = addItemResult;
            }

            // If the game still says it failed, remove suppression so a future retry is clean.
            if (!grantSucceeded)
            {
                SuppressedPuppyGiftChecks.Remove(normalizedGiftId);
                LogWarning($"{sourceTag}: puppy gift {normalizedGiftId} grant failed, suppression removed.");
                return false;
            }

            // Final ownership check for logging only.
            bool ownedAfter = inventory.HasItem(item.Id);
            LogInfo($"{sourceTag}: puppy treat {item.Id}, ownedAfter={ownedAfter}");

            return true;
        }
        catch (Exception ex)
        {
            // If anything throws, remove suppression so the queue can safely retry later.
            SuppressedPuppyGiftChecks.Remove(normalizedGiftId);
            LogError($"{sourceTag}: exception while granting puppy treat {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a key item through the game's inventory system.
    // The InventoryManager will internally route key items through AddKeyItem(...)
    internal static bool TryGrantKeyItem(PendingItem item, string sourceTag)
    {
        // Friendly log line so we can read logs more easily.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime inventory manager singleton.
        var inventory = Singleton<InventoryManager>.Instance;

        // Safety check in case inventory is not ready yet.
        if (inventory == null)
        {
            LogWarning($"{sourceTag}: key item grant failed, InventoryManager is null.");
            return false;
        }

        try
        {
            // Check whether the player already has this item before granting it.
            bool alreadyOwned = inventory.HasItem(item.Id);
            LogInfo($"{sourceTag}: key item {item.Id}, alreadyOwned={alreadyOwned}");

            // If already owned, treat it as success so it does not stay in the queue.
            if (alreadyOwned)
            {
                LogInfo($"{sourceTag}: skipping key item {item.Id} because player already owns it.");
                return true;
            }

            // Try to add it through InventoryManager.
            // If the item is marked as a key item internally, the game will route it through AddKeyItem(...).
            bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);
            LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

            if (addResult)
            {
                ApplyKeyItemProgressionFlags(item, sourceTag);
            }

            // Key items/upgrades may not show up in the normal HasItem() check.
            // If AddItem() returned true, trust the game's internal key item handling.
            LogInfo($"{sourceTag}: assuming success from AddItem result for key item {item.Id}");

            if (addResult && item.Id == "I_E_HOOK")
            {
                LogImportantQuestSnapshots($"{sourceTag}: hook grant follow-up");
            }

            return addResult;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting key item {item.Id}:\n{ex}");
            return false;
        }
    }

    // Map pieces are just progression unlocks under the hood,
    // so I can grant them straight through ProgressionData using the internal map ID.
    internal static bool TryGrantMapUnlock(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;

        if (progressionManager == null)
        {
            LogWarning($"{sourceTag}: map unlock grant failed, ProgressionManager is null.");
            return false;
        }

        try
        {
            // Ask the game to unlock the target map area directly.
            progressionManager.ProgressionData.UnlockMapArea(item.Id);
            LogInfo($"{sourceTag}: UnlockMapArea({item.Id}) called successfully.");

            // For now, trust the game's internal unlock flow.
            return true;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while unlocking map area {item.Id}:\n{ex}");
            return false;
        }
    }

    // ===== Progression helpers =====
    // Some key items are not enough by themselves.
    // Vanilla also flips progression flags when the player gets them, so I mirror that here
    // when AP gives the item early.
    internal static void ApplyKeyItemProgressionFlags(PendingItem item, string sourceTag)
    {
        // Dash needs the G_DASH_UNLOCKED progression flag in addition to the item itself.
        if (item.Id == "I_DASH")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_DASH_UNLOCKED", true, false);
            LogInfo($"{sourceTag}: set progression flag G_DASH_UNLOCKED for Dash.");
        }

        // Hook needs the G_HOOK_UNLOCKED progression flag in addition to the item itself.
        else if (item.Id == "I_E_HOOK")
        {
            MonoSingleton<ProgressionManager>.Instance.ProgressionData.SetAchievement("G_HOOK_UNLOCKED", true, false);
            LogInfo($"{sourceTag}: set progression flag G_HOOK_UNLOCKED for Hook.");
        }
    }

    // Tracks whether we already forced the parry unlock this session.
    // This prevents repeatedly writing the same progression flag.
    internal static bool ParryUnlockEnsuredThisSession = false;

    // Vanilla gives parry through the tutorial, so the player can never miss it there.
    // In AP that is not guaranteed, so I force the progression flag once the game managers are actually alive.
    // This only needs to happen once per launch.
    internal static void TryEnsureParryUnlockedOnce(string sourceTag)
    {
        if (ParryUnlockEnsuredThisSession)
            return;

        var progressionManager = MonoSingleton<ProgressionManager>.Instance;

        if (progressionManager == null)
        {
            LogWarning($"{sourceTag}: could not unlock parry because ProgressionManager is null.");
            return;
        }

        // This is the actual achievement checked by ParryShield.Update().
        progressionManager.ProgressionData.SetAchievement("G_PARRY_UNLOCKED", true, false);
        ParryUnlockEnsuredThisSession = true;

        LogInfo($"{sourceTag}: set progression flag G_PARRY_UNLOCKED.");
    }

    // Temporary quest-debug helpers.
    // These are useful when I need to inspect active goal ids and progression state for a broken quest.
    // They should stay out of normal runtime flow unless I am actively diagnosing a quest issue.
    internal static void LogQuestGoals(string questId, string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because QuestLog is null.");
                return;
            }

            List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

            if (activeQuests == null)
            {
                LogWarning($"{sourceTag}: could not log quest goals because active quest list was null.");
                return;
            }

            QuestInstance quest = activeQuests.Find(x => x != null && x.QuestId == questId);

            if (quest == null)
            {
                LogInfo($"{sourceTag}: quest {questId} is not active.");
                return;
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT START -> {questId}");

            foreach (QuestGoal goal in quest.goals)
            {
                if (goal == null)
                    continue;

                LogInfo(
                    $"{sourceTag}: " +
                    $"quest={questId}, " +
                    $"goalId={goal.GoalId}, " +
                    $"goalType={goal.GetType().Name}, " +
                    $"completed={goal.Completed}, " +
                    $"current={goal.CurrentAmount}, " +
                    $"required={goal.RequiredAmount}, " +
                    $"description={goal.Description}"
                );
            }

            QuestGoal currentGoal = quest.GetCurrentGoal();

            if (currentGoal != null)
            {
                LogInfo(
                    $"{sourceTag}: CURRENT GOAL -> " +
                    $"quest={questId}, " +
                    $"goalId={currentGoal.GoalId}, " +
                    $"goalType={currentGoal.GetType().Name}, " +
                    $"description={currentGoal.Description}"
                );
            }

            LogInfo($"{sourceTag}: QUEST SNAPSHOT END -> {questId}");
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while logging quest goals for {questId}:\n{ex}");
        }
    }

    internal static void LogImportantQuestSnapshots(string sourceTag)
    {
        LogQuestGoals("Q_D_S_OldWarfare", sourceTag);
        LogQuestGoals("Q_D_S_TutorialHook", sourceTag);
        LogQuestGoals("Q_D_2_Lighthouse", sourceTag);
    }

    // ===== Display-name helpers =====
    // Resolves a known AP location definition by internal Laika identifier.
    internal static bool TryGetLocationDefinition(string internalId, out APLocationDefinition definition)
    {
        if (string.IsNullOrEmpty(internalId))
        {
            definition = null;
            return false;
        }

        return LocationDefinitionsByInternalId.TryGetValue(internalId, out definition);
    }

    // Resolve a collectible's player-facing name from its internal game ID.
    internal static bool TryGetCollectibleDisplayName(string internalId, out string displayName)
    {
        return CollectibleDisplayNamesByInternalId.TryGetValue(internalId, out displayName);
    }

    // Build a pending collectible item using the canonical cassette display-name registry.
    internal static PendingItem CreateCollectiblePendingItem(string collectibleId, int amount = 1)
    {
        string displayName;
        if (!TryGetCollectibleDisplayName(collectibleId, out displayName))
        {
            displayName = $"Cassette Tape ({collectibleId})";
        }

        return new PendingItem(ItemKind.Collectible, collectibleId, amount, displayName);
    }

    // Centralized check-send path for all AP-style locations.
    // This is the one place that should handle duplicate suppression and persistent sent-state.
    internal static void TrySendLocationCheck(APLocationDefinition definition, string sourceTag)
    {
        if (definition == null)
        {
            LogWarning($"{sourceTag}: TrySendLocationCheck called with null definition.");
            return;
        }

        if (HasLocationBeenSent(definition.LocationId))
        {
            LogInfo($"{sourceTag}: check already sent -> {definition.DisplayName} ({definition.LocationId})");
            return;
        }

        if (ArchipelagoClientManager.Instance != null)
        {
            ArchipelagoClientManager.Instance.SendLocationCheck(definition);
        }
        else
        {
            LogWarning($"{sourceTag}: ArchipelagoClientManager missing, check not sent -> {definition.DisplayName}");
        }
    }

    internal static string BuildReceivedItemAnnouncement(PendingItem item)
    {
        if (item == null)
            return "[AP] Received: <null item>";

        if (item.Amount > 1)
            return $"[AP] Received: {item.DisplayName} x{item.Amount}";

        return $"[AP] Received: {item.DisplayName}";
    }

    // Centralized cassette check handler.
    // This consumes one-shot suppression for AP-granted cassette items so that receiving
    // a cassette does not accidentally count as finding its real in-world location.
    internal static void TryHandleCassetteLocationCheck(string cassetteId, string sourceTag)
    {
        if (string.IsNullOrEmpty(cassetteId))
        {
            LogWarning($"{sourceTag}: cassetteId was null or empty.");
            return;
        }

        if (SuppressedCassetteChecks.Remove(cassetteId))
        {
            LogInfo($"{sourceTag}: suppressed cassette check for AP-granted cassette {cassetteId}.");
            return;
        }

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(cassetteId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for cassetteId={cassetteId}");
            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag);
    }

    internal static string NormalizePuppyGiftId(string giftId)
    {
        if (string.IsNullOrEmpty(giftId))
            return giftId;

        string normalized = giftId.Trim();

        // Keep this here in case the game ever gives us something like I_TOY_BIKEgift.
        if (normalized.EndsWith("gift", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - 4);
        }

        return normalized;
    }

    internal static bool ConsumeSuppressedPuppyGiftCheck(string giftId, string sourceTag)
    {
        if (string.IsNullOrEmpty(giftId))
            return false;

        if (SuppressedPuppyGiftChecks.Remove(giftId))
        {
            LogInfo($"{sourceTag}: suppressed puppy gift check for AP-granted item {giftId}.");
            return true;
        }

        return false;
    }

    // Handles a real Puppy gift source and turns it into an AP location check.
    // If the gift came from AP instead of being found naturally, suppression eats it here
    // so the player does not get a fake local check for an item they were only sent.
    internal static void TryHandlePuppyGiftLocationCheck(string giftId, string sourceTag)
    {
        if (string.IsNullOrEmpty(giftId))
        {
            LogWarning($"{sourceTag}: puppy gift id was null or empty.");
            return;
        }

        string normalizedGiftId = NormalizePuppyGiftId(giftId);

        LogInfo($"{sourceTag}: puppy gift rawId={giftId}, normalizedId={normalizedGiftId}");

        if (ConsumeSuppressedPuppyGiftCheck(normalizedGiftId, sourceTag))
            return;

        APLocationDefinition locationDefinition;
        if (!TryGetLocationDefinition(normalizedGiftId, out locationDefinition))
        {
            LogWarning($"{sourceTag}: no AP location definition found for puppy gift id={normalizedGiftId}");
            return;
        }

        if (locationDefinition.Category != "PuppyGift")
        {
            LogWarning(
                $"{sourceTag}: resolved id {normalizedGiftId} but category was {locationDefinition.Category}, not PuppyGift."
            );
            return;
        }

        TrySendLocationCheck(locationDefinition, sourceTag);
    }

    // Looks for one active quest by id.
    // I only care about active quests here because these softlocks happen mid-quest.
    internal static QuestInstance FindActiveQuest(string questId)
    {
        QuestLog questLog = Singleton<QuestLog>.Instance;

        if (questLog == null)
            return null;

        List<QuestInstance> activeQuests = questLog.GetActiveQuestsList();

        if (activeQuests == null)
            return null;

        return activeQuests.Find(x => x != null && x.QuestId == questId);
    }

    // Some AP softlocks are not item grant failures.
    // They happen because the player already owns the required item before the vanilla quest step becomes current.
    // I reconcile those here by only completing the one blocked goal, not the whole quest.
    internal static void TryReconcileKnownQuestSoftlocks(string sourceTag)
    {
        try
        {
            QuestLog questLog = Singleton<QuestLog>.Instance;

            if (questLog == null)
            {
                LogWarning($"{sourceTag}: could not reconcile quest softlocks because QuestLog is null.");
                return;
            }

            TryReconcileOldWarfareShotgunGoal(questLog, sourceTag);
            TryReconcileTutorialHookGoal(questLog, sourceTag);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while reconciling known quest softlocks:\n{ex}");
        }
    }

    // Old Warfare can get stuck if AP gives the player the shotgun early.
    // CraftShotgun is a GatherQuestGoal, so the normal TryCompleteQuestGoal / ForceComplete path
    // does not advance it correctly here. Instead, I re-run the goal's own CheckGoal() logic
    // once the player already owns the shotgun so the quest can move on to ShowShotgun normally.
    internal static void TryReconcileOldWarfareShotgunGoal(QuestLog questLog, string sourceTag)
    {
        QuestInstance quest = FindActiveQuest("Q_D_S_OldWarfare");

        if (quest == null)
            return;

        QuestGoal currentGoal = quest.GetCurrentGoal();

        if (currentGoal == null)
            return;

        if (currentGoal.GoalId != "CraftShotgun")
            return;

        WeaponsInventory weaponsInventory = Singleton<WeaponsInventory>.Instance;

        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: could not reconcile Old Warfare because WeaponsInventory is null.");
            return;
        }

        if (!weaponsInventory.HasWeapon("I_W_SHOTGUN"))
            return;

        LogInfo($"{sourceTag}: reconciling Old Warfare softlock by re-checking CraftShotgun because the player already owns the shotgun.");
        AnnounceAPActivity("[AP] Old Warfare updated because you already had the shotgun.");

        try
        {
            // GatherQuestGoal does not implement ForceComplete(), so I need to call its own
            // internal CheckGoal() path directly. That lets the quest use its normal gather-goal
            // completion logic instead of me faking a quest close or skipping ahead too far.
            MethodInfo checkGoalMethod = currentGoal.GetType().GetMethod(
                "CheckGoal",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (checkGoalMethod == null)
            {
                LogWarning($"{sourceTag}: could not find CheckGoal() on CraftShotgun goal.");
                return;
            }

            checkGoalMethod.Invoke(currentGoal, null);
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while re-checking CraftShotgun:\n{ex}");
        }
    }

    // Bonehead's Hook can get stuck if AP gives the player the hook before the quest reaches GetHook.
    // In that case, the quest still waits on the first scripted goal even though the player already has the hook.
    // I only complete GetHook so the rest of the quest can still play out normally.
    internal static void TryReconcileTutorialHookGoal(QuestLog questLog, string sourceTag)
    {
        QuestInstance quest = FindActiveQuest("Q_D_S_TutorialHook");

        if (quest == null)
            return;

        QuestGoal currentGoal = quest.GetCurrentGoal();

        if (currentGoal == null)
            return;

        if (currentGoal.GoalId != "GetHook")
            return;

        InventoryManager inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: could not reconcile TutorialHook because InventoryManager is null.");
            return;
        }

        if (!inventory.HasItem("I_E_HOOK"))
            return;

        LogInfo($"{sourceTag}: reconciling Bonehead's Hook softlock by completing GetHook because the player already owns the hook.");
        AnnounceAPActivity("[AP] Bonehead's Hook updated because you already had the hook.");

        questLog.TryCompleteQuestGoal("Q_D_S_TutorialHook", "GetHook");
    }

    // ===== Weapon mode helpers =====
    // Returns the correct item definition for a major weapon unlock
    // based on the configured weapon grant mode.
    //
    // Direct mode:
    // Returns the weapon itself.
    //
    // Crafting mode:
    // Returns the weapon's unique crafting material instead.
    //
    // Note:
    // Crossbow does not currently have a clean one-to-one unique crafting material,
    // so it should remain direct-only until a better crafting-mode design is decided.
    internal static PendingItem GetWeaponUnlockItem(
        string directWeaponId,
        string directDisplayName,
        string craftingMaterialId,
        string craftingDisplayName)
    {
        if (WorldOptions.WeaponMode == WeaponGrantMode.Crafting)
        {
            return new PendingItem(ItemKind.Material, craftingMaterialId, 1, craftingDisplayName);
        }

        return new PendingItem(ItemKind.Weapon, directWeaponId, 1, directDisplayName);
    }

    // Specific weapon unlock helpers that use the generic weapon-mode resolver.
    
    //Forces the player to start with a pistol. The player doesn't require
    //it. However, if the player interacts with Jakob too early via a
    //different weapon, then there is a chance the player will never
    //acquire the pistol at all. This enforces it.
    internal static void EnqueueRequiredStartingItems()
    {
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_PISTOL", 1, "Pistol"));
    }

    internal static PendingItem GetRocketLauncherUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_ROCKETLAUNCHER",
            "Rocket Launcher",
            "I_MATERIAL_ROCKETLAUNCHER",
            "Missile (Rocket Launcher Material)"
        );
    }

    internal static PendingItem GetShotgunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SHOTGUN",
            "Shotgun",
            "I_MATERIAL_SHOTGUN",
            "Rusty Spring (Shotgun Material)"
        );
    }

    internal static PendingItem GetSniperUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SNIPER",
            "Sniper Rifle",
            "I_MATERIAL_SNIPER",
            "Magnifying Glass (Sniper Rifle Material)"
        );
    }

    internal static PendingItem GetMachineGunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_UZI",
            "Machine Gun",
            "I_MATERIAL_UZI",
            "Titanium Plates (Machine Gun Material)"
        );
    }

    // ===== DeathLink helpers =====
    // Kept for safety in case other game flows call the explicit RiderHead.Kill(bool, bool) overload.
    // Handles AP-local death counting after a real player death is detected.
    internal static void OnPlayerDeathDetected(string sourceTag, bool? useBlood = null, bool? moneySack = null)
    {
        try
        {
            // If this death was caused by a future incoming DeathLink,
            // do not count it toward outbound DeathLink logic.
            if (SuppressedDeathLinksRemaining > 0)
            {
                SuppressedDeathLinksRemaining--;

                LogInfo(
                    $"{sourceTag} | " +
                    $"Incoming DeathLink suppression consumed. " +
                    $"RemainingSuppressedDeaths={SuppressedDeathLinksRemaining}"
                );

                return;
            }

            // Increment AP-local counters only.
            // Do not use the game's total "deaths" stat for death amnesty.
            LocalDeathsThisSession++;
            DeathsSinceLastDeathLink++;

            LogInfo(
                $"{sourceTag} | " +
                $"Session={LocalDeathsThisSession}, " +
                $"SinceLink={DeathsSinceLastDeathLink}, " +
                $"blood={(useBlood.HasValue ? useBlood.Value.ToString() : "def")}, " +
                $"sack={(moneySack.HasValue ? moneySack.Value.ToString() : "def")}"
            );

            // Evaluate what DeathLink would do for this death.
            EvaluateDeathLinkAfterLocalDeath(sourceTag);
        }
        catch (Exception ex)
        {
            LogError($"OnPlayerDeathDetected exception:\n{ex}");
        }
    }

    // Evaluates whether the current local death should count toward DeathLink
    // and logs what would happen.
    // For now this is log-only scaffolding until real AP networking is added.
    internal static void EvaluateDeathLinkAfterLocalDeath(string sourceTag)
    {
        // If DeathLink is disabled entirely, do nothing.
        if (!WorldOptions.DeathLinkEnabled)
        {
            LogInfo($"{sourceTag}: DeathLink disabled. No outbound DeathLink would be sent.");
            return;
        }

        // If death amnesty is disabled, every valid local death would send immediately.
        if (!WorldOptions.DeathAmnestyEnabled)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty disabled).");
            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }
        }

        // Safety clamp in case a bad config sets the threshold too low.
        int requiredDeaths = Math.Max(1, WorldOptions.DeathAmnestyCount);

        LogInfo(
            $"{sourceTag}: Death Amnesty Progress = {DeathsSinceLastDeathLink} / {requiredDeaths}"

        );

        AnnounceAPActivity($"[AP] Death count: {DeathsSinceLastDeathLink}/{requiredDeaths}");

        // When enough deaths are reached, a DeathLink would send.
        if (DeathsSinceLastDeathLink >= requiredDeaths)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty threshold reached).");

            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            // Reset the amnesty counter after a real send.
            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after real send.");
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

    // One-shot suppression for cassette checks triggered by AP-granted cassette items.
    // If a cassette is granted through the AP receive-item path, the next matching cassette event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedCassetteChecks = new HashSet<string>();

    //Suppression for Puppy's Gifts/treats.
    // If a Puppy Gift is granted through the AP receive-item path, the next matching Puppy Gift event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedPuppyGiftChecks = new HashSet<string>();

    // ===== Harmony patches =====
    [HarmonyPatch(typeof(WeaponsOverlay), "InitializeWeaponsData")]
    public class WeaponsOverlayPatch
    {
        static void Postfix(WeaponsOverlay __instance)
        {
            Log.LogInfo("WeaponsOverlay.InitializeWeaponsData postfix triggered.");

            EnsureRuntimeDevOverlay(__instance);

            // Only log ingredient IDs once per launch.
            if (!IngredientIdsLogged)
            {
                IngredientIdsLogged = true;
                // LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;
                // LogAllCassetteIds();
            }

            // Process queued AP-style items once the game UI/inventory systems are ready.
            ProcessPendingItemQueue("InitialItemGrant");
        }
    }


    [HarmonyPatch(typeof(QuestLog), "TryCloseQuest")]
    public class QuestClosePatch
    {
        static void Postfix(string questId, bool silent, bool __result)
        {
            // Only log successful full quest completions.
            if (!__result)
                return;

            LaikaMod.LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(questId, out locationDefinition))
            {
                LaikaMod.LogWarning($"QuestClosePatch: no AP location definition found for questId={questId}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "QuestClosePatch");
        }
    }

    // Logs Renato's map popup data when the buy-map popup opens.
    // Useful for verifying mapAreaID and price against Renato's shop data.
    [HarmonyPatch(typeof(ShowBuyingMapPopup), "OnEnter")]
    public class ShowBuyingMapPopupPatch
    {
        static void Prefix(ShowBuyingMapPopup __instance)
        {
            try
            {
                // Safety check in case the FSM values are missing for some reason.
                if (__instance == null)
                {
                    LaikaMod.LogWarning("ShowBuyingMapPopupPatch: __instance was null.");
                    return;
                }

                // Read the real PlayMaker values that Renato's popup is using.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";
                int mapAreaPrice = __instance.mapAreaPrice != null ? __instance.mapAreaPrice.Value : -1;

                // Log both the area ID and the price so we can identify which map piece is which.
                LaikaMod.LogInfo($"RENATO MAP POPUP: mapAreaID={mapAreaId}, price={mapAreaPrice}");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"ShowBuyingMapPopupPatch: exception while logging Renato map popup:\n{ex}");
            }
        }
    }

    // Tracks Renato map purchases as AP location checks.
    // Resolves the unlocked mapAreaID through the AP location registry.
    [HarmonyPatch(typeof(UnlockMapArea), "OnEnter")]
    public class UnlockMapAreaPatch
    {
        static void Prefix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                {
                    LaikaMod.LogWarning("UnlockMapAreaPatch: __instance was null.");
                    return;
                }

                // Read the raw internal mapAreaID for AP location resolution.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";

                // Log the raw map ID even if no AP location definition exists yet.
                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                {
                    LaikaMod.LogWarning(
                        $"UnlockMapAreaPatch: no AP location definition found for mapAreaID={mapAreaId}"
                    );
                    return;
                }

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch: exception while logging map unlock action:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(CassetteData), typeof(bool) })]
    public class CassetteInventoryRealSourcePatch
    {
        static void Postfix(CassetteData cassette, bool silent, bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (cassette == null)
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassette was null.");
                    return;
                }

                string cassetteId = cassette.id;

                if (string.IsNullOrEmpty(cassetteId))
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassetteId was null or empty.");
                    return;
                }

                LaikaMod.LogInfo(
                    $"CASSETTE INVENTORY SOURCE DETECTED: id={cassetteId}, silent={silent}, result={__result}"
                );

                LaikaMod.TryHandleCassetteLocationCheck(cassetteId, "CassetteInventoryRealSourcePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"CassetteInventoryRealSourcePatch exception:\n{ex}");
            }
        }
    }

    // Tracks boss clears through progression achievement keys.
    // Boss kill completion is persisted by the game as achievement-style flags.
    [HarmonyPatch(typeof(ProgressionData), "SetAchievement", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
    public class BossAchievementPatch
    {
        static void Prefix(string name, bool value, bool reset)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (!value)
                return;

            if (name != "B_BOSS_00_DEFEATED" &&
                name != "B_BOSS_01_DEFEATED" &&
                name != "B_BOSS_ROSCO_DEFEATED" &&
                name != "B_BOSS_02_DEFEATED" &&
                name != "B_BOSS_03_DEFEATED" &&
                name != "BOSS_04_DEFEATED")
            {
                return;
            }

            LaikaMod.LogInfo($"BOSS CLEAR DETECTED: name={name}, value={value}, reset={reset}");

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(name, out locationDefinition))
            {
                LaikaMod.LogWarning($"BossAchievementPatch: no AP location definition found for name={name}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "BossAchievementPatch");
        }
    }

    [HarmonyPatch(typeof(Boss_00), "OnEndingVideoEnd")]
    public class Boss00VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_00.OnEndingVideoEnd fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_01_Manager), "PrepareToKill")]
    public class Boss01VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_01_Manager.PrepareToKill fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_02), "OnEndingSequenceEnds")]
    public class Boss02VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_02.OnEndingSequenceEnds fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_02_LighthouseManager), "DefeatBoss")]
    public class Boss02LighthouseVerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_02_LighthouseManager.DefeatBoss fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_03), "Defeated")]
    public class Boss03VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_03.Defeated fired.");
        }
    }

    [HarmonyPatch(typeof(Boss_04), "Kill")]
    public class Boss04VerifyPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("BOSS VERIFY: Boss_04.Kill fired.");
        }
    }

    // Detects player death when the parameterless RiderHead.Kill() overload is used.
    [HarmonyPatch(typeof(global::RiderHead), "Kill", new Type[] { })]
    public class PlayerDeathPatch_NoArgs
    {
        static void Prefix()
        {
            LaikaMod.OnPlayerDeathDetected("PLAYER DEATH DETECTED (Kill())");
        }
    }

    // Detects player death when the RiderHead.Kill(bool useBlood, bool moneySack) overload is used.
    [HarmonyPatch(typeof(global::RiderHead), "Kill", new Type[] { typeof(bool), typeof(bool) })]
    public class PlayerDeathPatch_WithArgs
    {
        static void Prefix(bool useBlood, bool moneySack)
        {
            LaikaMod.OnPlayerDeathDetected("PLAYER DEATH DETECTED (Kill(bool,bool))", useBlood, moneySack);
        }
    }

    [HarmonyPatch(typeof(InventoryManager), "AddKeyItem", new Type[] { typeof(ItemData) })]
    public class PuppyGiftKeyItemSourcePatch
    {
        static void Postfix(ItemData __0, bool __result)
        {
            try
            {
                // Ignore failed grants because they did not actually add the Puppy item.
                if (!__result)
                    return;

                // Harmony positional argument __0 is the original ItemData parameter.
                ItemData itemData = __0;

                if (itemData == null)
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemData was null.");
                    return;
                }

                string itemId = itemData.id;

                if (string.IsNullOrEmpty(itemId))
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemId was null or empty.");
                    return;
                }

                // Helpful debug log so we can confirm which Puppy items naturally route through AddKeyItem(...).
                LaikaMod.LogInfo(
                    $"KEY ITEM SOURCE DETECTED: id={itemId}, name={itemData.Name}, result={__result}"
                );

                // Only route known Puppy gift IDs into the AP Puppy location handler.
                if (
                    itemId == "I_TOY_BIKE" ||
                    itemId == "I_GAMEBOY" ||
                    itemId == "I_PLANT_PUPPY" ||
                    itemId == "I_TOY_ANIMAL" ||
                    itemId == "I_BOOK_MOTHER" ||
                    itemId == "I_DREAMCATCHER" ||
                    itemId == "I_UKULELE"
                )
                {
                    // Debug log so we can prove the Puppy gift is actually being routed
                    // into the AP Puppy location-check handler.
                    LaikaMod.LogInfo(
                        $"PuppyGiftKeyItemSourcePatch: routing puppy gift id {itemId} into TryHandlePuppyGiftLocationCheck."
                    );

                    LaikaMod.TryHandlePuppyGiftLocationCheck(itemId, "PuppyGiftKeyItemSourcePatch");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"PuppyGiftKeyItemSourcePatch exception:\n{ex}");
            }
        }
    }

    // Some AP softlocks happen right when a quest gets added or advanced.
    // I re-run the quest softlock reconciliation here so the fix can happen immediately
    // instead of waiting for a later zone load or queue wake-up.
    [HarmonyPatch(typeof(TryAddQuest), "OnEnter")]
    public class TryAddQuestReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryAddQuestReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryAddQuestReconcilePatch exception:\n{ex}");
            }
        }
    }

    // Some vanilla flows complete one goal and immediately move to the next.
    // If AP already gave the required item, that newly current goal can softlock on the spot.
    // Running the reconciliation here makes the fix happen much earlier.
    [HarmonyPatch(typeof(TryCompleteQuestGoal), "OnEnter")]
    public class TryCompleteQuestGoalReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryCompleteQuestGoalReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryCompleteQuestGoalReconcilePatch exception:\n{ex}");
            }
        }
    }

    // Update only exists to ensure one-time initialization confirmation.
    // Recent log visibility is driven by AnnounceAPActivity(...) + Invoke-based auto-hide.
    public class DevOverlayController : MonoBehaviour
    {
        private bool initialized = false;
        private bool updateLoggedOnce = false;
        private Rect apDebugPanelRect = new Rect(10f, 10f, 340f, 320f);
        private bool showAPDebugPanel = true;

        void Start()
        {
            LaikaMod.Log.LogInfo("DevOverlayController Start() fired.");
            InitializeOverlay();
        }

        void OnEnable()
        {
            LaikaMod.Log.LogInfo("DevOverlayController OnEnable() fired.");
            InitializeOverlay();
        }

        void Update()
        {
            if (!initialized)
                InitializeOverlay();

            if (Input.GetKeyDown(KeyCode.F10))
            {
                showAPDebugPanel = !showAPDebugPanel;

                LaikaMod.Log.LogInfo(
                    $"AP DEBUG UI visibility toggled -> {showAPDebugPanel}"
                );
            }

            if (!updateLoggedOnce)
            {
                updateLoggedOnce = true;
                LaikaMod.Log.LogInfo("DevOverlayController Update() is running.");
            }

            if (ArchipelagoClientManager.Instance != null)
            {
                ArchipelagoClientManager.Instance.PumpReceivedItems();
            }
        }

        void OnGUI()
        {
            if (!initialized)
                return;

            if (showAPDebugPanel)
            {
                apDebugPanelRect = GUI.Window(
                    928374,
                    apDebugPanelRect,
                    DrawAPDebugWindow,
                    "Laika AP Debug"
                );
            }
        }

        // Cancels any pending hide callback and schedules a new one.
        // This makes each new recent-log event extend the panel's visibility window.
        public void ResetRecentLogAutoHideTimer()
        {
            CancelInvoke(nameof(HideRecentLogs));
            Invoke(nameof(HideRecentLogs), LaikaMod.RecentLogAutoHideDelaySeconds);
        }

        // Called by Invoke(...) after RecentLogAutoHideDelaySeconds of inactivity.
        private void HideRecentLogs()
        {
            LaikaMod.Log.LogInfo("Recent log overlay auto-hidden.");
            LaikaMod.ShowRecentLogOverlay = false;
            LaikaMod.RefreshDevOverlay();
        }

        private void InitializeOverlay()
        {
            if (initialized)
                return;

            LaikaMod.EnsureDevOverlayCanvas();

            initialized = true;

            LaikaMod.ShowRecentLogOverlay = false;

            LaikaMod.RefreshDevOverlay();
            LaikaMod.Log.LogInfo("DevOverlayController initialized runtime overlay.");
        }

        private void DrawAPDebugWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUILayout.Label($"Active AP Slot: {LaikaMod.ActiveSaveSlotIndex}");

            bool isConnected = ArchipelagoClientManager.Instance != null &&
                   ArchipelagoClientManager.Instance.IsConnected;

            GUILayout.Label($"Connection: {(isConnected ? "Connected" : "Not Connected")}");

            GUILayout.Space(6f);

            if (GUILayout.Button("Load AP Slot 1"))
            {
                LaikaMod.ActiveSaveSlotIndex = 1;
                LaikaMod.LoadSessionState();
                LaikaMod.Log.LogInfo("AP DEBUG UI: loaded slot 1.");
                LaikaMod.AnnounceAPActivity("[AP] Loaded AP slot 1.");
            }

            if (GUILayout.Button("Load AP Slot 2"))
            {
                LaikaMod.ActiveSaveSlotIndex = 2;
                LaikaMod.LoadSessionState();
                LaikaMod.Log.LogInfo("AP DEBUG UI: loaded slot 2.");
                LaikaMod.AnnounceAPActivity("[AP] Loaded AP slot 2.");
            }

            if (GUILayout.Button("Load AP Slot 3"))
            {
                LaikaMod.ActiveSaveSlotIndex = 3;
                LaikaMod.LoadSessionState();
                LaikaMod.Log.LogInfo("AP DEBUG UI: loaded slot 3.");
                LaikaMod.AnnounceAPActivity("[AP] Loaded AP slot 3.");
            }

            GUILayout.Space(8f);

            if (GUILayout.Button("Connect Active Slot"))
            {
                LaikaMod.ConnectActiveSlotIfConfigured();
            }

            if (GUILayout.Button("Kill Player"))
            {
                var rider = GameObject.FindObjectOfType<RiderHead>();
                if (rider != null)
                {
                    rider.Kill();
                    LaikaMod.Log.LogInfo("AP DEBUG UI: forced player death.");
                }
            }

            if (GUILayout.Button("Trigger DeathLink Send"))
            {
                LaikaMod.OnPlayerDeathDetected("DEBUG BUTTON");
            }

            if (GUILayout.Button("Give Shotgun"))
            {
                LaikaMod.EnqueueItem(new PendingItem(
                    ItemKind.Weapon,
                    "I_W_SHOTGUN",
                    1,
                    "Shotgun"
                ));

                LaikaMod.ProcessPendingItemQueue("AP DEBUG UI");
            }

            GUILayout.Space(8f);

            if (LaikaMod.SessionState != null)
            {
                string host = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.Host : "<null>";
                int port = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.Port : 0;
                string slotName = LaikaMod.SessionState.Connection != null ? LaikaMod.SessionState.Connection.SlotName : "<null>";
                bool apEnabled = LaikaMod.SessionState.APEnabled;

                GUILayout.Label($"AP Enabled: {apEnabled}");
                GUILayout.Label($"Host: {host}:{port}");
                GUILayout.Label($"Slot: {slotName}");
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUI.DragWindow();
        }
    }
}

// ===== Models / enums =====
// High-level AP item categories.
public enum ItemKind
{
    Currency,

    Weapon,
    WeaponUpgrade,

    Ingredient,
    Material,

    Collectible,
    PuppyTreat,

    KeyItem,
    MapUnlock,

    Unknown
}

// Determines how major weapons should be granted in the future.
//
// Direct:
// The player receives the weapon itself outright.
// Example: Shotgun is granted as I_W_SHOTGUN.
//
// Crafting:
// The player receives the unique crafting material instead.
// Example: Shotgun unlock is represented by I_MATERIAL_SHOTGUN.
public enum WeaponGrantMode
{
    Direct,
    Crafting
}

// Stores future world-generation / slot options.
// Later this can be filled from YAML or another external config source.
public class APWorldOptions
{
    // Controls whether major weapons are granted directly
    // or represented by their unique crafting materials instead.
    public WeaponGrantMode WeaponMode { get; set; } = WeaponGrantMode.Direct;

    // Enables or disables DeathLink behavior entirely.
    // For now this only affects local logging/scaffolding.
    public bool DeathLinkEnabled { get; set; } = false;

    // Enables death amnesty behavior.
    // When enabled, local deaths only "send" a DeathLink after enough deaths accumulate.
    public bool DeathAmnestyEnabled { get; set; } = false;

    // Number of local deaths required before a DeathLink would send when amnesty is enabled.
    public int DeathAmnestyCount { get; set; } = 1;
}

// Represents one pending AP-style item waiting to be granted.
// This stores both the real internal game ID and a friendly display name.
public class PendingItem
{
    // What subsystem should handle this item.
    public ItemKind Kind { get; private set; }

    // The real internal game ID used by the mod/game code.
    // Example: I_W_SNIPER or I_C_MEAT
    public string Id { get; private set; }

    // Quantity / amount for the item.
    // Example: 250 viscera or 3 meat
    public int Amount { get; private set; }

    // Friendly player-facing name for logs/UI.
    // Example: "Sniper Rifle" or "Meat x3"
    public string DisplayName { get; private set; }

    public PendingItem(ItemKind kind, string id, int amount, string displayName)
    {
        Kind = kind;
        Id = id;
        Amount = amount;
        DisplayName = displayName;
    }

    public override string ToString()
    {
        return $"Kind={Kind}, Id={Id}, Amount={Amount}, DisplayName={DisplayName}";
    }
}