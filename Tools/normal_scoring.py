"""Freeze Normal leaderboard v1 targets. Existing rows never change with difficulty.

Run --check in validation; --write appends only newly shipped Normal levels. A later
scoring formula needs a new version and an explicit migration, never a silent retune.
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools" / "verify"))
import proto
import siege


def eligible():
    content = ROOT / "Assets/StreamingAssets/Content"
    manifest = json.loads((content / "manifest.json").read_text(encoding="utf-8"))
    levels = {}
    for chapter in manifest["chapters"]:
        if chapter.get("disabled") or chapter.get("track", "main") != "main":
            continue
        # Explicitly refuse a future mode rather than assigning it siege's arithmetic.
        if chapter.get("mode", "glade") != "siege":
            raise ValueError("Normal target authoring must support mode: " + chapter.get("mode", "glade"))
        body = json.loads((content / "chapters" / (chapter["id"] + ".json")).read_text(encoding="utf-8"))
        authored = {level["id"]: level for level in body["levels"]}
        for lid in chapter["levels"]:
            level = authored[lid]
            block = level["siege"]
            grid = proto.Grid(block["rows"], block["width"], block["height"], siege.LETTERS)
            layout = siege.Layout(grid, block.get("gems"), block.get("wards"), block.get("waves"),
                                 block.get("boss"), block.get("cogs", 0), tough=block.get("tough", 0),
                                 charms=block.get("charms"), obsidian=block.get("obsidian", False),
                                 singularity=block.get("singularity", False))
            if layout.fault:
                raise ValueError(lid + ": " + layout.fault)
            hundredths = int(float(level.get("goldFactor", 1.2)) * 100 + 0.5)
            levels[lid] = proto.over(siege.par(layout), hundredths)
    return levels


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    path = ROOT / "Assets/StreamingAssets/Content/progression.json"
    text = path.read_text(encoding="utf-8")
    progression = json.loads(text)
    existing = progression.get("normalScoring", {"version": 1, "levels": []})
    if existing["version"] != 1:
        raise ValueError("Unsupported Normal scoring version")
    targets = {row["level"]: row["target"] for row in existing["levels"]}
    if len(targets) != len(existing["levels"]) or any(type(n) is not int or not 1 <= n <= 1000000 for n in targets.values()):
        raise ValueError("Duplicate or invalid Normal scoring targets")
    shipped = eligible()
    missing = shipped.keys() - targets.keys()
    extra = targets.keys() - shipped.keys()
    if extra:
        raise ValueError("Targets outside the shipped Normal ladder: " + ", ".join(sorted(extra)))
    if missing and not args.write:
        raise ValueError("Missing Normal targets; run --write: " + ", ".join(sorted(missing)))
    if args.write and missing:
        targets.update({lid: shipped[lid] for lid in missing})
        block = {"version": 1, "levels": [{"level": lid, "target": targets[lid]} for lid in sorted(targets)]}
        # Preserve all unrelated content and formatting.
        encoded = '{\n    "version": 1,\n    "levels": [\n' + ',\n'.join(
            '      ' + json.dumps(row, ensure_ascii=False) for row in block['levels']) + '\n    ]\n  }'
        if "normalScoring" in progression:
            start = text.index('{', text.index('"normalScoring"'))
            _, length = json.JSONDecoder().raw_decode(text[start:])
            text = text[:start] + encoded + text[start + length:]
        else:
            text = text.rstrip()
            text = text[:-1].rstrip() + ',\n  "normalScoring": ' + encoded + '\n}\n'
        path.write_text(text, encoding="utf-8", newline="\n")
    print(f"Normal scoring v1: {len(targets)} frozen targets, all on the active Normal ladder")


if __name__ == "__main__":
    main()
