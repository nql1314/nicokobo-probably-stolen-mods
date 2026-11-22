# Data-only validation. This never turns an incomplete experiment into a pass.
function Test-NativeBreedingCheckpoint($Probe) {
    try {
        $p = $Probe.persistence; $b = $p.nativeBreeding; $c = $b.checkpoint
        if ($Probe.mode -cnotin @('breeding-save','breeding-reload') -or !$Probe.executed -or !$Probe.executedChecksPassed -or
            $Probe.error -or $Probe.phase -cne 'completed' -or @($Probe.checks).Count -eq 0 -or
            @($Probe.checks | Where-Object status -ceq 'failed').Count -or
            !@($Probe.checks | Where-Object { $_.name -clike 'native-breeding.checkpoint-real-es3.*' -and $_.passed }).Count -or
            !@($Probe.checks | Where-Object { $_.name -ceq 'persistence.actual-es3-carrier-readback' -and $_.passed }).Count) { return $false }
        if ($b.schema -ne 1 -or $b.goal -cne 'rat_800g' -or !$b.resumeAllowed -or
            $b.status -cnotin @('in-progress-not-tested','claimed-awaiting-independent-reload') -or
            $b.sourceRunId -cne $Probe.runId -or $b.sourceProcessToken -cne $Probe.processToken -or
            $b.gameRunId -cne $p.gameRunId -or $b.slotId -ne $p.slotId -or $b.persistentDataPath -cne $p.persistentDataPath -or
            $b.day -ne $p.day -or $b.cash -ne $p.cash -or $p.saveSha256.Length -ne 64 -or
            $b.es3Sha256AtReadback -cne $p.saveSha256 -or $b.es3PathAtReadback -cne $p.saveFile -or @($b.items).Count -eq 0) { return $false }
        if ($c.schema -ne 1 -or $c.goal -cne 'rat_800g' -or $c.status -cne $b.status -or
            $c.lastCheckpointRunId -cne $Probe.runId -or $c.lastCheckpointProcessToken -cne $Probe.processToken -or
            $c.gameRunId -cne $p.gameRunId -or $c.slotId -ne $p.slotId -or $c.day -ne $p.day -or $c.cash -ne $p.cash -or
            [string]::IsNullOrEmpty($b.checkpointJson) -or $p.modData.'nicokobo.content_native_probe:breeding-progress' -cne $b.checkpointJson) { return $false }
        $parsed = $b.checkpointJson | ConvertFrom-Json
        if (($parsed | ConvertTo-Json -Depth 90 -Compress) -cne ($c | ConvertTo-Json -Depth 90 -Compress)) { return $false }
        return $true
    } catch { return $false }
}
