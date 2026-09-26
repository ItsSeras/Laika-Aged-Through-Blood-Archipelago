using HarmonyLib;
using System;

public partial class LaikaMod
{
    // Player death Harmony patches.
    // These detect local deaths and forward them into DeathLink / Death Amnesty handling.
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

    // Both vanilla Kill overloads create this coroutine.
    // Change only its sack argument; preserve the original death sequence,
    // death detection, outgoing DeathLink and amnesty behavior.
    [HarmonyPatch(
        typeof(global::RiderHead),
        "KillC",
        new Type[] { typeof(bool) })]
    public class PlayerDeath_VisceraProtectionPatch
    {
        static void Prefix(ref bool moneySack)
        {
            if (LaikaMod.SessionState == null ||
                !LaikaMod.SessionState.APEnabled)
            {
                return;
            }

            if (LaikaMod.WorldOptions.VisceraProtection ==
                VisceraProtectionMode.AllDeaths)
            {
                moneySack = false;
            }
        }
    }
}