using System.Runtime.CompilerServices;
using Laika.UI.InGame;
using TMPro;
using HarmonyLib;
using Laika.PlayMaker.FsmActions;
using System;
using System.Reflection;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

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

    private static Sprite RenatoPopupLogoSprite;
    private static Texture2D RenatoPopupLogoTexture;
    private static bool TriedLoadRenatoPopupLogo;

    private const string RenatoPopupLogoResourceFileName =
        "renato_ap_logo.png";

    private static Sprite GetRenatoPopupLogoSprite()
    {
        if (TriedLoadRenatoPopupLogo)
            return RenatoPopupLogoSprite;

        TriedLoadRenatoPopupLogo = true;

        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            string resourceName = null;

            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(
                    RenatoPopupLogoResourceFileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(resourceName))
            {
                LogWarning(
                    "RENATO AP PREVIEW: embedded popup logo resource was not found. " +
                    "Make sure Assets\\renato_ap_logo.png has Build Action = Embedded Resource."
                );

                return null;
            }

            byte[] pngBytes;

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    LogWarning(
                        "RENATO AP PREVIEW: embedded popup logo stream was null for " +
                        resourceName + "."
                    );

                    return null;
                }

                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    pngBytes = memory.ToArray();
                }
            }

            RenatoPopupLogoTexture =
                new Texture2D(2, 2, TextureFormat.ARGB32, false);

            RenatoPopupLogoTexture.name =
                "LaikaAP_RenatoPopupLogo";

            if (!ImageConversion.LoadImage(
                RenatoPopupLogoTexture,
                pngBytes,
                false))
            {
                LogWarning(
                    "RENATO AP PREVIEW: failed to decode embedded popup logo."
                );

                UnityEngine.Object.Destroy(RenatoPopupLogoTexture);
                RenatoPopupLogoTexture = null;

                return null;
            }

            RenatoPopupLogoTexture.wrapMode =
                TextureWrapMode.Clamp;

            RenatoPopupLogoTexture.filterMode =
                FilterMode.Bilinear;

            RenatoPopupLogoSprite = Sprite.Create(
                RenatoPopupLogoTexture,
                new Rect(
                    0f,
                    0f,
                    RenatoPopupLogoTexture.width,
                    RenatoPopupLogoTexture.height
                ),
                new Vector2(0.5f, 0.5f),
                100f
            );

            RenatoPopupLogoSprite.name =
                "LaikaAP_RenatoPopupLogo";

            LogInfo(
                "RENATO AP PREVIEW: loaded embedded custom popup logo " +
                $"'{resourceName}' " +
                $"({RenatoPopupLogoTexture.width}x" +
                $"{RenatoPopupLogoTexture.height})."
            );
        }
        catch (Exception ex)
        {
            LogWarning(
                "RENATO AP PREVIEW: failed to load embedded custom popup logo:\n" +
                ex
            );

            if (RenatoPopupLogoTexture != null)
            {
                UnityEngine.Object.Destroy(RenatoPopupLogoTexture);
                RenatoPopupLogoTexture = null;
            }

            RenatoPopupLogoSprite = null;
        }

        return RenatoPopupLogoSprite;
    }

    private sealed class RenatoPopupLogoState
    {
        internal Image MapImage;
        internal Sprite OriginalSprite;
        internal Sprite OriginalOverrideSprite;
        internal Color OriginalColor;
        internal bool OriginalPreserveAspect;
        internal Vector3 OriginalScale;
    }

    private static readonly ConditionalWeakTable<BuyMapPopup, RenatoPopupLogoState>
        RenatoPopupLogoStates =
            new ConditionalWeakTable<BuyMapPopup, RenatoPopupLogoState>();

    private static void RestoreRenatoPopupLogo(BuyMapPopup popup)
    {
        if (popup == null)
            return;

        RenatoPopupLogoState state;
        if (!RenatoPopupLogoStates.TryGetValue(popup, out state))
            return;

        if (state.MapImage != null)
        {
            state.MapImage.sprite = state.OriginalSprite;
            state.MapImage.overrideSprite = state.OriginalOverrideSprite;
            state.MapImage.color = state.OriginalColor;
            state.MapImage.preserveAspect = state.OriginalPreserveAspect;

            if (state.MapImage.rectTransform != null)
                state.MapImage.rectTransform.localScale = state.OriginalScale;
        }

        RenatoPopupLogoStates.Remove(popup);
    }

    private static void ApplyRenatoPopupLogo(BuyMapPopup popup)
    {
        if (popup == null)
            return;

        // Restore any changes from a previous use of this popup first.
        RestoreRenatoPopupLogo(popup);

        Sprite logoSprite = GetRenatoPopupLogoSprite();
        if (logoSprite == null)
            return;

        Transform mapTransform =
            popup.transform.Find("Panel/ImageBg/Image");

        if (mapTransform == null)
        {
            LogWarning(
                "RENATO AP PREVIEW: could not find " +
                "'Panel/ImageBg/Image'. Keeping vanilla map icon."
            );
            return;
        }

        Image mapImage = mapTransform.GetComponent<Image>();

        if (mapImage == null)
        {
            LogWarning(
                "RENATO AP PREVIEW: 'Panel/ImageBg/Image' did not contain " +
                "a UnityEngine.UI.Image component."
            );
            return;
        }

        var state = new RenatoPopupLogoState
        {
            MapImage = mapImage,
            OriginalSprite = mapImage.sprite,
            OriginalOverrideSprite = mapImage.overrideSprite,
            OriginalColor = mapImage.color,
            OriginalPreserveAspect = mapImage.preserveAspect,
            OriginalScale = mapImage.rectTransform.localScale
        };

        RenatoPopupLogoStates.Add(popup, state);

        mapImage.sprite = logoSprite;
        mapImage.overrideSprite = logoSprite;
        mapImage.color = Color.white;
        mapImage.preserveAspect = true;

        // Keep the AP logo slightly smaller than the vanilla map's available area.
        Vector3 originalScale = state.OriginalScale;

        mapImage.rectTransform.localScale = new Vector3(
            originalScale.x * 0.84f,
            originalScale.y * 0.84f,
            originalScale.z
        );

        LogInfo(
            "RENATO AP PREVIEW: replaced vanilla icon_map with custom AP logo; " +
            "logoScale=84%."
        );
    }

    // Renato AP item previews.

    private sealed class RenatoPopupPreviewState
    {
        internal long LocationId;
        internal int Price;
        internal string OriginalText;
        internal object SaveState;
        internal float FallbackAt;
    }

    private static readonly ConditionalWeakTable<BuyMapPopup, RenatoPopupPreviewState>
        RenatoPopupPreviews = new ConditionalWeakTable<BuyMapPopup, RenatoPopupPreviewState>();

    private static void RefreshRenatoPreview(
        TextMeshProUGUI message, RenatoPopupPreviewState state)
    {
        if (message == null)
            return;

        if (SessionState == null || !SessionState.APEnabled ||
            !ReferenceEquals(state.SaveState, SessionState))
        {
            if (message.text != state.OriginalText)
                message.text = state.OriginalText;
            return;
        }

        var client = ArchipelagoClientManager.Instance;
        string preview = client != null ? client.GetRenatoPreview(state.LocationId) : null;

        // Avoid flashing a temporary paragraph during a quick first lookup.
        // Only this message is blanked; the popup and its controls remain active.
        bool awaitingFirstPreview = preview == null && client != null &&
            client.IsConnected && UnityEngine.Time.unscaledTime < state.FallbackAt;

        string loadingLine =
            client != null && client.IsConnected
                ? (WorldOptions.AutomaticPurchaseHints
                    ? "Creating hint and loading preview..."
                    : "Loading item preview...")
                : "Connect to Archipelago for preview.";

        string body = preview != null
            ? "Buy " + preview + "\nfor " + state.Price + " viscera?"
            : "Buy this randomized AP item\nfor " + state.Price +
                " viscera?\n" + loadingLine;

        string text = awaitingFirstPreview
            ? string.Empty
            : "<size=92%>" + body + "</size>";

        if (message.text != text)
            message.text = text;
    }

    [HarmonyPatch(typeof(BuyMapPopup), "SetUp")]
    public static class RenatoPreview_SetUpPatch
    {
        static void Postfix(BuyMapPopup __instance, object data,
            TextMeshProUGUI ___message)
        {
            try
            {
                // Restore any visual changes from a previous opening first.
                // This also guarantees AP-disabled popups stay completely vanilla.
                RestoreRenatoPopupLogo(__instance);

                RenatoPopupPreviews.Remove(__instance);

                var map = data as BuyMapPopup.Data;
                APLocationDefinition location;
                if (map == null || ___message == null || SessionState == null ||
                    !SessionState.APEnabled || !IsAPMapAreaLocation(map.MapAreaID) ||
                    !TryGetLocationDefinition(map.MapAreaID, out location))
                    return;

                var state = new RenatoPopupPreviewState
                {
                    LocationId = location.LocationId,
                    Price = map.MapAreaPrice,
                    OriginalText = ___message.text,
                    SaveState = SessionState,
                    FallbackAt = UnityEngine.Time.unscaledTime + 0.5f
                };
                RenatoPopupPreviews.Add(__instance, state);
                ApplyRenatoPopupLogo(__instance);
                RefreshRenatoPreview(___message, state);

                LogInfo("RENATO AP PREVIEW: opened " + map.MapAreaID +
                    ", location=" + location.LocationId + ", price=" + map.MapAreaPrice);
            }
            catch (Exception ex)
            {
                RenatoPopupPreviews.Remove(__instance);
                LogWarning("Renato preview setup failed: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(BuyMapPopup), "Update")]
    public static class RenatoPreview_UpdatePatch
    {
        static void Postfix(BuyMapPopup __instance, TextMeshProUGUI ___message)
        {
            RenatoPopupPreviewState state;
            if (!RenatoPopupPreviews.TryGetValue(__instance, out state))
                return;

            // Observe completed work every frame, without extra network requests.
            // RefreshRenatoPreview only assigns text when the value changes.
            try
            {
                RefreshRenatoPreview(___message, state);
            }
            catch (Exception ex)
            {
                if (___message != null)
                    ___message.text = state.OriginalText;
                RenatoPopupPreviews.Remove(__instance);
                LogWarning("Renato preview refresh failed: " + ex);
            }
        }
    }
}
