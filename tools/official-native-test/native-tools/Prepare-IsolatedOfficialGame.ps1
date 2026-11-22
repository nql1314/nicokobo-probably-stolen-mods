[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$AppManifestPath,
    [Parameter(Mandatory)][string]$FormalSaveDirectory,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][string]$PythonPath,
    [string]$ExpectedBuild,
    [guid]$RunId = [guid]::NewGuid(),
    [string]$Company = 'Questing Goose Studio',
    [string]$FormalProduct = 'Probably Stolen',
    [string]$GameVersion = '0.46D',
    [string]$ToolsDll = (Join-Path $PSScriptRoot 'bin\Release\net10.0\ContentNativeTools.dll')
)
. (Join-Path $PSScriptRoot 'Common.ps1')
Assert-NoGameProcess
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$artifact = Get-NativeFullPath $ArtifactRoot
$formalSave = Get-NativeFullPath $FormalSaveDirectory
$python = (Get-Command $PythonPath -CommandType Application -ErrorAction Stop).Source
$tool = (Resolve-Path -LiteralPath $ToolsDll).Path
$steam = Read-NativeSteamManifest $AppManifestPath $game
if ($ExpectedBuild -and $ExpectedBuild -cne $steam.buildId) { throw 'Installed Steam build differs from ExpectedBuild.' }
if ($Company -match '[\\/]' -or $FormalProduct -match '[\\/]' -or !$Company -or !$FormalProduct) { throw 'Invalid PlayerSettings identity.' }
$id = $RunId.ToString('N')
$root = Join-Path $artifact ('native-' + $id)
$clone = Join-Path $root 'game'
$product = 'NicoT_' + $id.Substring(0, 9)
$save = Join-Path (Split-Path -Parent $formalSave) $product
$registry = 'Software\' + $Company + '\' + $product
foreach ($path in @($root, $game, $formalSave, $save)) { Assert-NativeUnlinkedPath $path }
foreach ($formal in @($game, $formalSave)) {
    if ($root -ieq $formal -or $root.StartsWith($formal + '\', [StringComparison]::OrdinalIgnoreCase) -or $formal.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Formal data overlaps the test root.' }
}
if ((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $save)) { throw 'The environment and isolated save paths must both be new.' }
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registry)
if ($key) { $key.Dispose(); throw 'The isolated PlayerPrefs identity already exists.' }
$formalRegistry = 'Software\' + $Company + '\' + $FormalProduct
$formalKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($formalRegistry)
$formalRegistryExisted = $null -ne $formalKey
if ($formalKey) { $formalKey.Dispose() }
$sourceAssembly = Join-Path $game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
$sourceSettings = Join-Path $game 'Probably Stolen_Data\globalgamemanagers'
$environment = [ordered]@{
    schemaVersion = 2; tool = 'official-native-test'; runId = $RunId.ToString('D'); createdAtUtc = [DateTime]::UtcNow.ToString('o')
    artifactRoot = $artifact; root = $root; game = $clone; save = $save; product = $product; company = $Company
    playerPrefsRegistry = $registry; formalGame = $game; formalSave = $formalSave; formalProduct = $FormalProduct
    formalPlayerPrefsRegistry = $formalRegistry; formalPlayerPrefsExisted = $formalRegistryExisted; gameVersion = $GameVersion
    appId = $steam.appId; buildId = $steam.buildId; appManifestPath = $steam.path
    originalAssemblySha256 = (Get-FileHash -LiteralPath $sourceAssembly -Algorithm SHA256).Hash
    originalPlayerSettingsSha256 = (Get-FileHash -LiteralPath $sourceSettings -Algorithm SHA256).Hash
    formalWritesAllowed = $false; automaticRestoreAllowed = $false
}
$null = New-Item -ItemType Directory -Path $root
Write-NativeJson (Join-Path $root 'ownership.json') ([ordered]@{ tool = $environment.tool; runId = $environment.runId; root = $root; product = $product })
$environmentPath = Join-Path $root 'environment.json'
Write-NativeJson $environmentPath $environment
$phase = 'protect-formal-data'
try {
    $baseline = @()
    $trees = @(
        [pscustomobject]@{ name = 'Mods'; path = (Join-Path $game 'Mods'); existed = (Test-Path -LiteralPath (Join-Path $game 'Mods')) },
        [pscustomobject]@{ name = 'saves'; path = $formalSave; existed = (Test-Path -LiteralPath $formalSave) }
    )
    foreach ($tree in $trees) {
        foreach ($file in @(Get-NativeTreeFiles $tree.path)) {
            $source = Join-Path $tree.path $file.relative
            $backup = Join-Path $root ('formal-protected-bytes\' + $tree.name + '\' + $file.relative)
            Assert-NativeChildPath $backup $root
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force
            Copy-Item -LiteralPath $source -Destination $backup
            if ((Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -cne $file.sha256) { throw "Protected byte copy differs: $source" }
            $baseline += [pscustomobject]@{ path = $source; tree = $tree.name; relative = $file.relative; sha256 = $file.sha256; length = $file.length }
        }
    }
    foreach ($source in @($sourceAssembly, $sourceSettings, (Join-Path $game 'Probably Stolen.exe'))) {
        $baseline += [pscustomobject]@{ path = $source; tree = 'runtime'; relative = [IO.Path]::GetRelativePath($game, $source); sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; length = (Get-Item -LiteralPath $source).Length }
    }
    Write-NativeJson (Join-Path $root 'formal-before.json') @($baseline)
    Write-NativeJson (Join-Path $root 'formal-trees.json') @($trees)
    if ($formalRegistryExisted) {
        & reg.exe export ('HKCU\' + $formalRegistry) (Join-Path $root 'formal-playerprefs-before.reg') | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not record formal PlayerPrefs.' }
    }
    $phase = 'clone-runtime'
    $null = New-Item -ItemType Directory -Path $clone
    foreach ($name in @('Probably Stolen.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'MonoBleedingEdge', 'Probably Stolen_Data')) {
        $source = Join-Path $game $name
        Assert-NativeUnlinkedPath $source -Recurse
        Copy-Item -LiteralPath $source -Destination (Join-Path $clone $name) -Recurse
    }
    $null = New-Item -ItemType Directory -Path (Join-Path $clone 'Mods')
    [IO.File]::WriteAllText((Join-Path $clone 'steam_appid.txt'), $steam.appId, [Text.Encoding]::ASCII)
    $phase = 'isolate-player-settings'
    & $python (Join-Path $PSScriptRoot 'patch_product.py') (Join-Path $clone 'Probably Stolen_Data\globalgamemanagers') $product $root $Company $FormalProduct $GameVersion 2>&1 |
        Tee-Object -FilePath (Join-Path $root 'product-isolation.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'PlayerSettings isolation failed.' }
    $phase = 'isolate-steam-startup'
    $cloneAssembly = Join-Path $clone 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'
    $originalCopy = Join-Path $root 'original-Assembly-CSharp.dll'
    Assert-NativeChildPath $cloneAssembly $root
    Assert-NativeChildPath $originalCopy $root
    Move-Item -LiteralPath $cloneAssembly -Destination $originalCopy
    & dotnet $tool isolate-steam $sourceAssembly $cloneAssembly $save $product $Company $root $formalSave 2>&1 |
        Tee-Object -FilePath (Join-Path $root 'steam-isolation.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Steam startup isolation failed.' }
    $null = New-Item -ItemType Directory -Path $save
    $phase = 'verify-prepared-environment'
    & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $environmentPath -Phase prepared
    & (Join-Path $PSScriptRoot 'Audit-RuntimeCopy.ps1') -EnvironmentPath $environmentPath
    Write-NativeJson (Join-Path $root 'prepared.json') ([ordered]@{ environmentRunId = $environment.runId; completedAtUtc = [DateTime]::UtcNow.ToString('o'); passed = $true })
    Write-Output $environmentPath
} catch {
    Write-NativeJson (Join-Path $root 'prepare-failure.json') ([ordered]@{ phase = $phase; error = $_.Exception.Message; details = $_.ToString(); atUtc = [DateTime]::UtcNow.ToString('o') })
    throw
}
