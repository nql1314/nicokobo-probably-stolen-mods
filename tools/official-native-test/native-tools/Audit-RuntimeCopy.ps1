[CmdletBinding()]
param([Parameter(Mandatory)][string]$EnvironmentPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$environment = Read-NativeEnvironment $EnvironmentPath
$issues = [Collections.Generic.List[object]]::new()
$intentional = [Collections.Generic.List[string]]::new()
$count = 0; $bytes = 0L
try {
    foreach ($name in @('Probably Stolen.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'MonoBleedingEdge', 'Probably Stolen_Data')) {
        $source = Join-Path $environment.formalGame $name
        Assert-NativeUnlinkedPath $source -Recurse
        $item = Get-Item -LiteralPath $source
        $files = if ($item.PSIsContainer) { @(Get-ChildItem -LiteralPath $source -Recurse -File -Force) } else { @($item) }
        foreach ($file in $files) {
            $count++; $bytes += $file.Length
            $relative = [IO.Path]::GetRelativePath($environment.formalGame, $file.FullName)
            $clone = Join-Path $environment.game $relative
            if (!(Test-Path -LiteralPath $clone -PathType Leaf)) { $issues.Add([pscustomobject]@{ path = $relative; issue = 'missing' }); continue }
            $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $cloneHash = (Get-FileHash -LiteralPath $clone -Algorithm SHA256).Hash
            if ($sourceHash -ceq $cloneHash) { continue }
            if ($relative -ceq 'Probably Stolen_Data\Managed\Assembly-CSharp.dll') {
                $proof = Get-Content -LiteralPath ($clone + '.isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($proof.originalSha256 -ceq $sourceHash -and $sourceHash -ceq $environment.originalAssemblySha256 -and $proof.isolatedSha256 -ceq $cloneHash -and
                    @($proof.changedMethods).Count -eq 1 -and $proof.changedMethods[0] -ceq 'System.Void SteamManager::Awake()' -and $proof.protectedMethodsUnchanged -gt 0 -and
                    $proof.expectedPersistent -ieq $environment.save -and $proof.expectedProduct -ceq $environment.product -and $proof.expectedCompany -ceq $environment.company -and $proof.runRoot -ieq $environment.root) { $intentional.Add($relative); continue }
            }
            if ($relative -ceq 'Probably Stolen_Data\globalgamemanagers') {
                $proof = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $clone) 'product-isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($proof.beforeSha256 -ieq $sourceHash -and $sourceHash -ceq $environment.originalPlayerSettingsSha256 -and $proof.afterSha256 -ieq $cloneHash -and
                    $proof.changedByteRangeLength -eq 15 -and $proof.after.productName -ceq $environment.product -and $proof.after.companyName -ceq $environment.company) { $intentional.Add($relative); continue }
            }
            if ($relative -ceq 'Probably Stolen_Data\app.info') {
                $proof = Get-Content -LiteralPath (Join-Path $environment.root 'app-info-isolation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
                if ($proof.environmentRunId -ceq $environment.runId -and $proof.target -ieq $clone -and
                    $proof.beforeSha256 -ieq $sourceHash -and $proof.afterSha256 -ieq $cloneHash -and
                    [string]::Join("`n", [IO.File]::ReadAllLines($clone)) -ceq ($environment.company + "`n" + $environment.product)) {
                    $intentional.Add($relative); continue
                }
            }
            $issues.Add([pscustomobject]@{ path = $relative; issue = 'unexpected different bytes'; source = $sourceHash; clone = $cloneHash })
        }
    }
    if ($intentional.Count -ne 3) { throw 'Expected exactly Steam startup, PlayerSettings and early app.info identity isolation changes.' }
    if ([IO.File]::ReadAllText((Join-Path $environment.game 'steam_appid.txt')).Trim() -cne $environment.appId) { throw 'Clone steam_appid.txt differs.' }
} catch { $issues.Add([pscustomobject]@{ issue = 'audit-error'; detail = $_.Exception.Message }) }
$result = [ordered]@{ environmentRunId = $environment.runId; completedAtUtc = [DateTime]::UtcNow.ToString('o'); files = $count; bytes = $bytes; scope = 'Full recursive MonoBleedingEdge and Probably Stolen_Data plus player/runtime root files'; intentionalDifferences = @($intentional.ToArray()); issues = @($issues.ToArray()); passed = $issues.Count -eq 0 }
$evidence = Join-Path $environment.root ('runtime-copy-audit-' + [guid]::NewGuid().ToString('N') + '.json')
Write-NativeJson $evidence $result
if ($issues.Count) { throw "Runtime copy audit failed. Evidence: $evidence" }
Write-Host "Runtime copy verified: $count files; three recorded test isolation changes."
