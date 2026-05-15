using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public partial class LaikaMod
{
    private static string LastUniversalTrackerRegion = "";

    private static readonly Dictionary<string, string> SceneNameToUniversalTrackerRegion =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
    // Non-gameplay / loading.
    { "TitleScreen", "" },
    { "LoadingScreen", "" },

    // Tutorial / intro.
    { "Autosave", "Start / Tutorial Area" },
    { "Wasteland_01_01", "Start / Tutorial Area" },

    // Where We Live / camp hub.
    { "Camp", "Where We Live" },
    { "Camp_Night", "Where We Live" },
    { "Camp_Funeral", "Where We Live" },

    // Where We Dream
    { "laika_house", "Where We Dream" },
    { "laika_house_night", "Where We Dream" },

    // Where Mother Groans.
    { "esoteric_house", "Where Mother Groans" },
    { "esoteric_house_night", "Where Mother Groans" },
    { "esoteric_house_funeral", "Where Mother Groans" },

    // Where Shaza Tinkers.
    { "shaza_house", "Where Shaza Tinkers" },
    { "shaza_house_night", "Where Shaza Tinkers" },

    // Where Rules Are Made.
    { "chief_house", "Where Rules Are Made" },
    { "chief_house_night", "Where Rules Are Made" },

    // Where We Forget.
    { "bar", "Where We Forget" },
    { "bar_night", "Where We Forget" },

    // Main wasteland / dungeon scene names from map area IDs.
    { "Wasteland_01_06", "Where Our Bikes Growl" },

    { "Wasteland_01_01_Bottom", "Where All Was Lost" },
    { "Wasteland_01_01_Top", "Where All Was Lost" },
    { "Wasteland_01_02", "Where Doom Fell" },
    { "Wasteland_01_03", "Where Rust Weaves" },
    { "Wasteland_01_04", "Where Iron Caresses the Sky" },
    { "Wasteland_01_05", "Where the Waves Die" },
    { "Wasteland_01_07", "Where Our Ancestors Rest" },
    { "Wasteland_01_08", "Where Birds Came From" },

    { "Dungeon_00", "Where Birds Lurk" },
    { "Dungeon_01", "Where Rock Bleeds" },
    { "Dungeon_02", "Where Water Glistened" },
    { "Dungeon_03", "The Big Tree" },
    { "Dungeon_04", "Floating City" },

    // Floating City sub-scenes, if the game loads them separately.
    { "Dungeon_04_Center", "Floating City" },
    { "Dungeon_04_Town", "Floating City" },
    { "Dungeon_04_Zeppelin", "Floating City" },
    { "Dungeon_04_Factory", "Floating City" },
    { "Dungeon_04_Facilities", "Floating City" },

    // Keep readable placeholders as harmless fallback support.
    { "WhereOurBikesGrowl", "Where Our Bikes Growl" },
    { "WhereOurAncestorsRest", "Where Our Ancestors Rest" },
    { "WhereDoomFell", "Where Doom Fell" },
    { "WhereTheWavesDie", "Where the Waves Die" },
    { "WhereRustWeaves", "Where Rust Weaves" },
    { "WhereRockBleeds", "Where Rock Bleeds" },
    { "WhereWaterGlistened", "Where Water Glistened" },
    { "WhereAllWasLost", "Where All Was Lost" },
    { "WhereBirdsCameFrom", "Where Birds Came From" },
    { "TheBigTree", "The Big Tree" },
    { "WhereIronCaressesTheSky", "Where Iron Caresses the Sky" },
    { "WhereBirdsLurk", "Where Birds Lurk" },
    { "FloatingCity", "Floating City" },
    };

    private static readonly Dictionary<string, int> UniversalTrackerRegionToMapIndex =
    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Start / Tutorial Area", 0 },
        { "Where We Live", 1 },
        { "Where Mother Groans", 2 },
        { "Where We Dream", 3 },
        { "Where Shaza Tinkers", 4 },
        { "Where Rules Are Made", 5 },
        { "Where We Forget", 6 },
        { "Where Our Bikes Growl", 7 },
        { "Where Our Ancestors Rest", 8 },
        { "Where Doom Fell", 9 },
        { "Where the Waves Die", 10 },
        { "Where Rust Weaves", 11 },
        { "Where Rock Bleeds", 12 },
        { "Where Water Glistened", 13 },
        { "Where All Was Lost", 14 },
        { "Where Birds Came From", 15 },
        { "The Big Tree", 16 },
        { "Where Iron Caresses the Sky", 17 },
        { "Where Birds Lurk", 18 },
        { "Floating City", 19 },
    };

    internal static void TryUpdateUniversalTrackerRegionFromScene(Scene scene, string sourceTag)
    {
        try
        {
            if (SessionState == null || !SessionState.APEnabled)
                return;

            string sceneName = scene.name;

            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            string regionName = ResolveUniversalTrackerRegion(sceneName);

            if (string.IsNullOrWhiteSpace(regionName))
                return;

            bool isAuthenticated =
                SessionState.Connection != null &&
                SessionState.Connection.IsAuthenticated &&
                ArchipelagoClientManager.Instance != null &&
                ArchipelagoClientManager.Instance.IsConnected;

            if (!isAuthenticated)
            {
                LogInfo(
                    $"UT MAP: resolved {regionName} from scene={sceneName} source={sourceTag}, " +
                    "but AP is not authenticated yet. Deferring send."
                );

                return;
            }

            if (string.Equals(LastUniversalTrackerRegion, regionName, StringComparison.OrdinalIgnoreCase))
            {
                LogInfo(
                    $"UT MAP: region unchanged but forcing data-storage refresh -> {regionName} " +
                    $"from scene={sceneName} source={sourceTag}"
                );
            }
            else
            {
                LogInfo(
                    $"UT MAP: region changed -> {regionName} from scene={sceneName} source={sourceTag}"
                );
            }

            int mapIndex;
            if (!UniversalTrackerRegionToMapIndex.TryGetValue(regionName, out mapIndex))
            {
                LogWarning($"UT MAP: no map index configured for region={regionName}");
                return;
            }

            bool sent = ArchipelagoClientManager.Instance.SendUniversalTrackerRegion(regionName, mapIndex);

            if (!sent)
            {
                LogWarning(
                    $"UT MAP: send failed, leaving cached tracker region unchanged so it can retry. " +
                    $"Region={regionName}, scene={sceneName}, source={sourceTag}"
                );
                return;
            }

            // Only mark it as last after the authenticated send succeeds.
            LastUniversalTrackerRegion = regionName;
        }
        catch (Exception ex)
        {
            LogWarning($"UT MAP: failed to update region from scene. Source={sourceTag}\n{ex}");
        }
    }

    internal static void ResetUniversalTrackerRegionCache(string reason)
    {
        LastUniversalTrackerRegion = "";
        LogInfo($"UT MAP: reset cached tracker region. Reason={reason}");
    }

    private static string ResolveUniversalTrackerRegion(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return "";

        string regionName;
        if (SceneNameToUniversalTrackerRegion.TryGetValue(sceneName, out regionName))
            return regionName;

        // Fallbacks for scene names that contain readable area names.
        // These are intentionally conservative.
        string normalized = sceneName.Replace("_", "").Replace("-", "").Replace(" ", "");

        if (normalized.IndexOf("Wasteland0101", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("Autosave", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Start / Tutorial Area";

        if (normalized.IndexOf("BikesGrowl", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Our Bikes Growl";

        if (normalized.IndexOf("AncestorsRest", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Our Ancestors Rest";

        if (normalized.IndexOf("DoomFell", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Doom Fell";

        if (normalized.IndexOf("WavesDie", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where the Waves Die";

        if (normalized.IndexOf("RustWeaves", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Rust Weaves";

        if (normalized.IndexOf("RockBleeds", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Rock Bleeds";

        if (normalized.IndexOf("AllWasLost", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where All Was Lost";

        if (normalized.IndexOf("BirdsCameFrom", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Birds Came From";

        if (normalized.IndexOf("BigTree", StringComparison.OrdinalIgnoreCase) >= 0)
            return "The Big Tree";

        if (normalized.IndexOf("WaterGlistened", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Water Glistened";

        if (normalized.IndexOf("IronCaresses", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Iron Caresses the Sky";

        if (normalized.IndexOf("BirdsLurk", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Birds Lurk";

        if (normalized.IndexOf("Floating", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Floating City";

        if (normalized.IndexOf("shazahouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("shaza", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Shaza Tinkers";

        if (normalized.IndexOf("chiefhouse", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("chief", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where Rules Are Made";

        if (normalized.IndexOf("bar", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where We Forget";

        if (normalized.IndexOf("Puppy", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("Kidnap", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("Child", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Where They Keep Puppy";

        return "";
    }
}