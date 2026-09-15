#!/usr/bin/env python3
"""Holds the Android application manifest to the things no other gate can see.

Everything here is about one class of fault: a setting that is *absent* rather than
wrong. An Android manifest attribute that never reaches the merged application manifest
does not fail a build, does not warn and does not log — the APK simply behaves as though
nobody asked for it, which is indistinguishable from nobody having asked.

**Auto Backup is the reason this file exists.** It is on by default and copies
`shared_prefs`, `databases` and `files` off the device, then restores them onto a
reinstall or a new phone. For an app whose cross-device story is an account that is a
second sync path nobody designed, and it cannot merge: it is a blind file restore, where
every join this game makes is idempotent and monotonic (invariant 11). Three things it
restores are actively poisonous, and all three were found on a real device on
2026-09-15:

* `shared_prefs/com.google.firebase.auth.api.Store.*` — Firebase's persisted session,
  encrypted against `com.google.firebase.auth.api.crypto.*`, which is regenerated per
  install. Restored, it cannot be decrypted, so the SDK holds no user while everything
  derived from it still names the old account.
* `databases/firestore.*` — the Firestore offline cache. Restored, it serves another
  install's documents to a client that cannot authenticate, so the game draws a name, a
  wallet and a roster with no session behind any of it.
* the Unity player preferences — which carry the cached update wall (a fact about this
  install on this platform, invariant 49c) and the remembered chapter per mode (device
  local and never in the save, invariant 8b).

**And the fix has a silent failure mode of its own, which is the sharper half.** The
AppsFlyer SDK's own manifest sets `android:allowBackup="true"`. Two libraries at the same
level cannot settle a conflict between themselves and `tools:replace` in one of them does
not win it — declaring the attribute in an `.androidlib` failed the build outright the
first time and then, with one attribute dropped, **merged green and silently kept
`true`**. Only the application module outranks every library, which is
`Assets/Plugins/Android/LauncherManifest.xml`. So this gate checks both halves: that the
launcher manifest asks for the right thing, and that no library is quietly asking for
something else.

Run: `python Tools/verify/android.py`
"""

import os
import re
import sys
import xml.dom.minidom as minidom

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PLUGINS = os.path.join(ROOT, "Assets", "Plugins", "Android")
LAUNCHER = os.path.join(PLUGINS, "LauncherManifest.xml")
SETTINGS = os.path.join(ROOT, "ProjectSettings", "ProjectSettings.asset")
RULES = os.path.join(
    PLUGINS, "GlimmerBackup.androidlib", "res", "xml", "glimmer_data_extraction_rules.xml"
)

ANDROID = "http://schemas.android.com/apk/res/android"

# Every domain a restore can carry. Both transports exclude all of them, so the rule does
# not depend on which one the platform picks.
DOMAINS = {"root", "file", "database", "sharedpref", "external"}

# The attributes a library must never declare. `allowBackup` is the one that fails
# silently; the other two fail loudly, which is why they are the ones somebody "fixes" by
# moving them into a library and then hits the quiet one.
BACKUP_ATTRS = ("allowBackup", "fullBackupContent", "dataExtractionRules")

problems = []


def fail(message):
    problems.append(message)


def parse(path):
    """Reads a manifest the way Unity does, which is stricter than a browser.

    Unity opens an `.androidlib` manifest with `System.Xml` purely to learn the library's
    package name, so a malformed one is not a warning or a missing icon — it is an
    `XmlException` thrown out of Unity's own gradle generator, with a stack trace naming
    Unity's source files and never this project. A double hyphen inside a comment is the
    way that happens, and an em dash written as two hyphens is the way *that* happens.
    """
    with open(path, "rb") as handle:
        raw = handle.read()

    text = raw.decode("utf-8-sig")

    for comment in re.findall(r"<!--(.*?)-->", text, re.S):
        if "--" in comment:
            fail(
                "%s has a double hyphen inside an XML comment, which aborts the whole "
                "Android build with an XmlException out of Unity's gradle generator"
                % os.path.relpath(path, ROOT)
            )

    try:
        return minidom.parseString(raw)
    except Exception as error:                                    # noqa: BLE001
        fail("%s does not parse: %s" % (os.path.relpath(path, ROOT), error))
        return None


def check_launcher():
    """The application module, which is the only manifest that outranks every library."""
    if not os.path.isfile(LAUNCHER):
        fail(
            "Assets/Plugins/Android/LauncherManifest.xml is missing, so nothing turns Auto "
            "Backup off and a reinstall restores another install's Firebase session"
        )
        return

    document = parse(LAUNCHER)
    if document is None:
        return

    applications = document.getElementsByTagName("application")
    if len(applications) != 1:
        fail("LauncherManifest.xml must carry exactly one <application> element")
        return

    application = applications[0]

    backup = application.getAttributeNS(ANDROID, "allowBackup")
    if backup != "false":
        fail(
            "LauncherManifest.xml must set android:allowBackup=\"false\" (found %r). Auto "
            "Backup restores a Firebase session that cannot be decrypted on the install "
            "that receives it" % backup
        )

    rules = application.getAttributeNS(ANDROID, "dataExtractionRules")
    if not rules:
        fail(
            "LauncherManifest.xml must set android:dataExtractionRules. On Android 12 and "
            "up allowBackup governs cloud backup alone, and a device-to-device transfer — "
            "which is how a new phone is set up — is not covered by it"
        )

    # Without this the merger hits AppsFlyer's own values and fails the build. It is the
    # error somebody "fixes" by moving the attributes into a library, which is the quiet
    # failure this whole file is about.
    replace = application.getAttribute("tools:replace")
    for name in ("android:allowBackup", "android:dataExtractionRules"):
        if name not in replace:
            fail(
                "LauncherManifest.xml must carry tools:replace=\"...%s...\"; the AppsFlyer "
                "SDK declares the same attributes and the merge fails without it" % name
            )


def check_settings():
    """The flag, because the file is inert without it and nothing says so."""
    if not os.path.isfile(SETTINGS):
        fail("ProjectSettings/ProjectSettings.asset is missing")
        return

    with open(SETTINGS, "r", encoding="utf-8", errors="replace") as handle:
        text = handle.read()

    if not re.search(r"^\s*useCustomLauncherManifest:\s*1\s*$", text, re.M):
        fail(
            "ProjectSettings.asset must set useCustomLauncherManifest: 1, or Unity "
            "generates its own launcher manifest and LauncherManifest.xml is never read"
        )


def check_rules():
    """Both transports, every domain."""
    if not os.path.isfile(RULES):
        fail("%s is missing" % os.path.relpath(RULES, ROOT))
        return

    document = parse(RULES)
    if document is None:
        return

    for transport in ("cloud-backup", "device-transfer"):
        nodes = document.getElementsByTagName(transport)
        if not nodes:
            fail("the extraction rules must carry a <%s> section" % transport)
            continue

        # `domain` is a plain attribute here, not an `android:` one — unlike every other
        # attribute in an Android manifest, and the first thing this gate got wrong itself.
        excluded = {
            node.getAttribute("domain")
            for node in nodes[0].getElementsByTagName("exclude")
        }

        missing = sorted(DOMAINS - excluded)
        if missing:
            fail(
                "<%s> does not exclude %s, so a restore still carries %s"
                % (transport, ", ".join(missing), "them" if len(missing) > 1 else "it")
            )


def check_libraries():
    """No library may ask for a backup attribute, because losing is silent.

    A library declaring `allowBackup` does not fail the build: it loses to whichever other
    library declared it first, and the APK ships with backup on. That is the whole reason
    this gate is worth having, and the reason `GlimmerBackup.androidlib` carries the rules
    *resource* and no `<application>` element at all — resource merging has no priority
    contest in it, which is why that half works from a library when the attribute half
    does not.
    """
    if not os.path.isdir(PLUGINS):
        return

    for entry in sorted(os.listdir(PLUGINS)):
        if not entry.endswith(".androidlib"):
            continue

        manifest = os.path.join(PLUGINS, entry, "AndroidManifest.xml")
        if not os.path.isfile(manifest):
            continue

        document = parse(manifest)
        if document is None:
            continue

        for application in document.getElementsByTagName("application"):
            for name in BACKUP_ATTRS:
                if application.getAttributeNS(ANDROID, name):
                    fail(
                        "%s/AndroidManifest.xml declares android:%s. A library cannot win "
                        "that attribute against another library, and losing is silent — it "
                        "belongs in LauncherManifest.xml" % (entry, name)
                    )


def main():
    check_launcher()
    check_settings()
    check_rules()
    check_libraries()

    if problems:
        print("Android manifest: %d problem(s)\n" % len(problems))
        for problem in problems:
            print("  ERROR  %s" % problem)
        print(
            "\nThe merged result is the only proof. After a build:\n"
            "  adb shell dumpsys package com.tekoworld.glimmergroove | grep ALLOW_BACKUP\n"
            "wants no output at all."
        )
        return 1

    print("Android manifest: backup refused on both transports, and no library contests it")
    return 0


if __name__ == "__main__":
    sys.exit(main())
