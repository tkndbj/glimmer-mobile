"""Draws the first steps' three ceremonies, settled, for judging by eye - and measures every caption.

    python Tools/render_first_steps.py                    # all three, English, side by side
    python Tools/render_first_steps.py --scene turret     # the turret delivered (WardGiftOverlay)
    python Tools/render_first_steps.py --scene welcome    # "Well done!" with the reminder ask (WelcomeStepOverlay)
    python Tools/render_first_steps.py --scene welcome --no-ask    # ... with nothing to ask: one key
    python Tools/render_first_steps.py --scene chests     # two royal chests delivered (ChestGiftOverlay)
    python Tools/render_first_steps.py --lang de          # any Latin-script table
    python Tools/render_first_steps.py --measure          # every Latin table, every caption; exit 1 at a floor

A mirror, not a gate for anything but `--measure`: every number here is the three overlays' own and
the two are kept in step by hand (44d). What it draws is each panel's **settled** frame - after the
slam, once the dust has gone - because that is the frame a player reads; the motion is the
overlays' and `GiftTempoTests` holds its timing. The smoke is drawn as a few soft lobes on the floor
rather than the game's generated puffs: it is there to show where the dust lies, not what it looks
like. Arabic is not drawn - the game shapes it through `ArabicText` at runtime and this font has no
Arabic forms - so `--measure` skips it and `ArabicTextTests` carries that table.
"""

import argparse
import json
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import hudkit as K  # noqa: E402

W, H = K.W, K.H
LOC = K.REPO / "Assets/StreamingAssets/Content/loc"
SIEGE = K.REPO / "Assets/Game/Art/Siege"
PROGRESSION = K.REPO / "Assets/StreamingAssets/Content/progression.json"

LATIN = ["en", "es", "pt", "fr", "de", "it", "tr", "pl"]

# The room (GiftStage): a three-stop gradient, a shaft of warm light onto the floor, a pool.
SKY_TOP, SKY_MID, SKY_LOW = (11, 24, 48), (20, 40, 74), (10, 15, 28)
SPOT = (255, 233, 184)
DUST = (201, 183, 156)
GOLD = (255, 194, 60)
INK = (92, 64, 46)                  # new Color(.36f, .25f, .18f)

# WardGiftOverlay - every band a middle, y up from the canvas centre.
T_GROUND, T_TALL = -10.0, 440.0
T_WIDE = T_TALL * 1.72 / 2.15       # SiegeView.BodyWide / BodyTall
T_TITLE = (600.0, 960.0, 92.0, 76, 40)
T_CAPTION = (470.0, 900.0, 42.0, 36, 22)
T_NAME = (-140.0, 900.0, 90.0, 72, 36)
T_NOTE = (-280.0, 860.0, 100.0, 34, 22)
T_KEY = (-590.0, 560.0, 150.0, 56, 30)

# ChestGiftOverlay
C_GROUND, C_H = -30.0, 470.0
C_W = C_H * 176.0 / 244.0
C_PITCH = 360.0
C_PURSE = 650.0
C_TITLE = (510.0, 960.0, 92.0, 76, 40)
C_NAME = (-130.0, 900.0, 70.0, 58, 30)
C_LINE = (-250.0, 860.0, 90.0, 36, 22)
C_KEY = (-510.0, 560.0, 150.0, 56, 30)

# WelcomeStepOverlay - bands below the panel's top edge.
P_W, P_ONE_KEY_H, P_TWO_KEYS_H = 880.0, 1010.0, 1090.0
P_SEAT, P_SEAT_SIZE = 210.0, 220.0
P_NAME = (365.0, 760.0, 56.0, 50, 28)
P_UNLOCK = (480.0, 760.0, 160.0, 50, 34)
P_BOXES, P_BOX_MOST, P_BOX_GAP, P_BOX_ROW = 660.0, 120.0, 22.0, 760.0
P_YES = (232.0, 620.0, 138.0, 46, 26)
P_LATER = (96.0, 480.0, 108.0, 36, 22)
P_OK = (150.0, 560.0, 138.0, 46, 26)
KEY_PAD = 60.0                      # a TextButton's label room is its width less the skin's caps

SEAT_TINT = {"r": (242, 64, 79), "g": (84, 228, 140), "b": (79, 193, 255), "y": (255, 221, 87)}


def table(lang):
    rows = json.loads((LOC / ("%s.json" % lang)).read_text(encoding="utf-8"))["entries"]
    return {e["key"]: e["text"] for e in rows}


def steps():
    rules = json.loads(PROGRESSION.read_text(encoding="utf-8"))
    by_kind = {}
    for step in rules["firstSteps"]["steps"]:
        by_kind.setdefault(step["kind"], step)
    return rules, by_kind


def fmt(s, *args):
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


def y(cy):
    """A band's middle, from the room's centre-up units to the sheet's top-down pixels."""
    return H / 2 - cy


def turret_art(ward, seat):
    path = SIEGE / "Wards" / ("%s_%s.png" % (ward, seat))
    if not path.exists():
        path = SIEGE / "Wards" / ("%s.png" % ward)
    return Image.open(path).convert("RGBA")


# ------------------------------------------------------------------ the room
def room(sheet, ground):
    """GiftStage.Build, settled: the dark, the shaft, the pool, the vignette."""
    grad = Image.new("RGBA", (1, 3))
    grad.putpixel((0, 0), (*SKY_TOP, 255))
    grad.putpixel((0, 1), (*SKY_MID, 255))
    grad.putpixel((0, 2), (*SKY_LOW, 255))
    sky = grad.resize((W, H), Image.BICUBIC)
    sheet.alpha_composite(Image.blend(Image.new("RGBA", (W, H), (*K.GROUND, 255)), sky, .9))

    floor = y(ground)
    shaft = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(shaft)
    d.polygon([(W / 2 - 160, 0), (W / 2 + 160, 0), (W / 2 + 410, floor), (W / 2 - 410, floor)], fill=60)
    shaft = shaft.filter(ImageFilter.GaussianBlur(70))
    light = Image.new("RGBA", (W, H), (*SPOT, 0))
    light.putalpha(shaft)
    sheet.alpha_composite(light)

    pool = Image.new("L", (W, H), 0)
    ImageDraw.Draw(pool).ellipse([W / 2 - 500, floor - 130, W / 2 + 500, floor + 130], fill=105)
    pool = pool.filter(ImageFilter.GaussianBlur(40))
    lit = Image.new("RGBA", (W, H), (*SPOT, 0))
    lit.putalpha(pool)
    sheet.alpha_composite(lit)


def cracks(sheet, cx, ground, weight=1.0, seed=7):
    """GiftStage.Cracks, settled at their scar alpha."""
    rnd = random.Random(seed)
    d = ImageDraw.Draw(sheet)
    for i in range(7):
        a = math.radians(i / 7 * 360 + rnd.uniform(-14, 14))
        length = rnd.uniform(150, 300) * weight
        dx, dy = math.cos(a) * length, math.sin(a) * .30 * length
        d.line([(cx, y(ground)), (cx + dx, y(ground) - dy)], fill=(8, 5, 5, 97), width=rnd.randint(4, 7))


def dust(sheet, cx, ground, seed=3):
    """Where the settled dust lies: a few soft lobes along the floor either side."""
    rnd = random.Random(seed)
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for _ in range(14):
        x = cx + rnd.choice((-1, 1)) * rnd.uniform(160, 470)
        r = rnd.uniform(40, 95)
        d.ellipse([x - r, y(ground) - r * .7, x + r, y(ground) + r * .4], fill=(*DUST, 70))
    sheet.alpha_composite(layer.filter(ImageFilter.GaussianBlur(18)))


def stand(sheet, im, cx, ground, box_w, box_h, accent):
    """A gift standing on the floor: its glow and rays behind, its shadow under, its foot on the ground."""
    centre_y = y(ground + box_h * .5)
    K.paste(sheet, K.glow(int(box_h * 1.5), 2.0, accent, .42), cx, centre_y)
    shadow = Image.new("L", (int(box_w * 1.25), 70), 0)
    ImageDraw.Draw(shadow).ellipse([0, 0, shadow.width, shadow.height], fill=140)
    blob = Image.new("RGBA", shadow.size, (0, 0, 0, 0))
    blob.putalpha(shadow.filter(ImageFilter.GaussianBlur(10)))
    K.paste(sheet, blob, cx, y(ground))
    body = K.fit(im, (box_w, box_h))
    sheet.alpha_composite(body, (int(cx - body.width / 2), int(y(ground) - body.height)))


def caption(sheet, s, band, fill, outline=3):
    cy, w, h, size, floor = band
    return K.shrunk(sheet, s, W / 2, y(cy), w, h, size, floor, fill=fill, outline=outline), floor


def key(sheet, skin, s, band, what):
    cy, w, h, size, floor = band
    K.paste(sheet, K.skin(skin, w, h), W / 2, y(cy))
    return K.one_line(sheet, s, W / 2, y(cy) - 4, w - KEY_PAD, size, floor, what=what, outline=3), floor


# ------------------------------------------------------------------ the scenes
def turret(lang, draw=True):
    s = table(lang)
    _, kinds = steps()
    step = kinds["turret"]
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    accent = SEAT_TINT[step["colour"]]
    if draw:
        room(sheet, T_GROUND)
        cracks(sheet, W / 2, T_GROUND)
        dust(sheet, W / 2, T_GROUND)
        stand(sheet, turret_art(step["ward"], step["colour"]), W / 2, T_GROUND, T_WIDE, T_TALL, accent)

    lift = tuple(int(c + (255 - c) * .35) for c in accent)
    sizes = {
        "title": caption(sheet, s["ui.steps.gift_title"], T_TITLE, GOLD, 4),
        "caption": caption(sheet, s["ui.steps.turret_title"], T_CAPTION, K.CREAM, 2),
        "name": caption(sheet, s["ward.%s.name" % step["ward"]], T_NAME, lift, 4),
        "note": caption(sheet, s["ward.%s.note" % step["ward"]], T_NOTE, K.CREAM, 2),
        "equip": key(sheet, "btn_green", s["ui.loadout.stand"], T_KEY, "%s equip" % lang),
    }
    return sheet.convert("RGB"), sizes


def chests(lang, draw=True):
    s = table(lang)
    _, kinds = steps()
    step = kinds["chests"]
    count = int(step["count"])
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    if draw:
        room(sheet, C_GROUND)
        chest, _ = K.load("Chest/" + step["tier"])
        for i in range(count):
            cx = W / 2 + (i - (count - 1) * .5) * C_PITCH
            cracks(sheet, cx, C_GROUND, .7 if i else 1.0, seed=11 + i)
        dust(sheet, W / 2, C_GROUND)
        for i in range(count):
            cx = W / 2 + (i - (count - 1) * .5) * C_PITCH
            stand(sheet, chest, cx, C_GROUND, C_W, C_H, GOLD)

        # The purse: the hub's three readouts, drawn over the room.
        for x, icon in ((-322, "ic_heart"), (0, "ad_coin"),
                        (322, "ic_gem")):
            K.paste(sheet, K.skin("Hud/trough", 304, 96), W / 2 + x, y(C_PURSE))
            try:
                im, _ = K.load(icon)
                K.paste(sheet, K.fit(im, (62, 62)), W / 2 + x - 152 + 66, y(C_PURSE))
            except FileNotFoundError:
                pass
            K.text(sheet, "1,250", W / 2 + x + 34, y(C_PURSE), 37, outline=3)

    tier_name = s["chest.%s.name" % step["tier"]]
    sizes = {
        "title": caption(sheet, s["ui.steps.gift_title"], C_TITLE, GOLD, 4),
        "name": caption(sheet, fmt(s["ui.steps.chest_count"], tier_name, count), C_NAME, GOLD, 4),
        "line": caption(sheet, s["ui.steps.chests_line"], C_LINE, K.CREAM, 2),
        "open": key(sheet, "btn_green", s["ui.steps.open"], C_KEY, "%s open" % lang),
    }
    return sheet.convert("RGB"), sizes


def welcome(lang, ask=True, settings=False, done=1, draw=True):
    s = table(lang)
    rules, _ = steps()
    quest = rules["welcome"]["quests"][0]
    days = int(quest["days"])
    ready = done >= days
    ask = ask and not ready
    P_H = P_TWO_KEYS_H if ask else P_ONE_KEY_H
    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    if draw:
        K.room(sheet)
        sheet.alpha_composite(Image.new("RGBA", (W, H), (0, 0, 0, int(255 * .72))))

    top = H / 2 - P_H / 2
    K.paste(sheet, K.skin("panel_main", P_W, P_H), W / 2, H / 2)
    rib = K.skin("ribbon_orange", P_W * .78, 130).rotate(1.6, Image.BICUBIC, expand=True)
    K.paste(sheet, rib, W / 2, top - 22)
    sizes = {"title": (K.one_line(sheet, s["ui.steps.well_done"], W / 2, top - 28, P_W * .78 - 120, 54, 30,
                                  what="%s title" % lang, outline=4), 30)}

    accent = SEAT_TINT[quest["colour"]]
    if draw:
        K.paste(sheet, K.glow(int(P_SEAT_SIZE * 1.8), 2.0, accent, .45), W / 2, top + P_SEAT)
        K.paste(sheet, K.skin("Hud/slot", P_SEAT_SIZE, P_SEAT_SIZE), W / 2, top + P_SEAT)
        body = K.fit(turret_art(quest["ward"], quest["colour"]), (P_SEAT_SIZE * .62, P_SEAT_SIZE * .78))
        K.paste(sheet, body, W / 2, top + P_SEAT - 6)

    def band(b, text, fill, outline):
        dy, w, h, size, floor = b
        return K.shrunk(sheet, text, W / 2, top + dy, w, h, size, floor, fill=fill, outline=outline), floor

    sizes["name"] = band(P_NAME, s["ward.%s.name" % quest["ward"]], K.CREAM, 3)
    sizes["unlock"] = band(P_UNLOCK, s["ui.steps.unlocked" if ready else "ui.steps.unlock_turret"], GOLD, 4)

    n = days
    size = min(P_BOX_MOST, (P_BOX_ROW - P_BOX_GAP * (n - 1)) / n)
    width = n * size + (n - 1) * P_BOX_GAP
    check, _ = K.load("ic_check")
    for i in range(n):
        bx = W / 2 - width / 2 + size / 2 + i * (size + P_BOX_GAP)
        box = K.skin("Hud/slot", size, size)
        if i >= done:
            box = K.tint(box, (255, 255, 255), .55)
        K.paste(sheet, box, bx, top + P_BOXES)
        if i < done:
            K.paste(sheet, K.tint(K.fit(check, (size * .66, size * .66)), K.MINT), bx, top + P_BOXES - 2)

    def foot(b, skin, text, what):
        dy, w, h, fsize, floor = b
        ky = top + P_H - dy
        K.paste(sheet, K.skin(skin, w, h), W / 2, ky)
        return K.one_line(sheet, text, W / 2, ky - 4, w - KEY_PAD, fsize, floor, what=what, outline=3), floor

    if ready:
        sizes["claim"] = foot(P_OK, "btn_green", s["ui.steps.claim"], "%s claim" % lang)
    elif ask:
        sizes["yes"] = foot(P_YES, "btn_green", s["ui.reminders.open_settings" if settings else "ui.reminders.ask_yes"],
                            "%s yes" % lang)
        sizes["later"] = foot(P_LATER, "btn_blue", s["ui.reminders.not_now"], "%s later" % lang)
    else:
        sizes["ok"] = foot(P_OK, "btn_green", s["ui.common.got_it"], "%s ok" % lang)

    return sheet.convert("RGB"), sizes


SCENES = {
    "turret": lambda lang, a: turret(lang),
    "welcome": lambda lang, a: welcome(lang, ask=not a.no_ask, settings=a.settings, done=a.done),
    "chests": lambda lang, a: chests(lang),
}


def measure():
    tight = 0
    for lang in LATIN:
        for name, (_, sizes) in (("turret", turret(lang, draw=False)),
                                 ("chests", chests(lang, draw=False)),
                                 ("welcome", welcome(lang, ask=True, draw=False)),
                                 ("w-settings", welcome(lang, ask=True, settings=True, draw=False)),
                                 ("w-quiet", welcome(lang, ask=False, draw=False)),
                                 ("w-ready", welcome(lang, ask=False, done=3, draw=False))):
            line = "  %-2s %-10s" % (lang, name)
            for part, (got, floor) in sizes.items():
                flag = " TIGHT" if got <= floor else ""
                tight += bool(flag)
                line += " %s %d/%d%s" % (part, got, floor, flag)
            print(line)
    print("  ar          not drawn here (shaped at runtime; see ArabicTextTests)")
    return tight


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--scene", choices=sorted(SCENES), help="one scene; all three side by side when left out")
    ap.add_argument("--lang", default="en", choices=LATIN)
    ap.add_argument("--no-ask", action="store_true", help="welcome: nothing to ask, one key")
    ap.add_argument("--settings", action="store_true", help="welcome: the ask that opens the OS settings")
    ap.add_argument("--done", type=int, default=1, help="welcome: days already counted")
    ap.add_argument("--measure", action="store_true", help="every Latin table, every caption")
    ap.add_argument("--out", type=Path, default=Path("first_steps.png"))
    args = ap.parse_args()

    if args.measure:
        tight = measure()
        print("  %d caption(s) at their floor" % tight)
        sys.exit(1 if tight else 0)

    names = [args.scene] if args.scene else ["turret", "welcome", "chests"]
    frames = []
    for name in names:
        frame, sizes = SCENES[name](args.lang, args)
        print("  %-8s %s" % (name, "  ".join("%s %d/%d" % (k, g, f) for k, (g, f) in sizes.items())))
        frames.append(frame)

    out = Image.new("RGB", (W * len(frames) + 40 * (len(frames) - 1), H), (0, 0, 0))
    for i, frame in enumerate(frames):
        out.paste(frame, (i * (W + 40), 0))
    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)
    print("  wrote %s" % args.out)


if __name__ == "__main__":
    main()
