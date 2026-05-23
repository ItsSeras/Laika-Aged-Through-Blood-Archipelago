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

    // Specific weapon unlock helpers that use the generic weapon-mode resolver.

    //Forces the player to start with a pistol. The player doesn't require
    //it. However, if the player interacts with Jakob too early via a
    //different weapon, then there is a chance the player will never
    //acquire the pistol at all. This enforces it.
    internal static void EnqueueRequiredStartingItems()
    {
        EnqueueItem(new PendingItem(ItemKind.Weapon, "I_W_PISTOL", 1, "Pistol"));
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
