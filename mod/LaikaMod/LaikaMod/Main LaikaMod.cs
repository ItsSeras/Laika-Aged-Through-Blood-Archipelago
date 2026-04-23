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

// AP save-state, per-slot persistence, and connection-state helpers.
[BepInPlugin("com.seras.laikaapprototype", "Laika AP Alpha", "0.01")]
public partial class LaikaMod : BaseUnityPlugin
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

        LogInfo("AP LIFECYCLE: Awake() entered.");



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

    // ===== Dev overlay state =====
    // Stores recent player-facing AP activity lines for the recent overlay.
    internal static Queue<string> OverlayLines = new Queue<string>();

    // Maximum number of lines to keep in the overlay at once.
    internal static int MaxOverlayLines = 15;

    // Recent-log box visibility is separate from the always-visible status HUD.
    internal static bool ShowRecentLogOverlay = false;
    internal static float RecentLogAutoHideDelaySeconds = 15f;

    // Tracks whether we already forced the parry unlock this session.
    // This prevents repeatedly writing the same progression flag.
    internal static bool ParryUnlockEnsuredThisSession = false;

    // One-shot suppression for cassette checks triggered by AP-granted cassette items.
    // If a cassette is granted through the AP receive-item path, the next matching cassette event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedCassetteChecks = new HashSet<string>();

    //Suppression for Puppy's Gifts/treats.
    // If a Puppy Gift is granted through the AP receive-item path, the next matching Puppy Gift event
    // should be ignored instead of counted as a real location check.
    internal static HashSet<string> SuppressedPuppyGiftChecks = new HashSet<string>();
}