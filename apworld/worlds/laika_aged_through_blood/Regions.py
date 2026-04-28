from BaseClasses import Region

from .Locations import LOCATION_TABLE, LaikaLocation


def create_regions(world):
    menu = Region("Menu", world.player, world.multiworld)
    wasteland = Region("Wasteland", world.player, world.multiworld)

    world.multiworld.regions += [menu, wasteland]
    menu.connect(wasteland)

    # First pass:
    # All locations live in one abstract region.
    # Access is controlled by Rules.py through quest/item requirements.
    # Real map regions can be added later once movement/map traversal logic is fully documented.
    for location_name, location_id in LOCATION_TABLE.items():
        wasteland.locations.append(
            LaikaLocation(world.player, location_name, location_id, wasteland)
        )