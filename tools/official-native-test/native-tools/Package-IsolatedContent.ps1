[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentPath,
    [Parameter(Mandatory)][string[]]$ProbePackagePath,
    [string]$CandidatePackageDirectory,
    [string[]]$ExpectedModIds
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$environment = Read-NativeEnvironment $EnvironmentPath
Assert-NoGameProcess
Assert-NativeRuntime $environment
& (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase before-staging
$mods = Join-Path $environment.game 'Mods'
Assert-NativeUnlinkedPath $mods -Recurse
if (@(Get-ChildItem -LiteralPath $mods -Force).Count -or (Test-Path -LiteralPath (Join-Path $environment.root 'installed-packages.json'))) { throw 'Stage once into an empty new environment. Do not replace previously tested candidates.' }
$candidateSource = if ($CandidatePackageDirectory) { (Resolve-Path -LiteralPath $CandidatePackageDirectory).Path } else { Join-Path $environment.formalGame 'Mods' }
Assert-NativeUnlinkedPath $candidateSource -Recurse
$packages = @(Get-ChildItem -LiteralPath $candidateSource -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.xml') } | Sort-Object Name | ForEach-Object { Read-NativePackage $_.FullName })
Assert-NativePackageSet $packages
if ($ExpectedModIds -and @(Compare-Object -ReferenceObject @($ExpectedModIds | Sort-Object) -DifferenceObject @($packages.id | Sort-Object) -CaseSensitive).Count) { throw 'Candidate package IDs differ from ExpectedModIds.' }
$probes = @($ProbePackagePath | ForEach-Object { Read-NativePackage $_ })
if ($probes.Count -eq 0 -or @($probes.id | Select-Object -Unique).Count -ne $probes.Count -or @($probes.id | Where-Object { $_ -cin $packages.id }).Count) { throw 'Probe identities must be unique and separate from installed candidates.' }
Assert-NativePackageSet @($packages + $probes)
try {
    foreach ($package in @($packages + $probes)) {
        $folder = if ($package.id -cin $probes.id) { 'probe-' + $package.id } else { $package.folder }
        $destination = Join-Path $mods $folder
        Assert-NativeChildPath $destination $mods
        if (Test-Path -LiteralPath $destination) { throw "Duplicate package folder: $folder" }
        $sourceFiles = @(Get-NativeTreeFiles $package.source)
        Copy-Item -LiteralPath $package.source -Destination $destination -Recurse
        $copiedFiles = @(Get-NativeTreeFiles $destination)
        if ($copiedFiles.Count -ne $sourceFiles.Count) { throw "Copied package file inventory differs: $($package.id)" }
        $known = @{}; foreach ($file in $sourceFiles) { $known[$file.relative] = $file.sha256 }
        foreach ($file in $copiedFiles) {
            if (!$known.ContainsKey($file.relative) -or $known[$file.relative] -cne $file.sha256) { throw "Copied package bytes differ: $($package.id)/$($file.relative)" }
        }
        $copy = Read-NativePackage $destination
        if ($copy.manifestSha256 -cne $package.manifestSha256 -or @(Compare-Object -ReferenceObject @($package.assemblies.sha256) -DifferenceObject @($copy.assemblies.sha256) -CaseSensitive).Count) { throw "Package identity changed during staging: $($package.id)" }
        $package | Add-Member -NotePropertyName installedFolder -NotePropertyValue $folder
        $package | Add-Member -NotePropertyName installedDirectory -NotePropertyValue $destination
    }
    & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase after-staging
    Write-NativeJson (Join-Path $environment.root 'installed-file-hashes.json') @(Get-NativeTreeFiles $mods)
    Write-NativeJson (Join-Path $environment.root 'installed-packages.json') ([ordered]@{
        schemaVersion = 2; environmentRunId = $environment.runId; stagedAtUtc = [DateTime]::UtcNow.ToString('o'); gameBuild = $environment.buildId
        packages = $packages; probes = $probes
        source = $(if ($CandidatePackageDirectory) { 'explicit local candidate packages' } else { 'currently installed official packages' })
        sourceDirectory = $candidateSource; businessModsRebuilt = $false; formalInstalled = $false
        scanStatus = 'pending normal clone ModLoader scanner and UI activation'; passed = $true
    })
    $null = Assert-NativeCandidates $environment
    Write-Host "Staged $($packages.Count) candidates from $candidateSource and $($probes.Count) probes. Use the cloned game's normal Mod UI to inspect, trust and enable."
} catch {
    Write-NativeJson (Join-Path $environment.root 'staging-failure.json') ([ordered]@{ atUtc = [DateTime]::UtcNow.ToString('o'); error = $_.Exception.Message; partialClonePreserved = $true })
    throw
}
