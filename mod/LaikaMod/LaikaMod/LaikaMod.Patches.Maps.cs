using HarmonyLib;
using Laika.PlayMaker.FsmActions;
using System;
using System.Reflection;
using UnityEngine;

public partial class LaikaMod
{
    // Map and Renato map-purchase Harmony patches.
    // AP map items control map visibility, so vanilla map unlocks are intercepted here.
    [HarmonyPatch(typeof(MapArea), "OnEnable")]
    public class MapArea_OnEnable_APVisualUnlockPatch
    {
        private static readonly FieldInfo MapAreaIdField =
            typeof(MapArea).GetField(
                "mapAreaID",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        static void Postfix(MapArea __instance)
        {
            try
            {
                if (__instance == null || MapAreaIdField == null)
                    return;

                string mapAreaId = MapAreaIdField.GetValue(__instance) as string;

                if (string.IsNullOrEmpty(mapAreaId))
                    return;

                if (!LaikaMod.IsAPMapAreaLocation(mapAreaId))
                    return;

                if (LaikaMod.HasAPMapUnlock(mapAreaId))
                {
                    __instance.Enable(true);

                    LaikaMod.LogInfo($"AP MAP VISUAL: forced visible on MapArea.OnEnable -> {mapAreaId}");
                    return;
                }

                if (LaikaMod.SessionState != null && LaikaMod.SessionState.APEnabled)
                {
                    __instance.Enable(false);

                    LaikaMod.LogInfo($"AP MAP VISUAL: forced hidden on MapArea.OnEnable because AP has not granted it -> {mapAreaId}");
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"MapArea_OnEnable_APVisualUnlockPatch exception:\n{ex}");
            }
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
        static bool Prefix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                string mapAreaId = __instance.mapAreaID != null
                    ? __instance.mapAreaID.Value
                    : "<null>";

                LaikaMod.LogInfo($"MAP UNLOCK ACTION: mapAreaID={mapAreaId}");

                APLocationDefinition locationDefinition;
                if (!LaikaMod.TryGetLocationDefinition(mapAreaId, out locationDefinition))
                    return true;

                LaikaMod.TrySendLocationCheck(locationDefinition, "UnlockMapAreaPatch");

                LaikaMod.TryReconcileKnownQuestSoftlocks("UnlockMapAreaPatch");

                // Let vanilla continue so Renato's normal purchased/disappeared state is saved.
                // The Postfix/MapArea.OnEnable patches will control the actual map visual.
                return true;
            }
            catch (Exception ex)
            {
                LaikaMod.LogError($"UnlockMapAreaPatch.Prefix exception:\n{ex}");
                return true;
            }
        }

        static void Postfix(UnlockMapArea __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                string mapAreaId = __instance.mapAreaID != null
                    ? __instance.mapAreaID.Value
                    : "<null>";

                if (LaikaMod.SessionState == null || !LaikaMod.SessionState.APEnabled)
                    return;

                if (!LaikaMod.IsAPMapAreaLocation(mapAreaId))
                    return;

                if (LaikaMod.HasAPMapUnlock(mapAreaId))
                {
                    LaikaMod.RefreshMapAreaVisuals(mapAreaId);

                    LaikaMod.LogInfo(
                        $"UnlockMapAreaPatch: vanilla purchase completed for {mapAreaId}; map kept visible because AP already granted it."
                    );
                }
                else
                {
                    LaikaMod.HideMapAreaVisuals(mapAreaId, "UnlockMapAreaPatch/Postfix");

                    LaikaMod.LogInfo(
                        $"UnlockMapAreaPatch: vanilla purchase completed for {mapAreaId}; map visual hidden because AP has not granted it yet."
                    );
                }
            }
            catch (Exception ex)
            {
                LaikaMod.LogWarning($"UnlockMapAreaPatch.Postfix exception:\n{ex}");
            }
        }
    }

    internal static bool IsAPMapAreaLocation(string mapAreaId)
    {
        if (string.IsNullOrEmpty(mapAreaId))
            return false;

        APLocationDefinition definition;
        if (!TryGetLocationDefinition(mapAreaId, out definition))
            return false;

        if (definition.DisplayName != null &&
            definition.DisplayName.StartsWith("Map Piece:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return
            definition.Category == "Map" ||
            definition.Category == "MapUnlock" ||
            definition.Category == "MapPiece";
    }

    internal static void HideMapAreaVisuals(string mapAreaId, string sourceTag)
    {
        try
        {
            if (string.IsNullOrEmpty(mapAreaId))
                return;

            FieldInfo mapAreaIdField =
                typeof(MapArea).GetField(
                    "mapAreaID",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

            if (mapAreaIdField == null)
            {
                LogWarning($"{sourceTag}: could not hide map visuals because MapArea.mapAreaID field was not found.");
                return;
            }

            int hiddenCount = 0;

            foreach (MapArea area in Resources.FindObjectsOfTypeAll<MapArea>())
            {
                if (area == null)
                    continue;

                string currentId = mapAreaIdField.GetValue(area) as string;

                if (currentId != mapAreaId)
                    continue;

                area.Enable(false);
                hiddenCount++;
            }

            LogInfo($"{sourceTag}: hid {hiddenCount} MapArea object(s) for vanilla-only map area {mapAreaId}.");
        }
        catch (Exception ex)
        {
            LogWarning($"{sourceTag}: HideMapAreaVisuals failed for {mapAreaId}:\n{ex}");
        }
    }
}