import random

from worlds.AutoWorld import World, WebWorld

from .Items import ITEM_TABLE, LaikaItem
from .Locations import LOCATION_TABLE
from .Options import LaikaOptions
from .Regions import create_regions
from .Rules import set_rules


class LaikaWebWorld(WebWorld):
    rich_text_options_doc = True


class LaikaWorld(World):
    game = "Laika: Aged Through Blood"
    web = LaikaWebWorld()

    options_dataclass = LaikaOptions
    options: LaikaOptions

    item_name_to_id = {name: data["id"] for name, data in ITEM_TABLE.items()}
    location_name_to_id = LOCATION_TABLE

    def generate_early(self):
        if self.options.weapon_mode.current_key == "crafting":
            self.multiworld.local_early_items[self.player]["Rusty Spring (Shotgun Material)"] = 1
        else:
            self.multiworld.local_early_items[self.player]["Shotgun (Weapon)"] = 1

    def get_weapon_unlock_pool(self) -> list[str]:
        if self.options.weapon_mode.current_key == "crafting":
            return [
                "Rusty Spring (Shotgun Material)",
                "Magnifying Glass (Sniper Rifle Material)",
                "Titanium Plates (Machine Gun Material)",
                "Missile (Rocket Launcher Material)",
                "Crossbow (Weapon)",
            ]

        return [
            "Shotgun (Weapon)",
            "Sniper Rifle (Weapon)",
            "Machine Gun (Weapon)",
            "Rocket Launcher (Weapon)",
            "Crossbow (Weapon)",
        ]

    def get_filler_item_name(self) -> str:
        return random.choices(
            population=[
                "Viscera x100",
                "Beans", "Corn", "Worms", "Onion", "Chilly Pepper", "Ghost Pepper",
                "Lemon", "Garlic", "Meat", "Jackfruit", "Sardine", "Coco",
                "Coffee Beans", "Whiskey", "Tomato",
                "Toy Bike", "Handheld Console", "Tangerine Tree", "Toy Animal",
                "Great-Great-Grandma's Novella", "Dreamcatcher", "Ukulele",
                "Cassette: Bloody Sunset", "Cassette: Playing in the Sun",
                "Cassette: Lullaby of the Dead", "Cassette: Blue Limbo",
                "Cassette: Trust Them", "Cassette: My Destiny",
                "Cassette: The End of the Road", "Cassette: The Whisper",
                "Cassette: Heartglaze Hope", "Cassette: The Hero",
                "Cassette: Visions of Red", "Cassette: Through the Wind",
                "Cassette: Heartbeat from the Last Century", "Cassette: Coming Home",
                "Cassette: Mother", "Cassette: The Last Tear",
                "Cassette: The Final Hours", "Cassette: Overthinker",
                "Cassette: Recurring Dream", "Cassette: Lonely Mountain",
                "Map: Where Our Bikes Growl", "Map: Where All Was Lost (Bottom)",
                "Map: Where All Was Lost (Top)", "Map: Where Doom Fell",
                "Map: Where Rust Weaves (Left)", "Map: Where Rust Weaves (Center)",
                "Map: Where Rust Weaves (Right)",
                "Map: Where Iron Caresses the Sky (Bottom)",
                "Map: Where Iron Caresses the Sky (Top)",
                "Map: Where the Waves Die (Left)", "Map: Where the Waves Die (Right)",
                "Map: Where Our Ancestors Rest (Bottom)",
                "Map: Where Our Ancestors Rest (Top)",
                "Map: Where Birds Came From (Left/Bottom)",
                "Map: Where Birds Came From (Right/Top)",
                "Map: Where Birds Lurk (Left)", "Map: Where Birds Lurk (Right)",
                "Map: Where Rock Bleeds (Left)", "Map: Where Rock Bleeds (Center)",
                "Map: Where Rock Bleeds (Right)", "Map: Where Water Glistened (Borders)",
                "Map: Where Water Glistened (1st Ship)",
                "Map: Where Water Glistened (2nd Ship)",
                "Map: Where Water Glistened (3rd Ship)",
                "Map: Where Water Glistened (4th Ship)",
                "Map: The Big Tree", "Map: Floating City (Control Area)",
                "Map: Floating City (Old Town)", "Map: Floating City (Hangar)",
                "Map: Floating City (Factory)", "Map: Floating City (City Facilities)",
            ],
            weights=[
                10,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
                4, 4, 4, 4, 4, 4, 4,
                3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
                2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
                2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            ],
            k=1
        )[0]

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
        pool = [
            self.create_item("Dash (Bike Upgrade)"),
            self.create_item("Hook (Bike Upgrade)"),
            self.create_item("Maya's Pendant (Bike Upgrade)"),
        ]

        progression_key_items = [
            "Blueprint: Shotgun",
            "Jakob's Ashes",
        ]

        for item_name in progression_key_items:
            pool.append(self.create_item(item_name))

        useful_blueprints = [
            "Blueprint: Sniper",
            "Blueprint: Machine Gun",
            "Blueprint: Rocket Launcher",
        ]

        for item_name in useful_blueprints:
            pool.append(self.create_item(item_name))

        for item_name in self.get_weapon_unlock_pool():
            pool.append(self.create_item(item_name))

        weapon_upgrades = [
            "Shotgun Upgrade (Weapon Upgrade)",
            "Shotgun Upgrade (Weapon Upgrade)",
            "Shotgun Upgrade (Weapon Upgrade)",
            "Sniper Rifle Upgrade (Weapon Upgrade)",
            "Machine Gun Upgrade (Weapon Upgrade)",
            "Rocket Launcher Upgrade (Weapon Upgrade)",
            "Crossbow Upgrade (Weapon Upgrade)",
        ]

        for item_name in weapon_upgrades:
            pool.append(self.create_item(item_name))

        total_locations = len(self.multiworld.get_unfilled_locations(self.player))

        while len(pool) < total_locations:
            pool.append(self.create_item(self.get_filler_item_name()))

        self.multiworld.itempool += pool

    def set_rules(self):
        set_rules(self)

    def fill_slot_data(self) -> dict:
        return {
            "weapon_mode": self.options.weapon_mode.current_key,
            "death_link": bool(self.options.death_link.value),
            "death_amnesty": bool(self.options.death_amnesty.value),
            "death_amnesty_count": int(self.options.death_amnesty_count.value),
        }