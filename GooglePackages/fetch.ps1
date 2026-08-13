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

$ErrorActionPreference = 'Stop'

$FIREBASE = '13.15.0'
$EDM      = '1.2.186'      # External Dependency Manager, versioned separately

$packages = @(
    @{ id = 'com.google.external-dependency-manager'; version = $EDM },
    @{ id = 'com.google.firebase.app';                version = $FIREBASE },
    @{ id = 'com.google.firebase.auth';               version = $FIREBASE },
    @{ id = 'com.google.firebase.firestore';          version = $FIREBASE },
    @{ id = 'com.google.firebase.functions';          version = $FIREBASE }
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

    $url = "https://dl.google.com/games/registry/unity/$($package.id)/$file"
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
