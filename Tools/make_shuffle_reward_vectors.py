#!/usr/bin/env python3
"""Shared Shuffle payout cases. Run with --check to verify the committed contract."""
import json
from pathlib import Path
import sys

TARGET = Path(__file__).resolve().parents[1] / "firebase/shared/shuffle-reward-vectors.json"
CONSTANTS = dict(xpPerWave=10, creditsPerWave=10, maxRows=64, maxDevices=64,
                 maxDeviceIdLength=64, maxWavesPerDevice=1_000_000_000)


def row(*counts, level="s13_shuffle", wave=0):
    return dict(level=level, wave=wave,
                rewards=[dict(device=device, waves=waves) for device, waves in counts])


def total(rows):
    levels = {}
    for entry in rows[:64]:
        if not entry or not entry.get("level") or len(entry["level"]) > 48:
            continue
        devices = levels.setdefault(entry["level"], {})
        for count in (entry.get("rewards") or [])[:64]:
            if not count:
                continue
            device = count.get("device", "")
            if not device or len(device) > 64 or any(c not in "abcdefghijklmnopqrstuvwxyz0123456789_-" for c in device):
                continue
            waves = max(0, min(int(count.get("waves", 0)), 1_000_000_000))
            devices[device] = max(devices.get(device, 0), waves)
    return sum(sum(value for _, value in sorted(devices.items())[:64])
               for devices in levels.values())


def build():
    cases = []

    def case(name, rows):
        waves = total(rows)
        cases.append(dict(name=name, rows=rows, waves=waves, xp=waves * 10, credits=waves * 10))

    case("no runs", [])
    case("a pre-reward best never pays retroactively", [dict(level="s13_shuffle", wave=80)])
    case("one cleared wave", [row(("a", 1), wave=1)])
    case("a replay tally pays even below the best", [row(("a", 31), wave=100)])
    case("a forfeit pays its clears without publishing a best", [row(("a", 7))])
    case("two offline devices each pay", [row(("a", 12), ("b", 3))])
    case("duplicate counters take max rather than summing", [row(("a", 12), ("a", 5), ("b", 3))])
    case("duplicate level rows merge counters", [row(("a", 12)), row(("a", 5), ("b", 3))])
    case("negative counts never subtract", [row(("a", -12), ("b", 3))])
    case("an empty or invalid device contributes nothing", [row(("", 99), ("bad.device", 99), ("ok", 2))])
    case("an oversized device id contributes nothing", [row(("a" * 65, 99))])
    case("the longest device id is accepted", [row(("a" * 64, 2))])
    case("a row naming no level contributes nothing", [row(("a", 99), level="")])
    case("an impossible level id contributes nothing", [row(("a", 99), level="a" * 49)])
    case("null entries do not change the total", [None, dict(level="s13_shuffle", rewards=[None, dict(device="a", waves=3)])])
    case("separate levels sum", [row(("a", 4)), row(("a", 6), level="second_shuffle")])
    case("the per-device structural guard saturates", [row(("a", 10**12))])
    case("the row bound limits the walk including malformed entries", [None] * 64 + [row(("a", 100))])
    case("the device bound limits the walk including malformed entries",
         [dict(level="s13_shuffle", rewards=[None] * 64 + [dict(device="a", waves=100)])])
    case("merged device lists are bounded in ordinal order",
         [row(*[(f"b{i:02}", 2) for i in range(64)]), row(("a", 3))])
    case("largest device-counter sum and payout are exact in both runtimes",
         [row(*[(f"d{i:02}", 10**9) for i in range(64)])])
    return dict(constants=CONSTANTS, cases=cases)


def main():
    rendered = json.dumps(build(), indent=2, ensure_ascii=False) + "\n"
    if "--check" in sys.argv:
        if not TARGET.exists() or TARGET.read_text(encoding="utf-8") != rendered:
            sys.exit("Shuffle reward vectors differ; run Tools/make_shuffle_reward_vectors.py")
        print("Shuffle reward vectors match")
    else:
        TARGET.write_text(rendered, encoding="utf-8")
        print(f"wrote {len(build()['cases'])} Shuffle reward vectors")


if __name__ == "__main__":
    main()
