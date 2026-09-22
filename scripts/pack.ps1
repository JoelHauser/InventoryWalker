<#
.SYNOPSIS
    Builds Inventory Walker, runs the tests, checks the output's references, and zips it.

.DESCRIPTION
    Run this through PowerShell, not Bash. Bash mangles 'C:\HUH' into 'C:HUH', the same trap the
    sibling repos record.

.PARAMETER SPTPath
    The SPT install root. There is no sensible default that works on more than one machine, so it
    is passed explicitly: C:\HUH on the development box, H:\SPT4.1.X on the live one.

.PARAMETER Install
    Also copy the built plugin into the install's BepInEx\plugins.

.EXAMPLE
    scripts\pack.ps1 -SPTPath C:\HUH
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
#>
param(
    [Parameter(Mandatory = $true)][string]$SPTPath,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$plugin = Join-Path $root 'src\InventoryWalker\InventoryWalker.csproj'
$tests = Join-Path $root 'tests\InventoryWalker.Tests\InventoryWalker.Tests.csproj'

if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\core\BepInEx.dll'))) {
    throw "SPTPath '$SPTPath' is not an SPT install root: no BepInEx\core\BepInEx.dll."
}

# ---------------------------------------------------------------- version agreement
# The version lives in two places and they have to agree, or an install silently ships a DLL
# whose logged version is a lie.
$csprojVersion = ([xml](Get-Content $plugin)).Project.PropertyGroup.Version | Where-Object { $_ }
$sourceVersion = (Select-String -Path (Join-Path $root 'src\InventoryWalker\InventoryWalkerPlugin.cs') `
        -Pattern 'PluginVersion\s*=\s*"([^"]+)"').Matches[0].Groups[1].Value

if ($csprojVersion -ne $sourceVersion) {
    throw "Version mismatch: csproj says '$csprojVersion', InventoryWalkerPlugin.PluginVersion says '$sourceVersion'."
}
Write-Host "Version $csprojVersion" -ForegroundColor Cyan

# ---------------------------------------------------------------- build and test
Write-Host 'Building...' -ForegroundColor Cyan
dotnet build $plugin -c Release "-p:SPTPath=$SPTPath" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host 'Testing...' -ForegroundColor Cyan
dotnet test $tests --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# ---------------------------------------------------------------- reference check
# The Assembly-CSharp.dll in Managed is not the assembly the game runs; the Launcher's delta
# renames obfuscated types at startup. A plugin that references it does not load on a machine
# whose install has been launched. Every game member is resolved by name instead, so the output
# must carry no reference to it.
$built = Join-Path $root 'src\InventoryWalker\bin\Release\InventoryWalker.dll'
$cecil = Join-Path $SPTPath 'SPT_Runtime\Mono.Cecil.dll'

if (Test-Path $cecil) {
    Add-Type -Path $cecil
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($built)
    $refs = $module.AssemblyReferences | ForEach-Object { $_.Name }
    $module.Dispose()

    $forbidden = $refs | Where-Object { $_ -like 'Assembly-CSharp*' -or $_ -like 'spt-*' }
    if ($forbidden) {
        throw "The built plugin references the game assembly: $($forbidden -join ', ')"
    }
    Write-Host "References clean: $($refs -join ', ')" -ForegroundColor Green
}
else {
    Write-Warning "No Mono.Cecil at '$cecil'; skipping the reference check."
}

# ---------------------------------------------------------------- stage and zip
$staging = Join-Path $root "releases\staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
$pluginDir = Join-Path $staging 'BepInEx\plugins'
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
Copy-Item $built $pluginDir
Copy-Item (Join-Path $root 'README.md') $staging

# The zip is meant to be unpacked over the SPT root, so the layout inside it has to be exactly
# the layout there. A plugin in the wrong folder is not found, and it fails silently.
$expected = @('BepInEx\plugins\InventoryWalker.dll', 'README.md')
$actual = Get-ChildItem $staging -Recurse -File |
    ForEach-Object { $_.FullName.Substring($staging.Length + 1) } | Sort-Object
$diff = Compare-Object ($expected | Sort-Object) $actual
if ($diff) {
    throw "Staged layout is wrong:`n$($diff | Format-Table -AutoSize | Out-String)"
}

$zip = Join-Path $root "releases\InventoryWalker_V$csprojVersion.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force
Write-Host "Packed $zip" -ForegroundColor Green

# ---------------------------------------------------------------- install
if ($Install) {
    $dest = Join-Path $SPTPath 'BepInEx\plugins'
    Copy-Item $built $dest -Force
    Write-Host "Installed to $dest" -ForegroundColor Green
}
