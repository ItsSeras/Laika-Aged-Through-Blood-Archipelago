using System;
using System.Collections.Generic;

public partial class LaikaMod
{
    // Canonical AP location registry.
    // Map unlock and quest names should be maintained here as the single source of truth.
    internal static Dictionary<string, APLocationDefinition> LocationDefinitionsByInternalId =
        new Dictionary<string, APLocationDefinition>()
        {
            {
                "M_A_W06",
                new APLocationDefinition(
                    100002L,
                    "Map Piece: Where Our Bikes Growl",
                    "M_A_W06",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W01_BOTTOM",
                new APLocationDefinition(
                    100003L,
                    "Map Piece: Where All Was Lost (Bottom)",
                    "M_A_W01_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W01_TOP",
                new APLocationDefinition(
                    100004L,
                    "Map Piece: Where All Was Lost (Top)",
                    "M_A_W01_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W02",
                new APLocationDefinition(
                    100005L,
                    "Map Piece: Where Doom Fell",
                    "M_A_W02",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_LEFT",
                new APLocationDefinition(
                    100006L,
                    "Map Piece: Where Rust Weaves (Left)",
                    "M_A_W03_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_CENTER",
                new APLocationDefinition(
                    100007L,
                    "Map Piece: Where Rust Weaves (Center)",
                    "M_A_W03_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W03_RIGHT",
                new APLocationDefinition(
                    100008L,
                    "Map Piece: Where Rust Weaves (Right)",
                    "M_A_W03_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W04_BOTTOM",
                new APLocationDefinition(
                    100009L,
                    "Map Piece: Where Iron Caresses the Sky (Bottom)",
                    "M_A_W04_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W04_TOP",
                new APLocationDefinition(
                    100010L,
                    "Map Piece: Where Iron Caresses the Sky (Top)",
                    "M_A_W04_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W05_LEFT",
                new APLocationDefinition(
                    100011L,
                    "Map Piece: Where the Waves Die (Left)",
                    "M_A_W05_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W05_RIGHT",
                new APLocationDefinition(
                    100012L,
                    "Map Piece: Where the Waves Die (Right)",
                    "M_A_W05_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W07_BOTTOM",
                new APLocationDefinition(
                    100013L,
                    "Map Piece: Where Our Ancestors Rest (Bottom)",
                    "M_A_W07_BOTTOM",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W07_TOP",
                new APLocationDefinition(
                    100014L,
                    "Map Piece: Where Our Ancestors Rest (Top)",
                    "M_A_W07_TOP",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W08_LEFT",
                new APLocationDefinition(
                    100015L,
                    "Map Piece: Where Birds Came From (Left/Bottom)",
                    "M_A_W08_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_W08_RIGHT",
                new APLocationDefinition(
                    100016L,
                    "Map Piece: Where Birds Came From (Right/Top)",
                    "M_A_W08_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D00_LEFT",
                new APLocationDefinition(
                    100017L,
                    "Map Piece: Where Birds Lurk (Left)",
                    "M_A_D00_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D00_RIGHT",
                new APLocationDefinition(
                    100018L,
                    "Map Piece: Where Birds Lurk (Right)",
                    "M_A_D00_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_LEFT",
                new APLocationDefinition(
                    100019L,
                    "Map Piece: Where Rock Bleeds (Left)",
                    "M_A_D01_LEFT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_CENTER",
                new APLocationDefinition(
                    100020L,
                    "Map Piece: Where Rock Bleeds (Center)",
                    "M_A_D01_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D01_RIGHT",
                new APLocationDefinition(
                    100021L,
                    "Map Piece: Where Rock Bleeds (Right)",
                    "M_A_D01_RIGHT",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_BORDERS",
                new APLocationDefinition(
                    100022L,
                    "Map Piece: Where Water Glistened (Borders)",
                    "M_A_D02_BORDERS",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP1",
                new APLocationDefinition(
                    100023L,
                    "Map Piece: Where Water Glistened (1st Ship)",
                    "M_A_D02_SHIP1",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP2",
                new APLocationDefinition(
                    100024L,
                    "Map Piece: Where Water Glistened (2nd Ship)",
                    "M_A_D02_SHIP2",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP3",
                new APLocationDefinition(
                    100025L,
                    "Map Piece: Where Water Glistened (3rd Ship)",
                    "M_A_D02_SHIP3",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D02_SHIP4",
                new APLocationDefinition(
                    100026L,
                    "Map Piece: Where Water Glistened (4th Ship)",
                    "M_A_D02_SHIP4",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D03",
                new APLocationDefinition(
                    100027L,
                    "Map Piece: The Big Tree",
                    "M_A_D03",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_CENTER",
                new APLocationDefinition(
                    100028L,
                    "Map Piece: Floating City (Control Area)",
                    "M_A_D04_CENTER",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_TOWN",
                new APLocationDefinition(
                    100029L,
                    "Map Piece: Floating City (Old Town)",
                    "M_A_D04_TOWN",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_ZEPPELIN",
                new APLocationDefinition(
                    100030L,
                    "Map Piece: Floating City (Hangar)",
                    "M_A_D04_ZEPPELIN",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_FACTORY",
                new APLocationDefinition(
                    100031L,
                    "Map Piece: Floating City (Factory)",
                    "M_A_D04_FACTORY",
                    "MapUnlock",
                    true
                )
            },
            {
                "M_A_D04_FACILITIES",
                new APLocationDefinition(
                    100032L,
                    "Map Piece: Floating City (City Facilities)",
                    "M_A_D04_FACILITIES",
                    "MapUnlock",
                    true
                )
            },
            {
                "Q_D_0_Tutorial",
                new APLocationDefinition(
                    110001L,
                    "Quest Complete: Rage and Sorrow",
                    "Q_D_0_Tutorial",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_OldWarfare",
                new APLocationDefinition(
                    110002L,
                    "Quest Complete: Old Warfare",
                    "Q_D_S_OldWarfare",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_TutorialHook",
                new APLocationDefinition(
                    110003L,
                    "Quest Complete: The Bonehead's Hook",
                    "Q_D_S_TutorialHook",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_TutorialDash",
                new APLocationDefinition(
                    110004L,
                    "Quest Complete: Floating",
                    "Q_D_S_TutorialDash",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Flower",
                new APLocationDefinition(
                    110005L,
                    "Quest Complete: A Heart for Poochie",
                    "Q_D_S_Flower",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_1_Mines",
                new APLocationDefinition(
                    110006L,
                    "Quest Complete: Diplomacy",
                    "Q_D_1_Mines",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_2_Lighthouse",
                new APLocationDefinition(
                    110007L,
                    "Quest Complete: Radio Silence",
                    "Q_D_2_Lighthouse",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_3_TheBigTree",
                new APLocationDefinition(
                    110008L,
                    "Quest Complete: The Big Tree",
                    "Q_D_3_TheBigTree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_K_Kidnapping",
                new APLocationDefinition(
                    110009L,
                    "Quest Complete: Childless",
                    "Q_D_K_Kidnapping",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_PoochiesCorpse",
                new APLocationDefinition(
                    110010L,
                    "Quest Complete: Closure",
                    "Q_D_S_PoochiesCorpse",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_4_FloatingCity",
                new APLocationDefinition(
                    110011L,
                    "Quest Complete: Hell High",
                    "Q_D_4_FloatingCity",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_SacredPlace",
                new APLocationDefinition(
                    110012L,
                    "Quest Complete: Shake Off the Dead Leaves",
                    "Q_D_S_SacredPlace",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Lights",
                new APLocationDefinition(
                    110013L,
                    "Quest Complete: Stargazing",
                    "Q_D_S_Lights",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Remnants",
                new APLocationDefinition(
                    110014L,
                    "Quest Complete: The Remnants",
                    "Q_D_S_Remnants",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NewSheriff",
                new APLocationDefinition(
                    110015L,
                    "Quest Complete: A New Sheriff in Town",
                    "Q_D_S_NewSheriff",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Tombstone",
                new APLocationDefinition(
                    110016L,
                    "Quest Complete: A Little Tomb Stone",
                    "Q_D_S_Tombstone",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_FirstPeriod",
                new APLocationDefinition(
                    110017L,
                    "Quest Complete: First Blood",
                    "Q_D_S_FirstPeriod",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_EntomBrother",
                new APLocationDefinition(
                    110018L,
                    "Quest Complete: Life of the Party",
                    "Q_D_S_EntomBrother",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_BoneFlour",
                new APLocationDefinition(
                    110019L,
                    "Quest Complete: Bone Flour",
                    "Q_D_S_BoneFlour",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_CamillasJoint",
                new APLocationDefinition(
                    110020L,
                    "Quest Complete: A Break for Camilla",
                    "Q_D_S_CamillasJoint",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_OldCamp",
                new APLocationDefinition(
                    110021L,
                    "Quest Complete: From Mother to Daughter",
                    "Q_D_S_OldCamp",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Prophecy",
                new APLocationDefinition(
                    110022L,
                    "Quest Complete: The Prophecy",
                    "Q_D_S_Prophecy",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_LastMeal",
                new APLocationDefinition(
                    110023L,
                    "Quest Complete: Last Meal",
                    "Q_D_S_LastMeal",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_DyingBird",
                new APLocationDefinition(
                    110024L,
                    "Quest Complete: For the Cash",
                    "Q_D_S_DyingBird",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Immortal",
                new APLocationDefinition(
                    110025L,
                    "Quest Complete: Death on Demand",
                    "Q_D_S_Immortal",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_DeadTree",
                new APLocationDefinition(
                    110026L,
                    "Quest Complete: Family Tree",
                    "Q_D_S_DeadTree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_GhostRevenge",
                new APLocationDefinition(
                    110027L,
                    "Quest Complete: Fade Out",
                    "Q_D_S_GhostRevenge",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Rapist",
                new APLocationDefinition(
                    110028L,
                    "Quest Complete: Just a little girl",
                    "Q_D_S_Rapist",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_PlantSeed",
                new APLocationDefinition(
                    110029L,
                    "Quest Complete: We'll Never Know",
                    "Q_D_S_PlantSeed",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Gasoline",
                new APLocationDefinition(
                    110030L,
                    "Quest Complete: High Spirits",
                    "Q_D_S_Gasoline",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_Seashell",
                new APLocationDefinition(
                    110031L,
                    "Quest Complete: Water Whispers",
                    "Q_D_S_Seashell",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresOne",
                new APLocationDefinition(
                    110032L,
                    "Quest Complete: Worse than Nightmares",
                    "Q_D_S_NightmaresOne",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresTwo",
                new APLocationDefinition(
                    110033L,
                    "Quest Complete: Worse than Hives",
                    "Q_D_S_NightmaresTwo",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_S_NightmaresThree",
                new APLocationDefinition(
                    110034L,
                    "Quest Complete: Worse than Stomach Flu",
                    "Q_D_S_NightmaresThree",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansDrums",
                new APLocationDefinition(
                    110035L,
                    "Quest Complete: Fogg's Only Wish",
                    "Q_D_A_MusiciansDrums",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansErhu",
                new APLocationDefinition(
                    110036L,
                    "Quest Complete: The Last Erhu",
                    "Q_D_A_MusiciansErhu",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansFlute",
                new APLocationDefinition(
                    110037L,
                    "Quest Complete: Clean Your Beak",
                    "Q_D_A_MusiciansFlute",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansGuitar",
                new APLocationDefinition(
                    110038L,
                    "Quest Complete: Desperately in Need of Music",
                    "Q_D_A_MusiciansGuitar",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansPiano",
                new APLocationDefinition(
                    110039L,
                    "Quest Complete: Sober Up",
                    "Q_D_A_MusiciansPiano",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_A_MusiciansVoice",
                new APLocationDefinition(
                    110040L,
                    "Quest Complete: Oooo Ooo Oo O Ooo",
                    "Q_D_A_MusiciansVoice",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_PuppysBirth",
                new APLocationDefinition(
                    110041L,
                    "Quest Complete: Where We Used to Live",
                    "Q_D_F_PuppysBirth",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_YoungLaika",
                new APLocationDefinition(
                    110042L,
                    "Quest Complete: Target Practice",
                    "Q_D_F_YoungLaika",
                    "Quest",
                    true
                )
            },
            {
                "Q_D_F_DaughterDies",
                new APLocationDefinition(
                    110043L,
                    "Quest Complete: Ava",
                    "Q_D_F_DaughterDies",
                    "Quest",
                    true
                )
            },
            {
                "B_BOSS_00_DEFEATED",
                new APLocationDefinition(
                    120001L,
                    "Boss Defeated: A Hundred Hungry Beaks",
                    "B_BOSS_00_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_01_DEFEATED",
                new APLocationDefinition(
                    120002L,
                    "Boss Defeated: A Long Lost Woodcrawler",
                    "B_BOSS_01_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_ROSCO_DEFEATED",
                new APLocationDefinition(
                    120003L,
                    "Boss Defeated: A Caterpiller Made of Sadness",
                    "B_BOSS_ROSCO_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_02_DEFEATED",
                new APLocationDefinition(
                    120004L,
                    "Boss Defeated: A Gargantuan Swimcrab",
                    "B_BOSS_02_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "B_BOSS_03_DEFEATED",
                new APLocationDefinition(
                    120005L,
                    "Boss Defeated: Pope Melva VIII",
                    "B_BOSS_03_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "BOSS_04_DEFEATED",
                new APLocationDefinition(
                    120006L,
                    "Boss Defeated: Two-Beak God",
                    "BOSS_04_DEFEATED",
                    "Boss",
                    true
                )
            },
            {
                "I_CASSETTE_1",
                new APLocationDefinition(
                    130001L,
                    "Cassette Tape: Bloody Sunset",
                    "I_CASSETTE_1",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_2",
                new APLocationDefinition(
                    130002L,
                    "Cassette Tape: Playing in the Sun",
                    "I_CASSETTE_2",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_3",
                new APLocationDefinition(
                    130003L,
                    "Cassette Tape: Lullaby of the Dead",
                    "I_CASSETTE_3",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_4",
                new APLocationDefinition(
                    130004L,
                    "Cassette Tape: Blue Limbo",
                    "I_CASSETTE_4",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D01",
                new APLocationDefinition(
                    130005L,
                    "Cassette Tape: Trust Them",
                    "I_CASSETTE_D01",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D02",
                new APLocationDefinition(
                    130006L,
                    "Cassette Tape: My Destiny",
                    "I_CASSETTE_D02",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_D03",
                new APLocationDefinition(
                    130007L,
                    "Cassette Tape: The End of the Road",
                    "I_CASSETTE_D03",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_5",
                new APLocationDefinition(
                    130008L,
                    "Cassette Tape: The Whisper",
                    "I_CASSETTE_5",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_6",
                new APLocationDefinition(
                    130009L,
                    "Cassette Tape: Heartglaze Hope",
                    "I_CASSETTE_6",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_7",
                new APLocationDefinition(
                    130010L,
                    "Cassette Tape: The Hero",
                    "I_CASSETTE_7",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_8",
                new APLocationDefinition(
                    130011L,
                    "Cassette Tape: Visions of Red",
                    "I_CASSETTE_8",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_9",
                new APLocationDefinition(
                    130012L,
                    "Cassette Tape: Through the Wind",
                    "I_CASSETTE_9",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_10",
                new APLocationDefinition(
                    130013L,
                    "Cassette Tape: Heartbeat from the Last Century",
                    "I_CASSETTE_10",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_11",
                new APLocationDefinition(
                    130014L,
                    "Cassette Tape: Coming Home",
                    "I_CASSETTE_11",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_KIDNAPPING",
                new APLocationDefinition(
                    130015L,
                    "Cassette Tape: Mother",
                    "I_CASSETTE_KIDNAPPING",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_12",
                new APLocationDefinition(
                    130016L,
                    "Cassette Tape: The Last Tear",
                    "I_CASSETTE_12",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_13",
                new APLocationDefinition(
                    130017L,
                    "Cassette Tape: The Final Hours",
                    "I_CASSETTE_13",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_14",
                new APLocationDefinition(
                    130018L,
                    "Cassette Tape: Overthinker",
                    "I_CASSETTE_14",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_15",
                new APLocationDefinition(
                    130019L,
                    "Cassette Tape: Recurring Dream",
                    "I_CASSETTE_15",
                    "Cassette",
                    true
                )
            },
            {
                "I_CASSETTE_16",
                new APLocationDefinition(
                    130020L,
                    "Cassette Tape: Lonely Mountain",
                    "I_CASSETTE_16",
                    "Cassette",
                    true
                )
            },
            {
                "I_TOY_BIKE",
                new APLocationDefinition(
                    140001L,
                    "Puppy Gift: Toy Bike",
                    "I_TOY_BIKE",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_GAMEBOY",
                new APLocationDefinition(
                    140002L,
                    "Puppy Gift: Handheld Console",
                    "I_GAMEBOY",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_PLANT_PUPPY",
                new APLocationDefinition(
                    140003L,
                    "Puppy Gift: Tangerine Tree",
                    "I_PLANT_PUPPY",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_TOY_ANIMAL",
                new APLocationDefinition(
                    140004L,
                    "Puppy Gift: Toy Animal",
                    "I_TOY_ANIMAL",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_BOOK_MOTHER",
                new APLocationDefinition(
                    140005L,
                    "Puppy Gift: Great-Great-Grandma's Novella",
                    "I_BOOK_MOTHER",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_DREAMCATCHER",
                new APLocationDefinition(
                    140006L,
                    "Puppy Gift: Dreamcatcher",
                    "I_DREAMCATCHER",
                    "PuppyGift",
                    true
                )
            },
            {
                "I_UKULELE",
                new APLocationDefinition(
                    140007L,
                    "Puppy Gift: Ukulele",
                    "I_UKULELE",
                    "PuppyGift",
                    true
                )
            },


        };

    internal static Dictionary<long, Func<PendingItem>> ItemFactoriesByApId =
        new Dictionary<long, Func<PendingItem>>()
        {
        // ===== Key items =====
        { 1000L, () => new PendingItem(ItemKind.KeyItem, "I_DASH", 1, "Dash (Bike Upgrade)") },
        { 1001L, () => new PendingItem(ItemKind.KeyItem, "I_E_HOOK", 1, "Hook (Bike Upgrade)") },
        { 1002L, () => new PendingItem(ItemKind.KeyItem, "I_MAYA_PENDANT", 1, "Maya's Pendant (Bike Upgrade)") },

        // ===== Weapons / weapon-mode-aware unlocks =====
        { 1100L, () => GetShotgunUnlockItem() },
        { 1101L, () => GetSniperUnlockItem() },
        { 1102L, () => GetMachineGunUnlockItem() },
        { 1103L, () => GetRocketLauncherUnlockItem() },

        // Crossbow is still direct for now
        { 1104L, () => new PendingItem(ItemKind.Weapon, "I_W_CROSSBOW", 1, "Crossbow (Weapon)") },

        // ===== Crafting-mode weapon materials =====
        { 1150L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_SHOTGUN", 1, "Rusty Spring (Shotgun Material)") },
        { 1151L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_SNIPER", 1, "Magnifying Glass (Sniper Rifle Material)") },
        { 1152L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_UZI", 1, "Titanium Plates (Machine Gun Material)") },
        { 1153L, () => new PendingItem(ItemKind.Material, "I_MATERIAL_ROCKETLAUNCHER", 1, "Missile (Rocket Launcher Material)") },

        // ===== Puppy gifts =====
        { 1900L, () => new PendingItem(ItemKind.PuppyTreat, "I_TOY_BIKE", 1, "Toy Bike") },
        { 1901L, () => new PendingItem(ItemKind.PuppyTreat, "I_GAMEBOY", 1, "Handheld Console") },
        { 1902L, () => new PendingItem(ItemKind.PuppyTreat, "I_PLANT_PUPPY", 1, "Tangerine Tree") },
        { 1903L, () => new PendingItem(ItemKind.PuppyTreat, "I_TOY_ANIMAL", 1, "Toy Animal") },
        { 1904L, () => new PendingItem(ItemKind.PuppyTreat, "I_BOOK_MOTHER", 1, "Great-Great-Grandma's Novella") },
        { 1905L, () => new PendingItem(ItemKind.PuppyTreat, "I_DREAMCATCHER", 1, "Dreamcatcher") },
        { 1906L, () => new PendingItem(ItemKind.PuppyTreat, "I_UKULELE", 1, "Ukulele") },

        // ===== Currency =====
        { 1907L, () => new PendingItem(ItemKind.Currency, "VISCERA_100", 100, "Viscera x100") },

        // ===== Ingredients =====
        { 1950L, () => new PendingItem(ItemKind.Ingredient, "I_C_BEANS", 1, "Beans") },
        { 1951L, () => new PendingItem(ItemKind.Ingredient, "I_C_CORN", 1, "Corn") },
        { 1952L, () => new PendingItem(ItemKind.Ingredient, "I_C_WORMS", 1, "Worms") },
        { 1953L, () => new PendingItem(ItemKind.Ingredient, "I_C_ONION", 1, "Onion") },
        { 1954L, () => new PendingItem(ItemKind.Ingredient, "I_C_CHILLY", 1, "Chilly Pepper") },
        { 1955L, () => new PendingItem(ItemKind.Ingredient, "I_C_GHOSTPEPPER", 1, "Ghost Pepper") },
        { 1956L, () => new PendingItem(ItemKind.Ingredient, "I_C_LEMON", 1, "Lemon") },
        { 1957L, () => new PendingItem(ItemKind.Ingredient, "I_C_GARLIC", 1, "Garlic") },
        { 1958L, () => new PendingItem(ItemKind.Ingredient, "I_C_MEAT", 1, "Meat") },
        { 1959L, () => new PendingItem(ItemKind.Ingredient, "I_C_JACKFRUIT", 1, "Jackfruit") },
        { 1960L, () => new PendingItem(ItemKind.Ingredient, "I_C_SARDINE", 1, "Sardine") },
        { 1961L, () => new PendingItem(ItemKind.Ingredient, "I_C_COCO", 1, "Coco") },
        { 1962L, () => new PendingItem(ItemKind.Ingredient, "I_C_COFFEE", 1, "Coffee Beans") },
        { 1963L, () => new PendingItem(ItemKind.Ingredient, "I_C_WHISKEY", 1, "Whiskey") },
        { 1964L, () => new PendingItem(ItemKind.Ingredient, "I_C_TOMATO", 1, "Tomato") },

        // ===== Cassettes =====
        { 1970L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_1", 1, "Cassette: Bloody Sunset") },
        { 1971L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_2", 1, "Cassette: Playing in the Sun") },
        { 1972L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_3", 1, "Cassette: Lullaby of the Dead") },
        { 1973L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_4", 1, "Cassette: Blue Limbo") },
        { 1974L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D01", 1, "Cassette: Trust Them") },
        { 1975L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D02", 1, "Cassette: My Destiny") },
        { 1976L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_D03", 1, "Cassette: The End of the Road") },
        { 1977L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_5", 1, "Cassette: The Whisper") },
        { 1978L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_6", 1, "Cassette: Heartglaze Hope") },
        { 1979L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_7", 1, "Cassette: The Hero") },
        { 1980L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_8", 1, "Cassette: Visions of Red") },
        { 1981L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_9", 1, "Cassette: Through the Wind") },
        { 1982L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_10", 1, "Cassette: Heartbeat from the Last Century") },
        { 1983L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_11", 1, "Cassette: Coming Home") },
        { 1984L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_KIDNAPPING", 1, "Cassette: Mother") },
        { 1985L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_12", 1, "Cassette: The Last Tear") },
        { 1986L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_13", 1, "Cassette: The Final Hours") },
        { 1987L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_14", 1, "Cassette: Overthinker") },
        { 1988L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_15", 1, "Cassette: Recurring Dream") },
        { 1989L, () => new PendingItem(ItemKind.Collectible, "I_CASSETTE_16", 1, "Cassette: Lonely Mountain") },

        // ===== Map unlocks =====
        { 2000L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W06", 1, "Map: Where Our Bikes Growl") },
        { 2001L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W01_BOTTOM", 1, "Map: Where All Was Lost (Bottom)") },
        { 2002L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W01_TOP", 1, "Map: Where All Was Lost (Top)") },
        { 2003L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W02", 1, "Map: Where Doom Fell") },
        { 2004L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_LEFT", 1, "Map: Where Rust Weaves (Left)") },
        { 2005L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_CENTER", 1, "Map: Where Rust Weaves (Center)") },
        { 2006L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W03_RIGHT", 1, "Map: Where Rust Weaves (Right)") },
        { 2007L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W04_BOTTOM", 1, "Map: Where Iron Caresses the Sky (Bottom)") },
        { 2008L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W04_TOP", 1, "Map: Where Iron Caresses the Sky (Top)") },
        { 2009L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W05_LEFT", 1, "Map: Where the Waves Die (Left)") },
        { 2010L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W05_RIGHT", 1, "Map: Where the Waves Die (Right)") },
        { 2011L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W07_BOTTOM", 1, "Map: Where Our Ancestors Rest (Bottom)") },
        { 2012L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W07_TOP", 1, "Map: Where Our Ancestors Rest (Top)") },
        { 2013L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W08_LEFT", 1, "Map: Where Birds Came From (Left/Bottom)") },
        { 2014L, () => new PendingItem(ItemKind.MapUnlock, "M_A_W08_RIGHT", 1, "Map: Where Birds Came From (Right/Top)") },
        { 2015L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D00_LEFT", 1, "Map: Where Birds Lurk (Left)") },
        { 2016L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D00_RIGHT", 1, "Map: Where Birds Lurk (Right)") },
        { 2017L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_LEFT", 1, "Map: Where Rock Bleeds (Left)") },
        { 2018L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_CENTER", 1, "Map: Where Rock Bleeds (Center)") },
        { 2019L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D01_RIGHT", 1, "Map: Where Rock Bleeds (Right)") },
        { 2020L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_BORDERS", 1, "Map: Where Water Glistened (Borders)") },
        { 2021L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP1", 1, "Map: Where Water Glistened (1st Ship)") },
        { 2022L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP2", 1, "Map: Where Water Glistened (2nd Ship)") },
        { 2023L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP3", 1, "Map: Where Water Glistened (3rd Ship)") },
        { 2024L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D02_SHIP4", 1, "Map: Where Water Glistened (4th Ship)") },
        { 2025L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D03", 1, "Map: The Big Tree") },
        { 2026L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_CENTER", 1, "Map: Floating City (Control Area)") },
        { 2027L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_TOWN", 1, "Map: Floating City (Old Town)") },
        { 2028L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_ZEPPELIN", 1, "Map: Floating City (Hangar)") },
        { 2029L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_FACTORY", 1, "Map: Floating City (Factory)") },
        { 2030L, () => new PendingItem(ItemKind.MapUnlock, "M_A_D04_FACILITIES", 1, "Map: Floating City (City Facilities)") },
        };

    // Canonical collectible item registry.
    // Use this for AP received-item display names.
    // Do not use this as a location/check registry unless cassette checks are later
    // sourced from boombox destruction, shop purchase, quest reward, or Jakob bundle logic.
    internal static Dictionary<string, string> CollectibleDisplayNamesByInternalId =
        new Dictionary<string, string>()
        {
            { "I_CASSETTE_1", "Cassette Tape: Bloody Sunset" },
            { "I_CASSETTE_2", "Cassette Tape: Playing in the Sun" },
            { "I_CASSETTE_3", "Cassette Tape: Lullaby of the Dead" },
            { "I_CASSETTE_4", "Cassette Tape: Blue Limbo" },
            { "I_CASSETTE_D01", "Cassette Tape: Trust Them" },
            { "I_CASSETTE_D02", "Cassette Tape: My Destiny" },
            { "I_CASSETTE_D03", "Cassette Tape: The End of the Road" },
            { "I_CASSETTE_5", "Cassette Tape: The Whisper" },
            { "I_CASSETTE_6", "Cassette Tape: Heartglaze Hope" },
            { "I_CASSETTE_7", "Cassette Tape: The Hero" },
            { "I_CASSETTE_8", "Cassette Tape: Visions of Red" },
            { "I_CASSETTE_9", "Cassette Tape: Through the Wind" },
            { "I_CASSETTE_10", "Cassette Tape: Heartbeat from the Last Century" },
            { "I_CASSETTE_11", "Cassette Tape: Coming Home" },
            { "I_CASSETTE_KIDNAPPING", "Cassette Tape: Mother" },
            { "I_CASSETTE_12", "Cassette Tape: The Last Tear" },
            { "I_CASSETTE_13", "Cassette Tape: The Final Hours" },
            { "I_CASSETTE_14", "Cassette Tape: Overthinker" },
            { "I_CASSETTE_15", "Cassette Tape: Recurring Dream" },
            { "I_CASSETTE_16", "Cassette Tape: Lonely Mountain" },
            { "I_COLLECTION_JAKOB", "Cassette Tape: Jakob's Music Collection" }
        };

    internal static bool TryCreatePendingItemFromApItemId(long apItemId, out PendingItem pendingItem)
    {
        pendingItem = null;

        Func<PendingItem> factory;
        if (!ItemFactoriesByApId.TryGetValue(apItemId, out factory))
        {
            LogWarning($"AP ITEM: no C# mapping exists yet for AP item id {apItemId}");
            return false;
        }

        pendingItem = factory();
        return pendingItem != null;
    }

    // ===== Display-name helpers =====
    // Resolves a known AP location definition by internal Laika identifier.
    internal static bool TryGetLocationDefinition(string internalId, out APLocationDefinition definition)
    {
        if (string.IsNullOrEmpty(internalId))
        {
            definition = null;
            return false;
        }

        return LocationDefinitionsByInternalId.TryGetValue(internalId, out definition);
    }

    internal static bool TryGetLocationDefinition(long locationId, out APLocationDefinition definition)
    {
        definition = null;

        foreach (var kvp in LocationDefinitionsByInternalId)
        {
            APLocationDefinition entry = kvp.Value;
            if (entry != null && entry.LocationId == locationId)
            {
                definition = entry;
                return true;
            }
        }

        return false;
    }

    // Resolve a collectible's player-facing name from its internal game ID.
    internal static bool TryGetCollectibleDisplayName(string internalId, out string displayName)
    {
        return CollectibleDisplayNamesByInternalId.TryGetValue(internalId, out displayName);
    }

    // Build a pending collectible item using the canonical cassette display-name registry.
    internal static PendingItem CreateCollectiblePendingItem(string collectibleId, int amount = 1)
    {
        string displayName;
        if (!TryGetCollectibleDisplayName(collectibleId, out displayName))
        {
            displayName = $"Cassette Tape ({collectibleId})";
        }

        return new PendingItem(ItemKind.Collectible, collectibleId, amount, displayName);
    }
}