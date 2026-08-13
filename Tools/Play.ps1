<#
.SYNOPSIS
    Launch the Windows player, rebuilding it first if any source has changed.

.DESCRIPTION
    Freshness is tracked with a stamp file rather than the exe's timestamp, because
    Unity does not rewrite GlimmerGrove.exe when only managed assemblies or content
    change -- a real build here left the exe at 07:16 and its _Data payload at 08:51.
    Trusting the exe's mtime would silently launch a stale player.

    The stamp records the newest source timestamp observed AFTER a successful build.
    Scanning post-build (rather than stamping the clock) matters because Unity rewrites
    ProjectSettings.asset as it quits; stamping the clock would leave that write looking
    like a fresh edit and rebuild on every single launch.

.PARAMETER Force
    Rebuild even if nothing changed.

.PARAMETER SkipBuild
    Launch whatever is on disk, no staleness check.

.PARAMETER NoLaunch
    Build if needed, but do not start the player. For CI and unattended rebuilds.

.PARAMETER NonInteractive
    Never prompt. Any question that would have been asked is answered "no", so a
    blocked or failed build exits non-zero instead of hanging on a dead stdin.
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$SkipBuild,
    [switch]$NoLaunch,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'

$Root     = Split-Path -Parent $PSScriptRoot
$Exe      = Join-Path $Root 'Builds\Win\GlimmerGrove.exe'
$Stamp    = Join-Path $Root 'Builds\Win\build-stamp.json'
$BuildLog = Join-Path $Root 'Logs\Build.log'
$Watch    = @('Assets', 'Packages', 'ProjectSettings')

function Confirm-Continue([string]$message) {
    if ($NonInteractive) { return $false }
    return ((Read-Host $message) -match '^(y|yes)$')
}

function Get-NewestSourceUtc {
    $paths = $Watch | ForEach-Object { Join-Path $Root $_ } | Where-Object { Test-Path $_ }
    $newest = Get-ChildItem -Path $paths -Recurse -File -Force -ErrorAction SilentlyContinue |
              Measure-Object -Property LastWriteTimeUtc -Maximum
    if ($null -eq $newest.Maximum) { return [datetime]::MinValue }
    return $newest.Maximum
}

function Get-UnityExe {
    if ($env:GLIMMER_UNITY -and (Test-Path $env:GLIMMER_UNITY)) { return $env:GLIMMER_UNITY }

    $versionFile = Join-Path $Root 'ProjectSettings\ProjectVersion.txt'
    $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$'
    if (-not $line) { throw "Could not read the editor version from $versionFile" }
    $version = $line.Matches[0].Groups[1].Value.Trim()

    $candidate = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (-not (Test-Path $candidate)) {
        throw "Unity $version is not installed at $candidate. Set GLIMMER_UNITY to the Unity.exe to use."
    }
    return $candidate
}

# The Editor holds an exclusive lock on the project; a batchmode build against an open
# project fails outright, so detect it and let the user decide rather than failing late.
function Test-EditorOpen {
    $procs = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        if (-not $p.CommandLine) { continue }
        if ($p.CommandLine -match '-adb2|AssetImportWorker') { continue }
        if ($p.CommandLine -replace '/', '\' -like "*$Root*") { return $true }
    }
    return $false
}

function Invoke-Build {
    $unity = Get-UnityExe
    New-Item -ItemType Directory -Force -Path (Split-Path $BuildLog) | Out-Null

    Write-Host "Building the Windows player. This takes a few minutes..." -ForegroundColor Cyan
    Write-Host "  log: $BuildLog" -ForegroundColor DarkGray

    # Paths are quoted individually because Start-Process on Windows PowerShell 5.1 joins
    # -ArgumentList with spaces and does NOT quote the elements: an unquoted project path
    # splits at "...Desktop\mobile game 3" and Unity dies with exit 127.
    # Not $args -- that is an automatic variable inside a function.
    $unityArgs = @(
        '-batchmode', '-nographics'
        '-projectPath', "`"$Root`""
        '-executeMethod', 'GlimmerGrove.EditorTools.DevBuild.BuildWindowsBatch'
        '-logFile', "`"$BuildLog`""
    )
    $proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -Wait -NoNewWindow

    if ($proc.ExitCode -ne 0) {
        Write-Host "Build FAILED (exit $($proc.ExitCode))." -ForegroundColor Red
        if (Test-Path $BuildLog) {
            $errors = Select-String -Path $BuildLog -Pattern 'error CS|BuildFailedException|build failed|Error building' |
                      Select-Object -Last 15
            if ($errors) {
                Write-Host "`n--- from the build log ---" -ForegroundColor Red
                $errors | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
            }
        }
        return $false
    }

    # Re-scan after the build so anything Unity rewrote on its way out is counted as built.
    @{ newestSourceUtc = (Get-NewestSourceUtc).ToString('o'); builtAtUtc = (Get-Date).ToUniversalTime().ToString('o') } |
        ConvertTo-Json | Set-Content -Path $Stamp -Encoding utf8
    Write-Host "Build succeeded." -ForegroundColor Green
    return $true
}

# --- decide whether a rebuild is needed -------------------------------------------------

$needsBuild = $false
$reason     = ''

if ($SkipBuild) {
    $needsBuild = $false
} elseif ($Force) {
    $needsBuild = $true; $reason = '-Force was passed'
} elseif (-not (Test-Path $Exe)) {
    $needsBuild = $true; $reason = 'no player has been built yet'
} elseif (-not (Test-Path $Stamp)) {
    $needsBuild = $true; $reason = 'no build stamp -- the player on disk is of unknown vintage'
} else {
    $stampData = Get-Content $Stamp -Raw | ConvertFrom-Json
    $builtFrom = [datetime]::Parse($stampData.newestSourceUtc, $null, 'RoundtripKind')
    $newest    = Get-NewestSourceUtc
    if ($newest -gt $builtFrom) {
        $needsBuild = $true
        $reason = "sources changed since the last build ($($newest.ToLocalTime().ToString('yyyy-MM-dd HH:mm')))"
    }
}

if ($needsBuild) {
    Write-Host "Player is out of date: $reason." -ForegroundColor Yellow

    if (Test-EditorOpen) {
        Write-Host ""
        Write-Host "The Unity Editor has this project open, so a batchmode build cannot run." -ForegroundColor Yellow
        Write-Host "Either close the Editor and run this again, or build from the menu:" -ForegroundColor Yellow
        Write-Host "    Glimmer Grove > Build Windows Player" -ForegroundColor White
        Write-Host ""
        if (-not (Confirm-Continue "Launch the stale player anyway? (y/N)")) { exit 1 }
    }
    elseif (-not (Invoke-Build)) {
        Write-Host ""
        if (-not (Confirm-Continue "Launch the previous player anyway? (y/N)")) { exit 1 }
    }
}

if ($NoLaunch) { exit 0 }

if (-not (Test-Path $Exe)) {
    Write-Host "No player to launch at $Exe." -ForegroundColor Red
    exit 1
}

Write-Host "Launching $Exe" -ForegroundColor Cyan
Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
