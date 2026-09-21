# -*- coding: utf-8 -*-
"""Draws the invite page at the size a phone draws it.

    python Tools/render_referral.py                   # a fresh account: a code, nobody bound yet
    python Tools/render_referral.py --state invited   # typed a friend's code, chapter half done
    python Tools/render_referral.py --state welcome   # the welcome chest waiting to be opened
    python Tools/render_referral.py --state climb     # three friends finished: one paid, one half paid and lit
    python Tools/render_referral.py --state done      # twelve friends, every chest paid
    python Tools/render_referral.py --state offline   # never reached the server: no code yet
    python Tools/render_referral.py --contact         # all six side by side

**Why this exists.** Every question this page raises is a picture. Does the code read at a
glance, and does the well read as a thing to tap? Does the share key read as the page's one
action on a fresh account, and the lit rung as the one action once a friend has finished?
Does the offer row read as an invitation rather than a status bar? No numeric gate in this
project can open a PNG, so the page is judged here, the way the streak page is (`CRAFT.md`).

**It reads the shipped content.** The payments, the tiers, the milestone's name and every string
come out of `progression.json`, `manifest.json` and `loc/en.json`, so a retune redraws rather
than going stale — a mirror with its own numbers answers questions about a screen the game
does not draw (invariant 44d). Every constant is named after the `ReferralScreen` field it
mirrors.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw                            # noqa: E402
import hudkit as K                                          # noqa: E402

W, H = K.W, K.H

CONTENT = K.REPO / "Assets" / "StreamingAssets" / "Content"
LOC = CONTENT / "loc" / "en.json"
TABLE = CONTENT / "progression.json"
MANIFEST = CONTENT / "manifest.json"

# ReferralScreen
CHROME = 92.0
BANNER_H = 138.0
HERO_H = 300.0
#: `ReferralScreen.OfferH` - the same as a row, because in one of its two shapes it is one.
OFFER_H = 196.0
HEADING_H = 62.0
WIDTH = 1000.0
ROW_H, ROW_GAP = 196.0, 12.0

#: `ReferralScreen.CellH` - one `GridView` cell. The card is `ROW_H` and is centred in it, so
#: half the gap sits above every card and half below, which is what removed the special case
#: for the last row. A card therefore sits `ROW_GAP / 2` lower than it did when this board laid
#: itself out, and the whole board is `ROW_GAP` taller.
CELL_H = ROW_H + ROW_GAP

#: `ReferralScreen.OfferGap` and `BoardFoot`.
OFFER_GAP, BOARD_FOOT = 14.0, 20.0

#: `ReferralScreen.KeyW`, `KeyH`, `MarkH`, `MarkType`, `MarkLeast` and `MarkRoom` - the streak
#: board's numbers, because this is the streak board's row: the COLLECT key and the pill that
#: stands in the same place share one width, and the pill's line is fitted on write from its
#: design size rather than drawn at whatever it was built at.
KEY_W, KEY_H, MARK_H = 212.0, 84.0, 62.0
MARK_TYPE, MARK_LEAST = 23, 14
MARK_ROOM = KEY_W - 20.0 - 16.0
SEAT_SIZE, SEAT_X, REWARD_TALL = 160.0, 118.0, 124.0
TEXT_X, TEXT_W = 220.0, 450.0
CODE_W, CODE_H, SHARE_W, SHARE_H = 470.0, 96.0, 300.0, 104.0

#: `ChestPack.Fill` / `Aspect` / `Lift`: a chest is placed in *drawn* units (48j).
CHEST_FILL, CHEST_ASPECT, CHEST_LIFT = 155.0 / 244.0, 176.0 / 244.0, 38.5 / 155.0


def strings():
    table = json.loads(LOC.read_text(encoding="utf-8"))
    return {e["key"]: e["text"] for e in table["entries"]}


LOCS = strings()
TABLE_JSON = json.loads(TABLE.read_text(encoding="utf-8"))
REFERRAL = TABLE_JSON.get("referral") or {}
MAX_BOUND = REFERRAL.get("maxBound") or 50
INVITEE = REFERRAL.get("invitee") or {"tier": "royal", "count": 2}
PER = REFERRAL.get("perInvitee") or {"tier": "royal", "count": 2}
INVITEE_TIER = INVITEE.get("tier") or "royal"
PER_TIER = PER.get("tier") or "royal"

MILESTONE = REFERRAL.get("milestoneChapter") or ""
TIERS = [t["id"] for t in (TABLE_JSON.get("tasks") or {}).get("tiers") or []]


def txt(key, *args):
    s = LOCS.get(key, key)
    for i, a in enumerate(args):
        s = s.replace("{%d}" % i, str(a))
    return s


CHAPTER_NAME = txt("chapter.%s.name" % MILESTONE) if MILESTONE else "?"


def chest_art():
    out = {}
    for tier in TIERS:
        im = Image.open(K.UI / "Chest" / ("%s.png" % tier)).convert("RGBA")
        out[tier] = (im, im.getchannel("A").getbbox())
    return out


ART = chest_art()


#: `ReferralScreen.Paint`'s three reward tints: full colour where there is something to take,
#: the cool near-white on a row already paid, and the tasks ladder's grey on one not reached.
REWARD_TINT = {"paid": (230, 240, 255), "ahead": (199, 209, 230)}


def drawn(tier, h, face="lit"):
    im, box = ART[tier]
    w = h * (box[2] - box[0]) / float(box[3] - box[1])
    out = im.crop(box).resize((max(1, int(round(w))), max(1, int(round(h)))), Image.LANCZOS)
    tint = REWARD_TINT.get(face)
    return K.tint(out, tint) if tint else out


def icon(name, box):
    return K.fit(Image.open(K.UI / ("%s.png" % name)).convert("RGBA"), box)


# ------------------------------------------------------------------ the chrome
def header(sheet, y):
    """`ReferralScreen.BuildHeader` - the name, what it does, then the wallet."""
    cy = y + BANNER_H / 2
    K.paste(sheet, K.skin("sq_blue", CHROME, CHROME), 76, cy)
    K.paste(sheet, K.skin("sq_orange", CHROME, CHROME), W - 76, cy)
    for x, glyph in ((76, "ic_left"), (W - 76, "ic_info")):
        K.paste(sheet, K.tint(icon(glyph, (46, 46)), K.CREAM), x, cy)

    ribbon = K.skin("Hud/title", 720, BANNER_H)
    plate = Image.new("RGBA", ribbon.size, (0, 0, 0, 0))
    plate.alpha_composite(ribbon)
    K.text(plate, txt("ui.referral.title").upper(), plate.width / 2, plate.height / 2 - 6, 42, fill=K.SUN)
    K.paste(sheet, plate, W / 2, cy)
    y += BANNER_H + 4

    K.shrunk(sheet, txt("ui.referral.subtitle", CHAPTER_NAME), W / 2, y + 16, 880, 32, 24, 15,
             fill=(219, 229, 255), outline=0)
    y += 32 + 14

    money = [(-232, K.ROSE, "ic_heart", "5/5"), (0, K.GOLD, "Coin/f0", "12,480"),
             (232, K.BLOOM, "ic_gem", "1,240")]
    for dx, colour, glyph, value in money:
        cx = W / 2 + dx
        K.paste(sheet, K.skin("Hud/trough", 212, 78), cx, y + CHROME / 2)
        gx = cx - 106 + 52
        K.paste(sheet, K.glow(96, 2.0, colour, .30), gx, y + CHROME / 2)
        K.paste(sheet, icon(glyph, (52, 52)), gx, y + CHROME / 2)
        K.text(sheet, value, cx + 24, y + CHROME / 2, 30)

    return y + CHROME + 16


def hero(sheet, y, code, bound, finished):
    """`ReferralScreen.BuildHero` - the gift, the code in its well, the share key, the tally."""
    cy = y + HERO_H / 2
    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/panel", WIDTH, HERO_H), W / 2, cy)

    band = Image.new("RGBA", (int(WIDTH) - 12, int(HERO_H) - 12), (0, 0, 0, 0))
    fan = K.rays(256, 14).resize((760, 760), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .14)))
    band.alpha_composite(lit, (150 - 380 - 6, band.height // 2 - 380 - 10))
    K.paste(sheet, band, W / 2, cy)

    gx, gy = left + 128, cy - 6
    K.paste(sheet, K.glow(260, 2.0, K.SUN, .30), gx, gy)
    K.paste(sheet, icon("ic_gift", (150, 150)), gx, gy)

    text_left = left + 236
    K.text(sheet, txt("ui.referral.your_code").upper(), text_left, cy - 92, 24,
           fill=(255, 243, 220), outline=2, anchor="l")

    wx = text_left + CODE_W / 2
    K.paste(sheet, K.skin("Hud/trough", CODE_W, CODE_H), wx, cy - 26)
    if code:
        K.text(sheet, code[:4] + "-" + code[4:], wx, cy - 26, 46, fill=K.GOLD, outline=3)
    else:
        K.shrunk(sheet, txt("ui.referral.code_unknown"), wx, cy - 26, CODE_W - 40, 60, 24, 18,
                 fill=(255, 243, 220), outline=2)

    # the share key: btn_green with the share glyph before the word (UIKit.TextButton)
    bx = W / 2 + WIDTH / 2 - 176
    K.paste(sheet, K.skin("btn_green", SHARE_W, SHARE_H), bx, cy - 26)
    caption = txt("ui.referral.share")
    glyph = K.tint(icon("ic_share", (40, 40)), K.CREAM)
    gap = 10
    text_w = K.font(34).getlength(caption)
    block = glyph.width + gap + text_w
    lift = SHARE_H * 0.0231
    K.paste(sheet, glyph, bx - block / 2 + glyph.width / 2, cy - 26 - lift)
    K.text(sheet, caption, bx - block / 2 + glyph.width + gap, cy - 26 - lift, 34, anchor="l")

    room = WIDTH - 236 - 40
    K.shrunk(sheet, txt("ui.referral.tally", bound, MAX_BOUND, finished, CHAPTER_NAME),
             text_left + room / 2, cy + 72, room, 32, 24, 15, fill=(255, 243, 220), outline=2)

    return y + HERO_H + 14


def offer_row(sheet, y):
    """`ReferralScreen.BuildOffer`, the Code shape - an invitation to type a friend's code."""
    cy = y + OFFER_H / 2
    left = W / 2 - WIDTH / 2
    K.paste(sheet, K.skin("Hud/plate_blue", WIDTH, OFFER_H), W / 2, cy)

    K.paste(sheet, K.glow(286, 2.0, K.AQUA, .24), left + SEAT_X, cy)
    K.paste(sheet, icon("ic_key", (132, 132)), left + SEAT_X, cy)

    hint_w = TEXT_W
    K.text(sheet, txt("ui.referral.offer_title"), left + TEXT_X, cy - 54, 36, anchor="l")
    K.shrunk(sheet, txt("ui.referral.offer_hint", CHAPTER_NAME), left + TEXT_X + hint_w / 2, cy + 28,
             hint_w, 96, 26, 16, fill=(255, 243, 220), outline=0)

    bx = W / 2 + WIDTH / 2 - 150
    K.paste(sheet, K.skin("btn_blue", 272, 104), bx, cy)
    K.shrunk(sheet, txt("ui.referral.enter"), bx, cy - 104 * 0.0231, 236, 60, 30, 16)

    return y + OFFER_H + OFFER_GAP


def welcome_row(sheet, y, state, cleared, total, opened=0):
    """`ReferralScreen.BuildOffer`, the Welcome shape - the invitee's chests on their own plate."""
    cy = y + OFFER_H / 2
    count = INVITEE.get("count", 1)
    if state == "paid":
        sub = txt("ui.referral.welcome_done_hint", CHAPTER_NAME)
    elif state == "ahead":
        sub = txt("ui.referral.welcome_hint", CHAPTER_NAME)
    elif opened:
        sub = txt("ui.referral.opened", opened, count)
    else:
        sub = txt("ui.referral.pays", count, txt("chest.%s.name" % INVITEE_TIER))
    row(sheet, W / 2, cy, OFFER_H, "Hud/plate_blue", INVITEE_TIER, count,
        txt("ui.referral.welcome_title").upper(), sub, state,
        txt("ui.referral.settling").upper() if cleared >= total and state == "ahead"
        else txt("ui.referral.progress", cleared, total))
    return y + OFFER_H + OFFER_GAP


def heading(sheet, y):
    cy = y + HEADING_H / 2
    left = W / 2 - WIDTH / 2
    K.text(sheet, txt("ui.referral.heading", CHAPTER_NAME).upper(), left + 8, cy, 28, fill=K.GOLD, anchor="l")
    return y + HEADING_H


# ------------------------------------------------------------------- the board
def aura(sheet, cx, cy, size):
    K.paste(sheet, K.glow(int(size * 2.4), 1.7, K.GOLD, .46), cx, cy)
    fan = K.rays(256, 14).resize((int(size * 2.15), int(size * 2.15)), Image.LANCZOS)
    lit = Image.new("RGBA", fan.size, (*K.SUN, 0))
    lit.putalpha(fan.point(lambda v: int(v * .34)))
    sheet.alpha_composite(lit, (int(cx - fan.width / 2), int(cy - fan.height / 2)))


def pool_art(h):
    """`ReferralScreen.Furnish`'s pool: `Art.Glow(128, 1.35)` at the bright end of the breath
    `Shine` runs between (.42 and .80), sized `Width + 150` by `RowH + 130`."""
    return K.glow(420, 1.35, K.SUN, .80).resize((int(WIDTH + 150), int(h + 130)), Image.LANCZOS)


def row(sheet, cx, cy, h, plate, tier, count, title, sub, state, mark, flat=False, halo=True):
    """`ReferralScreen.Furnish` + `Paint`.

    `state` is 'paid', 'lit', 'waiting' or 'ahead'. One answer at the right end at a time:
    a COLLECT key, a seal, or the pill saying how far along the count is.

    `flat` draws the row without its own `CanvasGroup` fade, so the caller can apply that fade
    to the finished picture the way a group does - to everything on the card at once, rather
    than to each piece as it lands. It is a *parameter* rather than a spelling of `state`,
    because the recursion has to keep drawing the same state: written as a fifth state the row
    came back with a progress pill where its seal should have been.

    `halo` draws the pool of light under a lit row. **The board turns it off and paints the
    pool itself, first**, because the pool is 150 units wider than the card and 130 taller and
    the screen draws it *under every plate on the board* - `RowWidgets.Sink` puts the lit cell
    at the bottom of the sibling order for exactly that. Painted here, row by row, it lands on
    top of the card above instead, which is a mirror quietly answering a question about a
    screen the game does not draw (invariant 44d). The welcome row keeps it: its pool really
    is drawn straight under its own plate and over the hero above it.
    """
    lit = state == "lit"
    paid = state == "paid"
    waiting = state in ("lit", "waiting")

    # **A paid row is the only one that steps back**, at `Paint`'s .62 - the tasks page's rule.
    # This used to fade an *unreached* row to .74, which on a board where every row is unreached
    # is a page of ghosts; the mirror drew that faithfully and nobody read the picture until the
    # owner played it. See `ReferralScreen.Paint`.
    if paid and not flat:
        cell = Image.new("RGBA", (int(WIDTH + 200), int(h + 60)), (0, 0, 0, 0))
        row(cell, cell.width / 2, cell.height / 2, h, plate, tier, count, title, sub, state, mark,
            flat=True, halo=halo)
        cell.putalpha(cell.getchannel("A").point(lambda v: int(v * .62)))
        sheet.alpha_composite(cell, (int(cx - cell.width / 2), int(cy - cell.height / 2)))
        return

    if lit and halo:
        K.paste(sheet, pool_art(h), cx, cy)

    K.paste(sheet, K.skin(plate, WIDTH, h), cx, cy)

    if lit:
        d = ImageDraw.Draw(sheet)
        d.rounded_rectangle([cx - WIDTH / 2 + 3, cy - h / 2 + 3, cx + WIDTH / 2 - 3, cy + h / 2 - 3],
                            radius=30, outline=(255, 244, 206, 255), width=7)

    left = cx - WIDTH / 2
    sx = left + SEAT_X
    if lit:
        aura(sheet, sx, cy, SEAT_SIZE)
    K.paste(sheet, K.skin("Hud/slot", SEAT_SIZE, SEAT_SIZE), sx, cy)
    K.paste(sheet, drawn(tier, REWARD_TALL, "paid" if paid else "ahead" if not waiting else "lit"),
            sx, cy - REWARD_TALL * CHEST_LIFT)

    # the count, on the well's corner, only when it is more than one
    if count > 1:
        bx, by = sx + SEAT_SIZE / 2 - 14, cy + SEAT_SIZE / 2 - 16
        K.paste(sheet, K.round_rect(54, 54, 27, K.GOLD), bx, by)
        K.text(sheet, "x%d" % count, bx, by, 27, fill=(77, 51, 13), outline=0)

    colour = K.GOLD if lit else (K.MINT if paid else K.CREAM)
    K.text(sheet, title, left + TEXT_X, cy - 32, 32, fill=colour, anchor="l")
    K.text(sheet, sub, left + TEXT_X, cy + 28, 25, fill=(255, 243, 220), outline=2, anchor="l")

    if waiting:
        bx = cx + WIDTH / 2 - 130
        K.paste(sheet, K.skin("btn_green", KEY_W, KEY_H), bx, cy)
        K.shrunk(sheet, txt("ui.referral.collect").upper(), bx, cy - KEY_H * 0.0231,
                 KEY_W - 36, 52, 34, 20)
    elif paid:
        sxx = cx + WIDTH / 2 - 146
        K.paste(sheet, K.fit(Image.open(K.UI / "seal_gold.png").convert("RGBA"), (84, 84)), sxx, cy)
        K.paste(sheet, K.tint(icon("ic_check", (44, 44)), K.CREAM), sxx, cy)
    else:
        bx = cx + WIDTH / 2 - 130
        pill = Image.new("RGBA", (int(KEY_W), int(MARK_H)), (0, 0, 0, 0))
        dd = ImageDraw.Draw(pill)
        dd.rounded_rectangle([0, 0, KEY_W - 1, MARK_H - 1], radius=28, fill=(13, 23, 46, 179))
        dd.rounded_rectangle([1, 1, KEY_W - 2, MARK_H - 2], radius=28,
                             outline=(255, 255, 255, 33), width=3)
        K.paste(sheet, pill, bx, cy)
        K.one_line(sheet, mark.upper(), bx, cy, MARK_ROOM, MARK_TYPE, MARK_LEAST,
                   K.AQUA if mark == txt("ui.referral.settling").upper() else K.CREAM,
                   "a friend's pill")


def board(sheet, top, finished, paid):
    """`ReferralScreen.BuildBoard` - a `GridView` of one row a friend, every friend the cap allows.

    **The arithmetic mirrored here is `GridView`'s, not this screen's**, and that is the whole
    point of keeping the mirror honest about it: a cell is `CELL_H` and the card is centred in
    it, `GridView.Resize` never lets the content be shorter than its window, and
    `GridView.ScrollTo` centres on the *cell* rather than on the card. Draw this against the
    board's own numbers and every row is six units out - a mirror telling a comfortable lie
    (invariant 44d).

    `paid` maps a friend number to how many of its chests are paid. The lit row is the first
    finished friend with a chest still unpaid (`ReferralLedger.FirstClaimableFriend`).
    """
    count = PER.get("count", 1)
    rows = max(1, MAX_BOUND)  # `ReferralScreen.RowCount` - the whole board, always.
    bottom = K.NAV_HEIGHT + BOARD_FOOT
    band = H - top - bottom
    tall = max(rows * CELL_H, band)  # `GridView.Resize`

    lit = next((f for f in range(1, min(finished, MAX_BOUND) + 1) if paid.get(f, 0) < count), 0)

    offset = 0.0
    if lit:
        # `GridView.ScrollTo(lit - 1)`: the cell's top, less half the room the window has
        # beyond one cell, clamped to the content.
        want = (lit - 1) * CELL_H - max(0.0, band - CELL_H) * .5
        offset = max(0.0, min(want, tall - band))

    strip = Image.new("RGBA", (W, int(tall)), (0, 0, 0, 0))

    # The lit row's pool first, under every plate on the board — `RowWidgets.Sink` sinks the lit
    # cell to the bottom of the sibling order precisely so its halo passes under its
    # neighbours', and a pool painted row by row lands on top of the card above instead.
    if lit:
        K.paste(strip, pool_art(ROW_H), W / 2, (lit - 1) * CELL_H + CELL_H / 2)

    for i in range(rows):
        friend = i + 1
        opened = paid.get(friend, 0)
        reached = finished >= friend
        done = opened >= count
        state = "paid" if done else "lit" if friend == lit else "waiting" if reached else "ahead"
        if reached and not done and opened:
            sub = txt("ui.referral.opened", opened, count)
        else:
            sub = txt("ui.referral.pays", count, txt("chest.%s.name" % PER_TIER))
        row(strip, W / 2, i * CELL_H + CELL_H / 2, ROW_H, "Hud/plate_navy", PER_TIER, count,
            txt("ui.referral.friend_n", friend).upper(), sub, state,
            txt("ui.referral.progress", min(finished, friend), friend), halo=False)

    window = strip.crop((0, int(offset), W, int(offset + band)))
    sheet.alpha_composite(window, (0, int(top)))
    return rows * CELL_H > band


# ------------------------------------------------------------------- the states
def shot(state):
    """One page. The states are the six a player can actually be in."""
    code = "" if state == "offline" else "K7PQ2XM9"
    bound, finished, paid = 0, 0, {}
    offer = "code"
    welcome = None

    if state == "invited":
        offer, welcome = "welcome", ("ahead", 4, 10, 0)
    elif state == "welcome":
        offer, welcome = "welcome", ("lit", 10, 10, 1)
    elif state == "climb":
        bound, finished, paid, offer = 5, 3, {1: 2, 2: 1}, "none"
    elif state == "done":
        bound, finished, offer = 14, 12, "none"
        paid = {f: PER.get("count", 1) for f in range(1, 13)}
    elif state == "offline":
        offer = "code"

    sheet = Image.new("RGBA", (W, H), (*K.GROUND, 255))
    K.plain(sheet)
    K.rail(sheet, top=True)

    y = 22.0
    y = header(sheet, y)
    y = hero(sheet, y, code, bound, finished)
    if offer == "code":
        y = offer_row(sheet, y)
    elif offer == "welcome":
        y = welcome_row(sheet, y, *welcome)
    y = heading(sheet, y)
    board(sheet, y, finished, paid)

    K.navbar(sheet, "profile")
    return sheet.convert("RGB")


STATES = ("fresh", "invited", "welcome", "climb", "done", "offline")


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--state", choices=STATES, default="fresh")
    ap.add_argument("--contact", action="store_true", help="all six states side by side")
    ap.add_argument("--out", type=Path, default=Path("referral.png"))
    args = ap.parse_args()

    if not REFERRAL or REFERRAL.get("withdrawn"):
        sys.exit("progression.json has no live referral block to draw")

    if args.contact:
        shots = [shot(s) for s in STATES]
        cell = 540
        sheet = Image.new("RGB", (cell * len(shots), int(cell * H / W)), K.GROUND)
        for i, s in enumerate(shots):
            sheet.paste(s.resize((cell, int(cell * H / W)), Image.LANCZOS), (i * cell, 0))
        out = sheet
    else:
        out = shot(args.state)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    out.save(args.out)

    print("  every friend who finishes '%s' pays %dx %s, up to %d friends; the invitee opens %dx %s"
          % (CHAPTER_NAME, PER.get("count", 1), PER_TIER, MAX_BOUND, INVITEE.get("count", 1), INVITEE_TIER))
    print("  a row is %.0fx%.0f; the reward draws %.0f tall in a %.0f well" % (WIDTH, ROW_H, REWARD_TALL, SEAT_SIZE))
    print("  wrote %s  %dx%d  - look at it" % (args.out, out.width, out.height))


if __name__ == "__main__":
    main()
