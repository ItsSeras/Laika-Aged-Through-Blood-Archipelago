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

[HarmonyPatch(typeof(PersistenceManager), "OnSceneLoaded")]
internal static class LaikaBoatTraceBootstrap
{
    static void Postfix(PersistenceManager __instance, Scene scene)
    {
        try
        {
            if (__instance == null) return;
            var trace = __instance.GetComponent<LaikaBoatTrace>();
            if (trace == null) trace = __instance.gameObject.AddComponent<LaikaBoatTrace>();
            trace.CheckForBoat();
            LaikaBoatTrace.Write("SCENE_LOADED " + scene.name);
        }
        catch (Exception ex) { LaikaMod.LogWarning("BOAT TRACE bootstrap: " + ex); }
    }
}

// Temporary diagnostics for Roy's outbound ride and the Tutorial_Hook freeze.
// Starts automatically and continues across scenes until title/session change,
// application exit, or the record limit. Adds no input bindings.
// Coroutine references indicate stored handles, not proof of active execution.
public sealed class LaikaBoatTrace : MonoBehaviour
{
    private static LaikaBoatTrace owner;
    private StreamWriter writer;
    private readonly Harmony harmony = new Harmony("com.seras.laikaap.boattrace");
    private readonly Dictionary<int, string> signatures = new Dictionary<int, string>();
    private readonly Dictionary<int, string> variables = new Dictionary<int, string>();
    private readonly Dictionary<int, string> states = new Dictionary<int, string>();
    private readonly Dictionary<string, bool> achievements = new Dictionary<string, bool>();
    private float nextScan;
    private float nextSample;
    // Shared across trace components and save reloads in this game process.
    private static int processLineCount;
    private static bool traceFileInitialized;
    private bool disabled;
    private string lastScene;
    private string lastQuest;
    private string identity;
    private const int MaxLines = 250000;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private void Awake()
    {
        LaikaMod.LogInfo(
            "BOAT/HOOK TRACE ARMED: automatic recording when an AP save " +
            "reaches Tutorial_Hook or a boat scene; no hotkeys."
        );
    }

    private static bool APEnabled
    {
        get { return LaikaMod.SessionState != null && LaikaMod.SessionState.APEnabled; }
    }

    public void CheckForBoat()
    {
        if (writer != null || disabled || !APEnabled ||
            processLineCount >= MaxLines)
        {
            return;
        }

        Scene hookScene = SceneManager.GetSceneByName("Tutorial_Hook");

        if (hookScene.IsValid() && hookScene.isLoaded)
        {
            Begin();
            return;
        }

        foreach (Boat boat in Resources.FindObjectsOfTypeAll<Boat>())
        {
            if (!boat.gameObject.scene.IsValid() ||
                !boat.gameObject.scene.isLoaded)
            {
                continue;
            }

            Begin();
            return;
        }
    }

    private void Begin()
    {
        if (owner != null && owner != this) return;
        try
        {
            string path = Path.Combine(
                BepInEx.Paths.BepInExRootPath,
                "LaikaBoatTrace.log"
            );

            // Preserve pre-reload evidence within this game process.
            // The first recording in a new process replaces the previous run.
            FileMode mode = traceFileInitialized
                ? FileMode.Append
                : FileMode.Create;

            writer = new StreamWriter(new FileStream(
                path,
                mode,
                FileAccess.Write,
                FileShare.Read
            ));

            traceFileInitialized = true;
            owner = this;
            identity = Convert.ToString(Read(LaikaMod.SessionState, "SessionIdentityKey"));
            Write(
                "BEGIN boat/hook trace v2; diagnostic hooks observe calls and state; " +
                "existing gameplay patches remain enabled"
            );
            Write("ASSEMBLIES game=" + typeof(Boat).Assembly.FullName +
                " playmaker=" + typeof(PlayMakerFSM).Assembly.FullName);
            InstallHooks();
            writer.Flush();
            LaikaMod.LogInfo("BOAT TRACE STARTED: " + path);
        }
        catch (Exception ex)
        {
            LaikaMod.LogWarning("BOAT TRACE could not start: " + ex);
            Close("start failure");
            disabled = true;
        }
    }

    private void Update()
    {
        try
        {
            string scene = SceneManager.GetActiveScene().name;
            var loader = MonoSingleton<SceneLoader>.Instance;
            if (!APEnabled || (loader != null && loader.CurrentSceneIsTitleScreen))
            {
                Close("AP disabled or title screen");
                return;
            }
            if (writer != null && processLineCount >= MaxLines)
            {
                Close("record limit reached");
                disabled = true;
                return;
            }
            if (writer != null && identity != Convert.ToString(Read(LaikaMod.SessionState, "SessionIdentityKey")))
                Close("session identity changed");
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;
            CheckForBoat();
            if (writer == null) return;
            if (scene != lastScene) { lastScene = scene; Write("ACTIVE_SCENE " + scene); }
            ScanFsms();
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 1f;
                SampleBoatAndQuest();
                SampleTutorialHook();
                if (writer != null) writer.Flush();
            }
        }
        catch (Exception ex) { Write("SAMPLER_ERROR " + ex); }
    }

    private void OnDestroy() { Close("trace component destroyed"); }
    private void OnApplicationQuit() { Close("application quit"); }

    private void Close(string reason)
    {
        if (writer == null) return;
        try
        {
            Write("END " + reason);
            writer.Flush();
            writer.Dispose();
        }
        catch (Exception ex) { LaikaMod.LogWarning("BOAT TRACE close: " + ex.Message); }
        finally
        {
            writer = null;
            if (owner == this) owner = null;
            try { harmony.UnpatchSelf(); } catch { }
            signatures.Clear(); variables.Clear(); states.Clear(); achievements.Clear();
            lastScene = null;
            lastQuest = null;
        }
        LaikaMod.LogInfo("BOAT TRACE STOPPED: " + reason);
    }

    internal static void Write(string text)
    {
        var trace = owner;
        if (trace == null || trace.writer == null)
            return;

        try
        {
            if (processLineCount >= MaxLines)
                return;

            processLineCount++;

            trace.writer.WriteLine(
                DateTime.UtcNow.ToString("O") +
                " frame=" + Time.frameCount + " " + text
            );

            if (processLineCount == MaxLines)
            {
                trace.writer.WriteLine(
                    "TRACE_LIMIT_REACHED: recording stopped for this game " +
                    "process. Send this file before restarting the game."
                );

                trace.writer.Flush();
                trace.disabled = true;

                LaikaMod.LogWarning(
                    "BOAT/HOOK TRACE reached its 250000-record limit. " +
                    "Recording is disabled until the game restarts."
                );
            }
        }
        catch
        {
            // Diagnostics must not throw into vanilla code.
        }
    }

    private void InstallHooks()
    {
        // Discover PlayMaker internals at runtime rather than requiring a particular
        // PlayMaker DLL revision. Coverage/missing hooks are written to the trace.
        Type fsmType = typeof(PlayMakerFSM).Assembly.GetType("HutongGames.PlayMaker.Fsm");
        Type stateType = typeof(PlayMakerFSM).Assembly.GetType("HutongGames.PlayMaker.FsmState");
        Hook(fsmType, "Event", false);
        Hook(fsmType, "SetState", false);
        Hook(stateType, "OnEnter", false);
        Hook(typeof(Playmaker.MovePlatformController), "OnEnter", false);
        Hook(typeof(Playmaker.MovePlatformController), "OnMovementFinished", false);
        Hook(typeof(DialogueFSMLauncher), "Interact", false);
        Hook(typeof(DialogueFSMLauncher), "EndFSM", false);
        Hook(typeof(ProgressionData), "SetAchievement", false);
        try
        {
            harmony.Patch(AccessTools.Method(typeof(ProgressionData), "SetAchievement",
                new[] { typeof(string), typeof(bool), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(LaikaBoatTrace).GetMethod("AfterAchievement",
                    BindingFlags.Static | BindingFlags.NonPublic)));
            Write("HOOK_OK ProgressionData.SetAchievement actual-value postfix");
        }
        catch (Exception ex) { Write("HOOK_FAILED achievement postfix " + ex.Message); }
        Hook(typeof(QuestLog), "TryCompleteQuestGoal", false);
        Hook(typeof(QuestLog), "TryCompleteQuestGoal", true);
        Hook(typeof(Boat), "SaveBoatPosition", false);
        Hook(typeof(MovingPlatformInteractable), "MoveTo", false);
        Hook(typeof(MovingPlatformInteractable), "MoveToInmediate", false);
        Hook(typeof(SceneLoader), "LoadScene", false);
        Hook(typeof(HookPowerUp), "ChargeShoot", false);
        Hook(typeof(HookPowerUp), "ReleaseHook", false);
        Hook(typeof(HookPowerUp), "RestoreHookState", false);
        Hook(typeof(PowerUp), "ActivatePowerUp", false);
        Hook(typeof(PhysicsRope), "CreateRope", false);
        Hook(typeof(PhysicsRope), "DestroyRope", false);
        Hook(typeof(PhysicsGrippable), "OnGrip", false);
        Hook(typeof(PhysicsGrippable), "OnEndGrip", false);
        Hook(typeof(Explosive), "Kill", false);
        Hook(typeof(PlayerWeaponController), "OnHook", false);

        Hook(
            typeof(PlayerControllersManager),
            "SetPlayerControllerBlockingFlag",
            false
        );

        Hook(
            typeof(PlayerControllersManager),
            "UnsetPlayerControllerBlockingFlag",
            false
        );
    }

    private void SampleTutorialHook()
    {
        if (SceneManager.GetActiveScene().name != "Tutorial_Hook")
            return;

        var quest = LaikaMod.FindActiveQuest("Q_D_S_TutorialHook");
        string questSnapshot = "not active";

        if (quest != null)
        {
            var currentGoal = quest.GetCurrentGoal();
            questSnapshot = "current=" +
                (currentGoal == null ? "null" : currentGoal.GoalId);

            if (quest.goals != null)
            {
                foreach (var goal in quest.goals)
                {
                    if (goal != null)
                        questSnapshot += " " + goal.GoalId + "=" + goal.Completed;
                }
            }
        }

        Write(
            "HOOK_QUEST " + questSnapshot +
            " debrisEventObserved=" +
            (LaikaMod.SessionState != null &&
             LaikaMod.SessionState.TutorialHookDebrisEventObserved)
        );

        var controllers = MonoSingleton<PlayerControllersManager>.Instance;

        Write(
            "HOOK_CONTROLS flags=" +
            Format(Read(controllers, "BlockingFlags"), 0) +
            " hookInput=" +
            Format(
                Read(
                    Read(controllers, "PlayerWeaponController"),
                    "Hook"
                ),
                0
            ) +
            " timeScale=" +
            Time.timeScale.ToString("R", CultureInfo.InvariantCulture)
        );

        foreach (HookPowerUp hook in
            UnityEngine.Object.FindObjectsOfType<HookPowerUp>())
        {
            object rope = Read(hook, "rope");

            Write(
                "HOOK_STATE object=" + Format(hook, 0) +
                " enabled=" + hook.enabled +
                " active=" + hook.gameObject.activeInHierarchy +
                " canUse=" + hook.CanUse +
                " inUse=" + Format(Read(hook, "inUse"), 0) +
                " activationCoroutinePresent=" +
                (Read(hook, "powerUpCoroutine") != null) +
                " chargeCoroutinePresent=" +
                (Read(hook, "chargeShootC") != null) +
                " ropeInUse=" + Format(Read(rope, "InUse"), 0) +
                " gripObject=" +
                Format(Read(rope, "currentGripObject"), 0)
            );
        }
    }

    private void Hook(Type type, string name, bool after)
    {
        int count = 0;
        if (type != null)
        {
            foreach (MethodInfo method in type.GetMethods(Flags | BindingFlags.DeclaredOnly))
            {
                if (method.Name != name || method.IsAbstract || method.ContainsGenericParameters) continue;
                if (after && method.ReturnType != typeof(bool)) continue;
                try
                {
                    var callback = new HarmonyMethod(typeof(LaikaBoatTrace).GetMethod(
                        after ? "AfterQuest" : "BeforeCall", BindingFlags.Static | BindingFlags.NonPublic));
                    if (after) harmony.Patch(method, postfix: callback);
                    else harmony.Patch(method, prefix: callback);
                    count++;
                    Write("HOOK_OK " + type.FullName + "." + method + (after ? " postfix" : " prefix"));
                }
                catch (Exception ex) { Write("HOOK_FAILED " + type.FullName + "." + name + " " + ex.Message); }
            }
        }
        if (count == 0) Write("HOOK_UNAVAILABLE " + (type == null ? "<missing type>" : type.FullName) + "." + name);
    }

    private static void BeforeCall(object __instance, object[] __args, MethodBase __originalMethod)
    {
        if (owner == null || !APEnabled) return;
        try
        {
            string args = "";
            ParameterInfo[] parameters = __originalMethod.GetParameters();
            for (int i = 0; i < __args.Length; i++) args += " " + parameters[i].Name + "=" + Format(__args[i], 0);
            object fsm = Read(__instance, "Fsm");
            Write("CALL " + __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name +
                " owner=" + Label(fsm ?? __instance) + " state=" + Format(Read(__instance, "Name"), 0) + args);
            if (__instance is Playmaker.MovePlatformController)
                Write("MOVE_ACTION " + Fields(__instance));
            var launcher = __instance as DialogueFSMLauncher;
            if (launcher != null)
            {
                Write("DIALOGUE launcher=" + Format(launcher, 0) + " target=" + Format(launcher.target, 0) +
                    " text=" + Format(launcher.textToLaunch, 0) + " template=" + Format(launcher.fsmTemplate, 0));
            }
            if (fsm != null) owner.SnapshotVariables(fsm);
        }
        catch (Exception ex) { Write("HOOK_READ_ERROR " + ex.Message); }
    }

    private static void AfterQuest(string questId, string goalId, bool __result)
    {
        if (owner == null || !APEnabled) return;
        Write("QUEST_RESULT quest=" + questId + " goal=" + goalId + " result=" + __result);
    }

    private static void AfterAchievement(ProgressionData __instance, string name, bool value)
    {
        if (owner == null || !APEnabled) return;
        try
        {
            Write("FLAG_RESULT name=" + name + " requested=" + value +
                " actual=" + __instance.GetAchievementCompleted(name));
        }
        catch (Exception ex) { Write("FLAG_READ_ERROR " + ex.Message); }
    }

    private void ScanFsms()
    {
        foreach (PlayMakerFSM component in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
        {
            if (!component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded) continue;
            try
            {
                object fsm = component.Fsm;
                if (fsm == null) continue;
                object graph = Read(fsm, "States");
                int key = component.GetInstanceID();
                string signature = RuntimeHelpers.GetHashCode(fsm) + ":" +
                    (graph == null ? 0 : RuntimeHelpers.GetHashCode(graph));
                string old;
                if (!signatures.TryGetValue(key, out old) || old != signature)
                {
                    signatures[key] = signature;
                    Write("FSM_GRAPH " + Label(fsm) + " component=" + Format(component, 0));
                    var globals = Read(fsm, "GlobalTransitions") as IEnumerable;
                    if (globals != null) foreach (object transition in globals)
                        Write("GLOBAL_TRANSITION event=" + Format(Read(transition, "EventName"), 0) +
                            " to=" + Format(Read(transition, "ToState"), 0));
                    var allStates = graph as IEnumerable;
                    if (allStates != null) foreach (object state in allStates)
                    {
                        Write("STATE_DEF fsm=" + Label(fsm) + " name=" + Format(Read(state, "Name"), 0));
                        var transitions = Read(state, "Transitions") as IEnumerable;
                        if (transitions != null) foreach (object transition in transitions)
                            Write("TRANSITION event=" + Format(Read(transition, "EventName"), 0) +
                                " fsmEvent=" + Format(Read(transition, "FsmEvent"), 0) +
                                " to=" + Format(Read(transition, "ToState"), 0));
                        var actions = Read(state, "Actions") as IEnumerable;
                        if (actions != null) foreach (object action in actions)
                            Write("ACTION " + (action == null ? "null" : action.GetType().FullName + " " + Fields(action)));
                    }
                }
                string current = Format(Read(fsm, "ActiveStateName"), 0) +
                    " activeState=" + Format(Read(Read(fsm, "ActiveState"), "Name"), 0) +
                    " enabled=" + component.enabled + " active=" + component.gameObject.activeInHierarchy;
                if (!states.TryGetValue(key, out old) || old != current)
                {
                    states[key] = current;
                    Write("FSM_OBSERVED " + Label(fsm) + " " + current);
                }
                SnapshotVariables(fsm);
            }
            catch (Exception ex) { Write("FSM_SCAN_ERROR " + Format(component, 0) + " " + ex.Message); }
        }
    }

    private void SnapshotVariables(object fsm)
    {
        object vars = Read(fsm, "Variables");
        if (vars == null) vars = Read(fsm, "FsmVariables");
        if (vars == null) return;
        string snapshot = "";
        foreach (string name in new[] { "FloatVariables", "IntVariables", "BoolVariables", "StringVariables",
            "Vector2Variables", "Vector3Variables", "GameObjectVariables", "ObjectVariables", "ArrayVariables" })
        {
            var collection = Read(vars, name) as IEnumerable;
            if (collection == null) continue;
            foreach (object variable in collection) snapshot += " " + Format(variable, 0);
        }
        int key = RuntimeHelpers.GetHashCode(fsm);
        string old;
        if (!variables.TryGetValue(key, out old) || old != snapshot)
        {
            variables[key] = snapshot;
            Write("VARIABLES " + Label(fsm) + snapshot);
        }
    }

    private void SampleBoatAndQuest()
    {
        var progression = MonoSingleton<ProgressionManager>.Instance;
        if (progression != null && progression.ProgressionData != null)
        {
            foreach (var pair in progression.ProgressionData.achievements)
            {
                if (pair == null || pair.achName == null) continue;
                bool old;
                if (!achievements.TryGetValue(pair.achName, out old) || old != pair.completed)
                {
                    achievements[pair.achName] = pair.completed;
                    Write("FLAG_OBSERVED name=" + pair.achName + " actual=" + pair.completed);
                }
            }
        }
        if (Boat.Instance != null)
        {
            var boat = Boat.Instance;
            Write("BOAT position=" + Format(boat.transform, 0) +
                " platform=" + Format(boat.Platform, 0) +
                " alpha=" + (boat.Platform == null ? "null" : boat.Platform.Alpha.ToString("R", CultureInfo.InvariantCulture)) +
                " target=" + (boat.Platform == null ? "null" : Format(boat.Platform.Target, 0)));
        }
        var player = MonoSingleton<PlayerManager>.Instance;
        if (player != null && player.CurrentBike != null)
            Write("BIKE " + Format(player.CurrentBike.transform, 0) + " parent=" + Format(player.CurrentBike.transform.parent, 0));
        var quest = LaikaMod.FindActiveQuest("Q_D_2_Lighthouse");
        string snapshot = quest == null ? "not active" : "current=" +
            (quest.GetCurrentGoal() == null ? "null" : quest.GetCurrentGoal().GoalId);
        if (quest != null && quest.goals != null)
            foreach (var goal in quest.goals)
                if (goal != null) snapshot += " " + goal.GoalId + "=" + goal.Completed;
        if (snapshot != lastQuest) { lastQuest = snapshot; Write("RADIO_QUEST " + snapshot); }
    }

    private static object Read(object value, string name)
    {
        if (value == null) return null;
        try
        {
            var field = value.GetType().GetField(name, Flags);
            if (field != null) return field.GetValue(value);
            var property = value.GetType().GetProperty(name, Flags);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(value, null);
        }
        catch { }
        return null;
    }

    private static string Label(object fsm)
    {
        if (fsm == null) return "null";
        return "#" + RuntimeHelpers.GetHashCode(fsm) + " name=" + Format(Read(fsm, "Name"), 0) +
            " object=" + Format(Read(fsm, "GameObject"), 0);
    }

    private static string Fields(object value)
    {
        string result = "";
        foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try { result += field.Name + "=" + Format(field.GetValue(value), 0) + "; "; }
            catch { result += field.Name + "=<unreadable>; "; }
        }
        return result;
    }

    private static string Format(object value, int depth)
    {
        if (value == null) return "null";
        if (depth > 3) return "<depth-limit:" + value.GetType().Name + ">";
        if (value is string) return "\"" + ((string)value).Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        var unity = value as UnityEngine.Object;
        if (!ReferenceEquals(unity, null))
        {
            if (unity == null) return "<destroyed>";
            var component = unity as Component;
            var go = unity as GameObject;
            if (component != null) go = component.gameObject;
            if (go != null)
            {
                string path = go.name;
                for (Transform t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                return unity.GetType().Name + "#" + unity.GetInstanceID() + " " + go.scene.name + ":" + path +
                    " pos=" + go.transform.position.ToString("F3");
            }
            return unity.GetType().Name + "#" + unity.GetInstanceID() + ":" + unity.name;
        }
        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is Delegate) return "<callback>";
        var sequence = value as IEnumerable;
        if (sequence != null)
        {
            string output = "["; int count = 0;
            foreach (object entry in sequence)
            {
                if (count++ >= 128) { output += "<array-limit>"; break; }
                output += Format(entry, depth + 1) + ",";
            }
            return output + "]";
        }
        if (type.Namespace != null && type.Namespace.StartsWith("HutongGames.PlayMaker", StringComparison.Ordinal))
        {
            string output = type.Name + "{name=" + Format(Read(value, "Name"), depth + 1);
            var prop = type.GetProperty("Value", Flags);
            if (prop != null && prop.GetIndexParameters().Length == 0)
            {
                try { output += ",value=" + Format(prop.GetValue(value, null), depth + 1); } catch { }
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                try { output += "," + field.Name + "=" + Format(field.GetValue(value), depth + 1); } catch { }
            return output + "}";
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
