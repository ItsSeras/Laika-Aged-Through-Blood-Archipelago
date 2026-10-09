using HarmonyLib;
using Laika.Persistence;
using Laika.Quests;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;



public partial class LaikaMod
{
    // InitializeLocalSaveData sets LocalSaveData first, then restores inventory,
    // weapons, currency, and the player. Nested UI callbacks must wait until the
    // whole method completes successfully, not just until LocalSaveData exists.
    [HarmonyPatch(typeof(ProgressionManager), "InitializeLocalSaveData")]
    public class ProgressionManager_InitializeLocalSaveData_APItemDeliveryPatch
    {
        static void Prefix()
        {
            LaikaMod.SuspendAPItemDelivery("save initialization");
        }

        static void Postfix(ProgressionManager __instance)
        {
            LaikaMod.MarkAPItemSaveInitialized(__instance);
        }
    }

    [HarmonyPatch(typeof(ProgressionManager), "ResetLocalSaveData")]
    public class ProgressionManager_ResetLocalSaveData_APItemDeliveryPatch
    {
        static void Prefix()
        {
            LaikaMod.SuspendAPItemDelivery("local save reset");
        }
    }

    // Scene-load reconciliation Harmony patches.
    // These retry AP item/state reconciliation after Laika managers become available.
    public class PersistenceManager_OnSceneLoaded_APReconcilePatch
    {
        static void Postfix(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene.name == "LoadingScreen")
                    return;

                if (MonoSingleton<SceneLoader>.Instance != null &&
                    MonoSingleton<SceneLoader>.Instance.CurrentSceneIsTitleScreen)
                {
                    return;
                }

                LaikaMod.LogInfo($"AP SCENE RECONCILE: scene loaded -> {scene.name}");
                LaikaMod.ScheduleSceneLoadedAPReconcile($"PersistenceManager.OnSceneLoaded/{scene.name}");
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PersistenceManager_OnSceneLoaded_APReconcilePatch exception:\n{ex}");
            }
        }
    }
}