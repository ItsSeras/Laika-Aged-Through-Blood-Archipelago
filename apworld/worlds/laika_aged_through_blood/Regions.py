from BaseClasses import Region

from .Locations import LOCATION_TABLE, LaikaLocation


def create_regions(world):
    menu = Region("Menu", world.player, world.multiworld)
    wasteland = Region("Wasteland", world.player, world.multiworld)

    world.multiworld.regions += [menu, wasteland]
    menu.connect(wasteland)

    # First pass:
    # I am putting every location in one abstract region on purpose.
    # Right now progression is modeled through quest access rules rather than
    # full map/room traversal. I can split this into real regions later.
    for location_name, location_id in LOCATION_TABLE.items():
        wasteland.locations.append(
            LaikaLocation(world.player, location_name, location_id, wasteland)
        )