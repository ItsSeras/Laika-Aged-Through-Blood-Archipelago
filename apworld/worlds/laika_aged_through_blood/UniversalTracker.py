from __future__ import annotations

import json
from typing import Any


LAIKA_UT_MAP_ORDER = [
    "Start / Tutorial Area",
    "Where We Live",
    "Where Mother Groans",
    "Where We Dream",
    "Where Shaza Tinkers",
    "Where Rules Are Made",
    "Where We Forget",
    "Where Our Bikes Growl",
    "Where Our Ancestors Rest",
    "Where Doom Fell",
    "Where the Waves Die",
    "Where Rust Weaves",
    "Where Rock Bleeds",
    "Where Water Glistened",
    "Where All Was Lost",
    "Where Birds Came From",
    "The Big Tree",
    "Where Iron Caresses the Sky",
    "Where Birds Lurk",
    "Floating City",
]


def laika_ut_map_index(data: Any) -> int:
    """
    Resolve the Universal Tracker map tab index from AP data storage.

    The C# client currently writes values shaped like:
        {"index": int, "region": str, "nonce": int}

    Universal Tracker may pass that value back in several forms depending on
    whether it came directly from data storage, a SetReply wrapper, or a JSON
    string. Unknown or malformed values safely fall back to the tutorial map.
    """

    def valid_index(value: Any) -> int | None:
        try:
            index = int(value)
        except (TypeError, ValueError):
            return None

        if 0 <= index < len(LAIKA_UT_MAP_ORDER):
            return index

        return None

    def parse_text(text: str) -> int | None:
        text = text.strip()

        if len(text) >= 2 and text[0] == '"' and text[-1] == '"':
            text = text[1:-1].strip()

        direct = valid_index(text)
        if direct is not None:
            return direct

        if text in LAIKA_UT_MAP_ORDER:
            return LAIKA_UT_MAP_ORDER.index(text)

        if (text.startswith("{") and text.endswith("}")) or (
            text.startswith("[") and text.endswith("]")
        ):
            try:
                return extract_index(json.loads(text))
            except Exception:
                return None

        return None

    def extract_index(value: Any) -> int | None:
        if value is None:
            return None

        direct = valid_index(value)
        if direct is not None:
            return direct

        if isinstance(value, str):
            return parse_text(value)

        if isinstance(value, dict):
            if "index" in value:
                found = valid_index(value.get("index"))
                if found is not None:
                    return found

            for key in ("value", "Value", "data", "Data", "current", "Current"):
                if key in value:
                    found = extract_index(value[key])
                    if found is not None:
                        return found

            for key in ("operations", "Operations"):
                if key in value:
                    found = extract_index(value[key])
                    if found is not None:
                        return found

            return None

        get_method = getattr(value, "get", None)
        if callable(get_method):
            try:
                found = valid_index(get_method("index", None))
                if found is not None:
                    return found
            except Exception:
                pass

            for key in (
                "value",
                "Value",
                "data",
                "Data",
                "current",
                "Current",
                "operations",
                "Operations",
            ):
                try:
                    found = extract_index(get_method(key, None))
                    if found is not None:
                        return found
                except Exception:
                    pass

        for key in ("index", "value", "operations"):
            try:
                found = extract_index(value[key])
                if found is not None:
                    return found
            except Exception:
                pass

        if isinstance(value, (list, tuple)):
            for child in value:
                found = extract_index(child)
                if found is not None:
                    return found

        return parse_text(str(value))

    index = extract_index(data)
    return index if index is not None else 0


tracker_world = {
    "map_page_folder": "tracker",
    "map_page_maps": "maps/maps.json",
    "map_page_locations": "locations/locations.json",

    # The C# client writes this whenever the player enters or loads a major region.
    "map_page_setting_key": "laika_current_region_{team}_{player}",
    "map_page_index": laika_ut_map_index,
}