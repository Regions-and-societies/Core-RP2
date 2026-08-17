# release-manifest.ps1 — generate Assemblies/CHECKSUMS.sha256 from the FINAL release build.
#
# Run at release cut time only: after the last compile, before the tag (ship-it Step 1).
# Never regenerate retroactively — a manifest written from a dev build is a fabricated
# record and blinds the exact check it exists to provide (issue #4).
#
# The manifest is standard sha256sum format (one "<hash> *<file>" line per binary,
# paths relative to Assemblies/), so `sha256sum -c CHECKSUMS.sha256` also verifies it.
#
# Usage: ./harness/release-manifest.ps1 [-Root <repo>] [-Force]
#   -Force  skip the stale-build guard (source newer than DLL)

param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$assembliesDir = Join-Path $Root 'Assemblies'
$manifestPath = Join-Path $assembliesDir 'CHECKSUMS.sha256'

$dlls = @(Get-ChildItem -Path $assembliesDir -Filter '*.dll' -File -ErrorAction SilentlyContinue)
if ($dlls.Count -eq 0) {
    Write-Error "No DLLs found in $assembliesDir — build the release first (dotnet build -c Release)."
}

# Stale-build guard: any source file newer than the oldest DLL means the binaries
# do not come from the current source tree.
$oldestDll = ($dlls | Sort-Object LastWriteTimeUtc | Select-Object -First 1)
$newerSource = @(Get-ChildItem -Path (Join-Path $Root 'Source') -Filter '*.cs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.LastWriteTimeUtc -gt $oldestDll.LastWriteTimeUtc })
if ($newerSource.Count -gt 0 -and -not $Force) {
    $names = ($newerSource | Select-Object -First 5 | ForEach-Object { $_.Name }) -join ', '
    Write-Error "Stale build: $($newerSource.Count) source file(s) newer than $($oldestDll.Name) (e.g. $names). Rebuild, or pass -Force if you know the build is final."
}

$lines = foreach ($dll in ($dlls | Sort-Object Name)) {
    $hash = (Get-FileHash -Path $dll.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($dll.Name)"
}

# LF endings and no BOM so unix sha256sum -c accepts it verbatim
[System.IO.File]::WriteAllText($manifestPath, (($lines -join "`n") + "`n"))

Write-Host "Wrote $manifestPath"
$lines | ForEach-Object { Write-Host "  $_" }
Write-Host "Commit this file on the release branch before tagging."
