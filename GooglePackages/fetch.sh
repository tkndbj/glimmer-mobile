#!/usr/bin/env bash
#
# Downloads the third-party UPM packages this project depends on.
#
# Mostly Firebase, hence the folder's name, but not only: the ads plugin is here for the
# UMP consent SDK inside it and AppsFlyer for attribution. What they have in common is
# that they are large, versioned build inputs with a canonical download URL - which is
# what makes vendoring them better than a scoped registry, since a resolve then needs
# nothing to be reachable at build time.
#
#     bash GooglePackages/fetch.sh
#
# The macOS and Linux twin of fetch.ps1. It exists because the iOS half of this project
# can only be built on a Mac, and requiring PowerShell to be installed there before the
# project will even open is a step that buys nothing — curl and tar are already present.
#
# The two scripts must agree on the versions. They are checked against each other by
# nothing, so if you bump one, bump the other in the same commit; Firebase requires every
# Firebase package to be on the SAME version, so a half-applied bump does not merely
# mismatch across machines, it fails to resolve at all.
#
# The .tgz files are ~75 MB and are NOT committed — they are build inputs with a canonical
# download URL, and putting them in git would add 75 MB to the history on every version
# bump. Packages/manifest.json references them by relative path, so Unity will refuse to
# resolve until this has been run on a fresh clone.

set -uo pipefail

FIREBASE='13.15.0'
EDM='1.2.187'      # External Dependency Manager, versioned separately
ADS='11.4.0'       # Google Mobile Ads, for the UMP consent SDK inside it
APPSFLYER='6.17.1' # Attribution. Not Google's, and not on Google's registry.

GOOGLE='https://dl.google.com/games/registry/unity'
OPENUPM='https://package.openupm.com'

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
failed=0

# Google lays its registry out as <id>/<file>; a plain npm registry as <id>/-/<file>.
package_url() {
    local id="$1" file="$2" registry="$3"
    if [ "$registry" = "$OPENUPM" ]; then
        echo "${registry}/${id}/-/${file}"
    else
        echo "${registry}/${id}/${file}"
    fi
}

fetch() {
    local id="$1" version="$2" registry="$3"
    local file="${id}-${version}.tgz"
    local path="${here}/${file}"

    if [ -f "$path" ]; then
        echo "have $file"
        return 0
    fi

    echo "get  $file"

    # --fail so an HTTP error is an error rather than a saved error page, and -L because
    # the registry redirects.
    if ! curl -fsSL --max-time 600 -o "$path" \
         "$(package_url "$id" "$file" "$registry")"; then
        echo "FAILED $file : download error"
        rm -f "$path"
        failed=$((failed + 1))
        return 0
    fi

    # A 404 page would also "download" happily, so check it is really a package.
    if ! tar -tzf "$path" >/dev/null 2>&1; then
        echo "FAILED $file : downloaded file is not a valid tarball"
        rm -f "$path"
        failed=$((failed + 1))
    fi
}

# EDM is at 1.2.187 because the ads plugin asks for it. Safe for Firebase, which asks for
# 1.2.186 - a UPM dependency version is a minimum, not a pin, and the resolver takes the
# highest anybody asked for.
fetch 'com.google.external-dependency-manager' "$EDM"      "$GOOGLE"
fetch 'com.google.firebase.app'                "$FIREBASE" "$GOOGLE"
fetch 'com.google.firebase.auth'               "$FIREBASE" "$GOOGLE"
fetch 'com.google.firebase.firestore'          "$FIREBASE" "$GOOGLE"
fetch 'com.google.firebase.functions'          "$FIREBASE" "$GOOGLE"
fetch 'com.google.firebase.analytics'          "$FIREBASE" "$GOOGLE"
fetch 'com.google.ads.mobile'                  "$ADS"      "$OPENUPM"
fetch 'com.appsflyer.unity'                    "$APPSFLYER" "$OPENUPM"

if [ "$failed" -gt 0 ]; then
    echo
    echo "$failed package(s) missing. Check https://developers.google.com/unity/archive"
    echo "in case these versions have been retired, then update the versions above."
    exit 1
fi

echo
echo "all packages present in GooglePackages/"
