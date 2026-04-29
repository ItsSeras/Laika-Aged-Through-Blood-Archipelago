from worlds.generic.Rules import set_rule

def has_shotgun_access(state, player) -> bool:
    return (
        state.has("Shotgun (Weapon)", player)
        or state.has("Rusty Spring (Shotgun Material)", player)
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

    # I am keeping this first pass deliberately quest-based.
    # I do not have every individual movement gate documented yet,
    # so I am using known quest requirements as the logic backbone for now.
    # Map pieces, cassettes, and puppy gifts are intentionally left without extra rules for now.
    # That keeps them as fast filler checks while I focus on major progression first.

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
            and has(state, player, "Heartglaze Flower")
        )
    )

    set_rule(
        loc("Quest Complete: The Remnants"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Jakob's Ashes")
        )
    )

    set_rule(
        loc("Quest Complete: Shake Off the Dead Leaves"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Remnants")
    )

    set_rule(
        loc("Boss Defeated: A Long Lost Woodcrawler"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
    )

    set_rule(
        loc("Heartglaze Flower"),
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
        loc("Quest Complete: Radio Silence"),
        lambda state: war_chapter(state)
    )

    set_rule(
        loc("Quest Complete: The Big Tree"),
        lambda state: war_chapter(state)
    )

    set_rule(
        loc("Quest Complete: Stargazing"),
        lambda state: (
            war_chapter(state)
            # Brand-New Notebook skipped until internal ID is confirmed.
        )
    )

    set_rule(
        loc("Boss Defeated: A Caterpillar Made of Sadness"),
        lambda state: (
            war_chapter(state)
            and has(state, player, "1st Key To The Pit")
            and has(state, player, "2nd Key To The Pit")
            and has(state, player, "3rd Key To The Pit")
        )
    )

    set_rule(
        loc("Quest Complete: The Bonehead's Hook"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Hook (Bike Upgrade)")
        )
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
            and has(state, player, "Gutsy Gus's Gushing Gunfights")
        )
    )

    set_rule(
        loc("Quest Complete: A Little Tomb Stone"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Diplomacy")
            and has(state, player, "Iris")
        )
    )

    set_rule(
        loc("Quest Complete: First Blood"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    set_rule(
        loc("Quest Complete: Life of the Party"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has(state, player, "Jar Filled With Bugs")
        )
    )

    set_rule(
        loc("Quest Complete: Bone Flour"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has(state, player, "Vitamin-Coated Bones")
        )
    )

    set_rule(
        loc("Quest Complete: A Break for Camilla"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: The Big Tree")
            and has_shotgun_access(state, player)
            and has(state, player, "Camilla's Special Herbs")
        )
    )

    set_rule(
        loc("Quest Complete: From Mother to Daughter"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Family Braid")
        )
    )

    set_rule(
        loc("Quest Complete: The Prophecy"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Petey's Letter")
        )
    )

    set_rule(
        loc("Quest Complete: Last Meal"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Bluelemon Berries")
        )
    )

    # Childless side branch
    for name in [
        "Quest Complete: For the Cash",
        "Quest Complete: Death on Demand",
        "Quest Complete: Family Tree",
        "Quest Complete: Fade Out",
        "Quest Complete: Just a little girl",
        "Quest Complete: We'll Never Know",
        "Quest Complete: High Spirits",
    ]:
        set_rule(
            loc(name),
            lambda state: can_reach_loc(state, player, "Quest Complete: Childless")
        )

    set_rule(
        loc("Quest Complete: Water Whispers"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Childless")
            and has(state, player, "Seashell")
        )
    )

    # Nightmares
    set_rule(
        loc("Quest Complete: Worse than Nightmares"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Thistle Stems")
        )
    )

    set_rule(
        loc("Quest Complete: Worse than Hives"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Phalseria Sap")
        )
    )

    set_rule(
        loc("Quest Complete: Worse than Stomach Flu"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Radio Silence")
    )

    # Musicians
    set_rule(
        loc("Quest Complete: Fogg's Only Wish"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Fogg's Drumstick")
        )
    )

    set_rule(
        loc("Quest Complete: The Last Erhu"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Hook (Bike Upgrade)")
            and has_shotgun_access(state, player)
            and has(state, player, "Erhu Strings")
        )
    )

    set_rule(
        loc("Quest Complete: Clean Your Beak"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
            and has(state, player, "Flute Cleaning Brush")
        )
    )

    set_rule(
        loc("Quest Complete: Desperately in Need of Music"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has(state, player, "Guitar Strings")
        )
    )

    set_rule(
        loc("Quest Complete: Sober Up"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: Radio Silence")
            and has(state, player, "Sheet Music")
        )
    )

    set_rule(
        loc("Quest Complete: Oooo Ooo Oo O Ooo"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    # Flashbacks
    set_rule(
        loc("Quest Complete: Where We Used to Live"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Diplomacy")
    )

    set_rule(
        loc("Quest Complete: Target Practice"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Radio Silence")
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

    set_rule(
    loc("Cassette Tape: Coming Home"),
    lambda state: (
        can_reach_loc(state, player, "Quest Complete: Diplomacy")
        and has_shotgun_level_2(state, player)
    )
    )
    # We do not have the gun parts randomized yet. Once we do, we can uncomment this.
    #set_rule(
    #loc("Gun Part: Titanium Plates"),
    #lambda state: (
    #    can_reach_loc(state, player, "Quest Complete: Diplomacy")
    #    and has_shotgun_level_2(state, player)
    #)
    #)

    set_rule(
    loc("Cassette Tape: Overthinker"),
    lambda state: can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
    )

    set_rule(
    loc("Puppy Gift: Toy Animal"),
    lambda state: (
        can_reach_loc(state, player, "Quest Complete: Diplomacy")
        and has_shotgun_level_2(state, player)
        and has(state, player, "Hook (Bike Upgrade)")
    )
    )

    # Completion condition
    mw.completion_condition[player] = lambda state: state.can_reach_location("Boss Defeated: Two-Beak God", player)