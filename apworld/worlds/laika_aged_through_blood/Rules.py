from worlds.generic.Rules import set_rule

def has_shotgun_access(state, player) -> bool:
    return (
        state.has("Shotgun (Weapon)", player)
        or state.has("Rusty Spring (Shotgun Material)", player)
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
    set_rule(
        loc("Quest Complete: Rage and Sorrow"),
        lambda state: True
    )

    set_rule(
        loc("Quest Complete: A Heart for Poochie"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Rage and Sorrow")
    )

    # War chapter
    # These can be tackled in any order after A Heart for Poochie.
    set_rule(
        loc("Quest Complete: Diplomacy"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    set_rule(
        loc("Quest Complete: Radio Silence"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    set_rule(
        loc("Quest Complete: Old Warfare"),
        lambda state: (
            can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
            and has_shotgun_access(state, player)
        )
    )

    set_rule(
        loc("Quest Complete: The Big Tree"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
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
        loc("Quest Complete: Shake Off the Dead Leaves"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    set_rule(
        loc("Quest Complete: Stargazing"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    set_rule(
        loc("Quest Complete: The Remnants"),
        lambda state: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
    )

    set_rule(
        loc("Quest Complete: A New Sheriff in Town"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Diplomacy")
    )

    set_rule(
        loc("Quest Complete: A Little Tomb Stone"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Diplomacy")
    )

    set_rule(
        loc("Quest Complete: First Blood"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    set_rule(
        loc("Quest Complete: Life of the Party"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    set_rule(
        loc("Quest Complete: Bone Flour"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    set_rule(
        loc("Quest Complete: A Break for Camilla"),
        lambda state: can_reach_loc(state, player, "Quest Complete: The Big Tree")
    )

    set_rule(
        loc("Quest Complete: From Mother to Daughter"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Radio Silence")
    )

    set_rule(
        loc("Quest Complete: The Prophecy"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Radio Silence")
    )

    set_rule(
        loc("Quest Complete: Last Meal"),
        lambda state: can_reach_loc(state, player, "Quest Complete: Radio Silence")
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
        "Quest Complete: Water Whispers",
    ]:
        set_rule(
            loc(name),
            lambda state, quest_name=name: can_reach_loc(state, player, "Quest Complete: Childless")
        )

    # Nightmares
    for name in [
        "Quest Complete: Worse than Nightmares",
        "Quest Complete: Worse than Hives",
        "Quest Complete: Worse than Stomach Flu",
    ]:
        set_rule(
            loc(name),
            lambda state, quest_name=name: can_reach_loc(state, player, "Quest Complete: Radio Silence")
        )

    # Musicians
    for name in [
        "Quest Complete: Fogg's Only Wish",
        "Quest Complete: The Last Erhu",
        "Quest Complete: Clean Your Beak",
        "Quest Complete: Desperately in Need of Music",
        "Quest Complete: Sober Up",
        "Quest Complete: Oooo Ooo Oo O Ooo",
    ]:
        set_rule(
            loc(name),
            lambda state, quest_name=name: can_reach_loc(state, player, "Quest Complete: A Heart for Poochie")
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

    # Completion condition
    mw.completion_condition[player] = lambda state: state.can_reach_location("Boss Defeated: Two-Beak God", player)