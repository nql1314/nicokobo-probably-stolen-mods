[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentPath,
    [Parameter(Mandatory)][ValidateSet('catalog','production-save','production-reload','aug-save','aug-reload','theft-save','theft-reload','review-save','review-reload','manager-save','manager-reload','manageren-save','manageren-reload','night-save','night-reload','furnace-save','furnace-reload','grayline-save','grayline-reload','dossier-save','dossier-reload','dossieraug-save','dossieraug-reload','dossiertheft-save','dossiertheft-reload','speed-stock','storylegal-save','storylegal-reload','storysettled-save','storysettled-reload','storylost-save','storylost-reload','storybeyond-save','storybeyond-reload','theftui-save','theftui-reload','logisticsfull-save','logisticsfull-reload','achievements-save','achievements-reload','ratfixture-save','ratfixture-reload','achievementsgaps-save','achievementsgaps-reload','performance-save','reputation-save','reputation-reload','breeding-save','breeding-reload','augprotection-save','augprotection-reload','logisticsboundaries-save','logisticsboundaries-reload','deviceflows-save','deviceflows-reload','containerproduction-save','containerproduction-reload','bottleproduction-save','bottleproduction-reload','extendedproduction-save','extendedproduction-reload','vanillachemistry-save','vanillachemistry-reload','chemicalreactions-save','chemicalreactions-reload','nightdevices-save','nightdevices-reload','businessdevices-save','businessdevices-reload','vanillachemistrycapacity-save','nativedna-save','nativedna-reload')][string]$Mode,
    [Parameter(Mandatory)][ValidateRange(0,2147483647)][int]$Slot,
    [string]$Baseline,
    [switch]$DuplicateSlot,
    [switch]$UseCopy,
    [switch]$DeveloperMode,
    [switch]$AllowPrimaryDisplay,
    [ValidateRange(60,10830)][int]$TimeoutSeconds = 960,
    [guid]$RunId = [guid]::NewGuid()
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if ($Mode -notmatch '^(storylegal|storysettled|storylost|storybeyond|achievements|achievementsgaps|reputation|breeding|augprotection|chemicalreactions|nightdevices|businessdevices|extendedproduction)-' -and $TimeoutSeconds -gt 1830) { throw 'Only full story, achievements, reputation and breeding routes support supervision longer than 1830 seconds.' }
$environment = Read-NativeEnvironment $EnvironmentPath
$runIdText = $RunId.ToString('D')
$trustExpectation = if ($DeveloperMode) { 'developer' } else { 'ordinary' }
$run = Join-Path $environment.root ($Mode + '-' + $runIdText)
if (Test-Path -LiteralPath $run) { throw 'Every invocation requires a new run directory.' }
$null = New-Item -ItemType Directory -Path $run
$resultPath = Join-Path $run 'result.json'
$log = Join-Path $run 'Player.log'
$executable = Join-Path $environment.game 'Probably Stolen.exe'
$failures = [Collections.Generic.List[string]]::new()
$process = $null; $processId = $null; $processStart = $null; $exitCode = $null
$timedOut = $false; $probe = $null; $actualPrefs = $null; $inventoryHash = $null; $checked = 0; $checkpoint = $false
try {
    Assert-NoGameProcess
    Assert-NativeRuntime $environment
    $null = Assert-NativeCandidates $environment
    $inventoryHash = (Get-FileHash -LiteralPath (Join-Path $environment.root 'installed-file-hashes.json') -Algorithm SHA256).Hash
    if ($DuplicateSlot -and $Mode -notmatch '^(aug|storylegal|storysettled|storylost|storybeyond|achievements)-save$') { throw 'DuplicateSlot requires an Aug, full-story or achievements save.' }
    if ($UseCopy -and $Mode -notmatch '^(aug|storylegal|storysettled|storylost|storybeyond|achievements)-reload$') { throw 'UseCopy requires an Aug, full-story or achievements reload.' }
    if ($Mode -ceq 'speed-stock' -and ($Slot -ne 0 -or $Baseline -or $DuplicateSlot -or $UseCopy -or $TimeoutSeconds -lt 930)) { throw 'speed-stock requires Slot 0, timeout at least 930 seconds, and no save/copy baseline options.' }
    if (!$Mode.EndsWith('-reload') -and $Baseline) { throw 'Baseline is only valid for reload modes.' }
    if ($Mode.EndsWith('-reload')) {
        if (!$Baseline) { throw 'Reload needs the prior successful save evidence.' }
        $Baseline = (Resolve-Path -LiteralPath $Baseline).Path
        Assert-NativeChildPath $Baseline $environment.root
        $prior = Get-Content -LiteralPath $Baseline -Raw -Encoding UTF8 | ConvertFrom-Json
        $priorSupervisor = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $Baseline) 'supervisor.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $saveMode = $Mode.Substring(0, $Mode.Length - 7) + '-save'
        $priorCheckpoint = $Mode -ceq 'breeding-reload' -and $priorSupervisor.PSObject.Properties['checkpointReady'] -and
            $priorSupervisor.checkpointReady -and !$priorSupervisor.timedOut -and $priorSupervisor.exitCode -in @(0,23) -and (Test-NativeBreedingCheckpoint $prior)
        $sameFamily = $prior.mode -ceq $saveMode -or ($Mode -ceq 'breeding-reload' -and $prior.mode -ceq 'breeding-reload')
        if ((!$priorSupervisor.passed -and !$priorCheckpoint) -or $priorSupervisor.environmentRunId -cne $environment.runId -or
            $priorSupervisor.result -ine $Baseline -or $priorSupervisor.runId -cne $prior.runId -or
            $priorSupervisor.candidateInventorySha256 -cne $inventoryHash -or !$sameFamily -or $priorSupervisor.mode -cne $prior.mode -or
            $prior.runId -ceq $runIdText -or $priorSupervisor.trustExpectation -cne $trustExpectation) { throw 'Baseline must belong to a successful save run of this environment, mode family, trust expectation and unchanged candidate set.' }
        $null = Assert-NativeProbeResult $prior $priorSupervisor.runId $prior.mode $priorSupervisor.processId $environment -AllowBreedingCheckpoint:$priorCheckpoint
        $priorSlot = if ($UseCopy) { $prior.persistence.duplicateSlotId } else { $prior.persistence.slotId }
        if ($priorSlot -ne $Slot -or (Get-NativeFullPath $prior.persistence.persistentDataPath) -ine (Get-NativeFullPath $environment.save)) { throw 'Baseline slot or persistent directory differs from the requested reload.' }
    }
    & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase before-run
    $switch = if ($Mode -ceq 'speed-stock') { '--nicokobo-native-speedstock-test' } else { '--nicokobo-native-test' }
    $monitor = Get-NativeLaunchMonitor -AllowPrimaryDisplay:$AllowPrimaryDisplay
    $arguments = @('-logFile', ('"' + $log + '"'), '-monitor', $monitor, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        $switch, ('--native-test-run=' + $runIdText), ('"--native-test-output=' + $resultPath + '"'), ('--native-test-mode=' + $Mode),
        ('"--native-test-persistent=' + $environment.save + '"'), ('"--native-test-company=' + $environment.company + '"'),
        ('--native-test-product=' + $environment.product), ('--native-test-slot=' + $Slot), ('--native-test-timeout=' + ($TimeoutSeconds - 30)),
        ('--native-test-developer-mode=' + ([bool]$DeveloperMode).ToString().ToLowerInvariant()))
    if ($AllowPrimaryDisplay) { $arguments += '--native-test-allow-primary-display=true' }
    if ($Baseline) { $arguments += '"--native-test-baseline=' + $Baseline + '"' }
    if ($DuplicateSlot) { $arguments += '--native-test-duplicate-slot=true' }
    if ($UseCopy) { $arguments += '--native-test-use-copy=true' }
    $process = Start-Process -FilePath $executable -WorkingDirectory $environment.game -ArgumentList $arguments -WindowStyle Normal -PassThru
    $processId = $process.Id
    $processStart = $process.StartTime.ToUniversalTime()
    Write-NativeJson (Join-Path $run 'launch.json') ([ordered]@{
        environmentRunId = $environment.runId; runId = $runIdText; mode = $Mode; slot = $Slot; processId = $processId; processStartUtc = $processStart.ToString('o')
        executable = $executable; result = $resultPath; log = $log; arguments = $arguments; baseline = $Baseline; candidateInventorySha256 = $inventoryHash; trustExpectation = $trustExpectation; allowPrimaryDisplay = [bool]$AllowPrimaryDisplay
    })
    Write-Host "Started isolated $Mode PID=$processId; evidence=$run"
    $started = [DateTime]::UtcNow
    while (!$process.WaitForExit(1000)) {
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt $TimeoutSeconds) { $timedOut = $true; throw 'Own isolated test process exceeded its timeout.' }
    }
    $exitCode = $process.ExitCode
    if (!(Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'No structured result was produced.' }
    $probe = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($probe.PSObject.Properties['checks']) { $checked = @($probe.checks).Count }
    $checkpoint = $Mode -clike 'breeding-*' -and !$probe.passed -and (Test-NativeBreedingCheckpoint $probe)
    if ($exitCode -ne 0 -and !($checkpoint -and $exitCode -eq 23)) { $failures.Add("Isolated process exit code was $exitCode.") }
    $marker = Assert-NativeProbeResult $probe $runIdText $Mode $processId $environment -AllowBreedingCheckpoint:$checkpoint
    if ($probe.environment.trustExpectation -cne $trustExpectation) { throw 'Probe trust expectation differs from the requested coverage mode.' }
    if ($Baseline -and $probe.processId -eq $prior.processId) { throw 'Reload must have a different native process ID.' }
    $registry = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($environment.playerPrefsRegistry)
    if ($null -eq $registry) { throw 'The isolated PlayerPrefs registry identity was not created.' }
    try {
        $names = @($registry.GetValueNames() | Where-Object { $_ -ceq $marker.key -or $_.StartsWith($marker.key + '_h', [StringComparison]::Ordinal) })
        if ($names.Count -ne 1) { throw 'The nested PlayerPrefs marker key was not uniquely found in the actual isolated registry.' }
        $value = $registry.GetValue($names[0])
        $valueText = if ($value -is [byte[]]) { [Text.Encoding]::UTF8.GetString($value).TrimEnd([char]0) } else { [string]$value }
        $actualPrefs = [ordered]@{ key = $environment.playerPrefsRegistry; valueName = $names[0]; kind = [string]$registry.GetValueKind($names[0]); marker = $valueText }
        if ($valueText -cne $runIdText) { throw 'The actual isolated PlayerPrefs marker differs from this run.' }
    } finally { $registry.Dispose() }
} catch { $failures.Add($_.Exception.Message) }
finally {
    if ($null -ne $process) {
        try {
            $process.Refresh()
            if (!$process.HasExited) {
                if ($null -eq $processStart -or $process.StartTime.ToUniversalTime() -ne $processStart -or $process.Path -ine $executable) { throw 'Cannot prove ownership of the still-running process; it was not stopped.' }
                Stop-Process -InputObject $process
                if (!$process.WaitForExit(10000)) { throw 'Own process did not exit after termination.' }
            }
            $exitCode = $process.ExitCode
        } catch { $failures.Add($_.Exception.Message) }
    }
    try { & (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase after-run }
    catch { $failures.Add('Formal preservation failed: ' + $_.Exception.Message) }
    try { $null = Assert-NativeCandidates $environment }
    catch { $failures.Add('Candidate preservation failed: ' + $_.Exception.Message) }
    Write-NativeJson (Join-Path $run 'supervisor.json') ([ordered]@{
        environmentRunId = $environment.runId; completedAtUtc = [DateTime]::UtcNow.ToString('o'); runId = $runIdText; mode = $Mode; slot = $Slot
        processId = $processId; exitCode = $exitCode; timedOut = $timedOut; result = $resultPath; baseline = $Baseline
        candidateInventorySha256 = $inventoryHash; actualPlayerPrefs = $actualPrefs; failures = @($failures.ToArray()); trustExpectation = $trustExpectation
        passed = $failures.Count -eq 0 -and $null -ne $probe -and $probe.passed; checked = $checked
        checkpointReady = $checkpoint -and $failures.Count -eq 0 -and !$timedOut
        checkpointStatus = if ($checkpoint) { 'Incomplete native breeding experiment; not accepted as passed. Exact checkpoint may continue in a new isolated process.' } else { 'not-applicable' }
        environmentCleanup = 'pending explicit Clean-IsolatedRuntime after all rounds'
    })
    if ($null -ne $process) { $process.Dispose() }
}
if ($failures.Count) { throw (($failures -join ' ') + " Evidence: $run") }
if ($checkpoint) { Write-Output "Native breeding checkpoint preserved; required coverage remains incomplete, supervisor.passed=false: $run" }
else { Write-Output "Native required checks and formal preservation passed: $run" }
