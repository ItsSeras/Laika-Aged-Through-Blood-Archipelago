from dataclasses import dataclass

from Options import Choice, DefaultOnToggle, OptionSet, PerGameCommonOptions, Range, Toggle


GOAL_CATEGORY_BOSSES = "bosses"
GOAL_CATEGORY_PUPPY_GIFTS = "puppy_gifts"
GOAL_CATEGORY_WASTELANDERS = "wastelanders"

ALL_GOAL_CATEGORIES = frozenset({
    GOAL_CATEGORY_BOSSES,
    GOAL_CATEGORY_PUPPY_GIFTS,
    GOAL_CATEGORY_WASTELANDERS,
})

ALL_BOSS_NAMES = frozenset({
    "A Hundred Hungry Beaks",
    "A Long Lost Woodcrawler",
    "A Caterpillar Made of Sadness",
    "A Gargantuan Swimcrab",
    "Pope Melva VIII",
    "Two-Beak God",
})

PUPPY_GIFT_ITEM_NAMES = (
    "Puppy Gift: Toy Bike",
    "Puppy Gift: Handheld Console",
    "Puppy Gift: Tangerine Tree",
    "Puppy Gift: Toy Animal",
    "Puppy Gift: Great-Great-Grandma's Novella",
    "Puppy Gift: Dreamcatcher",
    "Puppy Gift: Ukulele",
)

WASTELANDER_QUEST_LOCATIONS = (
    "Quest Complete: Fogg's Only Wish",
    "Quest Complete: The Last Erhu",
    "Quest Complete: Clean Your Beak",
    "Quest Complete: Desperately in Need of Music",
    "Quest Complete: Sober Up",
    "Quest Complete: Oooo Ooo Oo O Ooo",
)


class Goals(OptionSet):
    """Choose which victory categories are enabled for this seed.

    ``bosses``
        Defeat enough of the bosses listed in the ``bosses`` option.

    ``puppy_gifts``
        Receive enough distinct Puppy Gift items from Archipelago.

    ``wastelanders``
        Complete enough of the six Wastelander band side quests.

    ``goal_amount`` controls how many enabled categories must be completed.
    """

    display_name = "Victory Goals"
    valid_keys = ALL_GOAL_CATEGORIES
    default = ALL_GOAL_CATEGORIES


class GoalAmount(Range):
    """How many enabled victory categories are required to win."""

    display_name = "Goal Amount"
    range_start = 1
    range_end = 3
    default = 1


class Bosses(OptionSet):
    """Choose which bosses are allowed to count toward the Bosses goal."""

    display_name = "Bosses"
    valid_keys = ALL_BOSS_NAMES
    default = frozenset({"Two-Beak God"})


class BossGoalAmount(Range):
    """How many selected bosses must be defeated to complete the Bosses goal."""

    display_name = "Boss Goal Amount"
    range_start = 1
    range_end = 6
    default = 1


class PuppyGiftGoalAmount(Range):
    """How many distinct Puppy Gift AP items are required for the Puppy Gifts goal."""

    display_name = "Puppy Gift Goal Amount"
    range_start = 1
    range_end = len(PUPPY_GIFT_ITEM_NAMES)
    default = len(PUPPY_GIFT_ITEM_NAMES)


class WastelanderGoalAmount(Range):
    """How many Wastelander band side quests are required for that goal."""

    display_name = "Wastelander Goal Amount"
    range_start = 1
    range_end = len(WASTELANDER_QUEST_LOCATIONS)
    default = len(WASTELANDER_QUEST_LOCATIONS)


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


class AutomaticPurchaseHints(Toggle):
    """Automatically create Archipelago hints for AP-aware purchase previews.

    When enabled, opening a randomized purchase preview creates and announces
    a hint for that location if the hint does not already exist.

    Previewing an offer never purchases it or sends its location check.
    """

    display_name = "Automatic Purchase Hints"
    default = 0


class ShowGoalChecklist(DefaultOnToggle):
    """Show the live victory-goal checklist beneath the AP connection status.

    The checklist tracks enabled goal categories, remaining required categories,
    selected bosses, Puppy Gifts, and Wastelander band quests.
    """

    display_name = "Show Goal Checklist"


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
    goals: Goals
    goal_amount: GoalAmount
    bosses: Bosses
    boss_goal_amount: BossGoalAmount
    puppy_gift_goal_amount: PuppyGiftGoalAmount
    wastelander_goal_amount: WastelanderGoalAmount
    weapon_mode: WeaponMode
    death_link: DeathLink
    death_amnesty: DeathAmnesty
    death_amnesty_count: DeathAmnestyCount
    skip_jakob_transition: SkipJakobTransition
    skip_orella_transition: SkipOrellaTransition
    skip_roy_boat: SkipRoyBoat
    automatic_purchase_hints: AutomaticPurchaseHints
    show_goal_checklist: ShowGoalChecklist
    viscera_protection: VisceraProtection