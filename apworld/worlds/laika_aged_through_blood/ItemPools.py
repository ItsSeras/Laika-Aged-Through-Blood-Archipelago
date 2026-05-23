from __future__ import annotations


STARTING_PROGRESSION_ITEMS = [
    "Bike Upgrade: Dash",
    "Bike Upgrade: Hook",
    "Bike Upgrade: Maya's Pendant",
]


CRAFTING_WEAPON_UNLOCKS = [
    "Blueprint: Shotgun",
    "Weapon Crafting Material: Rusty Spring",

    "Blueprint: Sniper",
    "Weapon Crafting Material: Magnifying Glass",

    "Blueprint: Machine Gun",
    "Weapon Crafting Material: Titanium Plates",

    "Blueprint: Rocket Launcher",
    "Weapon Crafting Material: Missile",

    # Crossbow does not use the crafting-mode blueprint/material split.
    "Crossbow (Weapon)",
]


DIRECT_WEAPON_UNLOCKS = [
    "Shotgun (Weapon)",
    "Sniper Rifle (Weapon)",
    "Machine Gun (Weapon)",
    "Rocket Launcher (Weapon)",
    "Crossbow (Weapon)",
]


PROGRESSION_KEY_ITEMS = [
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
    "Key Item: Radio Transmitter",
]


WEAPON_UPGRADES = [
    "Weapon Upgrade: Shotgun",
    "Weapon Upgrade: Shotgun",
    "Weapon Upgrade: Shotgun",
    "Weapon Upgrade: Sniper Rifle",
    "Weapon Upgrade: Machine Gun",
    "Weapon Upgrade: Rocket Launcher",
    "Weapon Upgrade: Crossbow",
]


UNIQUE_FILLER_ITEMS = [
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


FILLER_ITEM_NAMES = [
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
    "Ingredient: Coconut Milk",
    "Ingredient: Coffee Beans",
    "Ingredient: Whiskey",
    "Ingredient: Tomato",
]


FILLER_ITEM_WEIGHTS = [
    10,
    8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
]


def get_weapon_unlock_pool(weapon_mode: str) -> list[str]:
    """Return the weapon unlock items used by the selected weapon mode."""
    if weapon_mode == "crafting":
        return list(CRAFTING_WEAPON_UNLOCKS)

    return list(DIRECT_WEAPON_UNLOCKS)


def get_filler_item_name(world) -> str:
    """Choose a weighted filler item using Archipelago's seeded world RNG."""
    return world.random.choices(
        population=FILLER_ITEM_NAMES,
        weights=FILLER_ITEM_WEIGHTS,
        k=1,
    )[0]


def create_item_pool(world) -> list:
    """
    Build the complete Laika item pool for one player.

    This keeps the World class focused on Archipelago lifecycle methods while
    leaving the static item-pool composition in one easy-to-audit place.
    """
    pool = []

    for item_name in STARTING_PROGRESSION_ITEMS:
        pool.append(world.create_item(item_name))

    for item_name in PROGRESSION_KEY_ITEMS:
        pool.append(world.create_item(item_name))

    for item_name in get_weapon_unlock_pool(world.options.weapon_mode.current_key):
        pool.append(world.create_item(item_name))

    for item_name in WEAPON_UPGRADES:
        pool.append(world.create_item(item_name))

    for item_name in UNIQUE_FILLER_ITEMS:
        pool.append(world.create_item(item_name))

    total_locations = len(world.multiworld.get_unfilled_locations(world.player))

    while len(pool) < total_locations:
        pool.append(world.create_item(get_filler_item_name(world)))

    return pool