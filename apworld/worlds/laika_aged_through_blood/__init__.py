from typing import ClassVar

from worlds.AutoWorld import World, WebWorld

from .Items import ITEM_TABLE, LaikaItem
from .ItemPools import create_item_pool
from .Locations import LOCATION_TABLE
from .LogicExplanations import explain_rule as explain_laika_rule
from .Options import LaikaOptions
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
        if self.options.weapon_mode.current_key == "crafting":
            self.multiworld.local_early_items[self.player]["Weapon Crafting Material: Rusty Spring"] = 1
        else:
            self.multiworld.local_early_items[self.player]["Shotgun (Weapon)"] = 1

    def create_item(self, name: str):
        data = ITEM_TABLE[name]
        return LaikaItem(
            name,
            data["classification"],
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
            "weapon_mode": self.options.weapon_mode.current_key,
            "death_link": bool(self.options.death_link.value),
            "death_amnesty": bool(self.options.death_amnesty.value),
            "death_amnesty_count": int(self.options.death_amnesty_count.value),
        }