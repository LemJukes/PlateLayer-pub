<#
.SYNOPSIS
    Builds PlateLayer in Release and produces the distributable ZIP in dist\.

.DESCRIPTION
    The repeatable release step: the sequence of manual copies that would otherwise sit in an
    operator's head between a clean checkout and a file somebody else can install.

    Output: dist\PlateLayer-<FriendlyVersion>.zip, whose root contains

        PlateLayer.bundle\PackageContents.xml
        PlateLayer.bundle\Contents\PlateLayer.Cad.dll
        PlateLayer.bundle\Contents\PlateLayer.Core.dll
        INSTALL.txt

    THE CHECKS IN THIS SCRIPT ARE NOT EVIDENCE THAT THE PLUG-IN LOADS. They guard against shipping
    a structurally broken ZIP - a missing DLL, an unparseable manifest, a manifest whose ModuleName
    points at a file that is not in the package, a manifest whose version has drifted from the
    build. A directory listing saying "right files, right layout" is not the criterion; the
    criterion is that AutoCAD loads it, and only installing the ZIP on a machine and typing PLL
    asks AutoCAD anything.

.PARAMETER Configuration
    Build configuration. Defaults to Release. Debug is accepted for testing the script itself.

.PARAMETER AcadDir
    AutoCAD install directory, if not the default. Passed through to the build.

.PARAMETER SkipBuild
    Package whatever is already in bin\<Configuration>. For iterating on the packaging itself,
    not for producing a release.

.EXAMPLE
    pwsh -File dev\pack.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',
    [string] $AcadDir,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot    = Split-Path -Parent $PSScriptRoot
$manifestSrc = Join-Path $repoRoot 'PlateLayer.bundle\PackageContents.xml'
$propsSrc    = Join-Path $repoRoot 'Directory.Build.props'
$installSrc  = Join-Path $repoRoot 'dev\INSTALL.txt'
$solution    = Join-Path $repoRoot 'PlateLayer.sln'
$binDir      = Join-Path $repoRoot "src\PlateLayer.Cad\bin\$Configuration\net8.0-windows"
$distDir     = Join-Path $repoRoot 'dist'
$stageRoot   = Join-Path $distDir 'staging'
$bundleDir   = Join-Path $stageRoot 'PlateLayer.bundle'
$contentsDir = Join-Path $bundleDir 'Contents'

# The whole payload. The AutoCAD assemblies are referenced with <Private>false</Private> and
# System.Text.Json is in-box in the .NET 8 shared framework the host provides, so neither ships.
$payload = @('PlateLayer.Cad.dll', 'PlateLayer.Core.dll')

function Fail([string] $message) {
    Write-Host "pack: FAILED - $message" -ForegroundColor Red
    exit 1
}

# --- 1. Read the manifest, and hold it to the build's version -----------------------------------
# Directory.Build.props is the single source of truth for the version - PLL prints it off the
# assembly rather than from a literal. The manifest necessarily carries a second copy, so the two
# are compared here instead of being left to drift. PartLayer shipped that drift as a known rough
# edge; this catches it at pack time.

if (-not (Test-Path -LiteralPath $manifestSrc)) { Fail "no manifest at $manifestSrc" }
if (-not (Test-Path -LiteralPath $propsSrc))    { Fail "no Directory.Build.props at $propsSrc" }

try {
    [xml] $manifest = Get-Content -LiteralPath $manifestSrc -Raw
} catch {
    Fail "PackageContents.xml is not well-formed XML: $($_.Exception.Message)"
}

$appPackage = $manifest.ApplicationPackage
if (-not $appPackage) { Fail 'PackageContents.xml has no <ApplicationPackage> root element' }

$friendly = $appPackage.FriendlyVersion
$appVer   = $appPackage.AppVersion
if ([string]::IsNullOrWhiteSpace($friendly)) { Fail 'manifest carries no FriendlyVersion' }
if ([string]::IsNullOrWhiteSpace($appVer))   { Fail 'manifest carries no AppVersion' }

[xml] $props = Get-Content -LiteralPath $propsSrc -Raw
$propVersion  = ($props.SelectSingleNode('//VersionPrefix')).InnerText
$propFriendly = ($props.SelectSingleNode('//InformationalVersion')).InnerText

if ($appVer -ne $propVersion) {
    Fail "manifest AppVersion '$appVer' does not match Directory.Build.props VersionPrefix '$propVersion' - bump both or neither"
}
if ($friendly -ne $propFriendly) {
    Fail "manifest FriendlyVersion '$friendly' does not match Directory.Build.props InformationalVersion '$propFriendly'"
}

# An <ApplicationPackage> with no <ComponentEntry> is the shape AutoCAD parses, finds nothing to
# load in, and says nothing at all about.
$entries = @($manifest.SelectNodes('//ComponentEntry'))
if ($entries.Count -eq 0) { Fail 'manifest declares no <ComponentEntry> - AutoCAD would load nothing, silently' }

Write-Host "pack: PlateLayer $friendly ($Configuration)" -ForegroundColor Cyan

# --- 2. Build -----------------------------------------------------------------------------------

if ($SkipBuild) {
    Write-Host 'pack: -SkipBuild given; packaging whatever is already in bin' -ForegroundColor Yellow
} else {
    Write-Host "pack: dotnet build -c $Configuration"
    if ($AcadDir) {
        & dotnet build $solution -c $Configuration -p:AcadDir="$AcadDir" --nologo
    } else {
        & dotnet build $solution -c $Configuration --nologo
    }
    if ($LASTEXITCODE -ne 0) { Fail "dotnet build exited $LASTEXITCODE" }

    # PlateLayer.Cad.csproj's DeployBundle target runs AfterTargets="Build" in every configuration,
    # so this build has just replaced the local %APPDATA% bundle with $Configuration binaries.
    # Harmless, but it means "what is installed locally" tracks whichever configuration was built
    # last. Say so rather than letting it be discovered.
    Write-Host "pack: note - the local %APPDATA% bundle now holds $Configuration binaries (DeployBundle)" -ForegroundColor DarkGray

    Write-Host 'pack: dotnet test'
    & dotnet test (Join-Path $repoRoot 'test\PlateLayer.Core.Tests') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { Fail "dotnet test exited $LASTEXITCODE - not packaging a red build" }
}

# --- 3. Stage the bundle -------------------------------------------------------------------------

if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $contentsDir -Force | Out-Null

Copy-Item -LiteralPath $manifestSrc -Destination $bundleDir

foreach ($dll in $payload) {
    $src = Join-Path $binDir $dll
    if (-not (Test-Path -LiteralPath $src)) { Fail "$dll is not in $binDir - build $Configuration first" }
    Copy-Item -LiteralPath $src -Destination $contentsDir
}

if (-not (Test-Path -LiteralPath $installSrc)) { Fail "no install note at $installSrc" }
Copy-Item -LiteralPath $installSrc -Destination $stageRoot

# --- 4. The built assembly must agree with the manifest ------------------------------------------
# Belt and braces over the props check: this reads the DLL that is actually going in the ZIP.

$builtVersion = (Get-Item -LiteralPath (Join-Path $contentsDir 'PlateLayer.Cad.dll')).VersionInfo.FileVersion
if ($builtVersion -and -not $builtVersion.StartsWith($appVer)) {
    Fail "PlateLayer.Cad.dll is version $builtVersion but the manifest says $appVer - stale bin\, or a version bumped in only one place"
}

# --- 5. Cross-check the manifest against what was actually staged --------------------------------
# ModuleName is a path relative to the bundle folder. If it names a file the package does not
# contain, the bundle is broken in the one way that produces no error message at all.

foreach ($entry in $entries) {
    $moduleName = $entry.GetAttribute('ModuleName')
    if ([string]::IsNullOrWhiteSpace($moduleName)) { Fail 'a <ComponentEntry> has no ModuleName' }
    $moduleRel  = ($moduleName -replace '^\./', '') -replace '/', '\'
    if (-not (Test-Path -LiteralPath (Join-Path $bundleDir $moduleRel))) {
        Fail "ModuleName '$moduleName' is not in the staged bundle"
    }
}

foreach ($mapping in @($manifest.SelectNodes('//AssemblyMapping'))) {
    $mapPath = $mapping.GetAttribute('Path')
    $mapRel  = ($mapPath -replace '^\./', '') -replace '/', '\'
    if (-not (Test-Path -LiteralPath (Join-Path $bundleDir $mapRel))) {
        Fail "AssemblyMapping Path '$mapPath' is not in the staged bundle"
    }
}

# Nothing of Autodesk's may ship. The references are <Private>false</Private>; this asserts it.
foreach ($forbidden in @('acdbmgd.dll', 'accoremgd.dll', 'acmgd.dll')) {
    if (Test-Path -LiteralPath (Join-Path $contentsDir $forbidden)) {
        Fail "$forbidden is in the staged bundle - the reference lost its <Private>false</Private>"
    }
}

# --- 6. Zip --------------------------------------------------------------------------------------

$slug    = $friendly -replace '\s+', '-'
$zipPath = Join-Path $distDir "PlateLayer-$slug.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Compress-Archive -Path (Join-Path $stageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

# --- 7. Read the ZIP back and confirm it holds what it should ------------------------------------

Add-Type -AssemblyName System.IO.Compression.FileSystem
$expected = @(
    'PlateLayer.bundle/PackageContents.xml',
    'PlateLayer.bundle/Contents/PlateLayer.Cad.dll',
    'PlateLayer.bundle/Contents/PlateLayer.Core.dll',
    'INSTALL.txt'
)
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
    foreach ($want in $expected) {
        if ($names -notcontains $want) { Fail "ZIP is missing $want" }
    }
} finally {
    $archive.Dispose()
}

Remove-Item -LiteralPath $stageRoot -Recurse -Force

$zip  = Get-Item -LiteralPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $($zip.Name)" -NoNewline

Write-Host ''
Write-Host "pack: $($zip.FullName)" -ForegroundColor Green
Write-Host ("pack: {0:N0} bytes" -f $zip.Length)
Write-Host "pack: SHA256 $hash"
foreach ($name in $expected) { Write-Host "pack:   $name" }
Write-Host ''
Write-Host 'pack: structure only. Whether AutoCAD loads it is answered by installing this ZIP and typing PLL.' -ForegroundColor Yellow
