from typing import ClassVar

from BaseClasses import ItemClassification
from Options import OptionError
from worlds.AutoWorld import World, WebWorld

from .Items import ITEM_TABLE, LaikaItem
from .ItemPools import create_item_pool, get_filler_item_name, validate_start_inventory
from .Locations import LOCATION_TABLE
from .LogicExplanations import explain_rule as explain_laika_rule
from .Options import (
    GOAL_CATEGORY_BOSSES,
    GOAL_CATEGORY_PUPPY_GIFTS,
    PUPPY_GIFT_ITEM_NAMES,
    LaikaOptions,
)
from .Regions import create_regions
from .Rules import set_rules
from .UniversalTracker import tracker_world as LAIKA_TRACKER_WORLD


class LaikaWebWorld(WebWorld):
    rich_text_options_doc = True


class LaikaWorld(World):
    game = "Laika: Aged Through Blood"
    web = LaikaWebWorld()

    options_dataclass = LaikaOptions
    options: LaikaOptions

    item_name_to_id = {name: data["id"] for name, data in ITEM_TABLE.items()}
    location_name_to_id = LOCATION_TABLE

    tracker_world: ClassVar = LAIKA_TRACKER_WORLD

    def generate_early(self):
        validate_start_inventory(self)
        enabled_goals = set(self.options.goals.value)

        if not enabled_goals:
            raise OptionError(
                f"Laika slot {self.player} must enable at least one victory goal."
            )

        goal_amount = int(self.options.goal_amount.value)
        if goal_amount > len(enabled_goals):
            raise OptionError(
                f"Laika slot {self.player} has goal_amount={goal_amount}, "
                f"but only {len(enabled_goals)} victory goal categor{'y is' if len(enabled_goals) == 1 else 'ies are'} enabled."
            )

        if GOAL_CATEGORY_BOSSES in enabled_goals:
            selected_bosses = set(self.options.bosses.value)

            if not selected_bosses:
                raise OptionError(
                    f"Laika slot {self.player} enabled the bosses goal but selected no bosses."
                )

            boss_goal_amount = int(self.options.boss_goal_amount.value)
            if boss_goal_amount > len(selected_bosses):
                raise OptionError(
                    f"Laika slot {self.player} has boss_goal_amount={boss_goal_amount}, "
                    f"but only {len(selected_bosses)} boss{' is' if len(selected_bosses) == 1 else 'es are'} selected."
                )

        if self.options.weapon_mode.current_key == "crafting":
            early_item = "Weapon Crafting Material: Rusty Spring"
        else:
            early_item = "Shotgun (Weapon)"

        # Do not require a pool copy of an item that Core will precollect/remove.
        if not self.options.start_inventory_from_pool.value.get(early_item, 0):
            self.multiworld.local_early_items[self.player][early_item] = 1

    def get_filler_item_name(self) -> str:
        return get_filler_item_name(self)

    def create_item(self, name: str):
        data = ITEM_TABLE[name]
        classification = data["classification"]

        # Puppy Gifts are normally harmless filler, but when the Puppy Gifts
        # victory category is enabled they become a real path to victory.
        # Mark all seven as progression so that enabled route is actually
        # supported by generation rather than being treated as disposable filler.
        if (
            GOAL_CATEGORY_PUPPY_GIFTS in self.options.goals.value
            and name in PUPPY_GIFT_ITEM_NAMES
        ):
            classification = ItemClassification.progression

        return LaikaItem(
            name,
            classification,
            data["id"],
            self.player,
        )

    def create_regions(self):
        create_regions(self)

    def create_items(self):
        self.multiworld.itempool += create_item_pool(self)

    def set_rules(self):
        set_rules(self)

    def explain_rule(self, target_name: str, state):
        return explain_laika_rule(self, target_name, state)

    def fill_slot_data(self) -> dict:
        return {
            "goals": sorted(self.options.goals.value),
            "goal_amount": int(self.options.goal_amount.value),
            "bosses": sorted(self.options.bosses.value),
            "boss_goal_amount": int(self.options.boss_goal_amount.value),
            "puppy_gift_goal_amount": int(self.options.puppy_gift_goal_amount.value),
            "wastelander_goal_amount": int(self.options.wastelander_goal_amount.value),
            "weapon_mode": self.options.weapon_mode.current_key,
            "death_link": bool(self.options.death_link.value),
            "death_amnesty": bool(self.options.death_amnesty.value),
            "death_amnesty_count": int(self.options.death_amnesty_count.value),
            "skip_jakob_transition": bool(
                self.options.skip_jakob_transition.value
            ),
            "skip_orella_transition": bool(
                self.options.skip_orella_transition.value
            ),
            "skip_roy_boat": bool(self.options.skip_roy_boat.value),
            "automatic_purchase_hints": bool(
                self.options.automatic_purchase_hints.value
            ),
            "show_goal_checklist": bool(
                self.options.show_goal_checklist.value
            ),
            "viscera_protection": self.options.viscera_protection.current_key,
        }
