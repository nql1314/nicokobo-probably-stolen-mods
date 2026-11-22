[CmdletBinding()]
param(
    [string]$GameDirectory = 'F:\SteamLibrary\steamapps\common\Probably Stolen',
    [string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$GameCodeDirectory,
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot '..\..\..\_build\native-test\content-probe-build'),
    [switch]$ProbeOnly
)
$ErrorActionPreference = 'Stop'
$probeRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$probeGame = [IO.Path]::GetFullPath($GameDirectory)
$probeManaged = Join-Path $probeGame 'Probably Stolen_Data\Managed'
$probeCode = [IO.Path]::GetFullPath($GameCodeDirectory)
$probeArtifact = [IO.Path]::GetFullPath($ArtifactDirectory)
if (!$CandidateDirectory) { $CandidateDirectory = Join-Path $probeGame 'Mods' }
$probeCandidates = [IO.Path]::GetFullPath($CandidateDirectory)
foreach ($probeRequired in @((Join-Path $probeManaged 'Assembly-CSharp.dll'), (Join-Path $probeCode 'ModScanner.cs'), (Join-Path $probeCode 'ModCapability.cs'))) {
    if (!(Test-Path -LiteralPath $probeRequired -PathType Leaf)) { throw "Required local current-build input missing: $probeRequired" }
}
[IO.Directory]::CreateDirectory($probeArtifact) | Out-Null
Push-Location -LiteralPath $probeRoot
try {
    & dotnet build (Join-Path $probeRoot 'ContentNativeProbe.csproj') -c Release --nologo '-p:UseSharedCompilation=false' "-p:GameDir=$probeGame" *> (Join-Path $probeArtifact 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Probe build failed: $probeArtifact\build.log" }
    & dotnet build (Join-Path $probeRoot 'Inspector\Inspector.csproj') -c Release --nologo '-p:UseSharedCompilation=false' "-p:GameManaged=$probeManaged" "-p:GameCodeDir=$probeCode" *> (Join-Path $probeArtifact 'inspector-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Inspector build failed: $probeArtifact\inspector-build.log" }
    $probeDll = Join-Path $probeRoot 'bin\Release\netstandard2.1\Nicokobo.ContentNativeProbe.dll'
    $probeManifest = Join-Path $probeRoot 'manifest.xml'
    $probeInspector = Join-Path $probeRoot 'Inspector\bin\Release\net10.0\Inspector.dll'
    $probeEvidencePath = Join-Path $probeArtifact 'static-preflight.json'
    $probeInspectArgs = @($(if ($ProbeOnly) { '--probe-only' } else { '--candidates' }), $probeDll, $probeManifest, $probeEvidencePath, $probeManaged)
    if (!$ProbeOnly) { $probeInspectArgs += $probeCandidates }
    & dotnet $probeInspector @probeInspectArgs *> (Join-Path $probeArtifact 'static-preflight.log')
    if ($LASTEXITCODE -ne 0) { throw "Compiled metadata/scanner preflight failed: $probeArtifact\static-preflight.log" }
    $probePackage = Join-Path $probeArtifact 'package'
    [IO.Directory]::CreateDirectory($probePackage) | Out-Null
    Copy-Item -LiteralPath $probeDll -Destination (Join-Path $probePackage 'Nicokobo.ContentNativeProbe.dll') -Force
    Copy-Item -LiteralPath $probeManifest -Destination (Join-Path $probePackage 'manifest.xml') -Force
    $probeEvidence = [ordered]@{
        package = $probePackage; staticPreflight = $probeEvidencePath
        gameDirectory = $probeGame; gameCodeDirectory = $probeCode; candidateDirectory = $probeCandidates
        gameStarted = $false; gameInstalled = $false; businessModsBuilt = $false
        candidateScope = $(if ($ProbeOnly) { 'Probe metadata and current source-linked scanner only.' } else { 'Candidate directory: compiled metadata only. No candidate code was executed.' })
    }
    $probeEvidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $probeArtifact 'package-evidence.json') -Encoding utf8
    $probeEvidence | ConvertTo-Json -Depth 8
}
finally { Pop-Location }
