using HarmonyLib;
using Laika.Persistence;
using System;
using UnityEngine.SceneManagement;

public partial class LaikaMod
{
    // Scene-load reconciliation Harmony patches.
    // These retry AP item/state reconciliation after Laika managers become available.
    [HarmonyPatch(typeof(PersistenceManager), "OnSceneLoaded")]
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