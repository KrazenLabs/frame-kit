<#
.SYNOPSIS
  Builds the shared FrameKit panel service for the Steam Frame (ARM64 Linux) with Zig.

.DESCRIPTION
  Produces build\framekit-panel\ : the framekit-panel binary, its systemd unit and VERSION,
  which `framekit package` bundles into every app package. src\panel.js and VERSION are
  embedded into the binary through generated headers in build\gen\. The generic helpers (files,
  text, JSON, clock, log, stop signals, the DevTools client) come from the kit's ..\common\
  (FrameKit common, the one copy the apps vendor too), in ..\common\src\framekit\.

  The build goes to build\framekit-panel.new\ first. If its three files are the same as the ones
  in build\framekit-panel\ (the build is reproducible), that folder is kept, and with it the
  signature `framekit panel-sign` added; otherwise it is replaced and needs signing again.

  Zig is looked up in: $env:ZIG, tools\zig\zig.exe (in this folder), PATH.

.EXAMPLE
  .\build.ps1          # build for the headset
  .\build.ps1 -Test    # also build and run the offline tests on this PC
  .\build.ps1 -Clean   # wipe build\ first (also the signed build)
#>
param(
    [switch]$Test,
    [switch]$Clean
)

# Native tools report failure through exit codes (checked below); "Stop" would make
# PowerShell 5.1 abort on harmless stderr warnings when output is redirected.
$ErrorActionPreference = "Continue"
$root = $PSScriptRoot

function Find-Zig {
    $candidates = @()
    if ($env:ZIG) { $candidates += $env:ZIG }
    $candidates += (Join-Path $root "tools\zig\zig.exe")
    foreach ($c in $candidates) { if (Test-Path $c) { return (Resolve-Path $c).Path } }
    $onPath = Get-Command zig -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "Zig not found. Unpack zig-x86_64-windows-0.16.0.zip into tools\zig\."
}

$zig = Find-Zig

# Linking can make Zig build its bundled libc++ first (empty cache, new Zig or new target). That
# build prints thousands of harmless warnings from libc++'s own sources which look like errors
# in a console, so the output of those steps is kept back and only shown when the step fails.
function Invoke-ZigQuiet([string]$what, [string[]]$arguments) {
    $output = & $zig @arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "$what failed"
    }
}

# glibc 2.28 is old enough for any current SteamOS image; libc++ is linked statically by zig.
$target = "aarch64-linux-gnu.2.28"
$build = Join-Path $root "build"
$objDir = Join-Path $build "obj\aarch64"
$genDir = Join-Path $build "gen"
$kit = Split-Path $root
$contract = [IO.File]::ReadAllText((Join-Path $kit "contract\framekit-contract.json")) | ConvertFrom-Json
$panelBinary = $contract.device.panelBinary
$outDir = Join-Path $build $panelBinary
$newDir = "$outDir.new"
$jsoncpp = Join-Path $root "third_party\jsoncpp"
$utf8 = New-Object Text.UTF8Encoding $false

if ($Clean -and (Test-Path $build)) { Remove-Item $build -Recurse -Force }
if (Test-Path $newDir) { Remove-Item $newDir -Recurse -Force }
New-Item -ItemType Directory -Force $objDir, $genDir, $newDir | Out-Null

function Write-IfChanged([string]$path, [string]$content) {
    if (-not (Test-Path $path) -or [IO.File]::ReadAllText($path) -ne $content) {
        [IO.File]::WriteAllText($path, $content, $utf8)
    }
}

python -I (Join-Path $kit "contract\generate.py") --check
if ($LASTEXITCODE -ne 0) { throw "the contract's generated files are out of date: run python -I contract\generate.py" }

$version = ([IO.File]::ReadAllText((Join-Path $root $contract.package.panelVersionFile))).Trim()
if ($version -notmatch "^(?:$($contract.rules.versionPattern))\z") { throw "VERSION '$version' isn't a version number" }

# Embed the page script as a C++ raw string; only rewrite headers that changed, so unchanged
# builds stay incremental.
$script = [IO.File]::ReadAllText((Join-Path $root "src\panel.js")) -replace "`r`n", "`n"
if ($script.Contains(')FKPJS"')) { throw "src\panel.js must not contain the raw string delimiter )FKPJS`"" }
Write-IfChanged (Join-Path $genDir "panel_js.hpp") ("#pragma once`n`nnamespace fkp {`n`ninline const char* const kPanelScript = R`"FKPJS(" + $script + ")FKPJS`";`n`n}`n")
Write-IfChanged (Join-Path $genDir "panel_version.hpp") ("#pragma once`n`nnamespace fkp {`n`ninline const char* const kPanelVersion = `"$version`";`n`n}`n")

$commonDir = Join-Path $kit "common\src"
$includes = @("-I$(Join-Path $jsoncpp 'include')", "-I$genDir", "-I$commonDir")
$common = @("-target", $target, "-std=c++17", "-O2", "-fno-sanitize=all")

function Compile([string]$source, [string[]]$flags) {
    $name = [IO.Path]::GetFileNameWithoutExtension($source)
    if ($source.StartsWith($commonDir)) { $name = "framekit-$name" }
    $object = Join-Path $objDir "$name.o"
    $newestInput = (Get-Item $source).LastWriteTimeUtc
    $headers = @(Get-ChildItem (Join-Path $root "src") -Filter *.hpp) + @(Get-ChildItem $genDir -Filter *.hpp) +
               @(Get-ChildItem (Join-Path $commonDir "framekit") -Filter *.hpp) +
               @(Get-ChildItem (Join-Path $jsoncpp "include") -Recurse -Filter *.h)
    $newestHeader = $headers | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newestHeader -and $newestHeader.LastWriteTimeUtc -gt $newestInput) { $newestInput = $newestHeader.LastWriteTimeUtc }
    if ((Test-Path $object) -and (Get-Item $object).LastWriteTimeUtc -ge $newestInput) { return $object }
    Write-Host "  compile $([IO.Path]::GetFileName($source))"
    & $zig c++ @common @flags @includes -c $source -o $object
    if ($LASTEXITCODE -ne 0) { throw "compile failed: $source" }
    return $object
}

Write-Host "Building $panelBinary $version for $target with $zig"
$jsoncppSources = (Get-ChildItem (Join-Path $jsoncpp "src\*.cpp")).FullName
$objects = @($jsoncppSources | ForEach-Object { Compile $_ @("-w") })
foreach ($s in @((Get-ChildItem (Join-Path $commonDir "framekit\*.cpp")).FullName) + @((Get-ChildItem (Join-Path $root "src\*.cpp")).FullName)) {
    $objects += Compile $s @("-Wall", "-Wextra")
}

$binary = Join-Path $newDir $panelBinary
Write-Host "  link $panelBinary"
Invoke-ZigQuiet "link $panelBinary" (@("c++", "-target", $target, "-O2", "-s") + $objects + @("-pthread", "-o", $binary))

$unit = [IO.File]::ReadAllText((Join-Path $root "package\$($contract.device.panelUnit)")) -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $newDir $contract.device.panelUnit), $unit, $utf8)
[IO.File]::WriteAllText((Join-Path $newDir $contract.package.panelVersionFile), "$version`n", $utf8)

python -I (Join-Path $root "tools\check_elf.py") $binary
if ($LASTEXITCODE -ne 0) { throw "ELF check failed: $binary" }

function Test-SameFile([string]$a, [string]$b) {
    (Test-Path $a) -and (Test-Path $b) -and (Get-FileHash $a -Algorithm SHA256).Hash -eq (Get-FileHash $b -Algorithm SHA256).Hash
}
$builtFiles = @($panelBinary, $contract.device.panelUnit, $contract.package.panelVersionFile)
$unchanged = @($builtFiles | Where-Object { -not (Test-SameFile (Join-Path $newDir $_) (Join-Path $outDir $_)) }).Count -eq 0
if ($unchanged) {
    Remove-Item $newDir -Recurse -Force
    $signed = Test-Path (Join-Path $outDir $contract.package.panelSignature)
    Write-Host ("Built $outDir unchanged" + $(if ($signed) { ", so its signature stays valid" } else { " (not signed yet: framekit panel-sign, before apps can bundle it)" }))
} else {
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    Move-Item $newDir $outDir
    Write-Host "Built $outDir (not signed yet: framekit panel-sign, before apps can bundle it)"
}

if ($Test) {
    Write-Host "`nBuilding offline tests for this PC (the first time also Zig's C++ library: a minute or two)"
    $testDir = Join-Path $build "tests"
    New-Item -ItemType Directory -Force $testDir | Out-Null
    $testExe = Join-Path $testDir "panel_tests.exe"
    # Everything except the Linux-only glue (paths, process runner, DevTools socket, service).
    $portable = @("modules", "panel_model", "page_bridge", "pairing")
    $portableCommon = @("text", "files", "clock", "json_read", "json_write", "mailbox", "devtools_protocol")
    $testSources = @((Join-Path $root "tests\panel_tests.cpp")) + $jsoncppSources +
                   ($portableCommon | ForEach-Object { Join-Path $commonDir "framekit\$_.cpp" }) +
                   ($portable | ForEach-Object { Join-Path $root "src\$_.cpp" })
    Invoke-ZigQuiet "test build" (@("c++", "-target", "x86_64-windows-gnu", "-std=c++17", "-O1", "-w", "-I$(Join-Path $jsoncpp 'include')", "-I$genDir",
                                    "-I$(Join-Path $root 'src')", "-I$commonDir") + $testSources + @("-o", $testExe))
    & $testExe (Join-Path $kit "contract\cases.json")
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}
