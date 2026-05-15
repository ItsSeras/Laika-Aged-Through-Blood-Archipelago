import random
from typing import Any, ClassVar

from worlds.AutoWorld import World, WebWorld

from .Items import ITEM_TABLE, LaikaItem
from .Locations import LOCATION_TABLE
from .Options import LaikaOptions
from .Regions import create_regions
from .Rules import set_rules


class LaikaWebWorld(WebWorld):
    rich_text_options_doc = True

LAIKA_UT_MAP_ORDER = [
    "Start / Tutorial Area",
    "Where We Live",
    "Where Mother Groans",
    "Where We Dream",
    "Where Shaza Tinkers",
    "Where Rules Are Made",
    "Where We Forget",
    "Where Our Bikes Growl",
    "Where Our Ancestors Rest",
    "Where Doom Fell",
    "Where the Waves Die",
    "Where Rust Weaves",
    "Where Rock Bleeds",
    "Where Water Glistened",
    "Where All Was Lost",
    "Where Birds Came From",
    "The Big Tree",
    "Where Iron Caresses the Sky",
    "Where Birds Lurk",
    "Floating City",
]


def laika_ut_map_index(data: Any) -> int:
    """
    Universal Tracker passes the value stored in AP data storage here.

    Current C# writes:
      {"index": int, "region": str, "nonce": int}

    Possible incoming forms:
      - int
      - numeric string
      - region-name string
      - dict/JObject-like object with index
      - JSON string
      - SetReply-style wrapper with value/current/data/operations
    """
    import json

    def valid_index(value: Any) -> int | None:
        try:
            index = int(value)
        except (TypeError, ValueError):
            return None

        if 0 <= index < len(LAIKA_UT_MAP_ORDER):
            return index

        return None

    def parse_text(text: str) -> int | None:
        text = text.strip()

        if len(text) >= 2 and text[0] == '"' and text[-1] == '"':
            text = text[1:-1].strip()

        direct = valid_index(text)
        if direct is not None:
            return direct

        if text in LAIKA_UT_MAP_ORDER:
            return LAIKA_UT_MAP_ORDER.index(text)

        if (text.startswith("{") and text.endswith("}")) or (
            text.startswith("[") and text.endswith("]")
        ):
            try:
                return extract_index(json.loads(text))
            except Exception:
                return None

        return None

    def extract_index(value: Any) -> int | None:
        if value is None:
            return None

        direct = valid_index(value)
        if direct is not None:
            return direct

        if isinstance(value, str):
            return parse_text(value)

        if isinstance(value, dict):
            if "index" in value:
                found = valid_index(value.get("index"))
                if found is not None:
                    return found

            for key in ("value", "Value", "data", "Data", "current", "Current"):
                if key in value:
                    found = extract_index(value[key])
                    if found is not None:
                        return found

            for key in ("operations", "Operations"):
                if key in value:
                    found = extract_index(value[key])
                    if found is not None:
                        return found

            return None

        get_method = getattr(value, "get", None)
        if callable(get_method):
            try:
                found = valid_index(get_method("index", None))
                if found is not None:
                    return found
            except Exception:
                pass

            for key in ("value", "Value", "data", "Data", "current", "Current", "operations", "Operations"):
                try:
                    found = extract_index(get_method(key, None))
                    if found is not None:
                        return found
                except Exception:
                    pass

        try:
            found = extract_index(value["index"])
            if found is not None:
                return found
        except Exception:
            pass

        try:
            found = extract_index(value["value"])
            if found is not None:
                return found
        except Exception:
            pass

        try:
            found = extract_index(value["operations"])
            if found is not None:
                return found
        except Exception:
            pass

        if isinstance(value, (list, tuple)):
            for child in value:
                found = extract_index(child)
                if found is not None:
                    return found

        return parse_text(str(value))

    index = extract_index(data)

    print(f"[Laika UT DEBUG] map_page_index data={data!r}, type={type(data)}, resolved={index}")

    return index if index is not None else 0


class LaikaWorld(World):
    game = "Laika: Aged Through Blood"
    web = LaikaWebWorld()

    options_dataclass = LaikaOptions
    options: LaikaOptions

    item_name_to_id = {name: data["id"] for name, data in ITEM_TABLE.items()}
    location_name_to_id = LOCATION_TABLE

    tracker_world: ClassVar = {
        "map_page_folder": "tracker",
        "map_page_maps": "maps/maps.json",
        "map_page_locations": "locations/locations.json",

        # C# writes this whenever the player enters/loads a major region.
        "map_page_setting_key": "laika_current_region_{team}_{player}",
        "map_page_index": laika_ut_map_index,
    }

    def generate_early(self):
        if self.options.weapon_mode.current_key == "crafting":
            self.multiworld.local_early_items[self.player]["Weapon Crafting Material: Rusty Spring"] = 1
        else:
            self.multiworld.local_early_items[self.player]["Shotgun (Weapon)"] = 1

    def get_weapon_unlock_pool(self) -> list[str]:
        if self.options.weapon_mode.current_key == "crafting":
            return [
                "Blueprint: Shotgun",
                "Weapon Crafting Material: Rusty Spring",

                "Blueprint: Sniper",
                "Weapon Crafting Material: Magnifying Glass",

                "Blueprint: Machine Gun",
                "Weapon Crafting Material: Titanium Plates",

                "Blueprint: Rocket Launcher",
                "Weapon Crafting Material: Missile",

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
                "Ingredient: Beans",
                "Ingredient: Corn",
                "Ingredient: Worms",
                "Ingredient: Onion",
                "Ingredient: Chilly Pepper",
                "Ingredient: Ghost Pepper",
                "Ingredient: Lemon",
                "Ingredient: Garlic",
                "Ingredient: Meat",
                "Ingredient: Jackfruit",
                "Ingredient: Sardine",
                "Ingredient: Coco",
                "Ingredient: Coffee Beans",
                "Ingredient: Whiskey",
                "Ingredient: Tomato",
            ],
            weights=[
                10,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
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
            self.create_item("Bike Upgrade: Dash"),
            self.create_item("Bike Upgrade: Hook"),
            self.create_item("Bike Upgrade: Maya's Pendant"),
        ]

        progression_key_items = [
            "Key Item: Jakob's Ashes",
            "Key Item: Guitar Strings",
            "Key Item: Fogg's Drumstick",
            "Key Item: Gutsy Gus's Gushing Gunfights",
            "Key Item: Iris",
            "Key Item: Camilla's Special Herbs",
            "Key Item: Flute Cleaning Brush",
            "Key Item: Vitamin-Coated Bones",
            "Key Item: Erhu Strings",
            "Key Item: Sheet Music",
            "Key Item: Petey's Letter",
            "Key Item: Thistle Stems",
            "Key Item: Family Braid",
            "Key Item: Bluelemon Berries",
            "Key Item: Magical Book",
            "Key Item: Phalseria Sap",
            "Key Item: Jar Filled With Bugs",
            "Key Item: Seashell",
            "Key Item: Heartglaze Flower",
            "Key Item: 1st Key To The Pit",
            "Key Item: 2nd Key To The Pit",
            "Key Item: 3rd Key To The Pit",
            "Key Item: Brand-New Notebook",
            "Key Item: Pads",
            "Key Item: Moon Blossom",
            "Key Item: Lhey's Diary",
            "Key Item: Large Seed",
            "Key Item: Gallon of Gasoline",
            "Key Item: Banana Leaves",
            "Key Item: Ultra Fast Cough Syrup",
            "Key Item: Carved Whale Tooth",
            "Key Item: Long Rope",
            "Key Item: Mountainheart Card",
            "Key Item: Birthday Invitation",
            "Key Item: Rainbow Pebble",
            "Key Item: Poochie's Ashes",
        ]

        for item_name in progression_key_items:
            pool.append(self.create_item(item_name))

        for item_name in self.get_weapon_unlock_pool():
            pool.append(self.create_item(item_name))

        weapon_upgrades = [
            "Weapon Upgrade: Shotgun",
            "Weapon Upgrade: Shotgun",
            "Weapon Upgrade: Shotgun",
            "Weapon Upgrade: Sniper Rifle",
            "Weapon Upgrade: Machine Gun",
            "Weapon Upgrade: Rocket Launcher",
            "Weapon Upgrade: Crossbow",
        ]

        for item_name in weapon_upgrades:
            pool.append(self.create_item(item_name))

        unique_filler_items = [
            # Puppy gifts
            "Puppy Gift: Toy Bike",
            "Puppy Gift: Handheld Console",
            "Puppy Gift: Tangerine Tree",
            "Puppy Gift: Toy Animal",
            "Puppy Gift: Great-Great-Grandma's Novella",
            "Puppy Gift: Dreamcatcher",
            "Puppy Gift: Ukulele",

            # Cassettes
            "Cassette: Bloody Sunset",
            "Cassette: Playing in the Sun",
            "Cassette: Lullaby of the Dead",
            "Cassette: Blue Limbo",
            "Cassette: Trust Them",
            "Cassette: My Destiny",
            "Cassette: The End of the Road",
            "Cassette: The Whisper",
            "Cassette: Heartglaze Hope",
            "Cassette: The Hero",
            "Cassette: Visions of Red",
            "Cassette: Through the Wind",
            "Cassette: Heartbeat from the Last Century",
            "Cassette: Coming Home",
            "Cassette: Mother",
            "Cassette: The Last Tear",
            "Cassette: The Final Hours",
            "Cassette: Overthinker",
            "Cassette: Recurring Dream",
            "Cassette: Lonely Mountain",

            # Map unlocks
            "Map: Where Our Bikes Growl",
            "Map: Where All Was Lost (Bottom)",
            "Map: Where All Was Lost (Top)",
            "Map: Where Doom Fell",
            "Map: Where Rust Weaves (Left)",
            "Map: Where Rust Weaves (Center)",
            "Map: Where Rust Weaves (Right)",
            "Map: Where Iron Caresses the Sky (Bottom)",
            "Map: Where Iron Caresses the Sky (Top)",
            "Map: Where the Waves Die (Left)",
            "Map: Where the Waves Die (Right)",
            "Map: Where Our Ancestors Rest (Bottom)",
            "Map: Where Our Ancestors Rest (Top)",
            "Map: Where Birds Came From (Left/Bottom)",
            "Map: Where Birds Came From (Right/Top)",
            "Map: Where Birds Lurk (Left)",
            "Map: Where Birds Lurk (Right)",
            "Map: Where Rock Bleeds (Left)",
            "Map: Where Rock Bleeds (Center)",
            "Map: Where Rock Bleeds (Right)",
            "Map: Where Water Glistened (Borders)",
            "Map: Where Water Glistened (1st Ship)",
            "Map: Where Water Glistened (2nd Ship)",
            "Map: Where Water Glistened (3rd Ship)",
            "Map: Where Water Glistened (4th Ship)",
            "Map: The Big Tree",
            "Map: Floating City (Control Area)",
            "Map: Floating City (Old Town)",
            "Map: Floating City (Hangar)",
            "Map: Floating City (Factory)",
            "Map: Floating City (City Facilities)",
        ]

        for item_name in unique_filler_items:
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