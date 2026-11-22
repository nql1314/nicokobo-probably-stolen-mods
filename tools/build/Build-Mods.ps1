[CmdletBinding()]
param(
    [string]$GameDir = $env:PROBABLY_STOLEN_GAME_DIR,
    [switch]$NoInstall,
    [string[]]$Mod,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$sourceRoot = Join-Path $repoRoot 'mods'
$distRoot = Join-Path $repoRoot 'dist'
$excludedDocumentNames = @('README', 'README.md', 'README.txt', 'change.log')
if (-not $GameDir) { $GameDir = 'F:\SteamLibrary\steamapps\common\Probably Stolen' }
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managed = Join-Path $GameDir 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $managed -PathType Leaf)) {
    throw 'Select the full-release Mono game directory containing Probably Stolen_Data\Managed\Assembly-CSharp.dll.'
}
if (-not $NoInstall -and (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)) {
    throw 'Close Probably Stolen before installing. Use -NoInstall to package without installing.'
}

function Get-PackageManifest([string]$Path) {
    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $manifest = $document.Mod
    if ($null -eq $manifest -or [string]$manifest.ID -notmatch '^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}$' -or
        [string]::IsNullOrWhiteSpace([string]$manifest.Name) -or $null -eq $manifest.Capabilities) {
        throw "Invalid native Mod manifest: $Path"
    }
    $assemblies = @($manifest.Assemblies.Assembly | ForEach-Object { [string]$_ })
    if ($assemblies.Count -eq 0) { throw "Manifest has no assemblies: $Path" }
    foreach ($assembly in $assemblies) {
        if ([string]::IsNullOrWhiteSpace($assembly) -or [IO.Path]::IsPathRooted($assembly) -or
            $assembly -match '(^|[\\/])\.\.([\\/]|$)|:' -or [IO.Path]::GetExtension($assembly) -ne '.dll') {
            throw "Invalid package assembly path: $assembly"
        }
    }
    return [pscustomobject]@{ Xml = $manifest; Id = [string]$manifest.ID; Assemblies = $assemblies }
}

function Get-ValidatedAssemblies([string]$Compiled, $Manifest, [string]$EntryAssemblyName, [Version]$EntryVersion) {
    $entryDeclarations = @($Manifest.Assemblies | Where-Object {
        [IO.Path]::GetFileNameWithoutExtension($_) -ceq $EntryAssemblyName
    })
    if ($entryDeclarations.Count -ne 1) {
        throw "Manifest must declare the project entry assembly exactly once: $EntryAssemblyName.dll"
    }
    foreach ($assembly in $Manifest.Assemblies) {
        $binary = Join-Path $Compiled $assembly
        if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) { throw "Declared assembly was not built: $binary" }
        $identity = [Reflection.AssemblyName]::GetAssemblyName($binary)
        if ($identity.Name -cne [IO.Path]::GetFileNameWithoutExtension($assembly)) {
            throw "DLL filename must match its assembly name: $assembly"
        }
        # A shared dependency keeps its own assembly version across packages.
        # The package version belongs to the project's entry assembly.
        if ($identity.Name -ceq $EntryAssemblyName -and $identity.Version -ne $EntryVersion) {
            throw "Entry DLL version does not match the manifest: $assembly"
        }
        [pscustomobject]@{ RelativePath = $assembly; SourcePath = $binary; Identity = $identity }
    }
}

function Copy-Package([string]$Source, [string]$Destination, $Manifest) {
    $previous = @()
    $oldManifestPath = Join-Path $Destination 'manifest.xml'
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-Item -LiteralPath $Destination).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a linked package directory: $Destination"
        }
        if (-not (Test-Path -LiteralPath $oldManifestPath -PathType Leaf)) {
            throw "Existing package directory has no manifest; refusing to overwrite it: $Destination"
        }
        $old = Get-PackageManifest $oldManifestPath
        if ($old.Id -cne $Manifest.Id) { throw "A different Mod owns $Destination" }
        $previous = $old.Assemblies
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
        if ($file.Name -in $excludedDocumentNames) { continue }
        $relative = [IO.Path]::GetRelativePath($Source, $file.FullName)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        if (-not [Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
            [IO.File]::ReadAllBytes($file.FullName), [IO.File]::ReadAllBytes($target))) {
            throw "Package file verification failed: $target"
        }
    }
    # Remove obsolete declared DLLs and packaged resources while retaining
    # unrelated files, including user configuration.
    foreach ($assembly in $previous | Where-Object { $_ -notin $Manifest.Assemblies }) {
        $target = [IO.Path]::GetFullPath((Join-Path $Destination $assembly))
        $boundary = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (-not $target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe DLL cleanup path: $target" }
        if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
    foreach ($file in Get-ChildItem -LiteralPath $Destination -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($Destination, $file.FullName)
        $isPackagedResource = $relative -ceq 'preview.png' -or
            $relative -match '^(Textures|Sounds|Localization|LootTables|Books)[\\/]'
        if ($file.Name -in $excludedDocumentNames -or
            ($isPackagedResource -and -not (Test-Path -LiteralPath (Join-Path $Source $relative) -PathType Leaf))) {
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }
}

$projects = @(Get-ChildItem -LiteralPath $sourceRoot -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.xml') -PathType Leaf
})
if ($Mod) {
    $projects = @($projects | Where-Object { $_.Name -cin $Mod })
    foreach ($selection in $Mod) {
        if ($selection -cnotin $projects.Name) { throw "Unknown official Mod: $selection" }
    }
}
else {
    # Forge is distributed separately; ordinary content builds never select it
    # implicitly. Build the prerequisite explicitly with -Mod nicokobo-forge.
    $projects = @($projects | Where-Object { $_.Name -cne 'nicokobo-forge' })
}
if ($projects.Count -eq 0) { throw 'No native Mod projects were found in mods.' }
$modsRoot = Join-Path $GameDir 'Mods'
$runId = [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$stage = Join-Path $repoRoot "_build\official-mods\$runId"
$packages = @()

foreach ($directory in $projects) {
    $projectFiles = @(Get-ChildItem -LiteralPath $directory.FullName -Filter '*.csproj' -File)
    if ($projectFiles.Count -ne 1) { throw "Expected one project in $($directory.FullName)" }
    $manifestPath = Join-Path $directory.FullName 'manifest.xml'
    $manifest = Get-PackageManifest $manifestPath
    if ($manifest.Id -cin $packages.Manifest.Id) { throw "Duplicate Mod ID: $($manifest.Id)" }
    [xml]$projectXml = Get-Content -LiteralPath $projectFiles[0].FullName -Raw
    $version = [string](@($projectXml.Project.PropertyGroup.Version | Where-Object { $_ })[0])
    if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or $version -cne [string]$manifest.Xml.ModVersion) {
        throw "Project and manifest versions must match: $($directory.Name)"
    }
    $entryAssemblyName = [string](@($projectXml.Project.PropertyGroup.AssemblyName | Where-Object { $_ })[0])
    if ([string]::IsNullOrWhiteSpace($entryAssemblyName)) { $entryAssemblyName = $projectFiles[0].BaseName }

    if (-not $NoInstall -and (Test-Path -LiteralPath $modsRoot)) {
        foreach ($installed in Get-ChildItem -LiteralPath $modsRoot -Directory) {
            $installedManifest = Join-Path $installed.FullName 'manifest.xml'
            if (-not (Test-Path -LiteralPath $installedManifest -PathType Leaf)) { continue }
            [xml]$existing = Get-Content -LiteralPath $installedManifest -Raw
            if ([string]$existing.Mod.ID -ceq $manifest.Id -and $installed.Name -cne $directory.Name) {
                throw "The same Mod is already installed in another folder: $($installed.FullName)"
            }
        }
    }

    $compiled = Join-Path $stage "compiled\$($directory.Name)"
    $localized = Join-Path $stage "localized\$($directory.Name)"
    & dotnet build $projectFiles[0].FullName -c Release "-p:GameDir=$GameDir" "-p:ModLocalizationOutput=$localized" -p:NuGetAudit=false -o $compiled --nologo -v minimal | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($directory.Name)" }
    $expectedVersion = if ($version.Split('.').Count -eq 3) { [Version]"$version.0" } else { [Version]$version }
    $assemblies = @(Get-ValidatedAssemblies $compiled $manifest $entryAssemblyName $expectedVersion)
    $packageRoot = Join-Path $stage "packages\$($directory.Name)"
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    foreach ($assembly in $assemblies) {
        $target = Join-Path $packageRoot $assembly.RelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $assembly.SourcePath -Destination $target
    }
    Copy-Item -LiteralPath $manifestPath -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $localized 'Localization') -Destination $packageRoot -Recurse
    foreach ($name in @('preview.png','Textures','Sounds','LootTables','Books')) {
        $content = Join-Path $directory.FullName $name
        if (Test-Path -LiteralPath $content) { Copy-Item -LiteralPath $content -Destination $packageRoot -Recurse }
    }
    # Resource directories can also contain contributor readmes. Exclude them
    # from the fresh snapshot used by both ZIP and Steam publishing.
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -Recurse -File) {
        if ($file.Name -in $excludedDocumentNames) { Remove-Item -LiteralPath $file.FullName -Force }
    }
    $packages += [pscustomobject]@{ Folder = $directory.Name; Version = $version; Root = $packageRoot; Manifest = $manifest }
}
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
foreach ($package in ($packages | Sort-Object @{ Expression = { if ($_.Manifest.Id -ceq 'nicokobo.forge') { 0 } else { 1 } } }, Folder)) {
    $destination = Join-Path $distRoot $package.Folder
    Copy-Package $package.Root $destination $package.Manifest
    $zip = Join-Path $distRoot "$($package.Folder)-$($package.Version).zip"
    Compress-Archive -LiteralPath $package.Root -DestinationPath $zip -Force
    Write-Host "Packaged: $destination"
    Write-Host "Archive: $zip"
    if (-not $NoInstall) {
        if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) { throw 'Game started during packaging; close it before installing.' }
        $install = Join-Path $modsRoot $package.Folder
        Copy-Package $package.Root $install $package.Manifest
        Write-Host "Installed and verified: $install"
    }
}
if ($PassThru) { $packages }
