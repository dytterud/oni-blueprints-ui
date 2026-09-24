<#
.SYNOPSIS
    Rebuild the blueprints_ui prefabs from spec/ and build the bundle for all three platforms.

.DESCRIPTION
    Runs Unity in batch mode (BlueprintsUi.Editor.Build.All), writing out/{windows,mac,linux}/blueprints_ui,
    then diffs each built bundle against spec/ with tools/compare.py unless -SkipCompare is given.

    Needs a Unity 6000.3.x editor with Mac and Linux Build Support (Mono) installed, and for the
    compare step Python 3 with UnityPy and Pillow.

.PARAMETER Unity
    Path to Unity.exe. Defaults to the Unity Hub install matching ProjectSettings/ProjectVersion.txt,
    else the newest 6000.3.x the Hub has.
#>
param(
    [string]$Unity,
    [switch]$SkipCompare
)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot

if (-not $Unity) {
    $wanted = (Select-String -Path "$project/ProjectSettings/ProjectVersion.txt" -Pattern 'm_EditorVersion: (\S+)').Matches[0].Groups[1].Value
    $hub = 'C:\Program Files\Unity\Hub\Editor'
    $exact = Join-Path $hub "$wanted\Editor\Unity.exe"
    if (Test-Path $exact) {
        $Unity = $exact
    } else {
        $candidate = Get-ChildItem $hub -Directory -Filter '6000.3.*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($candidate) { $Unity = Join-Path $candidate.FullName 'Editor\Unity.exe' }
    }
    if (-not $Unity -or -not (Test-Path $Unity)) {
        throw "No Unity 6000.3.x found under $hub. Install one with Unity Hub (plus Mac and Linux Build Support (Mono)), or pass -Unity <path to Unity.exe>."
    }
}

New-Item -ItemType Directory -Force "$project/Build" | Out-Null
$log = "$project/Build/unity.log"
Write-Host "Building with $Unity (log: $log)"
$p = Start-Process -FilePath $Unity -Wait -PassThru -NoNewWindow -ArgumentList @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', "`"$project`"",
    '-executeMethod', 'BlueprintsUi.Editor.Build.All',
    '-logFile', "`"$log`""
)
Select-String -Path $log -Pattern '\[BlueprintsUi\]' | ForEach-Object { $_.Line }
if ($p.ExitCode -ne 0) {
    throw "Unity exited with $($p.ExitCode); see $log"
}

if (-not $SkipCompare) {
    $failed = $false
    foreach ($platform in 'windows', 'mac', 'linux') {
        Write-Host "--- compare $platform"
        python "$project/tools/compare.py" "$project/out/$platform/blueprints_ui"
        if ($LASTEXITCODE -ne 0) { $failed = $true }
    }
    if ($failed) { throw 'Built bundle differs from spec/; see above.' }
}
Write-Host "Done: out/{windows,mac,linux}/blueprints_ui"
