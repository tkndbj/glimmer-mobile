#!/usr/bin/env python3
"""
Run the EditMode tests without the Unity Editor.

Compiles Tools/verify/runner/Runner.cs against the bundled .NET 8 SDK, then points
it at the test assembly compile.py builds. Anything that needs a real engine — a
GameObject, a coroutine, a native call — fails here and has to go through Test
Runner; everything that is pure Domain logic, which is most of this suite, does
not.

    python Tools/verify/tests.py            # everything
    python Tools/verify/tests.py NearMiss   # fixtures matching a substring

Exit code is 1 if any test failed.
"""

import glob
import io
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import compile as build  # reuses its Unity discovery and assembly definitions

OUT = build.OUT
RUNNER_SRC = os.path.join(HERE, "runner", "Runner.cs")
RUNNER_DLL = os.path.join(OUT, "Runner.dll")


def net_ref():
    hits = sorted(glob.glob(os.path.join(build.DATA, "DotNetSdk", "packs",
                                         "Microsoft.NETCore.App.Ref", "*", "ref", "net8.0")))
    if not hits:
        sys.exit("no .NET 8 reference pack in the Unity SDK")
    return hits[-1]


def build_runner():
    refs = sorted(glob.glob(os.path.join(net_ref(), "*.dll")))

    lines = ["-nostdlib+", "-noconfig", "-langversion:9", "-target:exe", "-nullable:disable",
             '-out:"%s"' % RUNNER_DLL.replace("\\", "/")]
    lines += ['-r:"%s"' % r.replace("\\", "/") for r in refs]
    lines += ['"%s"' % RUNNER_SRC.replace("\\", "/")]

    rsp = os.path.join(OUT, "runner.rsp")
    os.makedirs(OUT, exist_ok=True)
    io.open(rsp, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")

    result = subprocess.run([build.DOTNET, build.CSC, "@" + rsp], capture_output=True, text=True)
    errors = [l for l in (result.stdout + result.stderr).splitlines() if ": error " in l]
    if errors:
        print("runner failed to build:")
        for line in errors[:20]:
            print("   " + line.strip())
        return False

    # A framework-dependent exe needs to be told which runtime to roll forward to.
    io.open(os.path.join(OUT, "Runner.runtimeconfig.json"), "w", encoding="utf-8").write(json.dumps({
        "runtimeOptions": {
            "tfm": "net8.0",
            "framework": {"name": "Microsoft.NETCore.App", "version": "8.0.0"},
            "rollForward": "latestMinor",
        }
    }, indent=2))
    return True


def main():
    print("building assemblies")
    for key, spec in build.ASSEMBLIES:
        if not build.build(key, spec):
            return 1

    if not build_runner():
        return 1

    tests_dll = os.path.join(OUT, "GlimmerGrove.Tests.dll")
    if not os.path.exists(tests_dll):
        sys.exit("test assembly was not built")

    probe = [OUT, build.SCRIPT_ASMS, build.ENGINE, os.path.join(build.DATA, "Managed")]
    probe += sorted({os.path.dirname(p) for p in build.package_plugins()})

    # NUnit itself, which the runner needs before it can even read a [Test]
    # attribute — reading one resolves its type.
    probe += [os.path.dirname(p) for p in build.nunit()]

    args = [build.DOTNET, RUNNER_DLL, tests_dll] + probe
    if len(sys.argv) > 1:
        args += ["--filter", sys.argv[1]]

    print()
    result = subprocess.run(args)
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
