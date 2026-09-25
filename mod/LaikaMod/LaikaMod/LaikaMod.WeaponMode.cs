using Laika.Inventory;
using Laika.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class LaikaMod
{
    // Weapon-mode helpers.
    //
    // Direct mode:
    // Returns the weapon itself.
    //
    // Crafting mode:
    // Returns the weapon's unique crafting material instead. APWorld handles the blueprint side of the unlock.
    //
    // Note:
    // Crossbow does not currently have a clean one-to-one unique crafting material,
    // so it should remain direct-only until a better crafting-mode design is decided.
    internal static PendingItem GetWeaponUnlockItem(
        string directWeaponId,
        string directDisplayName,
        string craftingMaterialId,
        string craftingDisplayName)
    {
        if (WorldOptions.WeaponMode == WeaponGrantMode.Crafting)
        {
            return new PendingItem(ItemKind.Material, craftingMaterialId, 1, craftingDisplayName);
        }

        return new PendingItem(ItemKind.Weapon, directWeaponId, 1, directDisplayName);
    }

    // The Pistol is a vanilla safety item rather than an AP-randomized weapon.
    //
    // IMPORTANT:
    // Adding the first weapon causes vanilla to set G_GUN_RECEIVED.
    // The opening/tutorial area uses that flag to change Jakob, activate the alarm,
    // and swap its enemy groups.
    //
    // This Pistol fallback must not activate G_GUN_RECEIVED itself.
    // A different AP-granted weapon can already have activated the flag.
    // Once it is completed, repair a missing Pistol without advancing it again.
    internal static void EnqueueRequiredStartingItems()
    {
        if (SessionState == null || !SessionState.APEnabled)
            return;

        try
        {
            ProgressionManager progression =
                MonoSingleton<ProgressionManager>.Instance;

            WeaponsInventory weapons =
                Singleton<WeaponsInventory>.Instance;

            if (progression == null ||
                progression.ProgressionData == null ||
                weapons == null)
            {
                return;
            }

            // Adding a first weapon changes vanilla tutorial progression.
            // Only repair the Pistol once this flag is already active.
            if (!progression.ProgressionData.GetAchievementCompleted(
                "G_GUN_RECEIVED"))
            {
                return;
            }

            if (weapons.HasWeapon("I_W_PISTOL"))
                return;

            foreach (PendingItem queuedItem in PendingItemQueue)
            {
                if (queuedItem != null &&
                    queuedItem.Kind == ItemKind.Weapon &&
                    queuedItem.Id == "I_W_PISTOL")
                {
                    return;
                }
            }

            LogInfo(
                "PISTOL SAFETY: G_GUN_RECEIVED is already active, " +
                "but the Pistol is missing. Queueing the safety Pistol."
            );

            EnqueueItem(
                new PendingItem(ItemKind.Weapon, "I_W_PISTOL", 1, "Pistol")
            );
        }
        catch (Exception ex)
        {
            LogWarning($"EnqueueRequiredStartingItems failed:\n{ex}");
        }
    }

    internal static PendingItem GetRocketLauncherUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_ROCKETLAUNCHER",
            "Rocket Launcher",
            "I_MATERIAL_ROCKETLAUNCHER",
            "Weapon Crafting Material: Missile"
        );
    }

    internal static PendingItem GetShotgunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SHOTGUN",
            "Shotgun",
            "I_MATERIAL_SHOTGUN",
            "Weapon Crafting Material: Rusty Spring"
        );
    }

    internal static PendingItem GetSniperUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_SNIPER",
            "Sniper Rifle",
            "I_MATERIAL_SNIPER",
            "Weapon Crafting Material: Magnifying Glass"
        );
    }

    internal static PendingItem GetMachineGunUnlockItem()
    {
        return GetWeaponUnlockItem(
            "I_W_UZI",
            "Machine Gun",
            "I_MATERIAL_UZI",
            "Weapon Crafting Material: Titanium Plates"
        );
    }
}
