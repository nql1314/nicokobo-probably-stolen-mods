# Shared boundaries for the maintained official-game runner. No entrypoint runs
# merely by dot-sourcing this file.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Checkpoint.ps1')

function Write-NativeJson([string]$Path, $Value) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 30))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}

function Get-NativeFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or ![IO.Path]::IsPathFullyQualified($Path)) { throw 'An absolute path is required.' }
    [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Assert-NativeChildPath([string]$Path, [string]$Root) {
    $target = Get-NativeFullPath $Path
    $boundary = (Get-NativeFullPath $Root) + [IO.Path]::DirectorySeparatorChar
    if (!$target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "Path escapes its owning root: $target" }
}

function Assert-NativeUnlinkedPath([string]$Path, [switch]$Recurse) {
    $current = Get-NativeFullPath $Path
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked path is not supported: $current" }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    if ($Recurse -and (Test-Path -LiteralPath $Path)) {
        if (Get-ChildItem -LiteralPath $Path -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1) {
            throw "Linked entry in tree: $Path"
        }
    }
}

function Assert-NoGameProcess {
    if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) { throw 'An existing game must exit normally first. No player process was stopped.' }
}

function Get-NativeLaunchMonitor([switch]$AllowPrimaryDisplay) {
    if ($AllowPrimaryDisplay) { return '1' }
    Add-Type -AssemblyName System.Windows.Forms
    if ([System.Windows.Forms.Screen]::AllScreens.Count -gt 1) { return '2' }
    return '1'
}

function Read-NativeSteamManifest([string]$Path, [string]$GameDirectory) {
    $path = (Resolve-Path -LiteralPath $Path).Path
    $raw = [IO.File]::ReadAllText($path)
    $values = @{}
    foreach ($key in @('appid', 'buildid', 'StateFlags', 'installdir')) {
        $match = [regex]::Match($raw, '"' + $key + '"\s+"([^"]+)"')
        if (!$match.Success) { throw "Steam manifest is missing $key" }
        $values[$key] = $match.Groups[1].Value
    }
    if ($values.appid -cne '4348910' -or $values.StateFlags -cne '4') { throw 'Expected a fully installed official game (App 4348910).' }
    $installed = Join-Path (Split-Path -Parent $path) ('common\' + $values.installdir)
    if ((Get-NativeFullPath $installed) -ine (Get-NativeFullPath $GameDirectory)) { throw 'Appmanifest and game directory do not describe the same installation.' }
    [pscustomobject]@{ appId = $values.appid; buildId = $values.buildid; path = $path }
}

function Read-NativeEnvironment([string]$EnvironmentPath) {
    $file = (Resolve-Path -LiteralPath $EnvironmentPath).Path
    $env = Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($env.schemaVersion -ne 2 -or $env.tool -cne 'official-native-test') { throw 'Not a maintained official native-test environment. Historical snapshots are not accepted.' }
    $id = [guid]::Parse($env.runId).ToString('N')
    $root = Get-NativeFullPath $env.root
    if ($root -ine (Join-Path (Get-NativeFullPath $env.artifactRoot) ('native-' + $id)) -or
        $file -ine (Join-Path $root 'environment.json') -or $env.game -ine (Join-Path $root 'game')) { throw 'Environment paths do not belong to this run.' }
    if ($env.product -cne ('NicoT_' + $id.Substring(0, 9)) -or
        $env.save -ine (Join-Path (Split-Path -Parent $env.formalSave) $env.product) -or
        $env.playerPrefsRegistry -cne ('Software\' + $env.company + '\' + $env.product)) { throw 'Unexpected isolated save or registry identity.' }
    foreach ($formal in @($env.formalGame, $env.formalSave)) {
        $formal = Get-NativeFullPath $formal
        if ($root -ieq $formal -or $root.StartsWith($formal + '\', [StringComparison]::OrdinalIgnoreCase) -or
            $formal.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Formal data overlaps the test root.' }
    }
    Assert-NativeUnlinkedPath $root
    Assert-NativeUnlinkedPath $env.save
    $owner = Get-Content -LiteralPath (Join-Path $root 'ownership.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($owner.tool -cne $env.tool -or $owner.runId -cne $env.runId -or $owner.root -ine $root -or $owner.product -cne $env.product) { throw 'Run ownership record does not match the environment.' }
    $env
}

function Get-NativeTreeFiles([string]$Root) {
    if (!(Test-Path -LiteralPath $Root)) { return }
    Assert-NativeUnlinkedPath $Root -Recurse
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Force -Recurse | Sort-Object FullName) {
        [pscustomobject]@{ relative = [IO.Path]::GetRelativePath($Root, $file.FullName); length = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
}

function Read-NativePackage([string]$Directory) {
    $directory = (Resolve-Path -LiteralPath $Directory).Path
    Assert-NativeUnlinkedPath $directory -Recurse
    $manifestPath = Join-Path $directory 'manifest.xml'
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    $id = [string]$manifest.Mod.ID
    if ($id -cnotmatch '^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}$') { throw "Invalid package ID: $id" }
    $assemblies = @($manifest.SelectNodes('/Mod/Assemblies/Assembly') | ForEach-Object {
        $relative = $_.InnerText
        if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)' -or [IO.Path]::GetExtension($relative) -ine '.dll') { throw "Unsafe manifest assembly: $relative" }
        $dll = Get-NativeFullPath (Join-Path $directory $relative)
        Assert-NativeChildPath $dll $directory
        $identity = [Reflection.AssemblyName]::GetAssemblyName($dll)
        if ($identity.Name -cne [IO.Path]::GetFileNameWithoutExtension($dll)) { throw "Assembly filename differs from its identity: $dll" }
        [pscustomobject]@{ relative = $relative; name = $identity.Name; version = $identity.Version.ToString(); sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash }
    })
    if ($assemblies.Count -eq 0 -or @($assemblies.relative | Select-Object -Unique).Count -ne $assemblies.Count) { throw "Invalid assembly list in $manifestPath" }
    [pscustomobject]@{
        id = $id; version = [string]$manifest.Mod.ModVersion; folder = Split-Path -Leaf $directory; source = $directory
        manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        assemblies = $assemblies; prerequisites = @($manifest.SelectNodes('/Mod/Prerequisites/Mod') | ForEach-Object InnerText)
    }
}

function Assert-NativePackageSet($Packages) {
    $ids = @($Packages | ForEach-Object id)
    if ($ids.Count -eq 0 -or @($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'Empty or duplicate installed package identities.' }
    foreach ($package in $Packages) {
        foreach ($dependency in $package.prerequisites) {
            if ($dependency -cnotin $ids) { throw "Missing dependency $dependency for $($package.id)" }
        }
    }
}

function Assert-NativeCandidates($Environment) {
    if (Test-Path -LiteralPath (Join-Path $Environment.root 'staging-failure.json')) { throw 'Staging failed; inspect its preserved evidence and prepare a fresh environment.' }
    $record = Get-Content -LiteralPath (Join-Path $Environment.root 'installed-packages.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$record.passed -or $record.environmentRunId -cne $Environment.runId -or $record.gameBuild -cne $Environment.buildId) { throw 'Candidate record belongs to another environment.' }
    $expected = @(Get-Content -LiteralPath (Join-Path $Environment.root 'installed-file-hashes.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
    $actual = @(Get-NativeTreeFiles (Join-Path $Environment.game 'Mods'))
    if ($actual.Count -ne $expected.Count) { throw 'Installed candidate file inventory changed.' }
    $known = @{}; foreach ($file in $expected) { $known[$file.relative] = $file.sha256 }
    foreach ($file in $actual) {
        if (!$known.ContainsKey($file.relative) -or $known[$file.relative] -cne $file.sha256) { throw "Installed candidate changed: $($file.relative)" }
    }
    $record
}

function Assert-NativeProbeResult($Probe, [string]$RunId, [string]$Mode, [int]$ProcessId, $Environment, [switch]$AllowBreedingCheckpoint) {
    if ($Probe.runId -cne $RunId -or $Probe.mode -cne $Mode -or $Probe.processId -ne $ProcessId) { throw 'Probe result identity does not match this process.' }
    $checkpoint = $AllowBreedingCheckpoint -and $Mode -clike 'breeding-*' -and (Test-NativeBreedingCheckpoint $Probe)
    if (!$checkpoint -and (!$Probe.executed -or !$Probe.passed -or @($Probe.checks).Count -eq 0 -or
        @($Probe.checks | Where-Object { $_.status -ceq 'failed' -or ($_.required -and !$_.passed) }).Count)) { throw 'Probe reported failed or incomplete required checks.' }
    if ((Get-NativeFullPath $Probe.environment.persistentDataPath) -ine (Get-NativeFullPath $Environment.save) -or $Probe.environment.product -cne $Environment.product -or
        $Probe.environment.company -cne $Environment.company -or $Probe.environment.steamInitialized -ne $false) { throw 'Actual runtime isolation evidence differs.' }
    $marker = $Probe.environment.playerPrefsMarker
    if ([string]::IsNullOrWhiteSpace($marker.key) -or $marker.value -cne $RunId -or
        $marker.expectedRegistryIdentity -cne ('HKEY_CURRENT_USER\' + $Environment.playerPrefsRegistry)) { throw 'Missing or mismatched isolated PlayerPrefs marker evidence.' }
    $marker
}

function Assert-NativeRuntime($Environment) {
    $prepared = Get-Content -LiteralPath (Join-Path $Environment.root 'prepared.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (!$prepared.passed -or $prepared.environmentRunId -cne $Environment.runId) { throw 'Environment was not prepared successfully.' }
    $dll = Join-Path $Environment.game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
    $proof = Get-Content -LiteralPath ($dll + '.isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($proof.originalSha256 -cne $Environment.originalAssemblySha256 -or
        $proof.isolatedSha256 -cne (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -or
        $proof.expectedPersistent -ine $Environment.save -or $proof.expectedProduct -cne $Environment.product -or
        $proof.expectedCompany -cne $Environment.company -or $proof.runRoot -ine $Environment.root -or
        @($proof.changedMethods).Count -ne 1 -or $proof.changedMethods[0] -cne 'System.Void SteamManager::Awake()' -or $proof.protectedMethodsUnchanged -le 0) { throw 'Runtime isolation proof changed.' }
    $settings = Join-Path $Environment.game 'Probably Stolen_Data\globalgamemanagers'
    $productProof = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $settings) 'product-isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($productProof.beforeSha256 -ine $Environment.originalPlayerSettingsSha256 -or
        $productProof.afterSha256 -ine (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash -or
        $productProof.changedByteRangeLength -ne 15 -or $productProof.after.productName -cne $Environment.product -or
        $productProof.after.companyName -cne $Environment.company) { throw 'PlayerSettings isolation proof changed.' }
    $appInfo = Join-Path $Environment.game 'Probably Stolen_Data\app.info'
    $appProof = Get-Content -LiteralPath (Join-Path $Environment.root 'app-info-isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($appProof.environmentRunId -cne $Environment.runId -or $appProof.target -ine $appInfo -or
        $appProof.beforeSha256 -ine (Get-FileHash -LiteralPath (Join-Path $Environment.formalGame 'Probably Stolen_Data\app.info') -Algorithm SHA256).Hash -or
        $appProof.afterSha256 -ine (Get-FileHash -LiteralPath $appInfo -Algorithm SHA256).Hash -or
        [string]::Join("`n", [IO.File]::ReadAllLines($appInfo)) -cne ($Environment.company + "`n" + $Environment.product)) {
        throw 'Early bootstrap app.info must use the same isolated company/product before Unity starts.'
    }
    if ([IO.File]::ReadAllText((Join-Path $Environment.game 'steam_appid.txt')).Trim() -cne $Environment.appId) { throw 'Clone steam_appid.txt differs from the current appmanifest.' }
}
