from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, PerGameCommonOptions, Range, Toggle


class WeaponMode(Choice):
    """Choose how Archipelago sends major weapons.

    ``direct``
        Weapons are sent as complete usable weapons. This is the simpler mode.

    ``crafting``
        Most weapons are split into two Archipelago items: the weapon blueprint
        and that weapon's unique crafting material. You must receive both before
        the weapon can be crafted in-game.

    The Crossbow is always sent directly because it does not use the same
    blueprint/material crafting path as the other major weapons.
    """

    display_name = "Weapon Mode"
    option_direct = 0
    option_crafting = 1
    default = 0


class DeathLink(Toggle):
    """Choose whether this slot starts with DeathLink enabled.

    Set this to ``true`` if you want your deaths to be shared with other
    DeathLink players, and set it to ``false`` if you do not.

    The in-game Archipelago menu can still toggle DeathLink after connecting,
    but this YAML option controls the starting value for the seed.
    """

    display_name = "DeathLink"


class DeathAmnesty(Toggle):
    """Choose whether DeathLink should wait for multiple deaths before sending.

    Set this to ``true`` if you want Laika to have a grace counter before
    sending a DeathLink. Set it to ``false`` if every eligible death should send
    immediately while DeathLink is enabled.

    This option only matters when DeathLink is enabled.
    """

    display_name = "Death Amnesty"


class DeathAmnestyCount(Range):
    """Choose how many local deaths are required before sending a DeathLink.

    Lower numbers are harsher. For example, ``1`` means every eligible death
    sends immediately, while ``3`` means the third eligible death sends.

    This option only matters when both DeathLink and Death Amnesty are enabled.
    """

    display_name = "Death Amnesty Count"
    range_start = 1
    range_end = 99
    default = 5

class SkipJakobTransition(DefaultOnToggle):
    """Skip the long road transition following Jakob's death.

    Recommended enabled for repeat playthroughs.
    Disabling this restores the vanilla transition.
    """

    display_name = "Skip Jakob Transition"


class SkipOrellaTransition(DefaultOnToggle):
    """Skip the long road transition following the Orella encounter.

    Recommended enabled for repeat playthroughs.
    Disabling this restores the vanilla transition.
    """

    display_name = "Skip Orella Transition"


class SkipRoyBoat(DefaultOnToggle):
    """Enable both existing Roy boat skips.

    Skips the long outbound presentation and the later return dialogue.
    Disabling this restores both sequences.
    Softlock prevention remains enabled regardless of this setting.
    """

    display_name = "Skip Roy Boat Sequences"


class VisceraProtection(Choice):
    """Choose which deaths preserve carried viscera.

    off: Use vanilla money-sack behavior.
    deathlink_only: Protect against deaths caused by incoming DeathLink.
    all_deaths: Protect against every death, including without DeathLink.

    Previously dropped bags are unaffected.
    """

    display_name = "Viscera Protection"
    option_off = 0
    option_deathlink_only = 1
    option_all_deaths = 2
    default = 0

@dataclass
class LaikaOptions(PerGameCommonOptions):
    # These field names become the player-facing YAML keys.
    #
    # Example YAML:
    #   weapon_mode: direct        # valid values: direct, crafting
    #   death_link: false          # valid values: true, false
    #   death_amnesty: false       # valid values: true, false
    #   death_amnesty_count: 5     # valid values: 1 through 99
    #
    # The option classes above define each key's display name, valid values,
    # default value, and WebHost/YAML documentation.
    weapon_mode: WeaponMode
    death_link: DeathLink
    death_amnesty: DeathAmnesty
    death_amnesty_count: DeathAmnestyCount
    skip_jakob_transition: SkipJakobTransition
    skip_orella_transition: SkipOrellaTransition
    skip_roy_boat: SkipRoyBoat
    viscera_protection: VisceraProtection