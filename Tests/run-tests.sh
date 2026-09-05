#!/usr/bin/env bash
# Runs every behaviour suite without RimWorld, Unity, Harmony or a game install.
#
# The suites exist because the 0.7 rules layers (Integration/, Placement/, Sizing/, Economy/) are
# deliberately dependency-free: no Find, no Harmony, no Unity, no TechLevel. That is what lets them
# be compiled against the hand-written doubles in RimWorldStubs.cs and run anywhere a C# compiler
# exists. Anything that genuinely needs the game is not tested here and is not pretended to be.
#
# Compiler: uses mono's mcs when present, otherwise falls back to the .NET SDK (dotnet), which is
# what Windows dev machines have. Same suites, same files, either way.
#
#   Ubuntu/WSL:  sudo apt-get install -y mono-mcs mono-runtime
#   Windows:     .NET SDK on PATH (dotnet)
#   Then:        Tests/run-tests.sh
#
# Exits non-zero if any suite fails to build or fails an assertion. Each binary is removed before
# its build so a compile failure can never be masked by a stale binary reporting a stale pass —
# that has happened here and produced a green run from a build that did not occur.
set -u

cd "$(dirname "$0")/.." || exit 1

SRC=Source
OUT="${TMPDIR:-/tmp}/rt-tests"
mkdir -p "$OUT"

MCS_FLAGS="-langversion:latest -nowarn:0169,0414,0649,0219,0067"
failures=0

if command -v mcs > /dev/null 2>&1; then
    COMPILER=mcs
elif command -v dotnet > /dev/null 2>&1; then
    COMPILER=dotnet
else
    echo "Neither mcs (mono) nor dotnet (.NET SDK) is on PATH; cannot build the suites." >&2
    exit 1
fi

# Windows-native tooling wants C:/ paths, not /c/ ones; cygpath exists exactly where that matters.
winpath() { if command -v cygpath > /dev/null 2>&1; then cygpath -m "$1"; else echo "$1"; fi; }

# run_suite <name> <Exe|Library> <files...> — build with whichever compiler is available; run if Exe.
run_suite() {
    name=$1; kind=$2; shift 2

    if [ "$COMPILER" = mcs ]; then
        if [ "$kind" = Exe ]; then target=exe; binary="$OUT/$name.exe"; else target=library; binary="$OUT/$name.dll"; fi
        rm -f "$binary"
        if ! mcs -target:$target $MCS_FLAGS -out:"$binary" "$@"; then
            echo "BUILD FAILED: $name"; failures=$((failures + 1)); return
        fi
        if [ "$kind" = Exe ] && ! mono "$binary"; then failures=$((failures + 1)); fi
        return
    fi

    # dotnet path: emit a minimal csproj listing exactly the same files, then build (and run, if Exe).
    proj="$OUT/$name"; rm -rf "$proj"; mkdir -p "$proj"
    {
        echo '<Project Sdk="Microsoft.NET.Sdk">'
        echo "  <PropertyGroup>"
        echo "    <OutputType>$kind</OutputType>"
        echo "    <TargetFramework>net8.0</TargetFramework>"
        echo "    <Nullable>disable</Nullable>"
        echo "    <LangVersion>latest</LangVersion>"
        echo "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>"
        echo "    <NoWarn>0169;0414;0649;0219;0067</NoWarn>"
        echo "    <AssemblyName>$name</AssemblyName>"
        echo "  </PropertyGroup>"
        echo "  <ItemGroup>"
        for f in "$@"; do echo "    <Compile Include=\"$(winpath "$PWD/$f")\" />"; done
        echo "  </ItemGroup>"
        echo '</Project>'
    } > "$proj/$name.csproj"

    if ! dotnet build "$proj/$name.csproj" -v q --nologo -c Release -o "$proj/bin" > "$proj/build.log" 2>&1; then
        echo "BUILD FAILED: $name"; tail -12 "$proj/build.log"; failures=$((failures + 1)); return
    fi
    if [ "$kind" = Exe ] && ! dotnet "$proj/bin/$name.dll"; then failures=$((failures + 1)); fi
}

# The pure Integration set: the adapter/classifier rules layer, minus the game-glue that moved into
# the folder over 0.7.2-0.8 (debug reports, MCP tool registration, the claim hook, the holding
# creators). Those files need a running game, are stubbed via RimWorldStubsExt, and are held to
# their real shapes by the type-check below instead.
INTEGRATION_PURE=$(ls $SRC/Integration/*.cs | grep -v -e RegionDebugReports -e RegionMcpTools -e TerritoryClaimHooks -e PopulationDynamics -e VoeOutpostCreator -e HoldingCreatorRegistry -e IHoldingCreator)

# Sizing tables are pure EXCEPT the game-coupled glue (SettlementGrowthUtility reads Find / region
# demographics), which cannot compile against the stubs — the real build covers it, like the other glue.
SIZING_PURE=$(ls $SRC/Sizing/*.cs | grep -v -e SettlementGrowthUtility -e SettlementGrowthHooks)

run_suite integration Exe \
    Tests/RimWorldStubs.cs Tests/IntegrationTests.cs \
    $INTEGRATION_PURE

run_suite placement Exe \
    Tests/RimWorldStubs.cs Tests/PlacementTests.cs \
    $INTEGRATION_PURE $SRC/Placement/*.cs

# 0.8 outpost-seeding rules: tier -> allowance and terrain -> archetype. Both pure, so this needs
# only the Sizing tables and the WorldObjectKind enum they read.
run_suite outpostrules Exe \
    Tests/RimWorldStubs.cs Tests/OutpostRulesTests.cs \
    $SRC/Integration/WorldObjectKind.cs $SIZING_PURE

# 0.3.0 settlement birthrate-growth core (#6): tech-informed rate + logistic step toward the target.
# Pure, no game — needs only the standalone BirthrateRules.
run_suite birthrate Exe \
    Tests/BirthrateRulesTests.cs \
    $SRC/Sizing/BirthrateRules.cs

# 0.8 demographics core: the deterministic per-tile seed + RNG + weighted picking. Pure, no game.
run_suite demographics Exe \
    Tests/DemographicsRulesTests.cs \
    $SRC/Demographics/DemographicsRules.cs

# 0.2.0 age-structure core (#10): tech-level base pyramids + natalist/longevity skews + median age.
# Pure, no game — same rationale as the demographics core above.
run_suite agestructure Exe \
    Tests/AgeStructureRulesTests.cs \
    $SRC/Demographics/AgeStructureRules.cs

# 0.2.0 education-structure core (#15): tech-level base distributions + research/aptitude skews + index.
run_suite education Exe \
    Tests/EducationRulesTests.cs \
    $SRC/Demographics/EducationRules.cs

# 0.3.0 residence core: population -> homes, occupancy (extended->nuclear), and land, driven by how
# urban a place is. Pure, no game.
run_suite residence Exe \
    Tests/ResidenceRulesTests.cs \
    $SRC/Demographics/ResidenceRules.cs

# 0.3.0 faction-character core (#27): base/DLC archetype classification + knowledge/wealth skews, and the
# end-to-end effect on education. Pure, no game — needs the character rules and the education core.
run_suite factioncharacter Exe \
    Tests/FactionCharacterRulesTests.cs \
    $SRC/Demographics/FactionCharacterRules.cs $SRC/Demographics/EducationRules.cs

# 0.2.0 socioeconomic-tiering core (#14): wealth thresholds -> SES tiers + index. Pure, no game.
run_suite socioeconomic Exe \
    Tests/SocioeconomicRulesTests.cs \
    $SRC/Demographics/SocioeconomicRules.cs

# 0.2.0 employment core (#16): tech base sector splits + signal-driven shares + employment rate.
run_suite employment Exe \
    Tests/EmploymentRulesTests.cs \
    $SRC/Demographics/EmploymentRules.cs

# 0.2.0 territory-shape core (#19): embeddedness, desired-ratio scoring, domain compactness.
run_suite compactness Exe \
    Tests/CompactnessRulesTests.cs \
    $SRC/Placement/CompactnessRules.cs

# 0.3.0 border-first partition core (#20): boundary strength from tile-definition shifts, wall
# thresholding, anchor sizing/spacing, undersized-merge, weakest-border merge target. Pure, no game.
run_suite border Exe \
    Tests/BorderRulesTests.cs \
    $SRC/Partition/BorderRules.cs

# 0.3.0 sprawl spread: a settlement's people over its own tile and the tiles its terrain-aware sprawl
# reaches, in proportion to the sprawl weights, total conserved. Pure, no game.
run_suite sprawl Exe \
    Tests/SprawlRulesTests.cs \
    $SRC/Demographics/SprawlRules.cs

# 0.3.0 road-linking search core (#38): hop-bounded settlement road search. Same road as the old
# unbounded search for any reachable pair; a local scan instead of a landmass flood for a cut-off one.
run_suite roads Exe \
    Tests/RoadPathRulesTests.cs \
    $SRC/Roads/RoadPathRules.cs

run_suite resource Exe \
    Tests/RimWorldStubs.cs Tests/ResourceTests.cs \
    $INTEGRATION_PURE $SRC/Economy/*.cs

# The residency suite moved to the Living World companion mod (0.8) along with the residency code it
# exercises — see LivingWorld/Tests/run-tests.sh.

# Not a suite: a type-check over the impure files that cannot be behaviour-tested without a running
# game, but whose signatures can still be held to the shapes they will really meet. This is what
# catches a patch calling a method that no longer exists — the failure mode a mod cannot see until
# the game loads it.
#
# Known gap: the game-glue files excluded from INTEGRATION_PURE above are not yet covered here
# either — RegionDebugReports and friends grew ~40 game members through 0.8 that RimWorldStubsExt
# does not stub. Tracked as harness drift; extend the stubs rather than widening this list blindly.
echo
echo "== type-check (impure files, signatures only) =="
pre_typecheck_failures=$failures
run_suite typecheck Library \
    Tests/RimWorldStubs.cs Tests/RimWorldStubsExt.cs \
    $INTEGRATION_PURE $SRC/Placement/*.cs $SRC/Economy/*.cs \
    $SRC/RegionalDomainStatus.cs $SIZING_PURE $SRC/Demographics/DemographicsRules.cs \
    $SRC/WorldObjectPlacementUtility.cs $SRC/OutpostPlacementUtility.cs \
    $SRC/RegionalOwnershipUtility.cs \
    $SRC/GeographicProvince.cs $SRC/BiomeSafe.cs $SRC/IRegionDemographicProvider.cs \
    $SRC/Demographics/AgeStructureRules.cs \
    $SRC/Demographics/EducationRules.cs \
    $SRC/Demographics/FactionCharacterRules.cs \
    $SRC/Demographics/SocioeconomicRules.cs \
    $SRC/Demographics/EmploymentRules.cs \
    $SRC/Partition/BorderRules.cs \
    $SRC/ProvinceAdjacency.cs \
    \
    $SRC/Patches/Patch_TileFinder_IsValidTileForNewSettlement.cs \
    $SRC/Patches/Patch_WorldInspectPane_TileInspectString.cs
[ "$failures" -eq "$pre_typecheck_failures" ] && echo "  type-check clean"

echo
if [ "$failures" -eq 0 ]; then
    echo "ALL SUITES PASSED"
    exit 0
fi

echo "$failures SUITE(S) FAILED"
exit 1
