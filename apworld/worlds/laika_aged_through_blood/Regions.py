from BaseClasses import Region

from .Locations import LOCATION_TABLE, LaikaLocation


REGION_LOCATION_TABLE = {
    "Start / Tutorial Area": [
        "Boss Defeated: A Hundred Hungry Beaks",
        "Quest Complete: Rage and Sorrow",
    ],

    "Where We Live": [
        "Blueprint: Shotgun",
        "Key Item: Jakob's Ashes",
        "Key Item: Brand-New Notebook",
        "Key Item: Birthday Invitation",
        "Key Item: Iris",
        "Key Item: Large Seed",
        "Puppy Gift: Ukulele",

        "Quest Complete: Water Whispers",
        "Quest Complete: Stargazing",
        "Quest Complete: Worse than Nightmares",
        "Quest Complete: Worse than Hives",
        "Quest Complete: Worse than Stomach Flu",
        "Quest Complete: The Prophecy",
        "Quest Complete: A New Sheriff in Town",
        "Quest Complete: We'll Never Know",
        "Quest Complete: Life of the Party",
        "Quest Complete: A Little Tomb Stone",
        "Quest Complete: From Mother to Daughter",
        "Quest Complete: Family Tree",
        "Quest Complete: First Blood",
        "Quest Complete: Bone Flour",
    ],

    "Where Mother Groans": [
        "Quest Complete: Old Warfare",
        "Quest Complete: Where We Used to Live",
        "Quest Complete: Target Practice",
    ],

    "Where We Dream": [
        "Quest Complete: A Heart for Poochie",
        "Quest Complete: Closure",
        "Quest Complete: Ava",
    ],

    "Where Shaza Tinkers": [
        "Quest Complete: Floating",
        "Cassette Tape: Mother",
    ],

    "Where Rules Are Made": [
        "Quest Complete: Diplomacy",
        "Quest Complete: The Big Tree",
        "Quest Complete: Radio Silence",

        "Cassette Tape: My Destiny",
        "Cassette Tape: The End of the Road",
        "Cassette Tape: Trust Them",
    ],

    "Where We Forget": [
        "Quest Complete: A Break for Camilla",
        "Quest Complete: Last Meal",
    ],

    "Where They Keep Puppy": [
        "Quest Complete: Childless",
    ],

    "Where Our Bikes Growl": [
        "Blueprint: Rocket Launcher",

        "Cassette Tape: Bloody Sunset",
        "Cassette Tape: Playing in the Sun",
        "Cassette Tape: Lullaby of the Dead",
        "Cassette Tape: Blue Limbo",
        "Cassette Tape: The Whisper",
        "Cassette Tape: Heartglaze Hope",
        "Cassette Tape: Overthinker",

        "Key Item: Guitar Strings",
        "Key Item: Gutsy Gus's Gushing Gunfights",
        "Map Piece: Where Our Bikes Growl",
        "Quest Complete: Desperately in Need of Music",
    ],

    "Where Our Ancestors Rest": [
        "Blueprint: Machine Gun",
        "Boss Defeated: A Long Lost Woodcrawler",
        "Cassette Tape: The Last Tear",

        "Key Item: Heartglaze Flower",
        "Key Item: Thistle Stems",
        "Key Item: Camilla's Special Herbs",
        "Key Item: Poochie's Ashes",
        "Key Item: Moon Blossom",

        "Map Piece: Where Our Ancestors Rest (Bottom)",
        "Map Piece: Where Our Ancestors Rest (Top)",

        "Puppy Gift: Toy Bike",
        "Puppy Gift: Handheld Console",

        "Quest Complete: The Remnants",
        "Quest Complete: Shake Off the Dead Leaves",
        "Quest Complete: Oooo Ooo Oo O Ooo",
    ],

    "Where All Was Lost": [
        "Blueprint: Sniper",
        "Cassette Tape: The Hero",

        "Key Item: Family Braid",
        "Key Item: Rainbow Pebble",
        "Key Item: Maya's Pendant",
        "Key Item: Flute Cleaning Brush",

        "Map Piece: Where All Was Lost (Bottom)",
        "Map Piece: Where All Was Lost (Top)",

        "Puppy Gift: Tangerine Tree",
        "Puppy Gift: Toy Animal",

        "Quest Complete: For the Cash",
        "Quest Complete: Clean Your Beak",
    ],

    "Where Doom Fell": [
        "Cassette Tape: Visions of Red",
        "Cassette Tape: Recurring Dream",

        "Map Piece: Where Doom Fell",
        "Weapon Crafting Material: Magnifying Glass",
        "Puppy Gift: Great-Great-Grandma's Novella",

        "Key Item: Magical Book",
        "Key Item: Pads",
    ],

    "Where the Waves Die": [
        "Cassette Tape: Coming Home",

        "Weapon Crafting Material: Missile",
        "Weapon Crafting Material: Titanium Plates",

        "Key Item: Lhey's Diary",
        "Key Item: Seashell",
        "Key Item: Erhu Strings",
        "Key Item: Vitamin-Coated Bones",

        "Map Piece: Where the Waves Die (Left)",
        "Map Piece: Where the Waves Die (Right)",

        "Quest Complete: The Last Erhu",
        "Quest Complete: Just a little girl"
    ],

    "Where Rust Weaves": [
        "Cassette Tape: Through the Wind",

        "Key Item: Hook Head",
        "Key Item: Jar Filled With Bugs",
        "Key Item: Fogg's Drumstick",
        "Key Item: Phalseria Sap",

        "Map Piece: Where Rust Weaves (Left)",
        "Map Piece: Where Rust Weaves (Center)",
        "Map Piece: Where Rust Weaves (Right)",

        "Quest Complete: Death on Demand",
        "Quest Complete: High Spirits",
        "Quest Complete: Fogg's Only Wish",
    ],

    "Where Rock Bleeds": [
        "Boss Defeated: A Caterpillar Made of Sadness",

        "Key Item: 1st Key To The Pit",
        "Key Item: 2nd Key To The Pit",
        "Key Item: 3rd Key To The Pit",
        "Key Item: Mountainheart Card",

        "Map Piece: Where Rock Bleeds (Left)",
        "Map Piece: Where Rock Bleeds (Center)",
        "Map Piece: Where Rock Bleeds (Right)",

        "Weapon Crafting Material: Rusty Spring",
    ],

    "Where Birds Came From": [
        "Cassette Tape: The Final Hours",

        "Key Item: Banana Leaves",
        "Key Item: Petey's Letter",

        "Map Piece: Where Birds Came From (Left/Bottom)",
        "Map Piece: Where Birds Came From (Right/Top)",

        "Quest Complete: Fade Out",
    ],

    "The Big Tree": [
        "Boss Defeated: Pope Melva VIII",
        "Map Piece: The Big Tree",

        "The Big Tree: Collapse Floor A",
        "The Big Tree: Collapse Floor B",
        "The Big Tree: Collapse Floor C",
        "The Big Tree: Collapse Floor D",
    ],

    "Where Water Glistened": [
        "Boss Defeated: A Gargantuan Swimcrab",

        "Key Item: Carved Whale Tooth",
        "Key Item: Long Rope",

        "Map Piece: Where Water Glistened (Borders)",
        "Map Piece: Where Water Glistened (1st Ship)",
        "Map Piece: Where Water Glistened (2nd Ship)",
        "Map Piece: Where Water Glistened (3rd Ship)",
        "Map Piece: Where Water Glistened (4th Ship)",

        "Radio Silence: Destroy Antenna 1",
        "Radio Silence: Destroy Antenna 2",
        "Radio Silence: Destroy Antenna 3",
        "Radio Silence: Destroy Antenna 4",
    ],

    "Where Iron Caresses the Sky": [
        "Cassette Tape: Heartbeat from the Last Century",
        "Cassette Tape: Lonely Mountain",

        "Key Item: Bluelemon Berries",
        "Key Item: Sheet Music",
        "Key Item: Hook Body",
        "Key Item: Ultra Fast Cough Syrup",
        "Key Item: Gallon of Gasoline",

        "Map Piece: Where Iron Caresses the Sky (Bottom)",
        "Map Piece: Where Iron Caresses the Sky (Top)",

        "Puppy Gift: Dreamcatcher",

        "Quest Complete: The Bonehead's Hook",
        "Quest Complete: Sober Up",
    ],

    "Where Birds Lurk": [
        "Map Piece: Where Birds Lurk (Right)",
        "Map Piece: Where Birds Lurk (Left)",
        "Key Item: Radio Transmitter",
    ],

    "Floating City": [
        "Boss Defeated: Two-Beak God",

        "Map Piece: Floating City (Control Area)",
        "Map Piece: Floating City (Old Town)",
        "Map Piece: Floating City (Hangar)",
        "Map Piece: Floating City (Factory)",
        "Map Piece: Floating City (City Facilities)",

        "Quest Complete: Hell High",
    ],
}

ONE_WAY_REGION_CONNECTIONS = [
    ("Menu", "Start / Tutorial Area"),

    # The tutorial is physically Where Birds Lurk-ish, but progression teleports
    # the player to Where We Live after Rage and Sorrow.
    ("Start / Tutorial Area", "Where We Live"),

    # Progression scene for Childless
    ("Floating City", "Where They Keep Puppy"),
    ("Where They Keep Puppy", "Where We Dream"),
]


TWO_WAY_REGION_CONNECTIONS = [
    # Early route after Rage and Sorrow
    ("Where We Live", "Where Our Bikes Growl"),
    ("Where Our Bikes Growl", "Where Our Ancestors Rest"),

    # Houses/Buildings inside of Where We Live
    ("Where We Live", "Where Mother Groans"),
    ("Where We Live", "Where We Dream"),
    ("Where We Live", "Where Shaza Tinkers"),
    ("Where We Live", "Where Rules Are Made"),
    ("Where We Live", "Where We Forget"),

    # Post A Heart for Poochie / war-chapter route web
    ("Where Our Bikes Growl", "Where Doom Fell"),
    ("Where Our Bikes Growl", "Where the Waves Die"),

    ("Where Our Ancestors Rest", "Where Iron Caresses the Sky"),
    ("Where Our Ancestors Rest", "Floating City"),

    ("Where the Waves Die", "Where Water Glistened"),
    ("Where the Waves Die", "Where Doom Fell"),

    ("Where Doom Fell", "Where Iron Caresses the Sky"),
    ("Where Doom Fell", "Where Rust Weaves"),
    ("Where Doom Fell", "Where All Was Lost"),

    ("Where Iron Caresses the Sky", "Where All Was Lost"),
    ("Where Iron Caresses the Sky", "Where Birds Came From"),

    ("Where Birds Came From", "The Big Tree"),

    ("Where All Was Lost", "Where Birds Lurk"),

    ("Where Rust Weaves", "Where All Was Lost"),
    ("Where Rust Weaves", "Where Rock Bleeds"),
]


def create_regions(world):
    region_lookup = {}

    # Create Menu first
    menu = Region("Menu", world.player, world.multiworld)
    world.multiworld.regions.append(menu)
    region_lookup["Menu"] = menu

    # Create every real region from REGION_LOCATION_TABLE
    for region_name in REGION_LOCATION_TABLE.keys():
        region = Region(region_name, world.player, world.multiworld)
        world.multiworld.regions.append(region)
        region_lookup[region_name] = region

    assigned_locations = set()

    # Populate region locations
    for region_name, location_names in REGION_LOCATION_TABLE.items():
        region = region_lookup[region_name]

        for location_name in location_names:
            if location_name not in LOCATION_TABLE:
                continue

            region.locations.append(
                LaikaLocation(
                    world.player,
                    location_name,
                    LOCATION_TABLE[location_name],
                    region,
                )
            )
            assigned_locations.add(location_name)

    # Create one-way story/teleport connections.
    for source_name, target_name in ONE_WAY_REGION_CONNECTIONS:
        source_region = region_lookup[source_name]
        target_region = region_lookup[target_name]

        source_region.connect(
            target_region,
            f"{source_name} -> {target_name}"
        )

    # Create normal two-way map connections.
    for source_name, target_name in TWO_WAY_REGION_CONNECTIONS:
        source_region = region_lookup[source_name]
        target_region = region_lookup[target_name]

        source_region.connect(
            target_region,
            f"{source_name} -> {target_name}"
        )
        target_region.connect(
            source_region,
            f"{target_name} -> {source_name}"
        )

    # Safety net for anything not yet region-assigned
    # Keep this for now while we are still refining the table.
    wasteland = Region("Wasteland", world.player, world.multiworld)
    world.multiworld.regions.append(wasteland)
    region_lookup["Wasteland"] = wasteland

    # Fallback region for any location we forgot to assign.
    # Keep it reachable so missing region assignments do not make locations impossible.
    region_lookup["Where We Live"].connect(
        wasteland,
        "Where We Live -> Wasteland"
    )

    for location_name, location_id in LOCATION_TABLE.items():
        if location_name in assigned_locations:
            continue

        wasteland.locations.append(
            LaikaLocation(
                world.player,
                location_name,
                location_id,
                wasteland,
            )
        )