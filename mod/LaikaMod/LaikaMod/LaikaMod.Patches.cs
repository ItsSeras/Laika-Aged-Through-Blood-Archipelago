using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Laika.Cassettes;
using Laika.Inventory;
using Laika.Persistence;
using Laika.PlayMaker.FsmActions;
using Laika.Quests;
using Laika.Quests.PlayMaker.FsmActions;
using Laika.UI.InGame.Inventory;

// Harmony patches that hook Laika gameplay events into AP behavior.
public partial class LaikaMod
{
    // ===== Harmony patches =====
    [HarmonyPatch(typeof(WeaponsOverlay), "InitializeWeaponsData")]
    public class WeaponsOverlayPatch
    {
        static void Postfix(WeaponsOverlay __instance)
        {
            Log.LogInfo("WeaponsOverlay.InitializeWeaponsData postfix triggered.");

            EnsureRuntimeDevOverlay(__instance);

            // Only log ingredient IDs once per launch.
            if (!IngredientIdsLogged)
            {
                IngredientIdsLogged = true;
                // LogAllIngredientIds();
            }

            // Only log cassette IDs once per launch.
            if (!CassetteIdsLogged)
            {
                CassetteIdsLogged = true;
                // LogAllCassetteIds();
            }

            // Process queued AP-style items once the game UI/inventory systems are ready.
            ProcessPendingItemQueue("InitialItemGrant");
        }
    }


    [HarmonyPatch(typeof(QuestLog), "TryCloseQuest")]
    public class QuestClosePatch
    {
        static void Postfix(string questId, bool silent, bool __result)
        {
            // Only log successful full quest completions.
            if (!__result)
                return;

            LaikaMod.LogInfo($"QUEST COMPLETED: questId={questId}, silent={silent}");

            APLocationDefinition locationDefinition;
            if (!LaikaMod.TryGetLocationDefinition(questId, out locationDefinition))
            {
                LaikaMod.LogWarning($"QuestClosePatch: no AP location definition found for questId={questId}");
                return;
            }

            LaikaMod.TrySendLocationCheck(locationDefinition, "QuestClosePatch");
        }
    }

    // Logs Renato's map popup data when the buy-map popup opens.
    // Useful for verifying mapAreaID and price against Renato's shop data.
    [HarmonyPatch(typeof(ShowBuyingMapPopup), "OnEnter")]
    public class ShowBuyingMapPopupPatch
    {
        static void Prefix(ShowBuyingMapPopup __instance)
        {
            try
            {
                // Safety check in case the FSM values are missing for some reason.
                if (__instance == null)
                {
                    LaikaMod.LogWarning("ShowBuyingMapPopupPatch: __instance was null.");
                    return;
                }

                // Read the real PlayMaker values that Renato's popup is using.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";
                int mapAreaPrice = __instance.mapAreaPrice != null ? __instance.mapAreaPrice.Value : -1;

                // Log both the area ID and the price so we can identify which map piece is which.
                LaikaMod.LogInfo($"RENATO MAP POPUP: mapAreaID={mapAreaId}, price={mapAreaPrice}");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"ShowBuyingMapPopupPatch: exception while logging Renato map popup:\n{ex}");
            }
        }
    }

    // Tracks Renato map purchases as AP location checks.
    // Resolves the unlocked mapAreaID through the AP location registry.
    [HarmonyPatch(typeof(UnlockMapArea), "OnEnter")]
    public class UnlockMapAreaPatch
    {
        static void Prefix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                {
                    LaikaMod.LogWarning("UnlockMapAreaPatch: __instance was null.");
                    return;
                }

                // Read the raw internal mapAreaID for AP location resolution.
                string mapAreaId = __instance.mapAreaID != null ? __instance.mapAreaID.Value : "<null>";

                // Log the raw map ID even if no AP location definition exists yet.
                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                {
                    LaikaMod.LogWarning(
                        $"UnlockMapAreaPatch: no AP location definition found for mapAreaID={mapAreaId}"
                    );
                    return;
                }

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch: exception while logging map unlock action:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(CassettesManager), "AddCassetteToInventory", new Type[] { typeof(CassetteData), typeof(bool) })]
    public class CassetteInventoryRealSourcePatch
    {
        static void Postfix(CassetteData cassette, bool silent, bool __result)
        {
            try
            {
                if (!__result)
                    return;

                if (cassette == null)
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassette was null.");
                    return;
                }

                string cassetteId = cassette.id;

                if (string.IsNullOrEmpty(cassetteId))
                {
                    LaikaMod.LogWarning("CassetteInventoryRealSourcePatch: cassetteId was null or empty.");
                    return;
                }

                LaikaMod.LogInfo(
                    $"CASSETTE INVENTORY SOURCE DETECTED: id={cassetteId}, silent={silent}, result={__result}"
                );

                LaikaMod.TryHandleCassetteLocationCheck(cassetteId, "CassetteInventoryRealSourcePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"CassetteInventoryRealSourcePatch exception:\n{ex}");
            }
        }
    }

    // Tracks boss clears through progression achievement keys.
    // Boss kill completion is persisted by the game as achievement-style flags.
    [HarmonyPatch(typeof(ProgressionData), "SetAchievement", new Type[] { typeof(string), typeof(bool), typeof(bool) })]
    public class BossAchievementPatch
    {
        static void Prefix(string name, bool value, bool reset)
        {
            if (string.IsNullOrEmpty(name))
                return;

            if (!value)
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

    [HarmonyPatch(typeof(InventoryManager), "AddKeyItem", new Type[] { typeof(ItemData) })]
    public class PuppyGiftKeyItemSourcePatch
    {
        static void Postfix(ItemData __0, bool __result)
        {
            try
            {
                // Ignore failed grants because they did not actually add the Puppy item.
                if (!__result)
                    return;

                // Harmony positional argument __0 is the original ItemData parameter.
                ItemData itemData = __0;

                if (itemData == null)
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemData was null.");
                    return;
                }

                string itemId = itemData.id;

                if (string.IsNullOrEmpty(itemId))
                {
                    LaikaMod.LogWarning("PuppyGiftKeyItemSourcePatch: itemId was null or empty.");
                    return;
                }

                // Helpful debug log so we can confirm which Puppy items naturally route through AddKeyItem(...).
                LaikaMod.LogInfo(
                    $"KEY ITEM SOURCE DETECTED: id={itemId}, name={itemData.Name}, result={__result}"
                );

                // Only route known Puppy gift IDs into the AP Puppy location handler.
                if (
                    itemId == "I_TOY_BIKE" ||
                    itemId == "I_GAMEBOY" ||
                    itemId == "I_PLANT_PUPPY" ||
                    itemId == "I_TOY_ANIMAL" ||
                    itemId == "I_BOOK_MOTHER" ||
                    itemId == "I_DREAMCATCHER" ||
                    itemId == "I_UKULELE"
                )
                {
                    // Debug log so we can prove the Puppy gift is actually being routed
                    // into the AP Puppy location-check handler.
                    LaikaMod.LogInfo(
                        $"PuppyGiftKeyItemSourcePatch: routing puppy gift id {itemId} into TryHandlePuppyGiftLocationCheck."
                    );

                    LaikaMod.TryHandlePuppyGiftLocationCheck(itemId, "PuppyGiftKeyItemSourcePatch");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"PuppyGiftKeyItemSourcePatch exception:\n{ex}");
            }
        }
    }

    // Some AP softlocks happen right when a quest gets added or advanced.
    // I re-run the quest softlock reconciliation here so the fix can happen immediately
    // instead of waiting for a later zone load or queue wake-up.
    [HarmonyPatch(typeof(TryAddQuest), "OnEnter")]
    public class TryAddQuestReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryAddQuestReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryAddQuestReconcilePatch exception:\n{ex}");
            }
        }
    }

    // Some vanilla flows complete one goal and immediately move to the next.
    // If AP already gave the required item, that newly current goal can softlock on the spot.
    // Running the reconciliation here makes the fix happen much earlier.
    [HarmonyPatch(typeof(TryCompleteQuestGoal), "OnEnter")]
    public class TryCompleteQuestGoalReconcilePatch
    {
        static void Postfix()
        {
            try
            {
                LaikaMod.TryReconcileKnownQuestSoftlocks("TryCompleteQuestGoalReconcilePatch");
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"TryCompleteQuestGoalReconcilePatch exception:\n{ex}");
            }
        }
    }
}