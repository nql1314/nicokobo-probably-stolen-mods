Set-StrictMode -Version Latest

function ConvertTo-VdfText([System.Collections.IDictionary]$Fields) {
    $lines = @('"workshopitem"', '{')
    foreach ($key in $Fields.Keys) {
        $value = [string]$Fields[$key]
        if ($value.Contains([char]0)) { throw 'VDF values cannot contain NUL.' }
        # SteamCMD keeps control-character escapes such as \n as visible text.
        # Quoted VDF values support actual line breaks; preserve them for descriptions and changenotes.
        $value = $value.Replace('\', '\\').Replace('"', '\"')
        $lines += '    "' + $key + '" "' + $value + '"'
    }
    ($lines + '}') -join "`n"
}

function Read-WorkshopIdentity([string]$Path) {
    $text = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $app = [regex]::Matches($text, '(?<!\\)"appid"\s*"([0-9]+)"')
    $id = [regex]::Matches($text, '(?<!\\)"publishedfileid"\s*"([0-9]+)"')
    if ($app.Count -ne 1 -or $app[0].Groups[1].Value -ne '4348910' -or $id.Count -ne 1) {
        throw "Invalid Probably Stolen Workshop identity: $Path"
    }
    $value = $id[0].Groups[1].Value
    if ($value -notmatch '^(0|[1-9][0-9]{0,19})$' -or -not [ulong]::TryParse($value, [ref]([ulong]0))) {
        throw "Invalid Workshop item ID: $value"
    }
    $value
}

function Get-WorkshopChangeNote([string]$Path, [string]$Version) {
    $text = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $header = [regex]::Match($text, '(?m)^' + [regex]::Escape($Version) + '\s*-\s*[^\r\n]+')
    if (-not $header.Success) { throw "change.log has no section for version $Version" }
    $tail = $text.Substring($header.Index + $header.Length)
    $next = [regex]::Match($tail, '(?m)^\d+\.\d+\.\d+(?:\.\d+)?\s*-\s*')
    if ($next.Success) { $tail = $tail.Substring(0, $next.Index) }
    if ([string]::IsNullOrWhiteSpace($tail)) { throw "Empty changelog for $Version" }
    ($header.Value + "`n" + $tail.Trim()).Trim()
}

function Assert-WorkshopResult([int]$ExitCode, [string]$Output, [string]$BeforeId, [string]$AfterId) {
    if ($ExitCode -ne 0 -or $AfterId -eq '0') { throw 'SteamCMD did not confirm upload; keep the VDF and inspect its output.' }
    if ($BeforeId -ne '0' -and $AfterId -cne $BeforeId) { throw 'SteamCMD changed an existing item ID unexpectedly; inspect the Workshop page.' }
    # SteamCMD can exit 0 even when a command failed. Require upload completion.
    $success = [regex]::Matches($Output, '(?im)Success\.\s+(?:Published|Updated)\s+(?:a\s+new\s+)?item\s+([0-9]+)')
    if (@($success | Where-Object { $_.Groups[1].Value -ceq $AfterId }).Count) { return }

    # Current output writes PublishFileID first, then ends with commit success.
    # Shutdown output may follow Success. without a newline.
    $targets = [regex]::Matches($Output, '(?i)\bPublishFileID\s+([0-9]+)')
    $wrongTarget = @($targets | Where-Object { $_.Groups[1].Value -cne $AfterId }).Count -gt 0
    $targetKnown = $BeforeId -ne '0' -or $targets.Count -eq 1
    $committed = $Output -match '(?im)^[ \t]*Committing[ \t]+update[ \t]*\.{3}[ \t]*Success\.'
    if ($committed -and $targetKnown -and -not $wrongTarget) { return }
    throw 'No matching Workshop upload success message. Do not recreate the item; inspect the saved VDF and SteamCMD output.'
}

function Complete-WorkshopUpload([int]$ExitCode, [string]$Output, [string]$BeforeId, [string]$AfterId, [string]$PendingPath) {
    $invalidPassword = $Output -match '(?im)\bERROR\s*\(Invalid Password\)\s*$'
    $uploadStarted = $Output -match '(?im)\b(?:workshop_build_item|Preparing\s+update|Uploading|Committing|Creating\s+(?:a\s+new\s+)?item|Success\.\s+(?:Published|Updated))\b'
    # Only a definite pre-upload rejection can clear an unassigned creation.
    # Timeouts, partial uploads and any returned ID must retain the recovery guard.
    if ($invalidPassword -and -not $uploadStarted -and $BeforeId -ceq $AfterId) {
        if ($BeforeId -eq '0' -and (Test-Path -LiteralPath $PendingPath)) {
            Remove-Item -LiteralPath $PendingPath
        }
        throw 'Steam login failed (Invalid Password). No Workshop upload started. Check your Steam login name and password, then retry.'
    }
    Assert-WorkshopResult $ExitCode $Output $BeforeId $AfterId
    if (Test-Path -LiteralPath $PendingPath) { Remove-Item -LiteralPath $PendingPath }
}

function Get-WorkshopStateDirectory([string]$RepoRoot) {
    $current = Join-Path $RepoRoot '.local/steam-workshop'
    $legacy = Join-Path $RepoRoot '.steam-workshop'
    if (Test-Path -LiteralPath $legacy -PathType Container) {
        if (Test-Path -LiteralPath $current) {
            throw 'Workshop state exists in both .local/steam-workshop and .steam-workshop. Reconcile the saved bindings and pending markers before proceeding.'
        }
        return $legacy
    }
    return $current
}

function Get-WorkshopSteamCmdPath([string]$RepoRoot) {
    $current = Join-Path $RepoRoot '.local/steamcmd/steamcmd.exe'
    $legacy = Join-Path $RepoRoot 'tools/steamcmd/steamcmd.exe'
    if ((Test-Path -LiteralPath $current -PathType Leaf) -or !(Test-Path -LiteralPath $legacy -PathType Leaf)) {
        return $current
    }
    return $legacy
}

Export-ModuleMember -Function Get-WorkshopStateDirectory, Get-WorkshopSteamCmdPath, ConvertTo-VdfText, Read-WorkshopIdentity, Get-WorkshopChangeNote, Assert-WorkshopResult, Complete-WorkshopUpload
