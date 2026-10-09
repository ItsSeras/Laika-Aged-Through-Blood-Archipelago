using HarmonyLib;
using Laika.Inventory;
using Laika.UI.InGame.Inventory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

public partial class LaikaMod
{
    // In-game overlay lifecycle Harmony patches.
    // These use vanilla UI initialization points to refresh AP overlays and queued item grants.
    [HarmonyPatch(typeof(WeaponsOverlay), "InitializeWeaponsData")]
    public class WeaponsOverlayPatch
    {
        static void Prefix()
        {
            EnsureAPEquippedBikeSlot(Singleton<WeaponsInventory>.Instance);
        }

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

    // An on-foot AP grant can arrive after the parked bike's WeaponArm.Start
    // has already built an empty list. GetOnC assigns CurrentWeaponArm but never
    // reloads it. Refresh after the actual mounting coroutine has completed.
    [HarmonyPatch(typeof(BikeManager), "GetOnC")]
    public class BikeManager_GetOnC_APWeaponRefreshPatch
    {
        static void Postfix(BikeManager __instance, ref IEnumerator __result)
        {
            if (SessionState != null && SessionState.APEnabled && __result != null)
                __result = RefreshAPWeaponsAfterMount(__result, __instance);
        }
    }

    internal static IEnumerator RefreshAPWeaponsAfterMount(
        IEnumerator mount, BikeManager bike)
    {
        var save = APItemDeliverySaveData;
        yield return mount;

        // A canceled mount, changed save, or title-screen transition must not
        // refresh a different player's inventory through persistent managers.
        if (bike == null || save == null ||
            !ReferenceEquals(save, APItemDeliverySaveData) || !CanProcessAPItems())
        {
            yield break;
        }

        var player = MonoSingleton<PlayerManager>.Instance;
        var inventory = Singleton<WeaponsInventory>.Instance;
        var sceneLoader = MonoSingleton<SceneLoader>.Instance;
        if (player.CurrentBike != bike || inventory == null ||
            inventory.Weapons == null || sceneLoader == null ||
            !sceneLoader.CurrentSceneAllowsWeapons)
        {
            yield break;
        }

        try
        {
            if (inventory.GetBikeWeapons().Count == 0)
                yield break;

            var arm = player.CurrentWeaponArm;
            if (arm == null)
            {
                LogWarning("AP WEAPONS: mounted bike has no active weapon arm to refresh.");
                yield break;
            }

            EnsureAPEquippedBikeSlot(inventory);
            // Unlike Initialize()/UpdateWeapons(false), true preserves the ammo
            // in weapons already cached by this arm. Boarding is not a reload.
            arm.UpdateWeapons(true);
            var overlay = MonoSingleton<WeaponsOverlay>.Instance;
            if (overlay != null)
                overlay.InitializeWeaponsData();

            LogInfo("AP WEAPONS: refreshed mounted bike and wheel. EquippedSlot=" +
                inventory.EquippedWeaponIdx + ", Carried=" + inventory.GetBikeWeapons().Count);
        }
        catch (Exception ex)
        {
            LogWarning("AP WEAPONS: refresh after mounting failed:\n" + ex);
        }
    }

    private static readonly FieldInfo APEquippedBikeSlotField =
        AccessTools.Field(typeof(WeaponsInventory), "equippedWeaponIdx");

    internal static void EnsureAPEquippedBikeSlot(WeaponsInventory inventory)
    {
        if (SessionState == null || !SessionState.APEnabled || inventory == null ||
            inventory.Weapons == null)
        {
            return;
        }

        var slots = inventory.GetBikeWeapons(true);
        int equipped = inventory.EquippedWeaponIdx;
        if (equipped >= 0 && equipped < slots.Count && slots[equipped] != null)
            return;

        int firstOccupied = slots.FindIndex(weapon => weapon != null);
        if (equipped != firstOccupied)
            APEquippedBikeSlotField.SetValue(inventory, firstOccupied);
    }

    internal static bool CycleAPBikeWeapon(WeaponsInventory inventory, int direction)
    {
        if (SessionState == null || !SessionState.APEnabled)
            return true;

        var slots = inventory.GetBikeWeapons(true);
        int current = inventory.EquippedWeaponIdx;
        int start = current >= 0 && current < slots.Count
            ? current
            : (direction > 0 ? -1 : 0);

        // Keep the actual slot layout and capacity. Only selection skips holes.
        for (int step = 1; step <= slots.Count; step++)
        {
            int next = ((start + direction * step) % slots.Count + slots.Count) % slots.Count;
            if (slots[next] == null)
                continue;

            if (next != current)
                inventory.EquipWeaponAtIndex(next);
            break;
        }

        return false;
    }

    [HarmonyPatch(typeof(WeaponsInventory), "EquipNextWeapon")]
    public class WeaponsInventory_EquipNextWeapon_APEmptySlotPatch
    {
        static bool Prefix(WeaponsInventory __instance)
        {
            return CycleAPBikeWeapon(__instance, 1);
        }
    }

    [HarmonyPatch(typeof(WeaponsInventory), "EquipPreviousWeapon")]
    public class WeaponsInventory_EquipPreviousWeapon_APEmptySlotPatch
    {
        static bool Prefix(WeaponsInventory __instance)
        {
            return CycleAPBikeWeapon(__instance, -1);
        }
    }

    [HarmonyPatch(typeof(WeaponsInventory), "EquipWeaponAtIndex")]
    public class WeaponsInventory_EquipWeaponAtIndex_APSlotGuardPatch
    {
        static bool Prefix(WeaponsInventory __instance, int index)
        {
            if (SessionState == null || !SessionState.APEnabled)
                return true;

            var slots = __instance.GetBikeWeapons(true);
            return index >= 0 && index < slots.Count && slots[index] != null;
        }
    }

    internal static WeaponDynamicData ResolveAPEquippedArmWeapon(
        List<WeaponDynamicData> weapons, int slotIndex)
    {
        if (SessionState == null || !SessionState.APEnabled)
            return weapons[slotIndex];

        var slots = Singleton<WeaponsInventory>.Instance.GetBikeWeapons(true);
        if (slotIndex < 0 || slotIndex >= slots.Count || slots[slotIndex] == null)
            return null;

        // WeaponArm's list omits empty slots, whereas EquippedWeaponIdx indexes
        // the full bike layout. Match the selected weapon rather than indexing
        // the shorter list, which can equip the wrong gun or throw after a gap.
        WeaponData selected = slots[slotIndex].WeaponData;
        return weapons.Find(weapon => weapon != null && weapon.weapon == selected);
    }

    [HarmonyPatch]
    public class WeaponArm_APSlotMappingPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(WeaponArm), "Initialize");
            yield return AccessTools.Method(typeof(WeaponArm), "UpdateWeapons");
            yield return AccessTools.Method(typeof(WeaponArm), "SwitchWeapon");
        }

        static void Prefix(MethodBase __originalMethod)
        {
            // SwitchWeapon runs every frame. Inventory changes already pass
            // through UpdateWeapons, so it needs only the mapped selection.
            if (__originalMethod.Name != "SwitchWeapon")
                EnsureAPEquippedBikeSlot(Singleton<WeaponsInventory>.Instance);
        }

        static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo listGetter = AccessTools.Method(
                typeof(List<WeaponDynamicData>), "get_Item", new[] { typeof(int) });
            MethodInfo resolver = AccessTools.Method(
                typeof(LaikaMod), "ResolveAPEquippedArmWeapon");
            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) &&
                    Equals(instruction.operand, listGetter))
                {
                    // Mutate the existing instruction so branch labels and
                    // exception blocks remain attached to the same position.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = resolver;
                    replacements++;
                }

                yield return instruction;
            }

            if (replacements != 1)
            {
                LogWarning("AP WEAPONS: expected one slot lookup in WeaponArm." +
                    __originalMethod.Name + ", found " + replacements + ".");
            }
        }
    }
}
