"""What a dependency costs a player: download size and cold start, before against after.

    python Tools/measure_build.py size  before.apk after.apk      # or .aab
    python Tools/measure_build.py start [--runs 10]                # the installed build, over adb

`size` compares two packages entry by entry (compressed, which is what a download costs, and
uncompressed, which is what the device stores), grouped by ABI library, dex, assets and the rest,
and prints the biggest movers. An .aab is larger than any one device's download; for a per-device
figure compare two APKs built from the same settings, or use Play Console's size report.

`start` force-stops the game and cold-launches it `--runs` times with `am start -W`, printing the
median and spread of TotalTime (process start to the first frame drawn), then reads the last
`boot_ready` load_ms from logcat when a development build's DebugAnalyticsSink printed one. Run it
on the same phone, plugged in, screen on, for both builds: the figure is only comparable to itself.

Standard library only.
"""
import argparse
import re
import statistics
import subprocess
import sys
import time
import zipfile

PACKAGE = "com.tekoworld.glimmergroove"


def group_of(name):
    if name.startswith("lib/") or "/lib/" in name:
        parts = name.split("/")
        i = parts.index("lib")
        return "lib/" + (parts[i + 1] if len(parts) > i + 1 else "?")
    if name.endswith(".dex"):
        return "dex"
    if name.startswith("assets/") or "/assets/" in name:
        return "assets"
    return "other"


def read(path):
    with zipfile.ZipFile(path) as z:
        return {i.filename: (i.compress_size, i.file_size) for i in z.infolist() if not i.is_dir()}


def mb(n):
    return "%+.2f MB" % (n / 1048576.0) if n else "     0"


def size(before, after):
    a, b = read(before), read(after)
    groups = {}
    for name in set(a) | set(b):
        ca, ua = a.get(name, (0, 0))
        cb, ub = b.get(name, (0, 0))
        g = groups.setdefault(group_of(name), [0, 0])
        g[0] += cb - ca
        g[1] += ub - ua

    total_c = sum(v[0] for v in a.values()), sum(v[0] for v in b.values())
    total_u = sum(v[1] for v in a.values()), sum(v[1] for v in b.values())
    print("package   %s -> %s" % (before, after))
    print("download  %.2f MB -> %.2f MB  (%s)" % (total_c[0] / 1048576.0, total_c[1] / 1048576.0,
                                                  mb(total_c[1] - total_c[0])))
    print("stored    %.2f MB -> %.2f MB  (%s)" % (total_u[0] / 1048576.0, total_u[1] / 1048576.0,
                                                  mb(total_u[1] - total_u[0])))
    print()
    for g, (dc, du) in sorted(groups.items(), key=lambda kv: -abs(kv[1][0])):
        if dc or du:
            print("  %-16s compressed %s   uncompressed %s" % (g, mb(dc), mb(du)))

    movers = []
    for name in set(a) | set(b):
        d = b.get(name, (0, 0))[0] - a.get(name, (0, 0))[0]
        if d:
            movers.append((d, name))
    print()
    for d, name in sorted(movers, key=lambda m: -abs(m[0]))[:15]:
        tag = "new " if name not in a else "gone" if name not in b else "    "
        print("  %s %10s  %s" % (tag, mb(d), name))


def adb(*args):
    return subprocess.run(["adb", "shell"] + list(args), capture_output=True, text=True).stdout


def start(runs):
    resolved = adb("cmd", "package", "resolve-activity", "--brief", PACKAGE).strip().splitlines()
    component = resolved[-1].strip() if resolved and "/" in resolved[-1] else None
    if not component:
        sys.exit("%s is not installed on the connected device" % PACKAGE)

    times = []
    for i in range(runs):
        adb("am", "force-stop", PACKAGE)
        time.sleep(2.0)
        out = adb("am", "start", "-W", "-n", component)
        m = re.search(r"TotalTime:\s*(\d+)", out)
        if m:
            times.append(int(m.group(1)))
            print("  run %2d  TotalTime %5d ms" % (i + 1, times[-1]))
        time.sleep(8.0)  # let the splash finish, so boot_ready is in the log

    if not times:
        sys.exit("am start -W reported no TotalTime")
    print("cold start (first frame): median %d ms, min %d, max %d over %d runs"
          % (statistics.median(times), min(times), max(times), len(times)))

    log = subprocess.run(["adb", "logcat", "-d", "-s", "Unity"], capture_output=True, text=True).stdout
    ready = re.findall(r"boot_ready.*?load_ms\D+(\d+)", log)
    if ready:
        print("boot_ready load_ms (last %d): %s" % (min(len(ready), runs), ", ".join(ready[-runs:])))
    else:
        print("no boot_ready in logcat (a release build does not print events; use DebugView)")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("size")
    s.add_argument("before")
    s.add_argument("after")
    t = sub.add_parser("start")
    t.add_argument("--runs", type=int, default=10)
    args = parser.parse_args()
    if args.cmd == "size":
        size(args.before, args.after)
    else:
        start(args.runs)


if __name__ == "__main__":
    main()
