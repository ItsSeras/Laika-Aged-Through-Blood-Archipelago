from dataclasses import dataclass
from Options import Choice, Toggle, Range, PerGameCommonOptions


class WeaponMode(Choice):
    """Controls how major weapons are granted."""
    display_name = "Weapon Mode"
    option_direct = 0
    option_crafting = 1
    default = 0


class DeathLink(Toggle):
    """Enables DeathLink behavior."""
    display_name = "DeathLink"


class DeathAmnesty(Toggle):
    """Enables death amnesty so multiple deaths are required before a DeathLink sends."""
    display_name = "Death Amnesty"


class DeathAmnestyCount(Range):
    """Number of deaths required before a DeathLink sends when Death Amnesty is enabled."""
    display_name = "Death Amnesty Count"
    range_start = 1
    range_end = 10
    default = 5


@dataclass
class LaikaOptions(PerGameCommonOptions):
    weapon_mode: WeaponMode
    death_link: DeathLink
    death_amnesty: DeathAmnesty
    death_amnesty_count: DeathAmnestyCount