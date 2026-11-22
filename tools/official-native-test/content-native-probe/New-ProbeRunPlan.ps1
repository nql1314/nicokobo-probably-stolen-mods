[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IsolationRoot,
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$PersistentDirectory,
    [Parameter(Mandatory)][string]$Company,
    [Parameter(Mandatory)][ValidatePattern('^NicoT_[0-9a-f]{9}$')][string]$Product,
    [Parameter(Mandatory)][ValidateSet('catalog','production-save','production-reload','aug-save','aug-reload','theft-save','theft-reload','review-save','review-reload','manager-save','manager-reload','manageren-save','manageren-reload','night-save','night-reload','furnace-save','furnace-reload','grayline-save','grayline-reload','dossier-save','dossier-reload','dossieraug-save','dossieraug-reload','dossiertheft-save','dossiertheft-reload','storylegal-save','storylegal-reload','storysettled-save','storysettled-reload','storylost-save','storylost-reload','storybeyond-save','storybeyond-reload','theftui-save','theftui-reload','logisticsfull-save','logisticsfull-reload','achievements-save','achievements-reload','achievementsgaps-save','achievementsgaps-reload','performance-save','reputation-save','reputation-reload','breeding-save','breeding-reload','augprotection-save','augprotection-reload','logisticsboundaries-save','logisticsboundaries-reload','deviceflows-save','deviceflows-reload','containerproduction-save','containerproduction-reload','bottleproduction-save','bottleproduction-reload','extendedproduction-save','extendedproduction-reload','vanillachemistry-save','vanillachemistry-reload','chemicalreactions-save','chemicalreactions-reload','nightdevices-save','nightdevices-reload','businessdevices-save','businessdevices-reload','vanillachemistrycapacity-save','nativedna-save','nativedna-reload')][string]$Mode,
    [Parameter(Mandatory)][ValidateRange(0,2147483647)][int]$Slot,
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [string]$Baseline,
    [ValidateRange(30,10800)][int]$TimeoutSeconds = 900,
    [switch]$DuplicateSlot,
    [switch]$UseCopy,
    [switch]$DeveloperMode
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../native-tools/Checkpoint.ps1')
if ($Mode -notmatch '^(storylegal|storysettled|storylost|storybeyond|achievements|achievementsgaps|reputation|breeding|augprotection|chemicalreactions|nightdevices|businessdevices|extendedproduction)-' -and $TimeoutSeconds -gt 1800) { throw 'Only full story, achievements, reputation and breeding routes support timeouts longer than 1800 seconds.' }
$probeWorkspace = [IO.Path]::GetFullPath($IsolationRoot).TrimEnd('\')
$probeClone = [IO.Path]::GetFullPath($GameDirectory).TrimEnd('\')
if (!(Test-Path -LiteralPath $probeWorkspace -PathType Container) -or !$probeClone.Equals((Join-Path $probeWorkspace 'game'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'GameDirectory must be the game child of the explicitly prepared IsolationRoot.'
}
$probeExe = Join-Path $probeClone 'Probably Stolen.exe'
if (!(Test-Path -LiteralPath $probeExe -PathType Leaf)) { throw "Test clone executable missing: $probeExe" }
$probePersistent = [IO.Path]::GetFullPath($PersistentDirectory).TrimEnd('\')
if ((Split-Path -Path $probePersistent -Leaf) -cne $Product) { throw 'Expected persistent leaf must equal unique test Product.' }
$probeExpectedTrust = if ($DeveloperMode) { 'developer' } else { 'ordinary' }
if ($Mode.EndsWith('-reload')) {
    if (!$Baseline -or !(Test-Path -LiteralPath $Baseline -PathType Leaf)) { throw 'Reload requires the distinct preceding save result JSON.' }
    $Baseline = [IO.Path]::GetFullPath($Baseline)
    $probeSource = Get-Content -LiteralPath $Baseline -Raw | ConvertFrom-Json
    $probeExpectedSave = $Mode.Replace('-reload', '-save')
    $probeSourceSlot = if ($UseCopy) { $probeSource.persistence.duplicateSlotId } else { $probeSource.persistence.slotId }
    $probeCheckpoint = $Mode -ceq 'breeding-reload' -and (Test-NativeBreedingCheckpoint $probeSource)
    $probeSameFamily = $probeSource.mode -ceq $probeExpectedSave -or ($Mode -ceq 'breeding-reload' -and $probeSource.mode -ceq 'breeding-reload')
    if ((!$probeSource.passed -and !$probeCheckpoint) -or $probeSource.executed -ne $true -or !$probeSameFamily -or
        $probeSourceSlot -ne $Slot -or $probeSource.persistence.persistentDataPath -ine $probePersistent -or
        $probeSource.environment.trustExpectation -cne $probeExpectedTrust) {
        throw 'Reload requires a successful same-family save result for this isolated path, selected slot and trust expectation.'
    }
}
elseif ($Baseline) { throw 'Baseline is only valid for reload modes.' }
if ($DuplicateSlot -and $Mode -notmatch '^(aug|storylegal|storysettled|storylost|storybeyond|achievements)-save$') { throw 'DuplicateSlot requires an Aug, full-story or achievements save.' }
if ($UseCopy -and $Mode -notmatch '^(aug|storylegal|storysettled|storylost|storybeyond|achievements)-reload$') { throw 'UseCopy requires an Aug, full-story or achievements reload.' }
$probeRunId = [guid]::NewGuid().ToString('D')
$probeArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot).TrimEnd('\')
if (!$probeArtifactRoot.StartsWith($probeWorkspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'ArtifactRoot must remain within IsolationRoot.' }
$probeArtifact = Join-Path $probeArtifactRoot $probeRunId
if (Test-Path -LiteralPath $probeArtifact) { throw 'Fresh artifact directory unexpectedly exists.' }
New-Item -ItemType Directory -Path $probeArtifact | Out-Null
$probeOutput = Join-Path $probeArtifact 'result.json'
$probeArguments = @(
    '--nicokobo-native-test', "--native-test-run=$probeRunId", "--native-test-output=$probeOutput",
    "--native-test-mode=$Mode", "--native-test-persistent=$probePersistent", "--native-test-company=$Company",
    "--native-test-product=$Product", "--native-test-slot=$Slot", "--native-test-timeout=$TimeoutSeconds",
    "--native-test-developer-mode=$($DeveloperMode.IsPresent.ToString().ToLowerInvariant())",
    '-logFile', (Join-Path $probeArtifact 'Player.log')
)
if ($Baseline) { $probeArguments += "--native-test-baseline=$Baseline" }
if ($DuplicateSlot) { $probeArguments += '--native-test-duplicate-slot=true' }
if ($UseCopy) { $probeArguments += '--native-test-use-copy=true' }
$probePlan = [ordered]@{
    schema = 1; runId = $probeRunId; mode = $Mode; executable = $probeExe; workingDirectory = $probeClone
    isolationRoot = $probeWorkspace
    arguments = $probeArguments; output = $probeOutput; artifactDirectory = $probeArtifact
    expectedPersistentDataPath = $probePersistent; expectedCompany = $Company; expectedProduct = $Product; expectedSlot = $Slot
    baseline = $Baseline; trustExpectation = $probeExpectedTrust; requiresUiActivationAndRestart = $true
    requiresOrdinaryUiActivationAndRestart = !$DeveloperMode.IsPresent; startsGame = $false
    requiredExternalGuards = @('Formal files and PlayerPrefs baseline recorded for read-only comparison','Unique clone product and Steam isolation verified','Only self-owned test PID may be terminated','User-selected scanner/trust/activation settings retained; read-only verification','Independent registry marker verification','Postrun read-only baseline verification; report external changes without restoring or overwriting them')
}
$probePlan | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $probeArtifact 'run-plan.json') -Encoding utf8
$probePlan | ConvertTo-Json -Depth 10
