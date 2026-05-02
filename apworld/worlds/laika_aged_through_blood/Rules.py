from worlds.generic.Rules import set_rule

def has_shotgun_access(state, player) -> bool:
    return (
        state.has("Shotgun (Weapon)", player)
        or (
            state.has("Blueprint: Shotgun", player)
            and state.has("Rusty Spring (Shotgun Material)", player)
        )
    )

def has_shotgun_level_2(state, player) -> bool:
    return (
        has_shotgun_access(state, player)
        and state.has("Shotgun Upgrade (Weapon Upgrade)", player)
    )

def has(state, player, item_name: str, count: int = 1) -> bool:
    return state.has(item_name, player, count)


def can_reach_loc(state, player, name: str) -> bool:
    return state.can_reach_location(name, player)


def set_rules(world):
    player = world.player
    mw = world.multiworld

    def loc(name: str):
        return mw.get_location(name, player)

    # Main opening
    # Player always starts with pistol + reflect, so Hundred Hungry Beaks has no AP item requirement.
    set_rule(
        loc("Boss Defeated: A Hundred Hungry Beaks"),
        lambda state: True
    )

    set_rule(
        loc("Quest Complete: Rage and Sorrow"),
        lambda state: can_reach_loc(state, player, "Boss Defeated: A Hundred Hungry Beaks")
    )

    # Post Rage and Sorrow
    set_rule(
        loc("Quest Complete: A Heart for Poochie"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
            and has(state, player, "Key Item: Heartglaze Flower")
        )
    )

    # Starting Jakob collection cassettes should not be sphere 1.
    for name in [
        "Cassette Tape: Bloody Sunset",
        "Cassette Tape: Playing in the Sun",
        "Cassette Tape: Lullaby of the Dead",
        "Cassette Tape: Blue Limbo",
        "Cassette Tape: The Whisper",
    ]:
        set_rule(loc(name), lambda state: post_rage(state))

    set_rule(
        loc("Quest Complete: The Remnants"),
        lambda state: (
            post_rage(state)
            and has(state, player, "Key Item: Jakob's Ashes")
        )
    )

    set_rule(
        loc("Quest Complete: Shake Off the Dead Leaves"),
        lambda state: (
            post_rage(state)
            and has(state, player, "Key Item: Jakob's Ashes")
        )
    )

    set_rule(
        loc("Boss Defeated: A Long Lost Woodcrawler"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
    )

    set_rule(
        loc("Key Item: Heartglaze Flower"),
        lambda state: can_reach_loc(state, player, "Boss Defeated: A Long Lost Woodcrawler")
    )

    # War chapter requires Rage and Sorrow + A Heart for Poochie.
    def war_chapter(state) -> bool:
        return (
            can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
            and can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
        )

    set_rule(
        loc("Quest Complete: Diplomacy"),
        lambda state: (
            war_chapter(state)
            and can_reach_loc(state, player, "Boss Defeated: A Caterpillar Made of Sadness")
        )
    )

    set_rule(
        loc("Key Item: Flute Cleaning Brush"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Key Item: Mountainheart Card"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
        )
    )

    set_rule(
        loc("Quest Complete: Radio Silence"),
        lambda state: (
            war_chapter(state)
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Carved Whale Tooth")
            and has(state, player, "Key Item: Long Rope")
        )
    )

    set_rule(
        loc("Boss Defeated: A Gargantuan Swimcrab"),
        lambda state: (
            post_radio_silence(state)
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Quest Complete: The Big Tree"),
        lambda state: (
            war_chapter(state)
            and has_shotgun_access(state, player)
            and can_reach_loc(state, player, "Quest Complete: The Bonehead's Hook")
        )
    )

    set_rule(
        loc("Boss Defeated: Pope Melva VIII"),
        lambda state: post_big_tree(state)
    )

    set_rule(
        loc("Quest Complete: Old Warfare"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Quest Complete: Stargazing"),
        lambda state: (
            war_chapter(state)
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Brand-New Notebook")
        )
    )

    set_rule(
        loc("Boss Defeated: A Caterpillar Made of Sadness"),
        lambda state: (
            war_chapter(state)
            and has(state, player, "Key Item: 1st Key To The Pit")
            and has(state, player, "Key Item: 2nd Key To The Pit")
            and has(state, player, "Key Item: 3rd Key To The Pit")
        )
    )

    set_rule(
        loc("Quest Complete: The Bonehead's Hook"),
        lambda state:
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
            and has(state, player, "Hook (Bike Upgrade)")
    )

    set_rule(
        loc("Key Item: Hook Head"),
        lambda state:
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
            and has(state, player, "Hook (Bike Upgrade)")
    )

    set_rule(
        loc("Key Item: Hook Body"),
        lambda state:
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
            and has(state, player, "Hook (Bike Upgrade)")
    )

    set_rule(
        loc("Puppy Gift: Dreamcatcher"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Stargazing")
    )

    # Escalation
    set_rule(
        loc("Quest Complete: Childless"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Diplomacy")
            and can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and can_reach_loc(state, player, "Quest Complete: The Big Tree")
        )
    )

    set_rule(
        loc("Quest Complete: Closure"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Childless")
    )

    # Finale
    set_rule(
        loc("Quest Complete: Hell High"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Childless")
    )

    set_rule(
        loc("Quest Complete: Floating"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Hell High")
            and has(state, player, "Dash (Bike Upgrade)")
        )
    )

    # Side quests
    set_rule(
        loc("Quest Complete: A New Sheriff in Town"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Diplomacy")
            and has(state, player, "Key Item: Gutsy Gus's Gushing Gunfights")
        )
    )

    set_rule(
        loc("Quest Complete: A Little Tomb Stone"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Diplomacy")
            and has(state, player, "Key Item: Iris")
        )
    )

    set_rule(
        loc("Quest Complete: First Blood"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has(state, player, "Key Item: Pads")
            and has(state, player, "Key Item: Moon Blossom")
        )
    )

    set_rule(
        loc("Quest Complete: Life of the Party"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Jar Filled With Bugs")
        )
    )

    set_rule(
        loc("Quest Complete: Bone Flour"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Vitamin-Coated Bones")
        )
    )

    set_rule(
        loc("Quest Complete: A Break for Camilla"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Diplomacy")
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Camilla's Special Herbs")
        )
    )

    set_rule(
        loc("Quest Complete: From Mother to Daughter"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Key Item: Family Braid")
        )
    )

    set_rule(
        loc("Quest Complete: The Prophecy"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has_shotgun_access(state, player)
            and has(state, player, "Hook (Bike Upgrade)")
            and has(state, player, "Key Item: Petey's Letter")
        )
    )

    set_rule(
        loc("Quest Complete: Last Meal"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Bluelemon Berries")
        )
    )

    set_rule(
        loc("Key Item: Bluelemon Berries"),
        lambda state:
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
    )

    # Childless side branch
    for name in [
        "Quest Complete: For the Cash",
        "Quest Complete: Death on Demand",
        "Quest Complete: Family Tree",
        "Quest Complete: Fade Out",
    ]:
        set_rule(
            loc(name),
            lambda state: can_reach_loc(state, player, "Quest Complete: Childless")
        )

    set_rule(
        loc("Quest Complete: Just a little girl"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Childless")
            and has(state, player, "Key Item: Lhey's Diary")
        )
    )

    set_rule(
        loc("Quest Complete: We'll Never Know"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Childless")
            and has(state, player, "Key Item: Large Seed")
        )
    )

    set_rule(
        loc("Quest Complete: High Spirits"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Childless")
            and has(state, player, "Key Item: Gallon of Gasoline")
        )
    )

    set_rule(
        loc("Quest Complete: Water Whispers"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Childless")
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Seashell")
        )
    )

    # Nightmares
    set_rule(
        loc("Quest Complete: Worse than Nightmares"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Key Item: Thistle Stems")
        )
    )

    set_rule(
        loc("Quest Complete: Worse than Hives"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Key Item: Phalseria Sap")
        )
    )

    set_rule(
        loc("Quest Complete: Worse than Stomach Flu"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Key Item: Banana Leaves")
        )
    )

    # Musicians
    set_rule(
        loc("Quest Complete: Fogg's Only Wish"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Key Item: Fogg's Drumstick")
        )
    )

    set_rule(
        loc("Quest Complete: The Last Erhu"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Hook (Bike Upgrade)")
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Erhu Strings")
        )
    )

    set_rule(
        loc("Quest Complete: Clean Your Beak"),
        lambda state: (
            war_chapter(state)
            and has_shotgun_access(state, player)
            and has(state, player, "Key Item: Flute Cleaning Brush")
        )
    )

    set_rule(
        loc("Quest Complete: Desperately in Need of Music"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Key Item: Guitar Strings")
        )
    )

    set_rule(
        loc("Quest Complete: Sober Up"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Key Item: Sheet Music")
        )
    )

    set_rule(
        loc("Quest Complete: Oooo Ooo Oo O Ooo"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Key Item: Ultra Fast Cough Syrup")
        )
    )

    # Flashbacks
    set_rule(
        loc("Quest Complete: Where We Used to Live"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Diplomacy")
    )

    set_rule(
        loc("Quest Complete: Target Practice"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and can_reach_loc(state, player, "Quest Complete: Where We Used to Live")
        )
    )

    set_rule(
        loc("Quest Complete: Ava"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    # Final boss goal
    set_rule(
        loc("Boss Defeated: Two-Beak God"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Floating")
            and has(state, player, "Dash (Bike Upgrade)")
            and has(state, player, "Hook (Bike Upgrade)")
            and has_shotgun_access(state, player)
        )
    )

    def has_hook_access(state) -> bool:
        return has(state, player, "Hook (Bike Upgrade)")

    def has_dash_access(state) -> bool:
        return has(state, player, "Dash (Bike Upgrade)")

    def post_rage(state) -> bool:
        return can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")

    def war_chapter_access(state) -> bool:
        return war_chapter(state)

    def post_diplomacy(state) -> bool:
        return can_reach_loc(state, player, "Quest Complete: Diplomacy")

    def post_radio_silence(state) -> bool:
        return can_reach_loc(state, player, "Quest Complete: Radio Silence")

    def post_big_tree(state) -> bool:
        return can_reach_loc(state, player, "Quest Complete: The Big Tree")

    def post_childless(state) -> bool:
        return can_reach_loc(state, player, "Quest Complete: Childless")

    # ===== Post Rage and Sorrow collectibles =====
    for name in [
        "Cassette Tape: Heartglaze Hope",
        "Cassette Tape: Overthinker",
        "Map Piece: Where Our Bikes Growl",
        "Map Piece: Where Our Ancestors Rest (Bottom)",
        "Map Piece: Where Our Ancestors Rest (Top)",
        "Puppy Gift: Handheld Console",
        "Puppy Gift: Toy Bike",
        "Blueprint: Shotgun",
        "Blueprint: Machine Gun",
    ]:
        set_rule(loc(name), lambda state: post_rage(state))

    # ===== War chapter collectibles =====
    for name in [
        "Cassette Tape: Lonely Mountain",
        "Map Piece: Where All Was Lost (Bottom)",
        "Map Piece: Where All Was Lost (Top)",
        "Map Piece: Where Iron Caresses the Sky (Bottom)",
        "Map Piece: Where Doom Fell",
        "Map Piece: Where Rock Bleeds (Left)",
        "Map Piece: Where Rock Bleeds (Center)",
        "Map Piece: Where Rock Bleeds (Right)",
        "Puppy Gift: Tangerine Tree",
        "Rusty Spring (Shotgun Material)",
    ]:
        set_rule(loc(name), lambda state: war_chapter_access(state))

    # ===== Shotgun-gated war / post-diplomacy collectibles =====
    for name in [
        "Cassette Tape: Heartbeat from the Last Century",
        "Map Piece: Where Iron Caresses the Sky (Top)",
        "Map Piece: Where the Waves Die (Left)",
        "Map Piece: Where the Waves Die (Right)",
        "Map Piece: Where Water Glistened (Borders)",
        "Map Piece: Where Water Glistened (1st Ship)",
        "Map Piece: Where Water Glistened (2nd Ship)",
        "Map Piece: Where Water Glistened (3rd Ship)",
        "Map Piece: Where Water Glistened (4th Ship)",
        "Puppy Gift: Great-Great-Grandma's Novella",
        "Blueprint: Sniper",
    ]:
        set_rule(
            loc(name),
            lambda state: post_diplomacy(state) and has_shotgun_access(state, player)
        )

    # ===== Hook-gated collectibles =====
    for name in [
        "Magnifying Glass (Sniper Rifle Material)",
    ]:
        set_rule(
            loc(name),
            lambda state: post_diplomacy(state) and has_hook_access(state)
        )

    # ===== Shotgun + Hook collectibles =====
    for name in [
        "Cassette Tape: The Hero",
        "Cassette Tape: The Final Hours",
        "Map Piece: The Big Tree",
        "Map Piece: Where Birds Came From (Left/Bottom)",
        "Map Piece: Where Birds Came From (Right/Top)",
        "Map Piece: Where Birds Lurk (Right)",
        "Puppy Gift: Toy Animal",
    ]:
        set_rule(
            loc(name),
            lambda state: post_diplomacy(state)
            and has_shotgun_access(state, player)
            and has_hook_access(state)
        )

    # ===== Shotgun-gated post-Heartglaze collectibles =====
    for name in [
        "Cassette Tape: Coming Home",
        "Titanium Plates (Machine Gun Material)",
    ]:
        set_rule(
            loc(name),
            lambda state: (
                can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
                and has_shotgun_access(state, player)
            )
        )

    # Radio Silence harpoon pieces.
    # These are physically found during Radio Silence after reaching Roy's boat/lighthouse route.
    for name in [
        "Key Item: Carved Whale Tooth",
        "Key Item: Long Rope",
    ]:
        set_rule(
            loc(name),
            lambda state: (
                can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
                and has_shotgun_access(state, player)
            )
        )

    # ===== Post-diplomacy loose collectibles =====
    for name in [
        "Cassette Tape: The Last Tear",
        "Cassette Tape: Through the Wind",
        "Map Piece: Where Birds Lurk (Left)",
    ]:
        set_rule(loc(name), lambda state: post_diplomacy(state))

    # ===== Later chapter collectibles =====
    set_rule(
        loc("Map Piece: Where Rust Weaves (Left)"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Map Piece: Where Rust Weaves (Center)"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Map Piece: Where Rust Weaves (Right)"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Map Piece: Where Rust Weaves (Center)"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Map Piece: Floating City (Control Area)"),
        lambda state: post_childless(state)
    )

    set_rule(
        loc("Map Piece: Floating City (Old Town)"),
        lambda state: post_childless(state)
    )

    set_rule(
        loc("Map Piece: Floating City (Hangar)"),
        lambda state: post_childless(state) and has_dash_access(state)
    )

    set_rule(
        loc("Map Piece: Floating City (Factory)"),
        lambda state: post_childless(state) and has_dash_access(state)
    )

    set_rule(
        loc("Map Piece: Floating City (City Facilities)"),
        lambda state: post_childless(state) and has_dash_access(state)
    )

    set_rule(
        loc("Missile (Rocket Launcher Material)"),
        lambda state:
            has(state, player, "Dash (Bike Upgrade)")
            and can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
    )

    set_rule(
        loc("Blueprint: Rocket Launcher"),
        lambda state: post_childless(state) and has_dash_access(state)
    )

    # ===== Progression key item pickup/check logic =====

    # Post Rage and Sorrow key items
    for name in [
        "Key Item: Jakob's Ashes",
        "Key Item: Guitar Strings",
    ]:
        set_rule(loc(name), lambda state: post_rage(state))

    # War chapter key items
    for name in [
        "Key Item: Fogg's Drumstick",
        "Key Item: Gutsy Gus's Gushing Gunfights",
        "Key Item: Iris",
        "Key Item: Erhu Strings",
        "Key Item: Sheet Music",
        "Key Item: Ultra Fast Cough Syrup",
        "Key Item: Brand-New Notebook",
        "Key Item: 1st Key To The Pit",
        "Key Item: 2nd Key To The Pit",
        "Key Item: 3rd Key To The Pit",
    ]:
        set_rule(loc(name), lambda state: war_chapter_access(state))

    # Diplomacy side quest key items
    for name in [
        "Key Item: Petey's Letter",
    ]:
        set_rule(loc(name), lambda state: post_diplomacy(state))

    # The Big Tree side quest key items
    for name in [
        "Key Item: Camilla's Special Herbs",
        "Key Item: Vitamin-Coated Bones",
        "Key Item: Jar Filled With Bugs",
        "Key Item: Pads",
        "Key Item: Moon Blossom",
    ]:
        set_rule(loc(name), lambda state: post_big_tree(state))

    # Radio Silence side quest key items
    for name in [
        "Key Item: Family Braid",
        "Key Item: Thistle Stems",
        "Key Item: Phalseria Sap",
        "Key Item: Banana Leaves",
    ]:
        set_rule(loc(name), lambda state: post_radio_silence(state))

    # Childless side quest key items
    for name in [
        "Key Item: Lhey's Diary",
        "Key Item: Large Seed",
        "Key Item: Gallon of Gasoline",
        "Key Item: Seashell",
    ]:
        set_rule(loc(name), lambda state: post_childless(state))

    # Hook / Big Tree area key items
    set_rule(
        loc("Key Item: Magical Book"),
        lambda state: post_big_tree(state) and has_hook_access(state)
    )

    # Puppy gift completion reward
    set_rule(
        loc("Puppy Gift: Ukulele"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Fogg's Only Wish")
            and can_reach_loc(state, player, "Quest Complete: The Last Erhu")
            and can_reach_loc(state, player, "Quest Complete: Clean Your Beak")
            and can_reach_loc(state, player, "Quest Complete: Desperately in Need of Music")
            and can_reach_loc(state, player, "Quest Complete: Sober Up")
            and can_reach_loc(state, player, "Quest Complete: Oooo Ooo Oo O Ooo")
        )
    )

    set_rule(
        loc("Cassette Tape: Visions of Red"),
        lambda state: war_chapter_access(state)
    )

    set_rule(
        loc("Cassette Tape: Trust Them"),
        lambda state: post_diplomacy(state)
    )

    set_rule(
        loc("Cassette Tape: My Destiny"),
        lambda state: post_radio_silence(state) and has_shotgun_access(state, player)
    )

    set_rule(
        loc("Cassette Tape: The End of the Road"),
        lambda state: post_big_tree(state) and has_hook_access(state)
    )

    set_rule(
        loc("Cassette Tape: Mother"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Floating")
    )

    set_rule(
        loc("Cassette Tape: Recurring Dream"),
        lambda state: post_diplomacy(state)
    )

    # Completion condition
    mw.completion_condition[player] = lambda state: state.can_reach_location("Boss Defeated: Two-Beak God", player)