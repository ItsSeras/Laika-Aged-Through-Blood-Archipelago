using HarmonyLib;
using Laika.UI.InGame.Inventory;

public partial class LaikaMod
{
    // In-game overlay lifecycle Harmony patches.
    // These use vanilla UI initialization points to refresh AP overlays and queued item grants.
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
            LaikaMod.ProcessPendingItemQueue("WeaponsOverlayInitializeQueueProcess");
        }
    }
}