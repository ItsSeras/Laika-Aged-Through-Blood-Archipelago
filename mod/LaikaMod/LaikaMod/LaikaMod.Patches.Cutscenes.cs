using HarmonyLib;
using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public partial class LaikaMod
{
    // Story, scene, dungeon-door, and scripted interaction Harmony patches.
    // These keep AP progression from being bypassed by vanilla cutscenes or scripted doors.
    [HarmonyPatch(typeof(CreditsDirector), "Start")]
    public class CreditsDirector_Start_APSkipRageCreditsPatch
    {
        static bool Prefix(CreditsDirector __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                LaikaMod.LogInfo("AP CREDITS SKIP: skipping Where We Say Who / Rage and Sorrow credits scene.");

                Singleton<QuestLog>.Instance.TryCloseQuest("Q_D_0_Tutorial", true);

                LaikaMod.EnsureCoroutineRunner();

                if (LaikaMod.CoroutineRunner != null)
                {
                    LaikaMod.CoroutineRunner.StartCoroutine(
                        LaikaMod.SkipRageCreditsToCampCoroutine(__instance)
                    );
                }
                else
                {
                    __instance.LoadCampScene();
                }

                return false;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"CreditsDirector_Start_APSkipRageCreditsPatch exception:\n{ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "OnSceneLoaded")]
    public class PersistenceManager_OnSceneLoaded_APCutsceneSkipPatch
    {
        private static readonly HashSet<string> RecentlySkippedScenes = new HashSet<string>();

        static void Postfix(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                string sceneName = scene.name;

                if (!LaikaMod.IsAPCutsceneSkipEnabled())
                    return;

                if (string.IsNullOrEmpty(sceneName))
                    return;

                string targetScene = null;

                if (sceneName == "Dungeon_01_endSequence")
                {
                    // Post-Big Tree drive/cutscene scene.
                    // Logs confirm this is the loaded scene name after The Big Tree.
                    targetScene = "Camp_Night";
                }

                if (string.IsNullOrEmpty(targetScene))
                    return;

                string skipKey = sceneName + "->" + targetScene;

                if (RecentlySkippedScenes.Contains(skipKey))
                    return;

                RecentlySkippedScenes.Add(skipKey);

                LaikaMod.LogInfo(
                    $"AP CUTSCENE SKIP: detected scene {sceneName}; loading {targetScene} instead."
                );

                LaikaMod.EnsureCoroutineRunner();

                if (LaikaMod.CoroutineRunner != null)
                {
                    LaikaMod.CoroutineRunner.StartCoroutine(
                        LaikaMod.SkipSceneToTargetCoroutine(
                            sceneName,
                            targetScene,
                            "PersistenceManager_OnSceneLoaded_APCutsceneSkipPatch"
                        )
                    );
                }
                else
                {
                    MonoSingleton<SceneLoader>.Instance.LoadScene(targetScene, false, false);
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PersistenceManager_OnSceneLoaded_APCutsceneSkipPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PersistenceManager), "OnSceneLoaded")]
    public class PersistenceManager_OnSceneLoaded_APSceneNameLoggerPatch
    {
        static void Postfix(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                string sceneName = scene.name;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (string.IsNullOrEmpty(sceneName))
                    return;

                LaikaMod.LogInfo($"AP SCENE LOGGER: loaded sceneName={sceneName}");

                LaikaMod.TryUpdateUniversalTrackerRegionFromScene(
                    scene,
                    "PersistenceManager.OnSceneLoaded"
                );

                if (sceneName.IndexOf("Kidnap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sceneName.IndexOf("Puppy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sceneName.IndexOf("Child", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sceneName.IndexOf("Keep", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    LaikaMod.LogInfo(
                        $"AP SCENE LOGGER: possible Childless/Where They Keep Puppy scene detected -> {sceneName}"
                    );

                    LaikaMod.LogImportantQuestSnapshots(
                        "APSceneNameLogger/ChildlessCandidate/" + sceneName
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PersistenceManager_OnSceneLoaded_APSceneNameLoggerPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Q_D_2_Antennas), "OnAntennaDestroyed")]
    public class QD2Antennas_OnAntennaDestroyed_APLocationPatch
    {
        static void Postfix(string antennaID)
        {
            try
            {
                if (string.IsNullOrEmpty(antennaID))
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(antennaID, out definition))
                    return;

                LaikaMod.TrySendLocationCheck(
                    definition,
                    "QD2Antennas_OnAntennaDestroyed_APLocationPatch",
                    false
                );

                LaikaMod.LogInfo(
                    $"RADIO SILENCE ANTENNA CHECK: sent/confirmed AP check for {antennaID} -> {definition.DisplayName}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"QD2Antennas_OnAntennaDestroyed_APLocationPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PyramidCenterController), "OnPillarDestroyed")]
    public class PyramidCenterController_OnPillarDestroyed_APLocationPatch
    {
        static void Postfix()
        {
            try
            {
                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (MonoSingleton<ProgressionManager>.Instance == null ||
                    MonoSingleton<ProgressionManager>.Instance.ProgressionData == null)
                    return;

                string[] floorAchievementIds =
                {
                "N_D_03_CENTER_A_BROKEN",
                "N_D_03_CENTER_B_BROKEN",
                "N_D_03_CENTER_C_BROKEN",
                "N_D_03_CENTER_D_BROKEN"
            };

                foreach (string achievementId in floorAchievementIds)
                {
                    if (!MonoSingleton<ProgressionManager>.Instance.ProgressionData.GetAchievementCompleted(achievementId))
                        continue;

                    APLocationDefinition definition;
                    if (!LaikaMod.TryGetLocationDefinition(achievementId, out definition))
                        continue;

                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "PyramidCenterController_OnPillarDestroyed_APLocationPatch",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"BIG TREE FLOOR CHECK: sent/confirmed AP check for {achievementId} -> {definition.DisplayName}"
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PyramidCenterController_OnPillarDestroyed_APLocationPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(D1_BossDoor), "CanInteract")]
    public class D1BossDoor_APKeyGatePatch
    {
        static void Postfix(D1_BossDoor __instance, ref bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (LaikaMod.Dungeon01FinalDoorAlreadyOpened())
                    return;

                if (LaikaMod.PlayerHasAllDungeon01PitKeys(__instance))
                    return;

                __result = false;
                LaikaMod.LogInfo("D1BossDoor_APKeyGatePatch: blocked final pit door because the player does not have all 3 AP pit keys.");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"D1BossDoor_APKeyGatePatch exception:\n{ex}");
            }
        }
    }

    internal static float BoneheadHookCaveBlockNoticeLastShownAt = -9999f;

    internal static bool HasProgressionFlag(string achievementId)
    {
        try
        {
            if (string.IsNullOrEmpty(achievementId))
                return false;

            var progressionManager = MonoSingleton<ProgressionManager>.Instance;

            if (progressionManager == null || progressionManager.ProgressionData == null)
                return false;

            return progressionManager.ProgressionData.HasAchievement(achievementId);
        }
        catch (Exception ex)
        {
            LogWarning($"HasProgressionFlag({achievementId}) failed:\n{ex}");
            return false;
        }
    }

    internal static bool HasAPHookUnlocked()
    {
        try
        {
            if (HasProgressionFlag("G_HOOK_UNLOCKED"))
                return true;

            var inventory = Singleton<InventoryManager>.Instance;

            if (inventory != null && inventory.HasItem("I_E_HOOK", 1))
                return true;

            return false;
        }
        catch (Exception ex)
        {
            LogWarning($"HasAPHookUnlocked failed:\n{ex}");
            return false;
        }
    }

    internal static string GetDoorSceneToLoad(DoorInteraction door)
    {
        try
        {
            if (door == null || DoorInteractionSceneToLoadField == null)
                return "";

            return DoorInteractionSceneToLoadField.GetValue(door) as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    internal static bool IsBoneheadHookCaveDoor(DoorInteraction door)
    {
        if (door == null)
            return false;

        string sceneToLoad = GetDoorSceneToLoad(door);
        string doorId = door.ID ?? "";
        string objectName = door.gameObject != null ? door.gameObject.name ?? "" : "";

        return
            sceneToLoad.Contains("Tutorial_Hook") ||
            sceneToLoad.Contains("ZN_Tutorial_Hook") ||
            sceneToLoad.Contains("Where_Chaos_Plots") ||
            doorId.Contains("Tutorial_Hook") ||
            doorId.Contains("ZN_Tutorial_Hook") ||
            doorId.Contains("Where_Chaos_Plots") ||
            objectName.Contains("Tutorial_Hook") ||
            objectName.Contains("ZN_Tutorial_Hook") ||
            objectName.Contains("Where_Chaos_Plots");
    }

    internal static bool ShouldBlockBoneheadHookCaveDoor(DoorInteraction door, string sourceTag, bool announce)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return false;

            if (!IsBoneheadHookCaveDoor(door))
                return false;

            bool hasHook = HasAPHookUnlocked();

            if (hasHook)
                return false;

            if (announce)
            {
                LogInfo(
                    $"{sourceTag}: blocked Where Chaos Plots / Tutorial Hook cave entrance because AP Hook is not unlocked. " +
                    $"sceneToLoad={GetDoorSceneToLoad(door)}, doorId={door.ID}"
                );
            }

            if (announce)
            {
                float now = Time.unscaledTime;

                if (now - BoneheadHookCaveBlockNoticeLastShownAt >= 3.0f)
                {
                    BoneheadHookCaveBlockNoticeLastShownAt = now;

                    AnnounceAPWarning(
                        "[AP] Where Chaos Plots is blocked until Hook is unlocked. This prevents a vanilla hook softlock."
                    );
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: ShouldBlockBoneheadHookCaveDoor failed:\n{ex}");
            return false;
        }
    }

    [HarmonyPatch(typeof(Checkpoint), "CanInteract")]
    public class Checkpoint_CanInteract_APBoneheadHookCaveGatePatch
    {
        static void Postfix(Checkpoint __instance, ref bool __result)
        {
            try
            {
                if (!__result)
                    return;

                DoorInteraction door = __instance as DoorInteraction;

                if (door == null)
                    return;

                if (LaikaMod.ShouldBlockBoneheadHookCaveDoor(
                    door,
                    "Checkpoint_CanInteract_APBoneheadHookCaveGatePatch",
                    false
                ))
                {
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"Checkpoint_CanInteract_APBoneheadHookCaveGatePatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(DoorInteraction), "Interact")]
    public class DoorInteraction_Interact_APBoneheadHookCaveGatePatch
    {
        static bool Prefix(DoorInteraction __instance)
        {
            try
            {
                if (LaikaMod.ShouldBlockBoneheadHookCaveDoor(
                    __instance,
                    "DoorInteraction_Interact_APBoneheadHookCaveGatePatch",
                    true
                ))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"DoorInteraction_Interact_APBoneheadHookCaveGatePatch exception:\n{ex}");
                return true;
            }
        }
    }

    internal static bool Dungeon01FinalDoorAlreadyOpened()
    {
        try
        {
            var progressionManager = MonoSingleton<ProgressionManager>.Instance;

            if (progressionManager == null || progressionManager.ProgressionData == null)
                return false;

            return
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_0") &&
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_1") &&
                progressionManager.ProgressionData.GetAchievementCompleted("Q_D_2_Dungeon_01_door_pieces_2");
        }
        catch (Exception ex)
        {
            LogError($"Dungeon01FinalDoorAlreadyOpened exception:\n{ex}");
            return false;
        }
    }

    internal static bool PlayerHasAllDungeon01PitKeys(D1_BossDoor door)
    {
        try
        {
            if (door == null)
                return false;

            var inventory = MonoSingleton<PlayerManager>.Instance.PlayerInventory;

            if (inventory == null)
                return false;

            var field = typeof(D1_BossDoor).GetField(
                "itemDatasNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            if (field == null)
            {
                LogWarning("PlayerHasAllDungeon01PitKeys: could not find D1_BossDoor.itemDatasNeeded.");
                return false;
            }

            ItemData[] neededItems = field.GetValue(door) as ItemData[];

            if (neededItems == null || neededItems.Length == 0)
            {
                LogWarning("PlayerHasAllDungeon01PitKeys: itemDatasNeeded was null or empty.");
                return false;
            }

            foreach (ItemData item in neededItems)
            {
                if (item == null || !inventory.HasItem(item, 1))
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError($"PlayerHasAllDungeon01PitKeys exception:\n{ex}");
            return false;
        }
    }
}
