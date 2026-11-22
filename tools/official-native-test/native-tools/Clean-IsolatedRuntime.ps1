[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentPath,
    [Parameter(Mandatory)][guid]$ConfirmRunId
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$environment = Read-NativeEnvironment $EnvironmentPath
if ($ConfirmRunId.ToString('D') -cne $environment.runId) { throw 'ConfirmRunId must match this new environment, never a historical snapshot.' }
Assert-NoGameProcess
& (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase before-cleanup
$evidence = Join-Path $environment.root 'cleanup-evidence'
if ((Test-Path -LiteralPath $evidence) -or (Test-Path -LiteralPath (Join-Path $environment.root 'cleanup.json'))) { throw 'Cleanup already started. Inspect preserved evidence before any manual recovery.' }
$null = New-Item -ItemType Directory -Path $evidence
$failure = $null; $prefsExisted = $false; $afterFormalVerified = $false
function Copy-NativeEvidenceTree([string]$Source, [string]$Destination) {
    Assert-NativeChildPath $Destination $evidence
    if (!(Test-Path -LiteralPath $Source)) { return }
    $before = @(Get-NativeTreeFiles $Source)
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse
    $after = @(Get-NativeTreeFiles $Destination)
    if ($before.Count -ne $after.Count) { throw "Evidence inventory differs: $Source" }
    $known = @{}; foreach ($file in $before) { $known[$file.relative] = $file.sha256 }
    foreach ($file in $after) {
        if (!$known.ContainsKey($file.relative) -or $known[$file.relative] -cne $file.sha256) { throw "Evidence bytes differ: $Source/$($file.relative)" }
    }
    Write-NativeJson ($Destination + '-files.json') $before
}
try {
    # Preserve the full isolated save tree (ES3 index, slots and Mod sidecars),
    # actual tested DLLs/resources, and registry before removing runtime copies.
    Copy-NativeEvidenceTree $environment.save (Join-Path $evidence 'isolated-saves')
    Copy-NativeEvidenceTree (Join-Path $environment.game 'Mods') (Join-Path $evidence 'tested-packages')
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($environment.playerPrefsRegistry)
    $prefsExisted = $null -ne $key
    if ($key) { $key.Dispose() }
    if ($prefsExisted) {
        & reg.exe export ('HKCU\' + $environment.playerPrefsRegistry) (Join-Path $evidence 'isolated-playerprefs.reg') | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not preserve isolated PlayerPrefs; cleanup stopped.' }
    }
    $targets = @($environment.save, $environment.game)
    foreach ($target in $targets) {
        if (!(Test-Path -LiteralPath $target)) { continue }
        $resolved = (Resolve-Path -LiteralPath $target).Path
        if ($resolved -ine $target -or $resolved -in @($environment.formalGame, $environment.formalSave)) { throw 'Unsafe resolved cleanup target.' }
        if ($target -ieq $environment.game) { Assert-NativeChildPath $resolved $environment.root }
        elseif ($resolved -ine (Join-Path (Split-Path -Parent $environment.formalSave) $environment.product)) { throw 'Unexpected isolated save cleanup path.' }
        Assert-NativeUnlinkedPath $resolved -Recurse
    }
    Assert-NoGameProcess
    if ($prefsExisted) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($environment.playerPrefsRegistry) }
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
    & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase after-cleanup
    $afterFormalVerified = $true
} catch { $failure = $_.Exception.Message }
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($environment.playerPrefsRegistry)
$prefsRemain = $null -ne $key
if ($key) { $key.Dispose() }
$gameRemoved = !(Test-Path -LiteralPath $environment.game)
$saveRemoved = !(Test-Path -LiteralPath $environment.save)
Write-NativeJson (Join-Path $environment.root 'cleanup.json') ([ordered]@{
    environmentRunId = $environment.runId; completedAtUtc = [DateTime]::UtcNow.ToString('o'); gameCopyRemoved = $gameRemoved
    isolatedSavesRemoved = $saveRemoved; isolatedPlayerPrefsRemoved = !$prefsRemain; evidence = $evidence
    formalRestored = $false; formalVerifiedBeforeCleanup = $true; formalVerifiedAfterCleanup = $afterFormalVerified; failure = $failure
    passed = $null -eq $failure -and $gameRemoved -and $saveRemoved -and !$prefsRemain
})
if ($failure -or !$gameRemoved -or !$saveRemoved -or $prefsRemain) { throw "Isolated cleanup failed; evidence retained: $($environment.root). $failure" }
Write-Output "Removed only this run's cloned runtime, isolated saves and PlayerPrefs; all run logs and copied save/package evidence retained: $($environment.root)"
