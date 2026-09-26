using System;
using System.Collections.Generic;
using UnityEngine;

public partial class LaikaMod
{
    // ===== DeathLink helpers =====
    //
    // Incoming DeathLinks can arrive through AP's networking callback.
    // Queue them here so the persistent AP coroutine runner can trigger
    // the actual player death safely on Unity's main thread.
    internal sealed class IncomingDeathLinkRequest
    {
        internal string Source;
        internal string Cause;

        // Prevent the delayed-death notice from spamming every frame while
        // Laika is talking, walking, transitioning, using UI, etc.
        internal bool DelayNoticeShown;

        // Used only for useful debug logging if the reason changes while
        // the DeathLink remains queued.
        internal string LastDelayReason;
    }

    private static readonly object IncomingDeathLinkQueueLock =
        new object();

    private static readonly Queue<IncomingDeathLinkRequest> IncomingDeathLinkQueue =
        new Queue<IncomingDeathLinkRequest>();

    internal static void QueueIncomingDeathLink(
        string source,
        string cause)
    {
        IncomingDeathLinkRequest request =
            new IncomingDeathLinkRequest
            {
                Source = string.IsNullOrWhiteSpace(source)
                    ? "<unknown>"
                    : source,

                Cause = string.IsNullOrWhiteSpace(cause)
                    ? "DeathLink"
                    : cause
            };

        lock (IncomingDeathLinkQueueLock)
        {
            IncomingDeathLinkQueue.Enqueue(request);
        }

        LogInfo(
            $"AP DEATHLINK: queued incoming death. " +
            $"Source={request.Source}, Cause={request.Cause}"
        );
    }

    private static bool TryGetSafeIncomingDeathLinkTarget(
        out RiderHead rider,
        out string delayReason)
    {
        rider = null;
        delayReason = "";

        // The persistent AP runner exists across every scene, including title/loading.
        // Never attempt to start Laika's death coroutine outside an active gameplay scene.
        UnityEngine.SceneManagement.Scene activeScene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            delayReason = "the active scene is not ready";
            return false;
        }

        string sceneName = activeScene.name ?? "";

        if (string.Equals(
                sceneName,
                "TitleScreen",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                sceneName,
                "LoadingScreen",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                sceneName,
                "Autosave",
                StringComparison.OrdinalIgnoreCase))
        {
            delayReason =
                $"Laika is not currently in active gameplay ({sceneName})";

            return false;
        }

        UITransitionsManager transitions =
            MonoSingleton<UITransitionsManager>.Instance;

        if (transitions != null && transitions.Fading)
        {
            delayReason = "a screen transition is currently fading";
            return false;
        }

        PlayerManager playerManager =
            MonoSingleton<PlayerManager>.Instance;

        if (playerManager == null)
        {
            delayReason = "PlayerManager is not available";
            return false;
        }

        PlayerControllersManager controllers =
            MonoSingleton<PlayerControllersManager>.Instance;

        if (controllers == null)
        {
            delayReason = "PlayerControllersManager is not available";
            return false;
        }

        if (!playerManager.PlayerAlive())
        {
            delayReason = "Laika is already dead";
            return false;
        }

        // Use the game's own definition of ordinary bike gameplay.
        //
        // Vanilla BikeUnblocked is true only for:
        //   NONE
        //   NO_WEAPON
        //
        // This automatically delays DeathLink during dialogue, interactions,
        // tutorial popups, UI, transitions, indoor/on-foot gameplay, and death.
        if (!controllers.BikeUnblocked)
        {
            delayReason =
                $"player controls are blocked ({controllers.BlockingFlags})";

            return false;
        }

        // Laika can retain a CurrentBike while physically walking around.
        // CurrentRider tells us that the actual controllable Laika is currently
        // the on-foot character rather than the bike rider.
        if (playerManager.CurrentRider != null)
        {
            delayReason = "Laika is currently on foot";
            return false;
        }

        BikeManager bike =
            playerManager.CurrentBike;

        if (bike == null)
        {
            delayReason = "the active bike is not available";
            return false;
        }

        if (!bike.gameObject.activeInHierarchy)
        {
            delayReason = "the active bike is not currently active";
            return false;
        }

        RiderHead activeRiderHead =
            bike.RiderHead;

        if (activeRiderHead == null)
        {
            delayReason = "the bike RiderHead is not available";
            return false;
        }

        if (!activeRiderHead.gameObject.activeInHierarchy)
        {
            delayReason = "the bike RiderHead is not currently active";
            return false;
        }

        // Vanilla RiderHead.KillC() accesses CurrentWeaponArm immediately.
        // Do not start the death sequence while a scene/mount transition is
        // still rebuilding that reference.
        if (playerManager.CurrentWeaponArm == null)
        {
            delayReason = "the weapon arm is not ready";
            return false;
        }

        rider = activeRiderHead;
        return true;
    }

    internal static void ProcessPendingIncomingDeathLink()
    {
        try
        {
            IncomingDeathLinkRequest pendingRequest = null;

            // Look at the oldest DeathLink without removing it yet.
            //
            // A remote death must remain owed until we successfully reach
            // a vanilla-safe gameplay state and actually trigger it.
            lock (IncomingDeathLinkQueueLock)
            {
                if (IncomingDeathLinkQueue.Count == 0)
                    return;

                pendingRequest =
                    IncomingDeathLinkQueue.Peek();
            }

            if (pendingRequest == null)
                return;

            RiderHead rider;
            string delayReason;

            if (!TryGetSafeIncomingDeathLinkTarget(
                out rider,
                out delayReason))
            {
                // Log the reason once, and again only if the reason changes.
                if (!string.Equals(
                    pendingRequest.LastDelayReason,
                    delayReason,
                    StringComparison.Ordinal))
                {
                    pendingRequest.LastDelayReason =
                        delayReason;

                    LogInfo(
                        $"AP DEATHLINK: incoming death waiting for safe gameplay state. " +
                        $"Source={pendingRequest.Source}, Reason={delayReason}"
                    );
                }

                // Player-facing warning only once for this particular DeathLink.
                if (!pendingRequest.DelayNoticeShown)
                {
                    pendingRequest.DelayNoticeShown = true;

                    AnnounceAPDeathLink(
                        $"[AP] DeathLink from {pendingRequest.Source} is delayed to avoid " +
                        $"breaking a dialogue, cutscene, or transition. " +
                        $"Laika will die once normal bike gameplay resumes."
                    );
                }

                return;
            }

            IncomingDeathLinkRequest request = null;

            // We have positively identified a normal vanilla riding state.
            // Only now consume the DeathLink from the queue.
            lock (IncomingDeathLinkQueueLock)
            {
                if (IncomingDeathLinkQueue.Count == 0)
                    return;

                request =
                    IncomingDeathLinkQueue.Dequeue();
            }

            if (request == null || rider == null)
                return;

            int suppressionBefore =
                SuppressedDeathLinksRemaining;

            // The Harmony RiderHead.Kill Prefix will consume this immediately,
            // causing this specific death to bypass Death Amnesty and preventing
            // it from bouncing another DeathLink back to the multiworld.
            SuppressedDeathLinksRemaining++;

            LogInfo(
                $"AP DEATHLINK: safe gameplay state reached. " +
                $"Applying incoming death through CurrentBike.RiderHead.Kill(). " +
                $"Source={request.Source}, Cause={request.Cause}, " +
                $"Suppression={SuppressedDeathLinksRemaining}"
            );

            try
            {
                bool protectIncomingViscera =
                    SessionState != null &&
                    SessionState.APEnabled &&
                    (WorldOptions.VisceraProtection ==
                        VisceraProtectionMode.DeathLinkOnly ||
                     WorldOptions.VisceraProtection ==
                        VisceraProtectionMode.AllDeaths);

                if (protectIncomingViscera)
                {
                    // This overload has its own existing death-detection patch,
                    // which consumes the same incoming suppression token.
                    rider.Kill(true, false);
                }
                else
                {
                    rider.Kill();
                }

                // The Harmony Prefix executes synchronously when Kill() is called.
                // If it did not consume our suppression token, clear it so the
                // player's next legitimate local death is never accidentally ignored.
                if (SuppressedDeathLinksRemaining > suppressionBefore)
                {
                    SuppressedDeathLinksRemaining =
                        suppressionBefore;

                    LogWarning(
                        "AP DEATHLINK: RiderHead.Kill returned without consuming " +
                        "incoming-death suppression. Cleared stale suppression."
                    );
                }
            }
            catch
            {
                SuppressedDeathLinksRemaining =
                    suppressionBefore;

                // If vanilla failed before the death could start, the player
                // still owes this DeathLink. Put it back at the front logically
                // by rebuilding the queue with this request first.
                lock (IncomingDeathLinkQueueLock)
                {
                    Queue<IncomingDeathLinkRequest> rebuiltQueue =
                        new Queue<IncomingDeathLinkRequest>();

                    rebuiltQueue.Enqueue(request);

                    while (IncomingDeathLinkQueue.Count > 0)
                    {
                        rebuiltQueue.Enqueue(
                            IncomingDeathLinkQueue.Dequeue()
                        );
                    }

                    while (rebuiltQueue.Count > 0)
                    {
                        IncomingDeathLinkQueue.Enqueue(
                            rebuiltQueue.Dequeue()
                        );
                    }
                }

                throw;
            }
        }
        catch (Exception ex)
        {
            LogError(
                $"AP DEATHLINK: failed applying queued incoming death:\n{ex}"
            );
        }
    }

    internal static void ClearPendingIncomingDeathLinks(string sourceTag)
    {
        lock (IncomingDeathLinkQueueLock)
        {
            IncomingDeathLinkQueue.Clear();
        }

        SuppressedDeathLinksRemaining = 0;

        LogInfo(
            $"AP DEATHLINK: cleared pending incoming deaths. Source={sourceTag}"
        );
    }


    // Handles AP-local death counting after a real player death is detected.
    // Both RiderHead.Kill overloads forward into this method through Harmony patches.
    internal static void OnPlayerDeathDetected(string sourceTag, bool? useBlood = null, bool? moneySack = null)
    {
        try
        {
            // If this death was caused by an incoming DeathLink,
            // do not count it toward outbound DeathLink logic.
            if (SuppressedDeathLinksRemaining > 0)
            {
                SuppressedDeathLinksRemaining--;

                LogInfo(
                    $"{sourceTag} | " +
                    $"Incoming DeathLink suppression consumed. " +
                    $"RemainingSuppressedDeaths={SuppressedDeathLinksRemaining}"
                );

                // This was a death intentionally caused by an incoming DeathLink.
                // Do not count it toward Death Amnesty and do not send another
                // DeathLink back to the server.
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
            // Prints Deathlink being disabled if the player dies with the setting off.
            // Commented out to allow for debugging use in the future, if necessary.
            //
            //AnnounceAPDeathLink("[AP] Local death detected. DeathLink is disabled.");
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
                string deathCause =
                    $"{SessionState.Connection.SlotName ?? "Laika"} " +
                    "couldn't survive in the Wasteland (Skill Issue).";
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
                string deathCause =
                    $"{SessionState.Connection.SlotName ?? "Laika"} " +
                    "couldn't survive in the Wasteland (Skill Issue).";
                ArchipelagoClientManager.Instance.SendDeathLink(deathCause);
            }

            DeathsSinceLastDeathLink = 0;

            LogInfo($"{sourceTag}: Death amnesty counter reset to 0 after real send.");
        }
    }
}
