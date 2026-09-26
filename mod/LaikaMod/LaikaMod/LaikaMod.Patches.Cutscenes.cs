using HarmonyLib;
using HutongGames.PlayMaker;
using Laika.Inventory;
using Laika.Persistence;
using Laika.Quests;
using System;
using System.Collections;
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

                if (!LaikaMod.WorldOptions.SkipJakobTransition)
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
        static void Postfix(
            UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!LaikaMod.IsAPCutsceneSkipEnabled() ||
                !LaikaMod.WorldOptions.SkipOrellaTransition ||
                scene.name != "AfterBoss")
            {
                return;
            }

            try
            {
                LaikaMod.EnsureCoroutineRunner();

                if (LaikaMod.CoroutineRunner == null)
                {
                    LaikaMod.LogWarning(
                        "AP ORELLA SKIP: no coroutine runner; " +
                        "leaving the vanilla sequence active."
                    );
                    return;
                }

                LaikaMod.CoroutineRunner.StartCoroutine(
                    SkipOrellaRoad(scene)
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"AP ORELLA SKIP: could not schedule skip:\n{ex}"
                );
            }
        }

        private static System.Collections.IEnumerator SkipOrellaRoad(
            UnityEngine.SceneManagement.Scene scene)
        {
            // PersistenceManager schedules player respawn after two frames.
            yield return null;
            yield return null;
            yield return null;

            try
            {
                if (!LaikaMod.IsAPCutsceneSkipEnabled() ||
                    !LaikaMod.WorldOptions.SkipOrellaTransition ||
                    !scene.IsValid() ||
                    !scene.isLoaded ||
                    UnityEngine.SceneManagement.SceneManager
                        .GetActiveScene().handle != scene.handle)
                {
                    yield break;
                }

                var questLog = Singleton<QuestLog>.Instance;
                var persistence = MonoSingleton<PersistenceManager>.Instance;
                var progression = MonoSingleton<ProgressionManager>.Instance;
                var sceneLoader = MonoSingleton<SceneLoader>.Instance;

                if (questLog == null ||
                    persistence == null ||
                    progression == null ||
                    progression.LocalSaveData == null ||
                    sceneLoader == null)
                {
                    LaikaMod.LogWarning(
                        "AP ORELLA SKIP: managers are not ready; " +
                        "leaving the vanilla sequence active."
                    );
                    yield break;
                }

                // Restrict this to the demonstrated post-Pope transition.
                if (!questLog.IsQuestGoalComplete(
                    "Q_D_3_TheBigTree", "KillPope"))
                {
                    yield break;
                }

                if (!persistence.CanSave)
                {
                    LaikaMod.LogWarning(
                        "AP ORELLA SKIP: saving is disabled; " +
                        "leaving the vanilla sequence active."
                    );
                    yield break;
                }

                AfterBossDirector director = null;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (AfterBossDirector candidate in
                        root.GetComponentsInChildren<AfterBossDirector>(false))
                    {
                        if (director != null)
                        {
                            LaikaMod.LogWarning(
                                "AP ORELLA SKIP: multiple directors found; " +
                                "leaving the vanilla sequence active."
                            );
                            yield break;
                        }

                        director = candidate;
                    }
                }

                if (director == null)
                {
                    LaikaMod.LogWarning(
                        "AP ORELLA SKIP: AfterBossDirector was not found; " +
                        "leaving the vanilla sequence active."
                    );
                    yield break;
                }

                LaikaMod.LogInfo(
                    "AP ORELLA SKIP: ending AfterBoss through " +
                    "vanilla AfterBossDirector.LoadCampScene()."
                );

                director.LoadCampScene();
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning(
                    $"AP ORELLA SKIP failed:\n{ex}"
                );
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

    [HarmonyPatch(
        typeof(ProgressionData),
        "SetAchievement",
        new Type[] { typeof(string), typeof(bool), typeof(bool) }
    )]
    public class ProgressionData_BlockEarlyHookUnlockPatch
    {
        static bool Prefix(string name, bool value, bool reset)
        {
            if (name != "G_HOOK_UNLOCKED" || !value || reset)
                return true;

            if (LaikaMod.SessionState == null ||
                !LaikaMod.SessionState.APEnabled)
            {
                return true;
            }

            // AP itself is currently granting Hook.
            if (LaikaMod.IsGrantingAPItem)
                return true;

            // AP has already legitimately sent Hook.
            if (LaikaMod.HasReceivedAPItem(
                ItemKind.KeyItem,
                "I_E_HOOK"
            ))
            {
                return true;
            }

            LaikaMod.LogWarning(
                "Blocked vanilla G_HOOK_UNLOCKED because Hook has not been received from Archipelago."
            );

            return false;
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
            if (SessionState != null && SessionState.APEnabled)
            {
                return HasReceivedAPItem(
                    ItemKind.KeyItem,
                    "I_E_HOOK"
                );
            }

            return HasProgressionFlag("G_HOOK_UNLOCKED");
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
            // This softlock protection is independent of cinematic settings.
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

public partial class LaikaMod
{
    private const string RoyOutboundFsmPath =
        "Dungeon_02/Q_D_2_Lighthouse_Dungeon/D_2_Lighthouse_BoatSequence/FSM";
    private const string ExteriorHectistPath =
        "Wasteland_01_04/Wasteland_01_04_Quests/Hectic/Interactions/Q_D_S_HookTutorial";

    private static string Release015ObjectPath(Transform transform)
    {
        if (transform == null) return "";
        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    private static bool IsPrematureInteriorHectist(
        DialogueFSMLauncher launcher)
    {
        if (SessionState == null ||
            !SessionState.APEnabled ||
            launcher == null ||
            launcher.gameObject.scene.name != "Tutorial_Hook")
        {
            return false;
        }

        // Exact final interior interaction observed in the runtime trace.
        const string finalHectistPath =
            "Tutorial Hook Level/Tutorial_Hook_Quests/Anarchist/" +
            "Interactions/Q_D_S_HookTutorial";

        if (Release015ObjectPath(launcher.transform) != finalHectistPath)
            return false;

        var quest = FindActiveQuest("Q_D_S_TutorialHook");
        var goal = quest == null ? null : quest.GetCurrentGoal();

        if (goal == null)
            return false;

        // Block only known earlier quest stages.
        // The intended final conversation and statue step remain available.
        switch (goal.GoalId)
        {
            case "GetHook":
            case "TalkToAnarchist1":
            case "GetMaterials":
            case "BombBlock":
            case "ExplodeDebris":
                return true;

            default:
                return false;
        }
    }

    [HarmonyPatch(typeof(DialogueFSMLauncher), "Interact")]
    public static class InteriorHectist_EarlyInteractionGuard
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(DialogueFSMLauncher __instance)
        {
            if (!IsPrematureInteriorHectist(__instance))
                return true;

            LogInfo(
                "HECTIST INTERIOR GUARD: blocked final conversation " +
                "before the preceding hook quest steps were completed."
            );
            return false;
        }
    }

    [HarmonyPatch(typeof(ProgressionInteractable), "CanInteract")]
    public static class InteriorHectist_EarlyPromptGuard
    {
        static void Postfix(
            ProgressionInteractable __instance,
            ref bool __result)
        {
            if (__result &&
                IsPrematureInteriorHectist(
                    __instance as DialogueFSMLauncher))
            {
                __result = false;
            }
        }
    }

    // The log proves this exterior launcher tries TalkToAnarchist2 while
    // GetMaterials is current. Prevent entry before Interactable blocks controls.
    private static bool IsStaleExteriorHectist(
        DialogueFSMLauncher launcher)
    {
        if (SessionState == null ||
            !SessionState.APEnabled ||
            launcher == null ||
            launcher.gameObject.scene.name != "Wasteland_01_04" ||
            launcher.target == null ||
            launcher.target.gameObject.name != "FSM" ||
            Release015ObjectPath(launcher.transform) != ExteriorHectistPath ||
            !HasAPHookUnlocked())
        {
            return false;
        }

        var quest = FindActiveQuest("Q_D_S_TutorialHook");
        var goal = quest == null ? null : quest.GetCurrentGoal();

        if (goal == null)
            return false;

        // With AP Hook already unlocked, this stale exterior launcher can
        // enter the final conversation before its quest goal is available.
        switch (goal.GoalId)
        {
            case "GetHook":
            case "TalkToAnarchist1":
            case "GetMaterials":
            case "BombBlock":
            case "ExplodeDebris":
                return true;

            default:
                return false;
        }
    }

    [HarmonyPatch(typeof(DialogueFSMLauncher), "Interact")]
    public static class ExteriorHectist_InteractionGuard
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(DialogueFSMLauncher __instance)
        {
            LogInfo(
                "HECTIST EXTERIOR GUARD: blocked premature exterior " +
                "interaction while AP Hook is unlocked."
            );
        }
    }

    // DialogueFSMLauncher inherits CanInteract from ProgressionInteractable.
    // This suppresses only the invalid exterior interaction prompt.
    [HarmonyPatch(typeof(ProgressionInteractable), "CanInteract")]
    public static class ExteriorHectist_PromptGuard
    {
        static void Postfix(ProgressionInteractable __instance, ref bool __result)
        {
            if (__result && IsStaleExteriorHectist(__instance as DialogueFSMLauncher))
                __result = false;
        }
    }

    private static readonly FieldInfo RoyDialogueQueueField =
        AccessTools.Field(typeof(PlayMaker.ExecuteILDialogue), "actions");

    private static bool IsRoyOutboundAction(FsmStateAction action)
    {
        if (SessionState == null ||
            !SessionState.APEnabled ||
            !WorldOptions.SkipRoyBoat ||
            action == null ||
            action.Fsm == null ||
            RoyDialogueQueueField == null)
        {
            return false;
        }

        GameObject owner = action.Fsm.GameObject;

        return owner != null &&
            owner.scene.name == "Dungeon_02" &&
            Release015ObjectPath(owner.transform) == RoyOutboundFsmPath;
    }

    // Use each recorded movement action's built-in immediate mode. Preserve its
    // target, the FSM transitions, checkpoint switches and all cleanup actions.
    [HarmonyPatch(typeof(Playmaker.MovePlatformController), "OnEnter")]
    public static class RoyOutbound_ImmediateMovement
    {
        static void Prefix(Playmaker.MovePlatformController __instance, out bool? __state)
        {
            __state = null;
            if (!IsRoyOutboundAction(__instance) || __instance.platform == null ||
                __instance.target == null || __instance.OnlySetUp || __instance.Inmediate)
                return;

            string targetName = __instance.target.name;
            if (__instance.target.parent != __instance.Fsm.GameObject.transform ||
                (targetName != "Target 1" && targetName != "Target 2" &&
                 targetName != "Target 3" && targetName != "Target 4")) return;

            __state = __instance.Inmediate;
            __instance.Inmediate = true;
            LogInfo("ROY OUTBOUND SKIP: immediate movement to " + targetName);
        }

        static void Finalizer(Playmaker.MovePlatformController __instance, bool? __state)
        {
            if (__state.HasValue) __instance.Inmediate = __state.Value;
        }
    }

    // Several dialogue actions have 10-120 second delayed starts. Remove their
    // start delay only in this FSM, and restore the serialized field afterward.
    [HarmonyPatch(typeof(PlayMaker.ExecuteILDialogue), "OnEnter")]
    public static class RoyOutbound_NoDialogueStartDelay
    {
        static void Prefix(PlayMaker.ExecuteILDialogue __instance, out FsmFloat __state)
        {
            __state = null;
            if (!IsRoyOutboundAction(__instance) || __instance.m_DelayedDialogueStart == null)
                return;
            __state = __instance.m_DelayedDialogueStart;
            __instance.m_DelayedDialogueStart = 0f;
        }

        static void Finalizer(PlayMaker.ExecuteILDialogue __instance, FsmFloat __state)
        {
            if (__state != null) __instance.m_DelayedDialogueStart = __state;
        }
    }

    // When this dialogue action generates its command queue, remove display,
    // wait and timer commands while retaining the other parsed commands.
    // Vanilla OnUpdate/OnExit handles execution and cleanup.
    // This filter does not guarantee that every dialogue action in a movement
    // state runs before immediate movement advances the FSM.
    [HarmonyPatch(typeof(PlayMaker.ExecuteILDialogue), "GenerateDialogueFromIL")]
    public static class RoyOutbound_TrimDialoguePresentation
    {
        static void Postfix(PlayMaker.ExecuteILDialogue __instance)
        {
            if (!IsRoyOutboundAction(__instance)) return;
            try
            {
                object original = RoyDialogueQueueField.GetValue(__instance);
                var commands = original as IEnumerable;
                if (commands == null) return;
                Type queueType = original.GetType();
                MethodInfo enqueue = queueType.GetMethod("Enqueue");
                if (enqueue == null) return;
                object replacement = Activator.CreateInstance(queueType);
                int removed = 0;
                int retained = 0;
                foreach (object command in commands)
                {
                    if (command == null) continue;
                    Type type = command.GetType();
                    bool presentation = type.DeclaringType == typeof(PlayMaker.ExecuteILDialogue) &&
                        (type.Name == "DisplayDialogueCommand" || type.Name == "WaitCommand" ||
                         type.Name == "TimerCommand");
                    if (presentation) { removed++; continue; }
                    enqueue.Invoke(replacement, new[] { command });
                    retained++;
                }
                // Commit only after constructing the entire replacement queue.
                RoyDialogueQueueField.SetValue(__instance, replacement);
                var asset = __instance.m_ilFile == null ? null : __instance.m_ilFile.Value as TextAsset;
                LogInfo("ROY OUTBOUND SKIP: dialogue=" + (asset == null ? "<null>" : asset.name) +
                    ", removed presentation commands=" + removed + ", retained commands=" + retained);
            }
            catch (Exception ex)
            {
                LogWarning("ROY OUTBOUND SKIP: dialogue filtering failed; original queue retained.\n" + ex);
            }
        }
    }
}
