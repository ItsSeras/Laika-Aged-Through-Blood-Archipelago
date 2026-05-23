from __future__ import annotations

from typing import Any, Callable

JSONMessagePart = dict[str, Any]
Requirement = tuple

# This file is intentionally data-driven and covers every current Laika AP check.
# It does not change generation logic. It only gives Universal Tracker's /explain
# command readable output for the lambda rules in Rules.py.

LOCATION_TO_REGION = {
    "Blueprint: Machine Gun": "Where Our Ancestors Rest",
    "Blueprint: Rocket Launcher": "Where Our Bikes Growl",
    "Blueprint: Shotgun": "Where We Live",
    "Blueprint: Sniper": "Where All Was Lost",
    "Boss Defeated: A Caterpillar Made of Sadness": "Where Rock Bleeds",
    "Boss Defeated: A Gargantuan Swimcrab": "Where Water Glistened",
    "Boss Defeated: A Hundred Hungry Beaks": "Start / Tutorial Area",
    "Boss Defeated: A Long Lost Woodcrawler": "Where Our Ancestors Rest",
    "Boss Defeated: Pope Melva VIII": "The Big Tree",
    "Boss Defeated: Two-Beak God": "Floating City",
    "Cassette Tape: Bloody Sunset": "Where Our Bikes Growl",
    "Cassette Tape: Blue Limbo": "Where Our Bikes Growl",
    "Cassette Tape: Coming Home": "Where the Waves Die",
    "Cassette Tape: Heartbeat from the Last Century": "Where Iron Caresses the Sky",
    "Cassette Tape: Heartglaze Hope": "Where Our Bikes Growl",
    "Cassette Tape: Lonely Mountain": "Where Iron Caresses the Sky",
    "Cassette Tape: Lullaby of the Dead": "Where Our Bikes Growl",
    "Cassette Tape: Mother": "Where Shaza Tinkers",
    "Cassette Tape: My Destiny": "Where Rules Are Made",
    "Cassette Tape: Overthinker": "Where Our Bikes Growl",
    "Cassette Tape: Playing in the Sun": "Where Our Bikes Growl",
    "Cassette Tape: Recurring Dream": "Where Doom Fell",
    "Cassette Tape: The End of the Road": "Where Rules Are Made",
    "Cassette Tape: The Final Hours": "Where Birds Came From",
    "Cassette Tape: The Hero": "Where All Was Lost",
    "Cassette Tape: The Last Tear": "Where Our Ancestors Rest",
    "Cassette Tape: The Whisper": "Where Our Bikes Growl",
    "Cassette Tape: Through the Wind": "Where Rust Weaves",
    "Cassette Tape: Trust Them": "Where Rules Are Made",
    "Cassette Tape: Visions of Red": "Where Doom Fell",
    "Key Item: 1st Key To The Pit": "Where Rock Bleeds",
    "Key Item: 2nd Key To The Pit": "Where Rock Bleeds",
    "Key Item: 3rd Key To The Pit": "Where Rock Bleeds",
    "Key Item: Banana Leaves": "Where Birds Came From",
    "Key Item: Birthday Invitation": "Where We Live",
    "Key Item: Bluelemon Berries": "Where Iron Caresses the Sky",
    "Key Item: Brand-New Notebook": "Where We Live",
    "Key Item: Camilla's Special Herbs": "Where Our Ancestors Rest",
    "Key Item: Carved Whale Tooth": "Where Water Glistened",
    "Key Item: Erhu Strings": "Where the Waves Die",
    "Key Item: Family Braid": "Where All Was Lost",
    "Key Item: Flute Cleaning Brush": "Where All Was Lost",
    "Key Item: Fogg's Drumstick": "Where Rust Weaves",
    "Key Item: Gallon of Gasoline": "Where Iron Caresses the Sky",
    "Key Item: Guitar Strings": "Where Our Bikes Growl",
    "Key Item: Gutsy Gus's Gushing Gunfights": "Where Our Bikes Growl",
    "Key Item: Heartglaze Flower": "Where Our Ancestors Rest",
    "Key Item: Hook Body": "Where Iron Caresses the Sky",
    "Key Item: Hook Head": "Where Rust Weaves",
    "Key Item: Iris": "Where We Live",
    "Key Item: Jakob's Ashes": "Where We Live",
    "Key Item: Jar Filled With Bugs": "Where Rust Weaves",
    "Key Item: Large Seed": "Where We Live",
    "Key Item: Lhey's Diary": "Where the Waves Die",
    "Key Item: Long Rope": "Where Water Glistened",
    "Key Item: Magical Book": "Where Doom Fell",
    "Key Item: Maya's Pendant": "Where All Was Lost",
    "Key Item: Moon Blossom": "Where Our Ancestors Rest",
    "Key Item: Mountainheart Card": "Where Rock Bleeds",
    "Key Item: Pads": "Where Doom Fell",
    "Key Item: Petey's Letter": "Where Birds Came From",
    "Key Item: Phalseria Sap": "Where Rust Weaves",
    "Key Item: Poochie's Ashes": "Where Our Ancestors Rest",
    "Key Item: Radio Transmitter": "Where Birds Lurk",
    "Key Item: Rainbow Pebble": "Where All Was Lost",
    "Key Item: Seashell": "Where the Waves Die",
    "Key Item: Sheet Music": "Where Iron Caresses the Sky",
    "Key Item: Thistle Stems": "Where Our Ancestors Rest",
    "Key Item: Ultra Fast Cough Syrup": "Where Iron Caresses the Sky",
    "Key Item: Vitamin-Coated Bones": "Where the Waves Die",
    "Map Piece: Floating City (City Facilities)": "Floating City",
    "Map Piece: Floating City (Control Area)": "Floating City",
    "Map Piece: Floating City (Factory)": "Floating City",
    "Map Piece: Floating City (Hangar)": "Floating City",
    "Map Piece: Floating City (Old Town)": "Floating City",
    "Map Piece: The Big Tree": "The Big Tree",
    "Map Piece: Where All Was Lost (Bottom)": "Where All Was Lost",
    "Map Piece: Where All Was Lost (Top)": "Where All Was Lost",
    "Map Piece: Where Birds Came From (Left/Bottom)": "Where Birds Came From",
    "Map Piece: Where Birds Came From (Right/Top)": "Where Birds Came From",
    "Map Piece: Where Birds Lurk (Left)": "Where Birds Lurk",
    "Map Piece: Where Birds Lurk (Right)": "Where Birds Lurk",
    "Map Piece: Where Doom Fell": "Where Doom Fell",
    "Map Piece: Where Iron Caresses the Sky (Bottom)": "Where Iron Caresses the Sky",
    "Map Piece: Where Iron Caresses the Sky (Top)": "Where Iron Caresses the Sky",
    "Map Piece: Where Our Ancestors Rest (Bottom)": "Where Our Ancestors Rest",
    "Map Piece: Where Our Ancestors Rest (Top)": "Where Our Ancestors Rest",
    "Map Piece: Where Our Bikes Growl": "Where Our Bikes Growl",
    "Map Piece: Where Rock Bleeds (Center)": "Where Rock Bleeds",
    "Map Piece: Where Rock Bleeds (Left)": "Where Rock Bleeds",
    "Map Piece: Where Rock Bleeds (Right)": "Where Rock Bleeds",
    "Map Piece: Where Rust Weaves (Center)": "Where Rust Weaves",
    "Map Piece: Where Rust Weaves (Left)": "Where Rust Weaves",
    "Map Piece: Where Rust Weaves (Right)": "Where Rust Weaves",
    "Map Piece: Where Water Glistened (1st Ship)": "Where Water Glistened",
    "Map Piece: Where Water Glistened (2nd Ship)": "Where Water Glistened",
    "Map Piece: Where Water Glistened (3rd Ship)": "Where Water Glistened",
    "Map Piece: Where Water Glistened (4th Ship)": "Where Water Glistened",
    "Map Piece: Where Water Glistened (Borders)": "Where Water Glistened",
    "Map Piece: Where the Waves Die (Left)": "Where the Waves Die",
    "Map Piece: Where the Waves Die (Right)": "Where the Waves Die",
    "Puppy Gift: Dreamcatcher": "Where Iron Caresses the Sky",
    "Puppy Gift: Great-Great-Grandma's Novella": "Where Doom Fell",
    "Puppy Gift: Handheld Console": "Where Our Ancestors Rest",
    "Puppy Gift: Tangerine Tree": "Where All Was Lost",
    "Puppy Gift: Toy Animal": "Where All Was Lost",
    "Puppy Gift: Toy Bike": "Where Our Ancestors Rest",
    "Puppy Gift: Ukulele": "Where We Live",
    "Quest Complete: A Break for Camilla": "Where We Forget",
    "Quest Complete: A Heart for Poochie": "Where We Dream",
    "Quest Complete: A Little Tomb Stone": "Where We Live",
    "Quest Complete: A New Sheriff in Town": "Where We Live",
    "Quest Complete: Ava": "Where We Dream",
    "Quest Complete: Bone Flour": "Where We Live",
    "Quest Complete: Childless": "Where They Keep Puppy",
    "Quest Complete: Clean Your Beak": "Where All Was Lost",
    "Quest Complete: Closure": "Where We Dream",
    "Quest Complete: Death on Demand": "Where Rust Weaves",
    "Quest Complete: Desperately in Need of Music": "Where Our Bikes Growl",
    "Quest Complete: Diplomacy": "Where Rules Are Made",
    "Quest Complete: Fade Out": "Where Birds Came From",
    "Quest Complete: Family Tree": "Where We Live",
    "Quest Complete: First Blood": "Where We Live",
    "Quest Complete: Floating": "Where Shaza Tinkers",
    "Quest Complete: Fogg's Only Wish": "Where Rust Weaves",
    "Quest Complete: For the Cash": "Where All Was Lost",
    "Quest Complete: From Mother to Daughter": "Where We Live",
    "Quest Complete: Hell High": "Floating City",
    "Quest Complete: High Spirits": "Where Rust Weaves",
    "Quest Complete: Just a little girl": "Where the Waves Die",
    "Quest Complete: Last Meal": "Where We Forget",
    "Quest Complete: Life of the Party": "Where We Live",
    "Quest Complete: Old Warfare": "Where Mother Groans",
    "Quest Complete: Oooo Ooo Oo O Ooo": "Where Our Ancestors Rest",
    "Quest Complete: Radio Silence": "Where Rules Are Made",
    "Quest Complete: Rage and Sorrow": "Start / Tutorial Area",
    "Quest Complete: Shake Off the Dead Leaves": "Where Our Ancestors Rest",
    "Quest Complete: Sober Up": "Where Iron Caresses the Sky",
    "Quest Complete: Stargazing": "Where We Live",
    "Quest Complete: Target Practice": "Where Mother Groans",
    "Quest Complete: The Big Tree": "Where Rules Are Made",
    "Quest Complete: The Bonehead's Hook": "Where Iron Caresses the Sky",
    "Quest Complete: The Last Erhu": "Where the Waves Die",
    "Quest Complete: The Prophecy": "Where We Live",
    "Quest Complete: The Remnants": "Where Our Ancestors Rest",
    "Quest Complete: Water Whispers": "Where We Live",
    "Quest Complete: We'll Never Know": "Where We Live",
    "Quest Complete: Where We Used to Live": "Where Mother Groans",
    "Quest Complete: Worse than Hives": "Where We Live",
    "Quest Complete: Worse than Nightmares": "Where We Live",
    "Quest Complete: Worse than Stomach Flu": "Where We Live",
    "Radio Silence: Destroy Antenna 1": "Where Water Glistened",
    "Radio Silence: Destroy Antenna 2": "Where Water Glistened",
    "Radio Silence: Destroy Antenna 3": "Where Water Glistened",
    "Radio Silence: Destroy Antenna 4": "Where Water Glistened",
    "The Big Tree: Collapse Floor A": "The Big Tree",
    "The Big Tree: Collapse Floor B": "The Big Tree",
    "The Big Tree: Collapse Floor C": "The Big Tree",
    "The Big Tree: Collapse Floor D": "The Big Tree",
    "Weapon Crafting Material: Magnifying Glass": "Where Doom Fell",
    "Weapon Crafting Material: Missile": "Where the Waves Die",
    "Weapon Crafting Material: Rusty Spring": "Where Rock Bleeds",
    "Weapon Crafting Material: Titanium Plates": "Where the Waves Die",
}

EXPLANATION_RULES = {
    "Blueprint: Machine Gun": [("loc", "Quest Complete: Rage and Sorrow")],
    "Blueprint: Rocket Launcher": [
        ("loc", "Quest Complete: Rage and Sorrow"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Blueprint: Shotgun": [("loc", "Quest Complete: Rage and Sorrow")],
    "Blueprint: Sniper": [("war_chapter",), ("shotgun",)],
    "Boss Defeated: A Caterpillar Made of Sadness": [("war_chapter",), ("pit_items",)],
    "Boss Defeated: A Gargantuan Swimcrab": [
        ("loc", "Quest Complete: Radio Silence"),
        ("shotgun",),
    ],
    "Boss Defeated: A Hundred Hungry Beaks": [],
    "Boss Defeated: A Long Lost Woodcrawler": [
        ("loc", "Quest Complete: Rage and Sorrow")
    ],
    "Boss Defeated: Pope Melva VIII": [("loc", "Quest Complete: The Big Tree")],
    "Boss Defeated: Two-Beak God": [
        ("loc", "Quest Complete: Floating"),
        ("item", "Key Item: Radio Transmitter"),
        ("item", "Bike Upgrade: Dash"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "Cassette Tape: Bloody Sunset": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Blue Limbo": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Coming Home": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Cassette Tape: Heartbeat from the Last Century": [("war_chapter",), ("shotgun",)],
    "Cassette Tape: Heartglaze Hope": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Lonely Mountain": [("war_chapter",)],
    "Cassette Tape: Lullaby of the Dead": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Mother": [("loc", "Quest Complete: Floating")],
    "Cassette Tape: My Destiny": [
        ("loc", "Quest Complete: Radio Silence"),
        ("shotgun",),
    ],
    "Cassette Tape: Overthinker": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Playing in the Sun": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Recurring Dream": [("loc", "Quest Complete: Rage and Sorrow"),
    ],
    "Cassette Tape: The End of the Road": [
        ("loc", "Quest Complete: The Big Tree"),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Cassette Tape: The Final Hours": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Cassette Tape: The Hero": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Cassette Tape: The Last Tear": [
        ("loc", "Boss Defeated: A Long Lost Woodcrawler"),
        ("shotgun",),
    ],
    "Cassette Tape: The Whisper": [("loc", "Quest Complete: Rage and Sorrow")],
    "Cassette Tape: Through the Wind": [("loc", "Quest Complete: A Heart for Poochie")],
    "Cassette Tape: Trust Them": [("loc", "Quest Complete: The Big Tree"),
    ],
    "Cassette Tape: Visions of Red": [("war_chapter",)],
    "Key Item: 1st Key To The Pit": [
        ("war_chapter",),
        (
            "or",
            "Bike Upgrade: Dash OR Key Item: Mountainheart Card",
            [("item", "Bike Upgrade: Dash"), ("item", "Key Item: Mountainheart Card")],
        ),
    ],
    "Key Item: 2nd Key To The Pit": [
        ("war_chapter",),
        ("item", "Key Item: Mountainheart Card"),
    ],
    "Key Item: 3rd Key To The Pit": [
        ("war_chapter",),
        ("item", "Key Item: Mountainheart Card"),
    ],
    "Key Item: Banana Leaves": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Birthday Invitation": [("loc", "Quest Complete: The Big Tree")],
    "Key Item: Bluelemon Berries": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Brand-New Notebook": [("war_chapter",)],
    "Key Item: Camilla's Special Herbs": [("loc", "Quest Complete: The Big Tree")],
    "Key Item: Carved Whale Tooth": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Key Item: Erhu Strings": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "Key Item: Family Braid": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Flute Cleaning Brush": [
        ("loc", "Quest Complete: Desperately in Need of Music"),
        ("shotgun",),
    ],
    "Key Item: Fogg's Drumstick": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: Desperately in Need of Music"),
    ],
    "Key Item: Gallon of Gasoline": [("loc", "Quest Complete: Childless")],
    "Key Item: Guitar Strings": [("loc", "Quest Complete: Rage and Sorrow")],
    "Key Item: Gutsy Gus's Gushing Gunfights": [("loc", "Quest Complete: Diplomacy")],
    "Key Item: Heartglaze Flower": [("loc", "Boss Defeated: A Long Lost Woodcrawler")],
    "Key Item: Hook Body": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Key Item: Hook Head": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Key Item: Iris": [("loc", "Quest Complete: Diplomacy")],
    "Key Item: Jakob's Ashes": [("loc", "Quest Complete: Rage and Sorrow")],
    "Key Item: Jar Filled With Bugs": [("loc", "Quest Complete: The Big Tree")],
    "Key Item: Large Seed": [("loc", "Quest Complete: Childless")],
    "Key Item: Lhey's Diary": [("loc", "Quest Complete: Childless")],
    "Key Item: Long Rope": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Key Item: Magical Book": [("loc", "Quest Complete: Childless")],
    "Key Item: Maya's Pendant": [("main_count", 3)],
    "Key Item: Moon Blossom": [("loc", "Quest Complete: The Big Tree")],
    "Key Item: Mountainheart Card": [("loc", "Quest Complete: A Heart for Poochie")],
    "Key Item: Pads": [("loc", "Quest Complete: The Big Tree")],
    "Key Item: Petey's Letter": [("loc", "Quest Complete: Diplomacy")],
    "Key Item: Phalseria Sap": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Poochie's Ashes": [("loc", "Quest Complete: Childless")],
    "Key Item: Radio Transmitter": [
        ("loc", "Quest Complete: Floating"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Key Item: Rainbow Pebble": [("loc", "Quest Complete: A Little Tomb Stone")],
    "Key Item: Seashell": [("loc", "Quest Complete: Childless")],
    "Key Item: Sheet Music": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Thistle Stems": [("loc", "Quest Complete: Radio Silence")],
    "Key Item: Ultra Fast Cough Syrup": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Key Item: Vitamin-Coated Bones": [("loc", "Quest Complete: The Big Tree")],
    "Map Piece: Floating City (City Facilities)": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Map Piece: Floating City (Control Area)": [("loc", "Quest Complete: Childless")],
    "Map Piece: Floating City (Factory)": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Map Piece: Floating City (Hangar)": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Map Piece: Floating City (Old Town)": [("loc", "Quest Complete: Childless")],
    "Map Piece: The Big Tree": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Map Piece: Where All Was Lost (Bottom)": [("war_chapter",)],
    "Map Piece: Where All Was Lost (Top)": [("war_chapter",)],
    "Map Piece: Where Birds Came From (Left/Bottom)": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Map Piece: Where Birds Came From (Right/Top)": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Map Piece: Where Birds Lurk (Left)": [
        ("loc", "Quest Complete: A Heart for Poochie")
    ],
    "Map Piece: Where Birds Lurk (Right)": [
        ("loc", "Quest Complete: A Heart for Poochie")
    ],
    "Map Piece: Where Doom Fell": [("war_chapter",)],
    "Map Piece: Where Iron Caresses the Sky (Bottom)": [("war_chapter",)],
    "Map Piece: Where Iron Caresses the Sky (Top)": [("war_chapter",), ("shotgun",)],
    "Map Piece: Where Our Ancestors Rest (Bottom)": [
        ("loc", "Quest Complete: Rage and Sorrow")
    ],
    "Map Piece: Where Our Ancestors Rest (Top)": [
        ("loc", "Quest Complete: Rage and Sorrow")
    ],
    "Map Piece: Where Our Bikes Growl": [("loc", "Quest Complete: Rage and Sorrow")],
    "Map Piece: Where Rock Bleeds (Center)": [("war_chapter",)],
    "Map Piece: Where Rock Bleeds (Left)": [("war_chapter",)],
    "Map Piece: Where Rock Bleeds (Right)": [
        ("war_chapter",),
        ("item", "Key Item: Mountainheart Card"),
    ],
    "Map Piece: Where Rust Weaves (Center)": [("war_chapter",)],
    "Map Piece: Where Rust Weaves (Left)": [("war_chapter",)],
    "Map Piece: Where Rust Weaves (Right)": [("war_chapter",)],
    "Map Piece: Where Water Glistened (1st Ship)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Map Piece: Where Water Glistened (2nd Ship)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Map Piece: Where Water Glistened (3rd Ship)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Map Piece: Where Water Glistened (4th Ship)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Map Piece: Where Water Glistened (Borders)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Map Piece: Where the Waves Die (Left)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Map Piece: Where the Waves Die (Right)": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Puppy Gift: Dreamcatcher": [("loc", "Quest Complete: Stargazing"),
    ],
    "Puppy Gift: Great-Great-Grandma's Novella": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Puppy Gift: Handheld Console": [("loc", "Quest Complete: Rage and Sorrow")],
    "Puppy Gift: Tangerine Tree": [("loc", "Quest Complete: A Heart for Poochie")],
    "Puppy Gift: Toy Animal": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Puppy Gift: Toy Bike": [("loc", "Quest Complete: Rage and Sorrow")],
    "Puppy Gift: Ukulele": [
        ("loc", "Quest Complete: Fogg's Only Wish"),
        ("loc", "Quest Complete: The Last Erhu"),
        ("loc", "Quest Complete: Clean Your Beak"),
        ("loc", "Quest Complete: Desperately in Need of Music"),
        ("loc", "Quest Complete: Sober Up"),
        ("loc", "Quest Complete: Oooo Ooo Oo O Ooo"),
    ],
    "Quest Complete: A Break for Camilla": [
        ("loc", "Quest Complete: The Big Tree"),
        ("shotgun",),
        ("item", "Key Item: Camilla's Special Herbs"),
    ],
    "Quest Complete: A Heart for Poochie": [
        ("loc", "Quest Complete: Rage and Sorrow"),
        ("item", "Key Item: Heartglaze Flower"),
    ],
    "Quest Complete: A Little Tomb Stone": [
        ("loc", "Quest Complete: Diplomacy"),
        ("item", "Key Item: Iris"),
    ],
    "Quest Complete: A New Sheriff in Town": [
        ("loc", "Quest Complete: Diplomacy"),
        ("item", "Key Item: Gutsy Gus's Gushing Gunfights"),
    ],
    "Quest Complete: Ava": [
        ("main_count", 3),
        ("loc", "Quest Complete: Where We Used to Live"),
        ("loc", "Quest Complete: Target Practice"),
    ],
    "Quest Complete: Bone Flour": [
        ("loc", "Quest Complete: The Big Tree"),
        ("shotgun",),
        ("item", "Key Item: Vitamin-Coated Bones"),
    ],
    "Quest Complete: Childless": [
        ("loc", "Quest Complete: Diplomacy"),
        ("loc", "Quest Complete: Radio Silence"),
        ("loc", "Quest Complete: The Big Tree"),
    ],
    "Quest Complete: Clean Your Beak": [
        ("loc", "Quest Complete: Desperately in Need of Music"),
        ("shotgun",),
        ("item", "Key Item: Flute Cleaning Brush"),
    ],
    "Quest Complete: Closure": [("loc", "Quest Complete: Childless")],
    "Quest Complete: Death on Demand": [("loc", "Quest Complete: Childless")],
    "Quest Complete: Desperately in Need of Music": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("item", "Key Item: Guitar Strings"),
    ],
    "Quest Complete: Diplomacy": [
        ("war_chapter",),
        ("pit_items",),
        ("loc", "Boss Defeated: A Caterpillar Made of Sadness"),
    ],
    "Quest Complete: Fade Out": [("loc", "Quest Complete: Childless")],
    "Quest Complete: Family Tree": [("loc", "Quest Complete: Childless")],
    "Quest Complete: First Blood": [
        ("loc", "Quest Complete: The Big Tree"),
        ("item", "Key Item: Pads"),
        ("item", "Key Item: Moon Blossom"),
    ],
    "Quest Complete: Floating": [
        ("loc", "Quest Complete: Hell High"),
        ("item", "Bike Upgrade: Dash"),
    ],
    "Quest Complete: Fogg's Only Wish": [
        ("loc", "Quest Complete: Desperately in Need of Music"),
        ("item", "Key Item: Fogg's Drumstick"),
    ],
    "Quest Complete: For the Cash": [("loc", "Quest Complete: Childless")],
    "Quest Complete: From Mother to Daughter": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Family Braid"),
    ],
    "Quest Complete: Hell High": [("loc", "Quest Complete: Childless")],
    "Quest Complete: High Spirits": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Key Item: Gallon of Gasoline"),
    ],
    "Quest Complete: Just a little girl": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Key Item: Lhey's Diary"),
    ],
    "Quest Complete: Last Meal": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Bluelemon Berries"),
    ],
    "Quest Complete: Life of the Party": [
        ("loc", "Quest Complete: The Big Tree"),
        ("shotgun",),
        ("item", "Key Item: Birthday Invitation"),
        ("item", "Key Item: Jar Filled With Bugs"),
    ],
    "Quest Complete: Old Warfare": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Quest Complete: Oooo Ooo Oo O Ooo": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("item", "Bike Upgrade: Dash"),
        ("item", "Key Item: Ultra Fast Cough Syrup"),
    ],
    "Quest Complete: Radio Silence": [("war_chapter",), ("shotgun",), ("radio_route",)],
    "Quest Complete: Rage and Sorrow": [
        ("loc", "Boss Defeated: A Hundred Hungry Beaks")
    ],
    "Quest Complete: Shake Off the Dead Leaves": [
        ("loc", "Quest Complete: Rage and Sorrow"),
        ("item", "Key Item: Jakob's Ashes"),
    ],
    "Quest Complete: Sober Up": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Sheet Music"),
    ],
    "Quest Complete: Stargazing": [
        ("war_chapter",),
        ("shotgun",),
        ("item", "Key Item: Brand-New Notebook"),
    ],
    "Quest Complete: Target Practice": [
        ("main_count", 2),
        ("loc", "Quest Complete: Where We Used to Live"),
    ],
    "Quest Complete: The Big Tree": [
        ("war_chapter",),
        ("shotgun",),
        ("loc", "Quest Complete: The Bonehead's Hook"),
    ],
    "Quest Complete: The Bonehead's Hook": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Quest Complete: The Last Erhu": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
        ("item", "Key Item: Erhu Strings"),
    ],
    "Quest Complete: The Prophecy": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Petey's Letter"),
    ],
    "Quest Complete: The Remnants": [
        ("loc", "Quest Complete: Rage and Sorrow"),
        ("item", "Key Item: Jakob's Ashes"),
    ],
    "Quest Complete: Water Whispers": [
        ("loc", "Quest Complete: Childless"),
        ("shotgun",),
        ("item", "Key Item: Seashell"),
    ],
    "Quest Complete: We'll Never Know": [
        ("loc", "Quest Complete: Childless"),
        ("item", "Key Item: Large Seed"),
    ],
    "Quest Complete: Where We Used to Live": [("main_count", 1)],
    "Quest Complete: Worse than Hives": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Phalseria Sap"),
    ],
    "Quest Complete: Worse than Nightmares": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Thistle Stems"),
    ],
    "Quest Complete: Worse than Stomach Flu": [
        ("loc", "Quest Complete: Radio Silence"),
        ("item", "Key Item: Banana Leaves"),
    ],
    "Radio Silence: Destroy Antenna 1": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Radio Silence: Destroy Antenna 2": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Radio Silence: Destroy Antenna 3": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "Radio Silence: Destroy Antenna 4": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
        ("radio_route",),
    ],
    "The Big Tree: Collapse Floor A": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "The Big Tree: Collapse Floor B": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "The Big Tree: Collapse Floor C": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "The Big Tree: Collapse Floor D": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("loc", "Quest Complete: The Bonehead's Hook"),
        ("item", "Bike Upgrade: Hook"),
        ("shotgun",),
    ],
    "Weapon Crafting Material: Magnifying Glass": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("item", "Bike Upgrade: Hook"),
    ],
    "Weapon Crafting Material: Missile": [
        ("item", "Bike Upgrade: Dash"),
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
    "Weapon Crafting Material: Rusty Spring": [("war_chapter",)],
    "Weapon Crafting Material: Titanium Plates": [
        ("loc", "Quest Complete: A Heart for Poochie"),
        ("shotgun",),
    ],
}


def text(message: str) -> JSONMessagePart:
    return {"type": "text", "text": message}


def color(message: str, color_name: str = "green") -> JSONMessagePart:
    return {"type": "color", "color": color_name, "text": message}


def has_item(state, player: int, item_name: str) -> bool:
    return state.has(item_name, player)


def can_reach_location(state, player: int, location_name: str) -> bool:
    try:
        return state.can_reach_location(location_name, player)
    except Exception:
        return False


def can_reach_region(state, player: int, region_name: str) -> bool:
    try:
        return state.can_reach_region(region_name, player)
    except Exception:
        try:
            return state.can_reach(region_name, "Region", player)
        except Exception:
            return False


def has_shotgun_access(state, player: int) -> bool:
    return has_item(state, player, "Shotgun (Weapon)") or (
        has_item(state, player, "Blueprint: Shotgun")
        and has_item(state, player, "Weapon Crafting Material: Rusty Spring")
    )


def has_radio_silence_route(state, player: int) -> bool:
    return has_item(state, player, "Bike Upgrade: Dash") or (
        has_item(state, player, "Key Item: Carved Whale Tooth")
        and has_item(state, player, "Key Item: Long Rope")
    )


def war_chapter_access(state, player: int) -> bool:
    return can_reach_location(
        state, player, "Quest Complete: Rage and Sorrow"
    ) and can_reach_location(state, player, "Quest Complete: A Heart for Poochie")


def completed_main_quest_count(state, player: int) -> int:
    count = 0
    for location_name in (
        "Quest Complete: Diplomacy",
        "Quest Complete: The Big Tree",
        "Quest Complete: Radio Silence",
    ):
        if can_reach_location(state, player, location_name):
            count += 1
    return count


def has_all_pit_access_items(state, player: int) -> bool:
    return (
        has_item(state, player, "Key Item: Mountainheart Card")
        and has_item(state, player, "Key Item: 1st Key To The Pit")
        and has_item(state, player, "Key Item: 2nd Key To The Pit")
        and has_item(state, player, "Key Item: 3rd Key To The Pit")
    )


def evaluate_requirement(requirement: Requirement, state, player: int) -> bool:
    kind = requirement[0]

    if kind == "item":
        return has_item(state, player, requirement[1])

    if kind == "loc":
        return can_reach_location(state, player, requirement[1])

    if kind == "shotgun":
        return has_shotgun_access(state, player)

    if kind == "war_chapter":
        return war_chapter_access(state, player)

    if kind == "radio_route":
        return has_radio_silence_route(state, player)

    if kind == "main_count":
        return completed_main_quest_count(state, player) >= requirement[1]

    if kind == "pit_items":
        return has_all_pit_access_items(state, player)

    if kind == "or":
        return any(
            evaluate_requirement(child, state, player) for child in requirement[2]
        )

    if kind == "all":
        return all(
            evaluate_requirement(child, state, player) for child in requirement[2]
        )

    return False


def requirement_label(requirement: Requirement) -> str:
    kind = requirement[0]

    if kind == "item":
        return requirement[1]

    if kind == "loc":
        return requirement[1]

    if kind == "shotgun":
        return "Shotgun access"

    if kind == "war_chapter":
        return "War chapter access: Rage and Sorrow + A Heart for Poochie"

    if kind == "radio_route":
        return "Radio Silence route: Dash OR Carved Whale Tooth + Long Rope"

    if kind == "main_count":
        needed = requirement[1]
        if needed == 1:
            return "At least one main quest complete: Diplomacy / Radio Silence / The Big Tree"
        if needed == 2:
            return "At least two main quests complete: Diplomacy / Radio Silence / The Big Tree"
        return (
            "All three main quests complete: Diplomacy + Radio Silence + The Big Tree"
        )

    if kind == "pit_items":
        return "Mountainheart Card + all three Keys To The Pit"

    if kind in ("or", "all"):
        return requirement[1]

    return str(requirement)


def status_line(label: str, is_met: bool) -> list[JSONMessagePart]:
    return [
        color(
            "[Have/Completed] " if is_met else "[Still Need] ",
            "green" if is_met else "red",
        ),
        text(label),
        text("\n"),
    ]


def explain_rule(world, target_name: str, state) -> list[JSONMessagePart] | None:
    player = world.player
    target_name = resolve_target_name(target_name)

    if target_name is None:
        return None

    parts: list[JSONMessagePart] = [
        color(target_name, "orange"),
        text("\n"),
    ]

    region_name = LOCATION_TO_REGION.get(target_name)
    if region_name:
        parts.append(text("Parent region:\n"))
        parts.extend(
            status_line(region_name, can_reach_region(state, player, region_name))
        )
        parts.append(text("\n"))

    requirements = EXPLANATION_RULES.get(target_name, [])

    if requirements:
        parts.append(text("Location-specific requirements:\n"))
        for requirement in requirements:
            parts.extend(
                status_line(
                    requirement_label(requirement),
                    evaluate_requirement(requirement, state, player),
                )
            )
    else:
        parts.append(text("Location-specific requirements:\n"))
        parts.extend(
            status_line(
                "No extra item/check requirement beyond reaching the parent region",
                True,
            )
        )

    return parts


def resolve_target_name(target_name: str) -> str | None:
    target_name = target_name.strip()

    if target_name in EXPLANATION_RULES:
        return target_name

    lowered = target_name.lower()
    matches = [name for name in EXPLANATION_RULES if name.lower() == lowered]
    if len(matches) == 1:
        return matches[0]

    matches = [name for name in EXPLANATION_RULES if lowered in name.lower()]
    if len(matches) == 1:
        return matches[0]

    return None
