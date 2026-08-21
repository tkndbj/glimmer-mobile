# Downloads the Firebase Unity SDK packages this project depends on.
#
#     pwsh GooglePackages/fetch.ps1
#
# The .tgz files are ~75 MB and are NOT committed — they are build inputs with a
# canonical download URL, and putting them in git would add 75 MB to the history on
# every version bump. Packages/manifest.json references them by relative path, so Unity
# will refuse to resolve until this has been run on a fresh clone.
#
# Firebase requires every Firebase package to be on the SAME version. Bump $FIREBASE
# and re-run; do not upgrade one package on its own.
#
# NOT EVERYTHING COMES FROM GOOGLE'S REGISTRY. The Mobile Ads plugin - which is here only
# because Google's consent SDK (UMP) is bundled inside it - is published to OpenUPM as a UPM
# package and to GitHub as a .unitypackage, and to dl.google.com not at all. The .unitypackage
# is the wrong one: it unpacks as loose files under Assets/ and is therefore not a package, so
# it carries no version, so `versionDefines` never fires and GLIMMER_UMP is never defined. The
# consent gateway would silently not compile and nobody would be asked anything. Hence the
# per-package registry below.

$ErrorActionPreference = 'Stop'

$FIREBASE = '13.15.0'
$EDM      = '1.2.187'      # External Dependency Manager, versioned separately
$ADS      = '11.4.0'       # Google Mobile Ads, for the UMP consent SDK inside it

$GOOGLE  = 'https://dl.google.com/games/registry/unity'
$OPENUPM = 'https://package.openupm.com'

# EDM is at 1.2.187 because the ads plugin asks for it. Safe for Firebase, which asks for
# 1.2.186 - a UPM dependency version is a minimum, not a pin, and the resolver takes the
# highest anybody asked for.
$packages = @(
    @{ id = 'com.google.external-dependency-manager'; version = $EDM;      registry = $GOOGLE },
    @{ id = 'com.google.firebase.app';                version = $FIREBASE; registry = $GOOGLE },
    @{ id = 'com.google.firebase.auth';               version = $FIREBASE; registry = $GOOGLE },
    @{ id = 'com.google.firebase.firestore';          version = $FIREBASE; registry = $GOOGLE },
    @{ id = 'com.google.firebase.functions';          version = $FIREBASE; registry = $GOOGLE },
    @{ id = 'com.google.ads.mobile';                  version = $ADS;      registry = $OPENUPM }
)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$failed = 0

foreach ($package in $packages) {
    $file = "$($package.id)-$($package.version).tgz"
    $path = Join-Path $here $file

    if (Test-Path $path) {
        Write-Host "have $file"
        continue
    }

    # Google lays its registry out as <id>/<file>; a plain npm registry as <id>/-/<file>.
    $url = if ($package.registry -eq $OPENUPM) {
        "$($package.registry)/$($package.id)/-/$file"
    } else {
        "$($package.registry)/$($package.id)/$file"
    }
    Write-Host "get  $file"

    try {
        Invoke-WebRequest -Uri $url -OutFile $path -TimeoutSec 600 -UseBasicParsing

        # A 404 page would also "download" happily, so check it is really a package.
        $probe = & tar -tzf $path 2>$null | Select-Object -First 1
        if (-not $probe) { throw "downloaded file is not a valid tarball" }
    }
    catch {
        Write-Host "FAILED $file : $($_.Exception.Message)"
        Remove-Item $path -Force -ErrorAction SilentlyContinue
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Host "`n$failed package(s) missing. Check https://developers.google.com/unity/archive"
    Write-Host "in case these versions have been retired, then update the versions above."
    exit 1
}

Write-Host "`nAll packages present. Click the Unity Editor window to make it re-resolve —"
Write-Host "it only does so on focus, which is the usual reason a manifest edit 'does nothing'."
