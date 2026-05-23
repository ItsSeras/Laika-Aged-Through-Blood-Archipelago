using System;

public partial class LaikaMod
{
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

                AnnounceAPDeathLink("[AP] Local death detected.");

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

    // Evaluate whether this local death should send a DeathLink now.
    // Death Amnesty delays the send until the configured death threshold is reached.
    internal static void EvaluateDeathLinkAfterLocalDeath(string sourceTag)
    {
        bool effectiveDeathLinkEnabled =
            WorldOptions.DeathLinkEnabled ||
            (SessionState != null &&
             SessionState.Options != null &&
             SessionState.Options.DeathLinkEnabled);

        if (!effectiveDeathLinkEnabled)
        {
            AnnounceAPDeathLink("[AP] Local death detected. DeathLink is disabled.");
            LogInfo($"{sourceTag}: DeathLink disabled. No outbound DeathLink would be sent.");
            return;
        }

        bool effectiveDeathAmnestyEnabled =
            WorldOptions.DeathAmnestyEnabled ||
            (SessionState != null &&
             SessionState.Options != null &&
             SessionState.Options.DeathAmnestyEnabled);

        if (!effectiveDeathAmnestyEnabled)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty disabled).");

            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            return;
        }

        int effectiveDeathAmnestyCount =
            SessionState != null && SessionState.Options != null
                ? SessionState.Options.DeathAmnestyCount
                : WorldOptions.DeathAmnestyCount;

        int requiredDeaths = Math.Max(1, effectiveDeathAmnestyCount);

        LogInfo($"{sourceTag}: Death Amnesty Progress = {DeathsSinceLastDeathLink} / {requiredDeaths}");

        AnnounceAPDeathLink(
            $"[AP] Your suffering inches closer to your friends... ({DeathsSinceLastDeathLink}/{requiredDeaths})"
        );

        if (DeathsSinceLastDeathLink >= requiredDeaths)
        {
            LogInfo($"{sourceTag}: DEATHLINK SEND NOW (death amnesty threshold reached).");

            if (ArchipelagoClientManager.Instance != null)
            {
                string deathCause = $"{SessionState.Connection.SlotName ?? "Laika"} couldn't survive in the Wasteland. (Skill issue)";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after real send.");
        }
    }
}
