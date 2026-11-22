[CmdletBinding()]
param([Parameter(Mandatory)][string]$EnvironmentPath, [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Phase = 'verify')
. (Join-Path $PSScriptRoot 'Common.ps1')
$environment = Read-NativeEnvironment $EnvironmentPath
$baseline = @(Get-Content -LiteralPath (Join-Path $environment.root 'formal-before.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
$trees = @(Get-Content -LiteralPath (Join-Path $environment.root 'formal-trees.json') -Raw -Encoding UTF8 | ConvertFrom-Json)
$evidence = Join-Path $environment.root ('verify-' + $Phase + '-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $evidence
$changes = [Collections.Generic.List[object]]::new()
function Read-RegistryExportEntries([string]$Path) {
    # Registry values have no ordering. reg.exe may enumerate them differently
    # after restoring a value; preserve each complete typed value's exact bytes.
    $text = [IO.File]::ReadAllText($Path, [Text.Encoding]::Unicode)
    $joined = [regex]::Replace($text, '\\\r?\n[ \t]*', '')
    [string[]]$entries = @($joined -split '\r?\n' | Where-Object { $_ -cne '' })
    [Array]::Sort($entries, [StringComparer]::Ordinal)
    return $entries
}
try {
    $steam = Read-NativeSteamManifest $environment.appManifestPath $environment.formalGame
    if ($steam.buildId -cne $environment.buildId) { $changes.Add([pscustomobject]@{ kind = 'game-build'; path = $steam.path }) }
    foreach ($file in $baseline) {
        if (!(Test-Path -LiteralPath $file.path -PathType Leaf)) { $changes.Add([pscustomobject]@{ kind = 'missing-file'; path = $file.path }); continue }
        if ((Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash -cne $file.sha256) { $changes.Add([pscustomobject]@{ kind = 'changed-file'; path = $file.path }) }
    }
    foreach ($tree in $trees) {
        if ((Test-Path -LiteralPath $tree.path) -ne $tree.existed) { $changes.Add([pscustomobject]@{ kind = 'tree-existence'; path = $tree.path }) }
        $known = @{}; foreach ($file in @($baseline | Where-Object tree -CEQ $tree.name)) { $known[$file.relative] = $true }
        foreach ($file in @(Get-NativeTreeFiles $tree.path)) {
            if (!$known.ContainsKey($file.relative)) { $changes.Add([pscustomobject]@{ kind = 'added-file'; path = (Join-Path $tree.path $file.relative) }) }
        }
    }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($environment.formalPlayerPrefsRegistry)
    $exists = $null -ne $key
    if ($key) { $key.Dispose() }
    if ($exists -ne $environment.formalPlayerPrefsExisted) { $changes.Add([pscustomobject]@{ kind = 'registry-existence'; path = $environment.formalPlayerPrefsRegistry }) }
    elseif ($exists) {
        $after = Join-Path $evidence 'formal-playerprefs-after.reg'
        & reg.exe export ('HKCU\' + $environment.formalPlayerPrefsRegistry) $after | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not read formal PlayerPrefs.' }
        $beforeEntries = @(Read-RegistryExportEntries (Join-Path $environment.root 'formal-playerprefs-before.reg'))
        $afterEntries = @(Read-RegistryExportEntries $after)
        if (Compare-Object -ReferenceObject $beforeEntries -DifferenceObject $afterEntries -CaseSensitive) { $changes.Add([pscustomobject]@{ kind = 'registry-value-bytes'; path = $environment.formalPlayerPrefsRegistry }) }
    }
} catch { $changes.Add([pscustomobject]@{ kind = 'verification-error'; detail = $_.Exception.Message }) }
Write-NativeJson (Join-Path $evidence 'result.json') ([ordered]@{ environmentRunId = $environment.runId; phase = $Phase; atUtc = [DateTime]::UtcNow.ToString('o'); protectedFiles = $baseline.Count; registryComparison = 'Complete typed value bytes; unordered entry enumeration'; changes = @($changes.ToArray()); passed = $changes.Count -eq 0 })
if ($changes.Count) { throw "Formal preservation verification failed. No data was restored. Evidence: $evidence" }
Write-Host "Formal files and PlayerPrefs unchanged: $evidence"
