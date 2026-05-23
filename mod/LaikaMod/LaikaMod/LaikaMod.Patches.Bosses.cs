using HarmonyLib;
using Laika.Persistence;
using System;


public partial class LaikaMod
{
    // Boss Harmony patches.
    // These send boss-defeat AP checks and prevent completed boss encounters from restarting incorrectly.
    // Tracks boss clears through progression achievement keys.
    // Boss kill completion is persisted by the game as achievement-style flags.
    [HarmonyPatch(typeof(ProgressionData), "SetAchievement", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
    public class BossAchievementPatch
    {
        static void Prefix(string name, bool value, bool reset)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (!value || reset)
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

            if (LaikaMod.IsGrantingAPItem)
            {
                LaikaMod.LogInfo($"BOSS CLEAR IGNORED during AP item grant: name={name}, value={value}, reset={reset}");
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

    [HarmonyPatch(typeof(Ending_Sequence), "DestroyBomb")]
    public class EndingSequence_DestroyBomb_APGoalPatch
    {
        static void Prefix()
        {
            LaikaMod.LogInfo("GOAL VERIFY: Ending_Sequence.DestroyBomb fired. Reporting AP goal completion.");
            LaikaMod.ReportLaikaGoalComplete("Ending_Sequence.DestroyBomb");
        }
    }

    [HarmonyPatch(typeof(Boss_01_Manager), "Restart")]
    public class Boss01Manager_Restart_APPreventRestartAfterDefeatPatch
    {
        static bool Prefix(Boss_01_Manager __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (MonoSingleton<ProgressionManager>.Instance == null ||
                    MonoSingleton<ProgressionManager>.Instance.ProgressionData == null)
                    return true;

                if (MonoSingleton<ProgressionManager>.Instance.ProgressionData.GetAchievementCompleted("B_BOSS_01_DEFEATED"))
                {
                    LaikaMod.LogInfo(
                        "BOSS_01: blocked Restart because Caterpillar is already defeated. " +
                        "This prevents post-kill transition/restart looping."
                    );

                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"Boss01Manager_Restart_APPreventRestartAfterDefeatPatch exception:\n{ex}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Boss_01_Launcher), "Interact")]
    public class Boss01Launcher_Interact_APPreventRelaunchAfterDefeatPatch
    {
        static bool Prefix()
        {
            try
            {
                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return true;

                if (MonoSingleton<ProgressionManager>.Instance == null ||
                    MonoSingleton<ProgressionManager>.Instance.ProgressionData == null)
                    return true;

                if (MonoSingleton<ProgressionManager>.Instance.ProgressionData.GetAchievementCompleted("B_BOSS_01_DEFEATED"))
                {
                    LaikaMod.LogInfo("BOSS_01: blocked launcher interaction because Caterpillar is already defeated.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"Boss01Launcher_Interact_APPreventRelaunchAfterDefeatPatch exception:\n{ex}");
                return true;
            }
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
}