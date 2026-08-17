# verify-binaries.ps1 — verify Assemblies/*.dll against Assemblies/CHECKSUMS.sha256.
#
# Run at release cut time (ship-it Step 1, after release-manifest.ps1) and against any
# deployed copy of the mod to prove the binaries are the ones the release recorded —
# in particular that an MMF/RP2 edition folder carries its OWN assembly (issue #4).
#
# Exit 0 only when every manifest entry matches and no unlisted DLL is present.
#
# Usage: ./harness/verify-binaries.ps1 [-Root <repo-or-deployed-mod-folder>]

param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$assembliesDir = Join-Path $Root 'Assemblies'
$manifestPath = Join-Path $assembliesDir 'CHECKSUMS.sha256'

if (-not (Test-Path $manifestPath)) {
    Write-Error "No manifest at $manifestPath — run release-manifest.ps1 at cut time first."
}

$failures = 0
$listed = @{}

foreach ($line in Get-Content $manifestPath) {
    if ($line -notmatch '^([0-9a-fA-F]{64}) \*?(.+)$') {
        Write-Host "MALFORMED  $line"; $failures++; continue
    }
    $expected = $Matches[1].ToLowerInvariant()
    $name = $Matches[2].Trim()
    $listed[$name] = $true
    $path = Join-Path $assembliesDir $name
    if (-not (Test-Path $path)) {
        Write-Host "MISSING    $name"; $failures++; continue
    }
    $actual = (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -eq $expected) {
        Write-Host "OK         $name"
    } else {
        Write-Host "MISMATCH   $name"
        Write-Host "           expected $expected"
        Write-Host "           actual   $actual"
        $failures++
    }
}

# A DLL the manifest never recorded is as much a provenance failure as a mismatch.
foreach ($dll in @(Get-ChildItem -Path $assembliesDir -Filter '*.dll' -File)) {
    if (-not $listed.ContainsKey($dll.Name)) {
        Write-Host "UNLISTED   $($dll.Name)"; $failures++
    }
}

if ($failures -gt 0) {
    Write-Error "verify-binaries: $failures failure(s)."
}
Write-Host "verify-binaries: clean."
