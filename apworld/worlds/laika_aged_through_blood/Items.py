# worlds/laika/Items.py

from BaseClasses import Item, ItemClassification

# I am keeping this version intentionally conservative.
# The first goal is to make a stable playable APWorld, not to perfectly model every shop item and collectible yet.
# If an item is in this table, the client should already know how to grant it safely.

class LaikaItem(Item):
    game = "Laika: Aged Through Blood"


ITEM_TABLE = {
    # ===== Core progression Key items =====
    "Dash (Bike Upgrade)": {
        "id": 1000,
        "classification": ItemClassification.progression,
        "internal_id": "I_DASH",
        "kind": "KeyItem",
    },
    "Hook (Bike Upgrade)": {
        "id": 1001,
        "classification": ItemClassification.progression,
        "internal_id": "I_E_HOOK",
        "kind": "KeyItem",
    },
    # ===== Useful Key items =====
    "Maya's Pendant (Bike Upgrade)": {
        "id": 1002,
        "classification": ItemClassification.useful,
        "internal_id": "I_MAYA_PENDANT",
        "kind": "KeyItem",
    },
    # ===== Core progression Key items =====
    "Shotgun (Weapon)": {
        "id": 1100,
        "classification": ItemClassification.progression,
        "internal_id": "I_W_SHOTGUN",
        "kind": "Weapon",
    },
    # ===== Useful Weapon items =====
    "Sniper Rifle (Weapon)": {
        "id": 1101,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_SNIPER",
        "kind": "Weapon",
    },
    "Machine Gun (Weapon)": {
        "id": 1102,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_UZI",
        "kind": "Weapon",
    },
    "Rocket Launcher (Weapon)": {
        "id": 1103,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_ROCKETLAUNCHER",
        "kind": "Weapon",
    },
    "Crossbow (Weapon)": {
        "id": 1104,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_CROSSBOW",
        "kind": "Weapon",
    },

    # ===== Weapon Upgrades =====
    "Shotgun Upgrade (Weapon Upgrade)": {
        "id": 1110,
        "classification": ItemClassification.progression,
        "internal_id": "I_W_SHOTGUN",
        "kind": "WeaponUpgrade",
    },
    "Sniper Rifle Upgrade (Weapon Upgrade)": {
        "id": 1111,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_SNIPER",
        "kind": "WeaponUpgrade",
    },
    "Machine Gun Upgrade (Weapon Upgrade)": {
        "id": 1112,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_UZI",
        "kind": "WeaponUpgrade",
    },
    "Rocket Launcher Upgrade (Weapon Upgrade)": {
        "id": 1113,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_ROCKETLAUNCHER",
        "kind": "WeaponUpgrade",
    },
    "Crossbow Upgrade (Weapon Upgrade)": {
        "id": 1114,
        "classification": ItemClassification.useful,
        "internal_id": "I_W_CROSSBOW",
        "kind": "WeaponUpgrade",
    },    

    # ===== Weapon recipes / blueprints =====
    "Blueprint: Shotgun": {
        "id": 1160,
        "classification": ItemClassification.progression,
        "internal_id": "I_RECIPE_SHOTGUN",
        "kind": "KeyItem",
    },
    "Blueprint: Sniper": {
        "id": 1161,
        "classification": ItemClassification.useful,
        "internal_id": "I_RECIPE_SNIPER",
        "kind": "KeyItem",
    },
    "Blueprint: Machine Gun": {
        "id": 1162,
        "classification": ItemClassification.useful,
        "internal_id": "I_RECIPE_UZI",
        "kind": "KeyItem",
    },
    "Blueprint: Rocket Launcher": {
        "id": 1163,
        "classification": ItemClassification.useful,
        "internal_id": "I_RECIPE_ROCKETLAUNCHER",
        "kind": "KeyItem",
    },

    # ===== Special key/progression items =====
    "Jakob's Ashes": {
        "id": 1165,
        "classification": ItemClassification.progression,
        "internal_id": "I_JAKOB_ASHES",
        "kind": "KeyItem",
    },
    "Guitar Strings": {
        "id": 1166,
        "classification": ItemClassification.progression,
        "internal_id": "I_GUITAR_STRINGS",
        "kind": "KeyItem",
    },
    "Fogg's Drumstick": {
        "id": 1167,
        "classification": ItemClassification.progression,
        "internal_id": "I_DRUMSTICKS",
        "kind": "KeyItem",
    },
    "Gutsy Gus's Gushing Gunfights": {
        "id": 1168,
        "classification": ItemClassification.progression,
        "internal_id": "I_ALFREDO_COMIC",
        "kind": "KeyItem",
    },
    "Iris": {
        "id": 1169,
        "classification": ItemClassification.progression,
        "internal_id": "I_TOMB_FLOWER",
        "kind": "KeyItem",
    },
    "Camilla's Special Herbs": {
        "id": 1170,
        "classification": ItemClassification.progression,
        "internal_id": "I_CAMILLA_HERBS",
        "kind": "KeyItem",
    },
    "Flute Cleaning Brush": {
        "id": 1171,
        "classification": ItemClassification.progression,
        "internal_id": "I_FLUTE_REED",
        "kind": "KeyItem",
    },
    "Vitamin-Coated Bones": {
        "id": 1172,
        "classification": ItemClassification.progression,
        "internal_id": "I_SPECIAL_BONES",
        "kind": "KeyItem",
    },
    "Erhu Strings": {
        "id": 1173,
        "classification": ItemClassification.progression,
        "internal_id": "I_ERHU_STRINGS",
        "kind": "KeyItem",
    },
    "Sheet Music": {
        "id": 1174,
        "classification": ItemClassification.progression,
        "internal_id": "I_PARTITURES",
        "kind": "KeyItem",
    },
    "Petey's Letter": {
        "id": 1175,
        "classification": ItemClassification.progression,
        "internal_id": "I_PETEY_PROPHECY",
        "kind": "KeyItem",
    },
    "Thistle Stems": {
        "id": 1176,
        "classification": ItemClassification.progression,
        "internal_id": "I_ANTI_NIGHTMARE",
        "kind": "KeyItem",
    },
    "Family Braid": {
        "id": 1177,
        "classification": ItemClassification.progression,
        "internal_id": "I_HILDA_BRAID",
        "kind": "KeyItem",
    },
    "Bluelemon Berries": {
        "id": 1178,
        "classification": ItemClassification.progression,
        "internal_id": "I_SUICIDE_BERRIES",
        "kind": "KeyItem",
    },
    "Magical Book": {
        "id": 1179,
        "classification": ItemClassification.progression,
        "internal_id": "I_DICTIONARY",
        "kind": "KeyItem",
    },
    "Phalseria Sap": {
        "id": 1180,
        "classification": ItemClassification.progression,
        "internal_id": "I_ANTI_URTICARIA",
        "kind": "KeyItem",
    },
    "Jar Filled With Bugs": {
        "id": 1181,
        "classification": ItemClassification.progression,
        "internal_id": "I_BUGS_JAR",
        "kind": "KeyItem",
    },
    "Seashell": {
        "id": 1182,
        "classification": ItemClassification.progression,
        "internal_id": "I_HILDA_SEASHELL",
        "kind": "KeyItem",
    },
    "Heartglaze Flower": {
        "id": 1183,
        "classification": ItemClassification.progression,
        "internal_id": "I_PUPPY_FLOWER",
        "kind": "KeyItem",
    },
    "1st Key To The Pit": {
        "id": 1184,
        "classification": ItemClassification.progression,
        "internal_id": "I_D_Dungeon_01_door_piece_1",
        "kind": "KeyItem",
    },
    "2nd Key To The Pit": {
        "id": 1185,
        "classification": ItemClassification.progression,
        "internal_id": "I_D_Dungeon_01_door_piece_2",
        "kind": "KeyItem",
    },
    "3rd Key To The Pit": {
        "id": 1186,
        "classification": ItemClassification.progression,
        "internal_id": "I_D_Dungeon_01_door_piece_3",
        "kind": "KeyItem",
    },
     "Brand-New Notebook": {
        "id": 1187,
        "classification": ItemClassification.progression,
        "internal_id": "I_DALIA_NOTEBOOK",
        "kind": "KeyItem",
    },   
     "Pads": {
        "id": 1188,
        "classification": ItemClassification.progression,
        "internal_id": "I_NAPKINS",
        "kind": "KeyItem",
    },   
     "Moon Blossom": {
        "id": 1189,
        "classification": ItemClassification.progression,
        "internal_id": "I_PERIOD_MUSHROOM",
        "kind": "KeyItem",
    },   
     "Lhey's Diary": {
        "id": 1190,
        "classification": ItemClassification.progression,
        "internal_id": "I_GIRL_DIARY",
        "kind": "KeyItem",
    },   
     "Large Seed": {
        "id": 1191,
        "classification": ItemClassification.progression,
        "internal_id": "I_LARGE_SEED",
        "kind": "KeyItem",
    },   
     "Gallon of Gasoline": {
        "id": 1192,
        "classification": ItemClassification.progression,
        "internal_id": "I_GASOLINE",
        "kind": "KeyItem",
    },   
     "Banana Leaves": {
        "id": 1193,
        "classification": ItemClassification.progression,
        "internal_id": "I_ANTI_DIARRHOEA",
        "kind": "KeyItem",
    },   
     "Ultra Fast Cough Syrup": {
        "id": 1194,
        "classification": ItemClassification.progression,
        "internal_id": "I_COUGHING_SYRUP",
        "kind": "KeyItem",
    },   
    # ===== Crafting-mode weapon material items =====
    "Rusty Spring (Shotgun Material)": {
        "id": 1150,
        "classification": ItemClassification.progression,
        "internal_id": "I_MATERIAL_SHOTGUN",
        "kind": "Material",
    },
    "Magnifying Glass (Sniper Rifle Material)": {
        "id": 1151,
        "classification": ItemClassification.useful,
        "internal_id": "I_MATERIAL_SNIPER",
        "kind": "Material",
    },
    "Titanium Plates (Machine Gun Material)": {
        "id": 1152,
        "classification": ItemClassification.useful,
        "internal_id": "I_MATERIAL_UZI",
        "kind": "Material",
    },
    "Missile (Rocket Launcher Material)": {
        "id": 1153,
        "classification": ItemClassification.useful,
        "internal_id": "I_MATERIAL_ROCKETLAUNCHER",
        "kind": "Material",
    },

    # ===== Safe filler items already supported by the client =====
    # Puppy gifts are nice first-pass filler because the client already has a dedicated grant path for them.
    "Toy Bike": {
        "id": 1900,
        "classification": ItemClassification.filler,
        "internal_id": "I_TOY_BIKE",
        "kind": "PuppyTreat",
    },
    "Handheld Console": {
        "id": 1901,
        "classification": ItemClassification.filler,
        "internal_id": "I_GAMEBOY",
        "kind": "PuppyTreat",
    },
    "Tangerine Tree": {
        "id": 1902,
        "classification": ItemClassification.filler,
        "internal_id": "I_PLANT_PUPPY",
        "kind": "PuppyTreat",
    },
    "Toy Animal": {
        "id": 1903,
        "classification": ItemClassification.filler,
        "internal_id": "I_TOY_ANIMAL",
        "kind": "PuppyTreat",
    },
    "Great-Great-Grandma's Novella": {
        "id": 1904,
        "classification": ItemClassification.filler,
        "internal_id": "I_BOOK_MOTHER",
        "kind": "PuppyTreat",
    },
    "Dreamcatcher": {
        "id": 1905,
        "classification": ItemClassification.filler,
        "internal_id": "I_DREAMCATCHER",
        "kind": "PuppyTreat",
    },
    "Ukulele": {
        "id": 1906,
        "classification": ItemClassification.filler,
        "internal_id": "I_UKULELE",
        "kind": "PuppyTreat",
    },

    # I am making currency explicit in the item name and internal id.
    # That keeps the APWorld simple and makes the client-side handling obvious.
    "Viscera x100": {
        "id": 1907,
        "classification": ItemClassification.filler,
        "internal_id": "VISCERA_100",
        "kind": "Currency",
    },
    # Ingredient filler
    "Beans": {
        "id": 1950,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_BEANS",
        "kind": "Ingredient",
    },
    "Corn": {
        "id": 1951,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_CORN",
        "kind": "Ingredient",
    },
    "Worms": {
        "id": 1952,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_WORMS",
        "kind": "Ingredient",
    },
    "Onion": {
        "id": 1953,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_ONION",
        "kind": "Ingredient",
    },
    "Chilly Pepper": {
        "id": 1954,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_CHILLY",
        "kind": "Ingredient",
    },
    "Ghost Pepper": {
        "id": 1955,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_GHOSTPEPPER",
        "kind": "Ingredient",
    },
    "Lemon": {
        "id": 1956,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_LEMON",
        "kind": "Ingredient",
    },
    "Garlic": {
        "id": 1957,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_GARLIC",
        "kind": "Ingredient",
    },
    "Meat": {
        "id": 1958,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_MEAT",
        "kind": "Ingredient",
    },
    "Jackfruit": {
        "id": 1959,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_JACKFRUIT",
        "kind": "Ingredient",
    },
    "Sardine": {
        "id": 1960,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_SARDINE",
        "kind": "Ingredient",
    },
    "Coco": {
        "id": 1961,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_COCO",
        "kind": "Ingredient",
    },
    "Coffee Beans": {
        "id": 1962,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_COFFEE",
        "kind": "Ingredient",
    },
    "Whiskey": {
        "id": 1963,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_WHISKEY",
        "kind": "Ingredient",
    },
    "Tomato": {
        "id": 1964,
        "classification": ItemClassification.filler,
        "internal_id": "I_C_TOMATO",
        "kind": "Ingredient",
    },

    # Cassette filler
    "Cassette: Bloody Sunset": {
        "id": 1970,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_1",
        "kind": "Collectible",
    },
    "Cassette: Playing in the Sun": {
        "id": 1971,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_2",
        "kind": "Collectible",
    },
    "Cassette: Lullaby of the Dead": {
        "id": 1972,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_3",
        "kind": "Collectible",
    },
    "Cassette: Blue Limbo": {
        "id": 1973,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_4",
        "kind": "Collectible",
    },
    "Cassette: Trust Them": {
        "id": 1974,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_D01",
        "kind": "Collectible",
    },
    "Cassette: My Destiny": {
        "id": 1975,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_D02",
        "kind": "Collectible",
    },
    "Cassette: The End of the Road": {
        "id": 1976,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_D03",
        "kind": "Collectible",
    },
    "Cassette: The Whisper": {
        "id": 1977,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_5",
        "kind": "Collectible",
    },
    "Cassette: Heartglaze Hope": {
        "id": 1978,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_6",
        "kind": "Collectible",
    },
    "Cassette: The Hero": {
        "id": 1979,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_7",
        "kind": "Collectible",
    },
    "Cassette: Visions of Red": {
        "id": 1980,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_8",
        "kind": "Collectible",
    },
    "Cassette: Through the Wind": {
        "id": 1981,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_9",
        "kind": "Collectible",
    },
    "Cassette: Heartbeat from the Last Century": {
        "id": 1982,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_10",
        "kind": "Collectible",
    },
    "Cassette: Coming Home": {
        "id": 1983,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_11",
        "kind": "Collectible",
    },
    "Cassette: Mother": {
        "id": 1984,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_KIDNAPPING",
        "kind": "Collectible",
    },
    "Cassette: The Last Tear": {
        "id": 1985,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_12",
        "kind": "Collectible",
    },
    "Cassette: The Final Hours": {
        "id": 1986,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_13",
        "kind": "Collectible",
    },
    "Cassette: Overthinker": {
        "id": 1987,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_14",
        "kind": "Collectible",
    },
    "Cassette: Recurring Dream": {
        "id": 1988,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_15",
        "kind": "Collectible",
    },
    "Cassette: Lonely Mountain": {
        "id": 1989,
        "classification": ItemClassification.filler,
        "internal_id": "I_CASSETTE_16",
        "kind": "Collectible",
    },

    # Map unlock filler
    "Map: Where Our Bikes Growl": {
        "id": 2000,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W06",
        "kind": "MapUnlock",
    },
    "Map: Where All Was Lost (Bottom)": {
        "id": 2001,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W01_BOTTOM",
        "kind": "MapUnlock",
    },
    "Map: Where All Was Lost (Top)": {
        "id": 2002,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W01_TOP",
        "kind": "MapUnlock",
    },
    "Map: Where Doom Fell": {
        "id": 2003,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W02",
        "kind": "MapUnlock",
    },
    "Map: Where Rust Weaves (Left)": {
        "id": 2004,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W03_LEFT",
        "kind": "MapUnlock",
    },
    "Map: Where Rust Weaves (Center)": {
        "id": 2005,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W03_CENTER",
        "kind": "MapUnlock",
    },
    "Map: Where Rust Weaves (Right)": {
        "id": 2006,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W03_RIGHT",
        "kind": "MapUnlock",
    },
    "Map: Where Iron Caresses the Sky (Bottom)": {
        "id": 2007,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W04_BOTTOM",
        "kind": "MapUnlock",
    },
    "Map: Where Iron Caresses the Sky (Top)": {
        "id": 2008,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W04_TOP",
        "kind": "MapUnlock",
    },
    "Map: Where the Waves Die (Left)": {
        "id": 2009,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W05_LEFT",
        "kind": "MapUnlock",
    },
    "Map: Where the Waves Die (Right)": {
        "id": 2010,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W05_RIGHT",
        "kind": "MapUnlock",
    },
    "Map: Where Our Ancestors Rest (Bottom)": {
        "id": 2011,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W07_BOTTOM",
        "kind": "MapUnlock",
    },
    "Map: Where Our Ancestors Rest (Top)": {
        "id": 2012,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W07_TOP",
        "kind": "MapUnlock",
    },
    "Map: Where Birds Came From (Left/Bottom)": {
        "id": 2013,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W08_LEFT",
        "kind": "MapUnlock",
    },
    "Map: Where Birds Came From (Right/Top)": {
        "id": 2014,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_W08_RIGHT",
        "kind": "MapUnlock",
    },
    "Map: Where Birds Lurk (Left)": {
        "id": 2015,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D00_LEFT",
        "kind": "MapUnlock",
    },
    "Map: Where Birds Lurk (Right)": {
        "id": 2016,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D00_RIGHT",
        "kind": "MapUnlock",
    },
    "Map: Where Rock Bleeds (Left)": {
        "id": 2017,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D01_LEFT",
        "kind": "MapUnlock",
    },
    "Map: Where Rock Bleeds (Center)": {
        "id": 2018,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D01_CENTER",
        "kind": "MapUnlock",
    },
    "Map: Where Rock Bleeds (Right)": {
        "id": 2019,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D01_RIGHT",
        "kind": "MapUnlock",
    },
    "Map: Where Water Glistened (Borders)": {
        "id": 2020,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D02_BORDERS",
        "kind": "MapUnlock",
    },
    "Map: Where Water Glistened (1st Ship)": {
        "id": 2021,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D02_SHIP1",
        "kind": "MapUnlock",
    },
    "Map: Where Water Glistened (2nd Ship)": {
        "id": 2022,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D02_SHIP2",
        "kind": "MapUnlock",
    },
    "Map: Where Water Glistened (3rd Ship)": {
        "id": 2023,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D02_SHIP3",
        "kind": "MapUnlock",
    },
    "Map: Where Water Glistened (4th Ship)": {
        "id": 2024,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D02_SHIP4",
        "kind": "MapUnlock",
    },
    "Map: The Big Tree": {
        "id": 2025,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D03",
        "kind": "MapUnlock",
    },
    "Map: Floating City (Control Area)": {
        "id": 2026,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D04_CENTER",
        "kind": "MapUnlock",
    },
    "Map: Floating City (Old Town)": {
        "id": 2027,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D04_TOWN",
        "kind": "MapUnlock",
    },
    "Map: Floating City (Hangar)": {
        "id": 2028,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D04_ZEPPELIN",
        "kind": "MapUnlock",
    },
    "Map: Floating City (Factory)": {
        "id": 2029,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D04_FACTORY",
        "kind": "MapUnlock",
    },
    "Map: Floating City (City Facilities)": {
        "id": 2030,
        "classification": ItemClassification.filler,
        "internal_id": "M_A_D04_FACILITIES",
        "kind": "MapUnlock",
    },
}