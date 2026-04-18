using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Economy;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[BepInPlugin("com.seras.laikaapprototype", "Laika AP Prototype", "1.0.0")]
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

    // Temporary world options used for future YAML/config support.
    // For now this is hardcoded, but later it can be loaded from a YAML file.
    internal static APWorldOptions WorldOptions = new APWorldOptions();

    // Development mode toggle.
    // Enqueues development stress test items when set to true.
    internal static bool EnableDevelopmentStressTest = false;

    // Canvas-based dev overlay objects.
    internal static GameObject DevOverlayCanvasObject;
    internal static Text DevOverlayStatusText;
    internal static Text DevOverlayRecentLogText;

    internal static GameObject DevOverlayControllerObject;
    internal static DevOverlayController ActiveDevOverlayController;

    // ===== Startup =====
    private void Awake()
    {
        // Temporary hardcoded option for future YAML support.
        // Change this to Crafting to test unique-material weapon unlocks instead of direct weapon grants.
        WorldOptions.WeaponMode = WeaponGrantMode.Direct;

        // Temporary hardcoded DeathLink options for future YAML support.
        // For now this only controls local logging behavior.
        WorldOptions.DeathLinkEnabled = false;
        WorldOptions.DeathAmnestyEnabled = false;
        WorldOptions.DeathAmnestyCount = 3;

        // Save logger for static patches.
        Log = Logger;

        LoadSessionState();

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
    internal static void LoadSessionState()
    {
        try
        {
            SessionState = new APSessionState();

            if (!File.Exists(SessionStateFilePath))
            {
                Log.LogInfo("AP session state file not found. Using defaults.");
                return;
            }

            string[] lines = File.ReadAllLines(SessionStateFilePath);

            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();

                if (line.StartsWith("last_index="))
                {
                    int parsedValue;
                    if (int.TryParse(line.Substring("last_index=".Length), out parsedValue))
                    {
                        SessionState.LastProcessedReceivedItemIndex = parsedValue;
                    }
                }
                else if (line.StartsWith("goal_reported="))
                {
                    bool parsedValue;
                    if (bool.TryParse(line.Substring("goal_reported=".Length), out parsedValue))
                    {
                        SessionState.GoalReported = parsedValue;
                    }
                }
                else if (line.StartsWith("server_address="))
                {
                    SessionState.Connection.ServerAddress = line.Substring("server_address=".Length);
                }
                else if (line.StartsWith("server_port="))
                {
                    int parsedValue;
                    if (int.TryParse(line.Substring("server_port=".Length), out parsedValue))
                    {
                        SessionState.Connection.ServerPort = parsedValue;
                    }
                }
                else if (line.StartsWith("slot_name="))
                {
                    SessionState.Connection.SlotName = line.Substring("slot_name=".Length);
                }
                else if (line.StartsWith("team="))
                {
                    int parsedValue;
                    if (int.TryParse(line.Substring("team=".Length), out parsedValue))
                    {
                        SessionState.Connection.Team = parsedValue;
                    }
                }
                else if (line.StartsWith("slot="))
                {
                    int parsedValue;
                    if (int.TryParse(line.Substring("slot=".Length), out parsedValue))
                    {
                        SessionState.Connection.Slot = parsedValue;
                    }
                }
                else if (line.StartsWith("is_connected="))
                {
                    bool parsedValue;
                    if (bool.TryParse(line.Substring("is_connected=".Length), out parsedValue))
                    {
                        SessionState.Connection.IsConnected = parsedValue;
                    }
                }
                else if (line.StartsWith("is_authenticated="))
                {
                    bool parsedValue;
                    if (bool.TryParse(line.Substring("is_authenticated=".Length), out parsedValue))
                    {
                        SessionState.Connection.IsAuthenticated = parsedValue;
                    }
                }
                else if (line.StartsWith("sent_location="))
                {
                    long parsedValue;
                    if (long.TryParse(line.Substring("sent_location=".Length), out parsedValue))
                    {
                        SessionState.SentLocationIds.Add(parsedValue);
                    }
                }
            }

            Log.LogInfo(
                $"AP session state loaded. " +
                $"LastReceivedIndex={SessionState.LastProcessedReceivedItemIndex}, " +
                $"SentChecks={SessionState.SentLocationIds.Count}, " +
                $"GoalReported={SessionState.GoalReported}"
            );
        }
        catch (Exception ex)
        {
            SessionState = new APSessionState();
            Log.LogError($"Failed to load AP session state:\n{ex}");
        }
    }

    // Persistent local AP session state.
    // This stores client-side sync information so reconnects/resyncs are possible later.
    internal static APSessionState SessionState = new APSessionState();

    // Local path for AP session state persistence.
    // For now this uses a simple text file in the BepInEx config folder.
    internal static string SessionStateFilePath =
        Path.Combine(Paths.ConfigPath, "laika_ap_session_state.txt");

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
                    "Map Piece: Where Birds Came From (Left)",
                    "M_A_W08_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W08_RIGHT",
                new APLocationDefinition(
                    100016L,
                    "Map Piece: Where Birds Came From (Right)",
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


        };

    // Saves persistent AP session state to disk.
    internal static void SaveSessionState()
    {
        try
        {
            List<string> lines = new List<string>();

            lines.Add("last_index=" + SessionState.LastProcessedReceivedItemIndex);
            lines.Add("goal_reported=" + SessionState.GoalReported);
            lines.Add("server_address=" + SessionState.Connection.ServerAddress);
            lines.Add("server_port=" + SessionState.Connection.ServerPort);
            lines.Add("slot_name=" + SessionState.Connection.SlotName);
            lines.Add("team=" + SessionState.Connection.Team);
            lines.Add("slot=" + SessionState.Connection.Slot);
            lines.Add("is_connected=" + SessionState.Connection.IsConnected);
            lines.Add("is_authenticated=" + SessionState.Connection.IsAuthenticated);

            foreach (long locationId in SessionState.SentLocationIds)
            {
                lines.Add("sent_location=" + locationId);
            }

            File.WriteAllLines(SessionStateFilePath, lines.ToArray());

            Log.LogInfo(
                $"AP session state saved. " +
                $"LastReceivedIndex={SessionState.LastProcessedReceivedItemIndex}, " +
                $"SentChecks={SessionState.SentLocationIds.Count}, " +
                $"GoalReported={SessionState.GoalReported}"
            );
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to save AP session state:\n{ex}");
        }
    }

    // Marks a location as sent locally so reconnect/resync logic can reuse this data later.
    internal static void MarkLocationCheckedAndSent(long locationId)
    {
        if (SessionState.SentLocationIds.Add(locationId))
        {
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
        SessionState.Connection = new APConnectionInfo();

        Log.LogInfo("AP runtime connection state reset.");
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
        AddOverlayLine("[INFO] " + message);
    }

    internal static void LogWarning(string message)
    {
        Log.LogWarning(message);
        AddOverlayLine("[WARN] " + message);
    }

    internal static void LogError(string message)
    {
        Log.LogError(message);
        AddOverlayLine("[ERROR] " + message);
    }

    // Adds a new recent activity line, shows the recent-log panel,
    // and resets the auto-hide timer through the persistent overlay controller.
    internal static void AddOverlayLine(string line)
    {
        OverlayLines.Enqueue(line);

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
    // Stores recent on-screen debug lines for the in-game developer overlay.
    internal static Queue<string> OverlayLines = new Queue<string>();

    // Maximum number of lines to keep in the overlay at once.
    internal static int MaxOverlayLines = 10;

    // Recent-log box visibility is separate from the always-visible status HUD.
    internal static bool ShowRecentLogOverlay = false;
    internal static float RecentLogAutoHideDelaySeconds = 10f;

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
        statusPanelRect.sizeDelta = new Vector2(320f, 150f);

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
            "Laika AP Dev Overlay\n\n" +
            $"Queue={PendingItemQueue.Count}\n" +
            $"SessionDeaths={LocalDeathsThisSession}\n" +
            $"DeathsSinceLastLink={DeathsSinceLastDeathLink}\n" +
            $"DeathLink={WorldOptions.DeathLinkEnabled}\n" +
            $"DeathAmnesty={WorldOptions.DeathAmnestyEnabled} ({WorldOptions.DeathAmnestyCount})";

        DevOverlayRecentLogText.transform.parent.gameObject.SetActive(ShowRecentLogOverlay);

        if (ShowRecentLogOverlay)
        {
            DevOverlayRecentLogText.text =
                "Recent Logs:\n\n" +
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
    // Optional development hook for manually enqueueing test items.
    internal static void EnqueueDevelopmentStressTestItems()
    {
        // Intentionally left empty for now.
    }

    // Adds a pending item to the queue.
    internal static void EnqueueItem(PendingItem item)
    {
        PendingItemQueue.Enqueue(item);
        LogInfo($"QUEUE: added pending item -> {item}");
    }

    // Main queue processor. Called when game systems are in a good state.
    internal static void ProcessPendingItemQueue(string sourceTag)
    {
        if (IsProcessingQueue)
        {
            LogInfo($"{sourceTag}: queue processing already in progress, skipping nested call.");
            return;
        }

        if (PendingItemQueue.Count == 0)
        {
            Log.LogInfo($"{sourceTag}: no pending items to process.");
            return;
        }

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

                    if (!granted)
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
    // Routes an item to the correct grant handler.
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
    internal static bool TryGrantCurrency(PendingItem item, string sourceTag)
    {
        // Friendly log line for player-facing readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        var economy = Singleton<EconomyManager>.Instance;

        if (economy == null)
        {
            LogWarning($"{sourceTag}: currency grant failed, EconomyManager is null.");
            return false;
        }

        try
        {
            // Read current money before adding.
            int before = economy.Money;
            LogInfo($"{sourceTag}: currency before grant = {before}");

            // Add the requested amount.
            economy.AddMoney(item.Amount);

            // Read current money after adding.
            int after = economy.Money;
            LogInfo($"{sourceTag}: currency after grant = {after}");

            // Success if the money increased.
            return after > before;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting currency:\n{ex}");
            return false;
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

        return ownedAfter;
    }

    // Grants one weapon upgrade level using the game's own weapon upgrade method.
    internal static bool TryGrantWeaponUpgrade(PendingItem item, string sourceTag)
    {
        // Friendly log line for readability.
        LogInfo($"{sourceTag}: granting {item.DisplayName}");

        // Grab the runtime weapons inventory singleton.
        var weaponsInventory = Singleton<WeaponsInventory>.Instance;

        // Safety check in case the manager is not ready yet.
        if (weaponsInventory == null)
        {
            LogWarning($"{sourceTag}: weapon upgrade grant failed, WeaponsInventory is null.");
            return false;
        }

        try
        {
            // Make sure the player actually owns the weapon before trying to upgrade it.
            bool alreadyOwned = weaponsInventory.HasWeapon(item.Id);
            LogInfo($"{sourceTag}: weapon upgrade target {item.Id}, alreadyOwned={alreadyOwned}");

            // If the player does not own the weapon yet, do not consume the queue item.
            if (!alreadyOwned)
            {
                LogWarning($"{sourceTag}: cannot upgrade weapon {item.Id} because player does not own it yet.");
                return false;
            }

            // Ask the game to upgrade the weapon by one level.
            bool upgradeResult = weaponsInventory.UpgradeWeapon(item.Id);
            LogInfo($"{sourceTag}: UpgradeWeapon({item.Id}) returned {upgradeResult}");

            // Trust the game's own result here.
            return upgradeResult;
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

            // Success if the player now owns it.
            return ownedAfter;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting cassette {item.Id}:\n{ex}");
            return false;
        }
    }

    // Puppy treats are tracked through the normal inventory system.
    // If already owned, treat the grant as success so the item does not stay stuck in the queue.
    internal static bool TryGrantPuppyTreat(PendingItem item, string sourceTag)
    {
        LogInfo($"{sourceTag}: granting puppy treat {item.DisplayName}");

        var inventory = Singleton<InventoryManager>.Instance;

        if (inventory == null)
        {
            LogWarning($"{sourceTag}: puppy treat grant failed, InventoryManager is null.");
            return false;
        }

        bool alreadyOwned = inventory.HasItem(item.Id);

        LogInfo($"{sourceTag}: puppy treat {item.Id}, alreadyOwned={alreadyOwned}");

        if (alreadyOwned)
            return true;

        bool addResult = inventory.AddItem(item.Id, item.Amount, null, false);

        LogInfo($"{sourceTag}: AddItem({item.Id}, {item.Amount}) returned {addResult}");

        return addResult;
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

            return addResult;
        }
        catch (Exception ex)
        {
            LogError($"{sourceTag}: exception while granting key item {item.Id}:\n{ex}");
            return false;
        }
    }

    // Grants a map unlock through ProgressionData.
    // Renato's map popup and unlock flow use IDs like M_A_W06.
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
    // Applies extra progression flags required when AP grants key items early.
    // Expand this if more vanilla quest chains break from early item delivery.
    internal static void ApplyKeyItemProgressionFlags(PendingItem item, string sourceTag)
    {
        // Dash needs the G_DASH_UNLOCKED progression flag in addition to the item itself.
        if (item.Id == "I_E_DASH")
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

    // Centralized check-send path for all AP-style locations.
    // This is the one place that should handle duplicate suppression and persistent sent-state.
    internal static void TrySendLocationCheck(APLocationDefinition definition, string sourceTag)
    {
        if (definition == null)
        {
            LogWarning($"{sourceTag}: TrySendLocationCheck received null definition.");
            return;
        }

        if (HasLocationBeenSent(definition.LocationId))
        {
            LogInfo(
                $"{sourceTag}: LOCATION CHECK ALREADY SENT -> " +
                $"{definition.DisplayName} ({definition.LocationId})"
            );
            return;
        }

        MarkLocationCheckedAndSent(definition.LocationId);

        LogInfo(
            $"{sourceTag}: CHECK SENT -> " +
            $"{definition.DisplayName} ({definition.LocationId})"
        );
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
            LogInfo($"{sourceTag}: DEATHLINK WOULD SEND NOW (death amnesty disabled).");
            return;
        }

        // Safety clamp in case a bad config sets the threshold too low.
        int requiredDeaths = Math.Max(1, WorldOptions.DeathAmnestyCount);

        LogInfo(
            $"{sourceTag}: Death Amnesty Progress = {DeathsSinceLastDeathLink} / {requiredDeaths}"
        );

        // When enough deaths are reached, a DeathLink would send.
        if (DeathsSinceLastDeathLink >= requiredDeaths)
        {
            LogInfo($"{sourceTag}: DEATHLINK WOULD SEND NOW (death amnesty threshold reached).");

            // Reset the amnesty counter after a "send".
            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after simulated send.");
        }
    }

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
                // Temporarily disabled during overlay auto-hide/input testing.
                // LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;
                // Temporarily disabled during overlay auto-hide/input testing.
                // LogAllCassetteIds();
            }
            // Process AP queue afterward.
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

    // Update only exists to ensure one-time initialization confirmation.
    // Recent log visibility is driven by AddOverlayLine(...) + Invoke-based auto-hide.
    public class DevOverlayController : MonoBehaviour
    {
        private bool initialized = false;
        private bool updateLoggedOnce = false;

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

            if (!updateLoggedOnce)
            {
                updateLoggedOnce = true;
                LaikaMod.Log.LogInfo("DevOverlayController Update() is running.");
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

public class APSessionState
{
    // Last processed index from Archipelago ReceivedItems.
    // Needed so the client can reject already-processed items on reconnect.
    public int LastProcessedReceivedItemIndex { get; set; } = 0;

    // AP location ids already sent by this client.
    // This supports reconnect/resync and prevents duplicate sends.
    public HashSet<long> SentLocationIds { get; set; } = new HashSet<long>();

    // Whether this slot's goal completion has already been reported.
    public bool GoalReported { get; set; } = false;

    // Connection/session metadata.
    public APConnectionInfo Connection { get; set; } = new APConnectionInfo();
}

public class APConnectionInfo
{
    // Last server address the client tried to use.
    public string ServerAddress { get; set; } = string.Empty;

    // Last server port the client tried to use.
    public int ServerPort { get; set; } = 0;

    // Last authenticated slot/player name.
    public string SlotName { get; set; } = string.Empty;

    // Team/slot identifiers once connected.
    public int Team { get; set; } = 0;
    public int Slot { get; set; } = 0;

    // Whether the mod currently considers itself connected.
    public bool IsConnected { get; set; } = false;

    // Whether authentication completed successfully.
    public bool IsAuthenticated { get; set; } = false;
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