"""Draws the "remind me" panel (`ReminderAskOverlay`, invariant 50p) over the hub, for judging by eye.

    python Tools/render_reminders.py                     # the system route, English
    python Tools/render_reminders.py --settings          # the route that opens the OS settings
    python Tools/render_reminders.py --lang de           # any Latin-script table
    python Tools/render_reminders.py --measure           # every Latin table, both routes; exit 1 on a tight fit

A mirror, not a gate for anything but `--measure`: every number here is `ReminderAskOverlay`'s and
the two are kept in step by hand (44d). The panel is `ModalView.MakePanel`'s: `panel_main` with the
orange ribbon title. Arabic is not drawn - the game shapes it through `ArabicText` at runtime and this
font has no Arabic forms - so `--measure` skips it and `ArabicTextTests` carries that table.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

W, H = K.W, K.H
LOC = K.REPO / "Assets/StreamingAssets/Content/loc"

# ReminderAskOverlay
PANEL_W, PANEL_H = 880.0, 880.0
SEAT_Y, GLOW, CHEST, CHEST_LIFT = 225.0, 300.0, 280.0, 44.0
BODY_Y, BODY_W, BODY_H, BODY_SIZE, BODY_FLOOR = 435.0, 700.0, 180.0, 32, 22
YES_Y, YES_W, YES_H, YES_SIZE, YES_FLOOR = 232.0, 620.0, 138.0, 46, 26
LATER_Y, LATER_W, LATER_H, LATER_SIZE, LATER_FLOOR = 92.0, 620.0, 126.0, 42, 24
KEY_PAD = 60.0                      # a TextButton's label room is its width less the skin's caps
INK = (92, 64, 46)                  # new Color(.36f, .25f, .18f)

LATIN = ["en", "es", "pt", "fr", "de", "it", "tr", "pl"]


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in rows}


def top_chest():
    """`ReminderAskOverlay.ChestIcon`: the grandest tier the task ladder names."""
    rules = json.loads((K.REPO / "Assets/StreamingAssets/Content/progression.json").read_text(encoding="utf-8"))
    tiers = rules.get("tasks", {}).get("tiers", [])
    return "Chest/" + tiers[-1]["id"] if tiers else "ic_chest"


def panel(lang, settings, draw=True):
    s = table(lang)
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    if draw:
        K.room(sheet)
        K.navbar(sheet, "home")
        sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    top = H / 2 - PANEL_H / 2
    K.paste(sheet, K.skin("panel_main", PANEL_W, PANEL_H), W / 2, H / 2)
    rib = K.skin("ribbon_orange", PANEL_W * .78, 130).rotate(1.6, Image.BICUBIC, expand=True)
    K.paste(sheet, rib, W / 2, top - 22)
    K.text(sheet, s["ui.reminders.ask_title"], W / 2, top - 28, 54, outline=4)

    K.paste(sheet, K.glow(GLOW, 2.2, (255, 209, 92), .5), W / 2, top + SEAT_Y)
    chest, _ = K.load(top_chest())
    K.paste(sheet, K.fit(chest, (CHEST, CHEST)), W / 2, top + SEAT_Y - CHEST_LIFT)

    body = s["ui.reminders.settings_body" if settings else "ui.reminders.ask_body"]
    sizes = {"body": (K.shrunk(sheet, body, W / 2, top + BODY_Y, BODY_W, BODY_H, BODY_SIZE, BODY_FLOOR,
                               fill=INK, outline=0), BODY_FLOOR)}

    yes_y = top + PANEL_H - YES_Y
    K.paste(sheet, K.skin("btn_green", YES_W, YES_H), W / 2, yes_y)
    yes = s["ui.reminders.open_settings" if settings else "ui.reminders.ask_yes"]
    sizes["yes"] = (K.one_line(sheet, yes, W / 2, yes_y - 4, YES_W - KEY_PAD, YES_SIZE, YES_FLOOR,
                               what="%s yes" % lang, outline=3), YES_FLOOR)

    later_y = top + PANEL_H - LATER_Y
    K.paste(sheet, K.skin("btn_blue", LATER_W, LATER_H), W / 2, later_y)
    sizes["later"] = (K.one_line(sheet, s["ui.reminders.not_now"], W / 2, later_y - 4, LATER_W - KEY_PAD,
                                 LATER_SIZE, LATER_FLOOR, what="%s later" % lang, outline=3), LATER_FLOOR)

    return sheet.convert("RGB"), sizes


def measure():
    tight = 0
    for lang in LATIN:
        for settings in (False, True):
            _, sizes = panel(lang, settings, draw=False)
            line = "  %-2s %-8s" % (lang, "settings" if settings else "system")
            for name, (got, floor) in sizes.items():
                flag = "  TIGHT" if got <= floor else ""
                tight += bool(flag)
                line += "  %s %d/%d%s" % (name, got, floor, flag)
            print(line)
    print("  ar       not drawn here (shaped at runtime; see ArabicTextTests)")
    return tight


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--settings", action="store_true", help="the route that opens the OS settings")
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--measure", action="store_true", help="every Latin table, both routes")
    ap.add_argument("--out", type=Path, default=Path("reminders.png"))
    args = ap.parse_args()

    if args.measure:
        tight = measure()
        print("  %d caption(s) at their floor" % tight)
        sys.exit(1 if tight else 0)

    out, sizes = panel(args.lang, args.settings)
    for name, (got, floor) in sizes.items():
        print("  %-6s %d (floor %d)" % (name, got, floor))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s" % args.out)


if __name__ == "__main__":
    main()
