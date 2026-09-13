#!/usr/bin/env python3
"""Re-namespaces AppsFlyer's purchase connector so an Android build is possible at all.

    python Tools/make_appsflyer_connector.py
    python Tools/make_appsflyer_connector.py --check    # prove it reproduces, write nothing

WHY THIS EXISTS
---------------
AppsFlyer's Unity package declares two Android libraries, and both of them declare the
Android namespace `com.appsflyer`:

    com.appsflyer:af-android-sdk:6.17.0
    com.appsflyer:purchase-connector:2.1.0

Unity 6000.5 builds with **AGP 9**, which made a namespace shared by two modules a hard
error rather than the warning it was under AGP 8. So the build dies at
`:launcher:processDebugMainManifest` with "Manifest merger failed with multiple errors",
which reads as a manifest problem and is really a dependency one. Every published version
of both libraries declares the same namespace, so upgrading fixes nothing.

WHY THE CONNECTOR CANNOT SIMPLY BE DROPPED
------------------------------------------
The obvious move is to exclude it: this project does not validate store receipts through
AppsFlyer, and `AppsFlyerSink` deliberately forwards no purchase event. That was tried and
it fails at runtime, silently as far as Gradle is concerned:

    java.lang.NoClassDefFoundError: Failed resolution of: Lcom/appsflyer/api/Store;
        at com.unity3d.player.ReflectionHelper.getMethodID

`com.appsflyer.unity.AppsFlyerAndroidWrapper` declares `private static Store mappingEnum(int)`,
and Unity's reflection helper calls `getDeclaredMethods()` on that class to find *any* method
by name — which resolves the signatures of all of them. So the first call into AppsFlyer
throws, and attribution never starts. The connector has to be present even though nothing
here uses it.

WHAT THIS DOES INSTEAD
----------------------
An AAR's namespace is only used to generate its R class. The connector ships **no resources
at all** — its `R.txt` is empty and its manifest declares nothing but the package and a
`uses-sdk` — so its namespace is purely nominal and renaming it changes no behaviour. The
Java classes stay in `com.appsflyer.api.*`, which is what the wrapper looks for.

So: download the published AAR, rewrite one attribute in its manifest, and vendor the result
into `Assets/Plugins/Android/`. A Gradle exclusion in `mainTemplate.gradle` then keeps the
Maven coordinate out, so exactly one copy reaches the build.

WHEN TO RE-RUN IT
-----------------
Whenever the AppsFlyer package moves. The version is not typed here twice — it is read out of
the vendored tarball's own `AppsFlyerDependencies.xml`, so a plugin bump changes the version
this produces and a stale vendored AAR is caught by `--check` rather than by a build failure
three steps later.
"""

import argparse
import hashlib
import io
import os
import re
import sys
import tarfile
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

VENDOR = os.path.join(ROOT, "GooglePackages")
OUT_DIR = os.path.join(ROOT, "Assets", "Plugins", "Android")

MAVEN = "https://repo1.maven.org/maven2/com/appsflyer/purchase-connector"

# The namespace the vendored copy declares. Anything unique will do; this one says what the
# library is, so a future reader of a merged manifest can tell where it came from.
NAMESPACE = "com.appsflyer.purchaseconnector"


def connector_version():
    """The version AppsFlyer's own package asks for, read out of the vendored tarball.

    Read rather than typed, so this tool cannot quietly produce a different version from the
    one the plugin resolves — which would put two copies of the connector in the build and
    reintroduce the collision under a different name.
    """
    tarballs = [f for f in os.listdir(VENDOR)
                if f.startswith("com.appsflyer.unity-") and f.endswith(".tgz")]
    if not tarballs:
        sys.exit("no AppsFlyer tarball in GooglePackages/ — run GooglePackages/fetch.ps1 first")

    tarballs.sort()
    path = os.path.join(VENDOR, tarballs[-1])

    with tarfile.open(path, "r:gz") as tar:
        member = tar.getmember("package/Editor/AppsFlyerDependencies.xml")
        xml = tar.extractfile(member).read().decode("utf-8")

    found = re.search(r'spec="com\.appsflyer:purchase-connector:([0-9.]+)"', xml)
    if not found:
        sys.exit("the AppsFlyer package no longer declares purchase-connector; "
                 "if the collision is gone, delete this tool and the Gradle exclusion with it")

    return found.group(1), os.path.basename(path)


def fetch(version):
    url = f"{MAVEN}/{version}/purchase-connector-{version}.aar"
    print(f"  get  {url}")
    with urllib.request.urlopen(url, timeout=300) as response:
        return response.read()


def renamespace(aar_bytes, version):
    """Rewrites the manifest's package attribute, leaving every other entry byte-for-byte.

    Entries are copied with their original compression and order so the output is stable
    across runs — which is what makes --check meaningful.
    """
    source = zipfile.ZipFile(io.BytesIO(aar_bytes))

    manifest = source.read("AndroidManifest.xml").decode("utf-8")
    if 'package="com.appsflyer"' not in manifest:
        sys.exit("the connector's manifest no longer declares package=\"com.appsflyer\"; "
                 "check whether AppsFlyer has fixed the collision upstream")

    patched = manifest.replace('package="com.appsflyer"', f'package="{NAMESPACE}"')

    # An empty R.txt is the evidence that the namespace is nominal. If the library ever
    # grows resources, renaming its namespace stops being free and this has to be rethought.
    if source.read("R.txt").strip():
        sys.exit("the connector now ships resources, so its namespace is no longer nominal; "
                 "re-namespacing it would move its R class")

    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as dest:
        for item in source.infolist():
            data = patched.encode("utf-8") if item.filename == "AndroidManifest.xml" \
                else source.read(item.filename)

            # Fixed timestamp, so two runs a day apart produce identical bytes.
            info = zipfile.ZipInfo(item.filename, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = item.compress_type
            info.external_attr = item.external_attr
            dest.writestr(info, data)

    return out.getvalue()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true",
                        help="prove the vendored AAR is what this tool produces; write nothing")
    args = parser.parse_args()

    version, tarball = connector_version()
    print(f"AppsFlyer package: {tarball}")
    print(f"purchase-connector: {version}")

    target = os.path.join(OUT_DIR, f"appsflyer-purchase-connector-{version}.aar")

    produced = renamespace(fetch(version), version)
    digest = hashlib.sha256(produced).hexdigest()[:16]

    if args.check:
        if not os.path.exists(target):
            sys.exit(f"FAILED: {os.path.relpath(target, ROOT)} is missing")

        with open(target, "rb") as handle:
            on_disk = handle.read()

        if on_disk != produced:
            sys.exit(f"FAILED: {os.path.relpath(target, ROOT)} is not what this tool produces "
                     f"(on disk {hashlib.sha256(on_disk).hexdigest()[:16]}, "
                     f"expected {digest}) — re-run without --check")

        print(f"OK  {os.path.relpath(target, ROOT)} matches ({digest})")
        return

    # Any older version is a second copy of the same classes in the build.
    for stale in os.listdir(OUT_DIR):
        if stale.startswith("appsflyer-purchase-connector-") and stale.endswith(".aar") \
                and stale != os.path.basename(target):
            os.remove(os.path.join(OUT_DIR, stale))
            meta = os.path.join(OUT_DIR, stale + ".meta")
            if os.path.exists(meta):
                os.remove(meta)
            print(f"  removed stale {stale}")

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(target, "wb") as handle:
        handle.write(produced)

    print(f"wrote {os.path.relpath(target, ROOT)}  ({len(produced):,} bytes, {digest})")
    print(f"namespace: com.appsflyer -> {NAMESPACE}")


if __name__ == "__main__":
    main()
