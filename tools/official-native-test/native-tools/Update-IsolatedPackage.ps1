[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentPath,
    [Parameter(Mandatory)][string]$PackagePath,
    [ValidateSet('Probe','Business')][string]$Role = 'Probe',
    [switch]$DeveloperMode
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$isBusiness = $Role -eq 'Business'
$environment = Read-NativeEnvironment $EnvironmentPath
Assert-NoGameProcess
Assert-NativeRuntime $environment
$installed = Assert-NativeCandidates $environment
& (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase ('before-' + $Role.ToLowerInvariant() + '-update')
$replacement = Read-NativePackage $PackagePath
if ($isBusiness) {
    $registered = @($installed.packages | Where-Object id -CEQ $replacement.id)
    if ($registered.Count -ne 1 -or $replacement.id -cin @($installed.probes.id)) { throw 'Only one already registered business package may be replaced in Business mode.' }
} else {
    $registered = @($installed.probes | Where-Object id -CEQ $replacement.id)
    if ($registered.Count -ne 1 -or $replacement.id -cin @($installed.packages.id)) { throw 'Only one already registered probe may be replaced; business packages cannot be updated here.' }
}
$previous = $registered[0]
$settingsHash = $null
if ($DeveloperMode) {
    # This only observes the setting the user already chose in the native UI.
    $settingsPath = Join-Path $environment.save 'SaveFile.es3'
    Assert-NativeChildPath $settingsPath $environment.save
    Assert-NativeUnlinkedPath $settingsPath
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $trust = $settings.modTrust.value | ConvertFrom-Json
    $matching = @($trust.records | Where-Object id -CEQ $replacement.id)
    if ($matching.Count -ne 1 -or $matching[0].developerMode -ne $true) { throw 'Developer-mode update requires this package to have Developer mode already enabled by the user.' }
    if ($isBusiness -and $matching[0].source -cne 'Local') { throw 'Business Developer-mode updates require an existing Local trust record.' }
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
}
if ($isBusiness -and !$DeveloperMode) {
    $settingsPath = Join-Path $environment.save 'SaveFile.es3'
    Assert-NativeChildPath $settingsPath $environment.save
    Assert-NativeUnlinkedPath $settingsPath
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $trust = $settings.modTrust.value | ConvertFrom-Json
    $matching = @($trust.records | Where-Object id -CEQ $replacement.id)
    if ($matching.Count -ne 1 -or $matching[0].developerMode -ne $false -or $matching[0].source -cne 'Local') { throw 'Ordinary business updates require one existing Local record with Developer mode off; use the normal UI for review.' }
    $settingsHash = (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash
}
$mods = Join-Path $environment.game 'Mods'
$target = Get-NativeFullPath $previous.installedDirectory
$folder = if ($isBusiness) { [string]$previous.installedFolder } else { 'probe-' + $replacement.id }
if (!$folder -or $folder -cne (Split-Path -Leaf $folder) -or $folder -in @('.', '..') -or
    $previous.installedFolder -cne $folder -or $target -ine (Join-Path $mods $folder)) { throw 'Package installation path does not match its registered identity.' }
Assert-NativeChildPath $target $mods
Assert-NativeUnlinkedPath $target -Recurse
if ((Read-NativePackage $target).id -cne $replacement.id) { throw 'Registered target contains a different package identity.' }
if ($replacement.source -ieq $target -or $replacement.source.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Replacement source must be separate from the installed package.' }
$replacement | Add-Member -NotePropertyName installedFolder -NotePropertyValue $folder
$replacement | Add-Member -NotePropertyName installedDirectory -NotePropertyValue $target
$nextPackages = @($installed.packages | ForEach-Object { if ($isBusiness -and $_.id -ceq $replacement.id) { $replacement } else { $_ } })
$nextProbes = @($installed.probes | ForEach-Object { if (!$isBusiness -and $_.id -ceq $replacement.id) { $replacement } else { $_ } })
Assert-NativePackageSet @($nextPackages + $nextProbes)
$inventoryPath = Join-Path $environment.root 'installed-file-hashes.json'
$packagesPath = Join-Path $environment.root 'installed-packages.json'
$beforeHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash
$before = @(Get-Content -LiteralPath $inventoryPath -Raw -Encoding UTF8 | ConvertFrom-Json)
$prefix = $folder + [IO.Path]::DirectorySeparatorChar
$oldPackageFiles = @($before | Where-Object { $_.relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
$sourceFiles = @(Get-NativeTreeFiles $replacement.source)
$knownSource = @{}; foreach ($file in $sourceFiles) { $knownSource[$file.relative] = $file.sha256 }
$changed = $oldPackageFiles.Count -ne $sourceFiles.Count
foreach ($file in $oldPackageFiles) {
    $relative = $file.relative.Substring($prefix.Length)
    if (!$knownSource.ContainsKey($relative) -or $knownSource[$relative] -cne $file.sha256) { $changed = $true }
}
if (!$changed) { throw "$Role package bytes are unchanged; no update is needed." }
$updateId = [guid]::NewGuid().ToString('D')
$historyDirectory = if ($isBusiness) { 'package-updates' } else { 'probe-updates' }
$update = Join-Path $environment.root ($historyDirectory + '\' + $updateId)
Assert-NativeChildPath $update $environment.root
Assert-NativeUnlinkedPath $update
$null = New-Item -ItemType Directory -Path $update
$oldPackage = Join-Path $update 'previous-package'
Write-NativeJson (Join-Path $update 'request.json') ([ordered]@{
    environmentRunId = $environment.runId; updateId = $updateId; probeId = if ($isBusiness) { $null } else { $replacement.id }; requestedAtUtc = [DateTime]::UtcNow.ToString('o')
    role = $Role; packageId = $replacement.id; previous = $previous; replacement = $replacement; beforeInventorySha256 = $beforeHash
    requiresNormalUiReview = !$DeveloperMode; userDeveloperModeVerified = [bool]$DeveloperMode; developerModeSettingsSha256 = $settingsHash
})
$phase = 'archive-current-candidate-records'
try {
    # Removing current records blocks subsequent launches if any update step fails.
    # Their original bytes and the entire previous probe remain as evidence.
    foreach ($path in @($inventoryPath, $packagesPath)) {
        Assert-NativeChildPath $path $environment.root
        Assert-NativeUnlinkedPath $path
        $archived = Join-Path $update (Split-Path -Leaf $path)
        Assert-NativeChildPath $archived $update
        Move-Item -LiteralPath $path -Destination $archived
    }
    $phase = 'archive-old-package'
    $resolvedTarget = (Resolve-Path -LiteralPath $target).Path
    if ($resolvedTarget -ine $target) { throw 'Resolved package target differs from the registered installation.' }
    Assert-NativeChildPath $resolvedTarget $mods
    Assert-NativeChildPath $oldPackage $update
    Assert-NativeUnlinkedPath $resolvedTarget -Recurse
    Move-Item -LiteralPath $resolvedTarget -Destination $oldPackage
    $phase = 'copy-new-package'
    Copy-Item -LiteralPath $replacement.source -Destination $target -Recurse
    $copied = @(Get-NativeTreeFiles $target)
    if ($copied.Count -ne $sourceFiles.Count) { throw 'Updated package file inventory differs from its source.' }
    foreach ($file in $copied) {
        if (!$knownSource.ContainsKey($file.relative) -or $knownSource[$file.relative] -cne $file.sha256) { throw "Updated package bytes differ: $($file.relative)" }
    }
    $archivedFiles = @(Get-NativeTreeFiles $oldPackage)
    if ($archivedFiles.Count -ne $oldPackageFiles.Count) { throw 'Previous package evidence inventory differs.' }
    $knownOld = @{}; foreach ($file in $oldPackageFiles) { $knownOld[$file.relative.Substring($prefix.Length)] = $file.sha256 }
    foreach ($file in $archivedFiles) {
        if (!$knownOld.ContainsKey($file.relative) -or $knownOld[$file.relative] -cne $file.sha256) { throw 'Previous package evidence bytes differ.' }
    }
    $phase = 'verify-unaffected-candidates'
    $after = @(Get-NativeTreeFiles $mods)
    $unaffected = @($before | Where-Object { !$_.relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    $currentUnaffected = @($after | Where-Object { !$_.relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    if ($currentUnaffected.Count -ne $unaffected.Count) { throw 'Unrelated candidate inventory changed.' }
    $known = @{}; foreach ($file in $unaffected) { $known[$file.relative] = $file.sha256 }
    foreach ($file in $currentUnaffected) {
        if (!$known.ContainsKey($file.relative) -or $known[$file.relative] -cne $file.sha256) { throw "Unrelated candidate changed: $($file.relative)" }
    }
    & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase ('after-' + $Role.ToLowerInvariant() + '-update')
    if ($settingsHash -and (Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash -cne $settingsHash) { throw 'Isolated settings changed during the update; no trust or activation changes are allowed.' }
    $phase = 'publish-new-candidate-records'
    $installed.packages = $nextPackages
    $installed.probes = $nextProbes
    $installed.scanStatus = if ($DeveloperMode) { "$Role package updated; user-enabled Developer mode verified; runtime developer-trust checks required" } else { "$Role package updated; pending normal clone ModLoader scanner and UI review" }
    if ($isBusiness) {
        $installed.source = 'original installed candidates with recorded isolated business updates'
        $installed | Add-Member -NotePropertyName lastBusinessUpdateId -NotePropertyValue $updateId -Force
    } else {
        $installed | Add-Member -NotePropertyName lastProbeUpdateId -NotePropertyValue $updateId -Force
    }
    Write-NativeJson $inventoryPath $after
    Write-NativeJson $packagesPath $installed
    $null = Assert-NativeCandidates $environment
    $afterHash = (Get-FileHash -LiteralPath $inventoryPath -Algorithm SHA256).Hash
    if ($afterHash -ceq $beforeHash) { throw 'Updated candidate inventory must invalidate preceding save baselines.' }
    Write-NativeJson (Join-Path $update 'result.json') ([ordered]@{
        environmentRunId = $environment.runId; updateId = $updateId; probeId = if ($isBusiness) { $null } else { $replacement.id }; completedAtUtc = [DateTime]::UtcNow.ToString('o')
        beforeInventorySha256 = $beforeHash; afterInventorySha256 = $afterHash; previousPackage = $oldPackage
        role = $Role; packageId = $replacement.id; businessPackagesChanged = $isBusiness; otherPackagesChanged = $false
        trustOrActivationWritten = $false; requiresNormalUiReview = !$DeveloperMode; userDeveloperModeVerified = [bool]$DeveloperMode
        developerModeSettingsSha256 = $settingsHash; passed = $true
    })
    if ($DeveloperMode) { Write-Output "Updated registered $Role package $($replacement.id) with the user's existing Developer mode unchanged. Invoke with -DeveloperMode to verify actual runtime trust. Previous evidence: $update" }
    else { Write-Output "Updated registered $Role package $($replacement.id). Reopen the isolated game's normal Mod UI for scanner/security review before testing. Previous evidence: $update" }
} catch {
    Write-NativeJson (Join-Path $update 'failure.json') ([ordered]@{ environmentRunId = $environment.runId; updateId = $updateId; phase = $phase; atUtc = [DateTime]::UtcNow.ToString('o'); error = $_.Exception.Message; passed = $false })
    throw
}
