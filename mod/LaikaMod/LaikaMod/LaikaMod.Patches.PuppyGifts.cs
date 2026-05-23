using HarmonyLib;
using Laika.Inventory;
using System;
using System.Reflection;
using UnityEngine;

public partial class LaikaMod
{
    // Puppy gift Harmony patches.
    // These keep AP-owned but unchecked Puppy gifts visible/interactable until their AP checks are sent.
    [HarmonyPatch(typeof(PuppyGiftPickItem), "Start")]
    public class PuppyGiftPickItem_Start_APOwnedUncheckedGiftPatch
    {
        private static readonly FieldInfo GiftField =
            typeof(PuppyGiftPickItem).GetField(
                "gift",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        private static readonly FieldInfo GfxField =
            typeof(PuppyGiftPickItem).GetField(
                "gfx",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(PuppyGiftPickItem __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (GiftField == null || GfxField == null)
                    return;

                ItemData gift = GiftField.GetValue(__instance) as ItemData;
                if (gift == null || string.IsNullOrEmpty(gift.id))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(gift.id, out definition))
                    return;

                if (definition.Category != "PuppyGift")
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                if (LaikaMod.IsPuppyGiftPlacedInHouse(gift.id))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "PuppyGiftPickItem_CanInteract/AlreadyPlacedInHouse",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"PUPPY GIFT PICKUP: sent AP check for already-placed puppy gift -> {gift.id}, location={definition.DisplayName}"
                    );

                    return;
                }

                if (!LaikaMod.HasReceivedAPItem(ItemKind.PuppyTreat, gift.id))
                    return;

                SpriteRenderer gfx = GfxField.GetValue(__instance) as SpriteRenderer;
                if (gfx != null)
                {
                    gfx.enabled = true;
                }

                LaikaMod.LogInfo(
                    $"PUPPY GIFT PICKUP: kept AP-owned unchecked gift visible -> {gift.id}, location={definition.DisplayName}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PuppyGiftPickItem_Start_APOwnedUncheckedGiftPatch exception:\n{ex}");
            }
        }
    }

    [HarmonyPatch(typeof(PuppyGiftPickItem), "CanInteract")]
    public class PuppyGiftPickItem_CanInteract_APOwnedUncheckedGiftPatch
    {
        private static readonly FieldInfo GiftField =
            typeof(PuppyGiftPickItem).GetField(
                "gift",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(PuppyGiftPickItem __instance, ref bool __result)
        {
            try
            {
                if (__result)
                    return;

                if (__instance == null)
                    return;

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (GiftField == null)
                    return;

                ItemData gift = GiftField.GetValue(__instance) as ItemData;
                if (gift == null || string.IsNullOrEmpty(gift.id))
                    return;

                APLocationDefinition definition;
                if (!LaikaMod.TryGetLocationDefinition(gift.id, out definition))
                    return;

                if (definition.Category != "PuppyGift")
                    return;

                if (LaikaMod.HasSentLocationCheck(definition))
                    return;

                if (LaikaMod.IsPuppyGiftPlacedInHouse(gift.id))
                {
                    LaikaMod.TrySendLocationCheck(
                        definition,
                        "PuppyGiftPickItem_CanInteract/AlreadyPlacedInHouse",
                        false
                    );

                    LaikaMod.LogInfo(
                        $"PUPPY GIFT PICKUP: sent AP check for already-placed puppy gift -> {gift.id}, location={definition.DisplayName}"
                    );

                    return;
                }

                if (!LaikaMod.HasReceivedAPItem(ItemKind.PuppyTreat, gift.id))
                    return;

                __result = true;

                LaikaMod.LogInfo(
                    $"PUPPY GIFT PICKUP: allowing AP-owned unchecked gift interaction -> {gift.id}, location={definition.DisplayName}"
                );
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"PuppyGiftPickItem_CanInteract_APOwnedUncheckedGiftPatch exception:\n{ex}");
            }
        }
    }
}