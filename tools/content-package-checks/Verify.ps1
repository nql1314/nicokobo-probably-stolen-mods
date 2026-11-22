#requires -Version 7.0
param([string]$GameDir = 'F:\SteamLibrary\steamapps\common\Probably Stolen')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scriptPath = Join-Path $repoRoot 'tools/build/Build-Mods.ps1'
$syntaxTokens = $null
$syntaxErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$syntaxTokens, [ref]$syntaxErrors)
if ($syntaxErrors.Count -ne 0) { throw ($syntaxErrors.Message -join [Environment]::NewLine) }
# Load only the package functions. The build/package/install main body is never run.
foreach ($name in @('Get-PackageManifest', 'Get-ValidatedAssemblies', 'Copy-Package')) {
    $definition = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) |
        Where-Object Name -CEQ $name
    if (@($definition).Count -ne 1) { throw "Expected one validation function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}

$checks = 0
$excludedDocumentNames = @('README', 'README.md', 'README.txt', 'change.log')
function Assert($condition, [string]$message) {
    $script:checks++
    if (-not $condition) { throw $message }
}
function Reject([scriptblock]$action, [string]$message) {
    $failure = $null
    try { & $action | Out-Null } catch { $failure = $_.Exception.Message }
    Assert ($null -ne $failure -and $failure -like $message) "Expected rejection matching '$message'; got '$failure'."
}

# Cecil emits metadata-only fixture DLLs; no fixture assembly or game code is executed.
$cecilPath = Join-Path $GameDir 'Probably Stolen_Data\Managed\DseCecil.dll'
[void][Reflection.Assembly]::LoadFrom($cecilPath)
$fixtureBase = [IO.Path]::GetFullPath((Join-Path $repoRoot '_build/content-package-checks'))
$fixtureRoot = Join-Path $fixtureBase ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
function Write-FixtureAssembly([string]$path, [string]$name, [string]$version) {
    $identity = [DseCecil.AssemblyNameDefinition]::new($name, [Version]$version)
    $assembly = [DseCecil.AssemblyDefinition]::CreateAssembly($identity, ($name + '.dll'), [DseCecil.ModuleKind]::Dll)
    try { $assembly.Write($path) } finally { $assembly.Dispose() }
}
function Manifest([string[]]$assemblies) { [pscustomobject]@{ Assemblies = $assemblies } }

try {
    $entryPath = Join-Path $fixtureRoot 'Fixture.Entry.dll'
    $sharedPath = Join-Path $fixtureRoot 'Nicokobo.ContentInterop.dll'
    Write-FixtureAssembly $entryPath 'Fixture.Entry' '1.2.0.0'
    Write-FixtureAssembly $sharedPath 'Nicokobo.ContentInterop' '1.0.0.0'
    $manifestPath = Join-Path $fixtureRoot 'manifest.xml'
    Set-Content -LiteralPath $manifestPath '<Mod><ID>fixture.entry</ID><Name>Fixture</Name><ModVersion>1.2.0</ModVersion><Assemblies><Assembly>Fixture.Entry.dll</Assembly><Assembly>Nicokobo.ContentInterop.dll</Assembly></Assemblies><Capabilities /></Mod>'
    $manifest = Get-PackageManifest $manifestPath
    $validated = @(Get-ValidatedAssemblies $fixtureRoot $manifest 'Fixture.Entry' ([Version]'1.2.0.0'))
    Assert ($validated.Count -eq 2) 'The entry and independently versioned dependency were not both accepted.'
    Assert ($validated[0].Identity.Name -ceq 'Fixture.Entry' -and $validated[0].Identity.Version -eq [Version]'1.2.0.0') 'Entry identity changed.'
    Assert ($validated[1].Identity.Name -ceq 'Nicokobo.ContentInterop' -and $validated[1].Identity.Version -eq [Version]'1.0.0.0') 'Shared dependency version was forced to the package version.'
    Assert ($validated[1].SourcePath -ceq $sharedPath) 'Validation did not preserve the actual built dependency path.'

    # Dependencies need not be listed after the entry, or have its version.
    $reordered = Manifest @('Nicokobo.ContentInterop.dll', 'Fixture.Entry.dll')
    Assert (@(Get-ValidatedAssemblies $fixtureRoot $reordered 'Fixture.Entry' ([Version]'1.2.0.0')).Count -eq 2) 'Assembly order changed version ownership.'
    Reject { Get-ValidatedAssemblies $fixtureRoot $manifest 'Fixture.Entry' ([Version]'1.2.1.0') } '*Entry DLL version does not match the manifest*'
    Reject { Get-ValidatedAssemblies $fixtureRoot (Manifest @('Nicokobo.ContentInterop.dll')) 'Fixture.Entry' ([Version]'1.2.0.0') } '*project entry assembly exactly once*'
    Reject { Get-ValidatedAssemblies $fixtureRoot (Manifest @('Fixture.Entry.dll', 'Fixture.Entry.dll')) 'Fixture.Entry' ([Version]'1.2.0.0') } '*project entry assembly exactly once*'
    Reject { Get-ValidatedAssemblies $fixtureRoot (Manifest @('Fixture.Entry.dll', 'Missing.dll')) 'Fixture.Entry' ([Version]'1.2.0.0') } '*Declared assembly was not built*'

    $renamed = Join-Path $fixtureRoot 'Renamed.dll'
    Copy-Item -LiteralPath $sharedPath -Destination $renamed
    Reject { Get-ValidatedAssemblies $fixtureRoot (Manifest @('Fixture.Entry.dll', 'Renamed.dll')) 'Fixture.Entry' ([Version]'1.2.0.0') } '*DLL filename must match its assembly name*'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Invalid.dll') 'not a managed assembly'
    Reject { Get-ValidatedAssemblies $fixtureRoot (Manifest @('Fixture.Entry.dll', 'Invalid.dll')) 'Fixture.Entry' ([Version]'1.2.0.0') } '*'

    # Nested dependency paths remain valid and keep the declared package path.
    $nested = Join-Path $fixtureRoot 'Assemblies'
    New-Item -ItemType Directory -Path $nested | Out-Null
    Copy-Item -LiteralPath $sharedPath -Destination $nested
    $nestedValidated = @(Get-ValidatedAssemblies $fixtureRoot (Manifest @('Fixture.Entry.dll', 'Assemblies/Nicokobo.ContentInterop.dll')) 'Fixture.Entry' ([Version]'1.2.0.0'))
    Assert ($nestedValidated[1].RelativePath -ceq 'Assemblies/Nicokobo.ContentInterop.dll') 'A declared nested dependency path was flattened.'

    # A later package must not retain removed game resources or delete user config.
    $source = Join-Path $fixtureRoot 'package-source'
    $destination = Join-Path $fixtureRoot 'package-destination'
    New-Item -ItemType Directory -Path (Join-Path $source 'Textures/Items'), (Join-Path $destination 'Textures/Items') -Force | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $source 'manifest.xml')
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'manifest.xml')
    Copy-Item -LiteralPath $entryPath -Destination (Join-Path $source 'Fixture.Entry.dll')
    Set-Content -LiteralPath (Join-Path $source 'Textures/Items/current.png') 'current'
    Set-Content -LiteralPath (Join-Path $destination 'Textures/Items/removed.png') 'old'
    Set-Content -LiteralPath (Join-Path $destination 'preview.png') 'old preview'
    Set-Content -LiteralPath (Join-Path $destination 'user-config.json') 'keep'
    Copy-Package $source $destination $manifest
    Assert (Test-Path -LiteralPath (Join-Path $destination 'Textures/Items/current.png')) 'Current packaged resource was not copied.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $destination 'Textures/Items/removed.png'))) 'Removed packaged resource was retained.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $destination 'preview.png'))) 'Removed package preview was retained.'
    Assert ((Get-Content -LiteralPath (Join-Path $destination 'user-config.json') -Raw).Trim() -ceq 'keep') 'User configuration was changed.'

    Assert ($syntaxErrors.Count -eq 0) 'Build script syntax is invalid.'
    Write-Host "PASS: $checks content package checks (assembly validation, resource cleanup and user configuration preservation). No package build or install."
}
finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $boundary = $fixtureBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedFixture.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
