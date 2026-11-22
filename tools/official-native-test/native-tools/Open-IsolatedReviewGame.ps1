[CmdletBinding()]
param([Parameter(Mandatory)][string]$EnvironmentPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$environment = Read-NativeEnvironment $EnvironmentPath
Assert-NoGameProcess
Assert-NativeRuntime $environment
$installed = Assert-NativeCandidates $environment
& (Join-Path $PSScriptRoot 'Verify-FormalPreservation.ps1') -EnvironmentPath $EnvironmentPath -Phase before-review
$review = Join-Path $environment.root ('review-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $review
$log = Join-Path $review 'Player.log'
$executable = Join-Path $environment.game 'Probably Stolen.exe'
$monitor = Get-NativeLaunchMonitor
# This is the interactive ordinary Mod UI. Test switches remain absent, so the
# installed probes are dormant while the operator reviews and enables packages.
$process = Start-Process -FilePath $executable -WorkingDirectory $environment.game -ArgumentList @('-logFile', ('"' + $log + '"'), '-monitor', $monitor, '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720') -WindowStyle Normal -PassThru
Write-NativeJson (Join-Path $review 'launch.json') ([ordered]@{
    environmentRunId = $environment.runId; startedAtUtc = [DateTime]::UtcNow.ToString('o'); processId = $process.Id; processStartUtc = $process.StartTime.ToUniversalTime().ToString('o')
    executable = $executable; log = $log; gameBuild = $environment.buildId; product = $environment.product; expectedPersistent = $environment.save
    candidateCount = @($installed.packages).Count; probeCount = @($installed.probes).Count
    purpose = 'Normal isolated UI inspection, trust and activation. Close normally, then Verify-FormalPreservation before a probe round.'
})
Write-Output "Opened isolated review game PID=$($process.Id); evidence=$review"
$process.Dispose()
