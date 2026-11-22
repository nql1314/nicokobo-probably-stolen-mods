#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $root 'tools/publish/SteamWorkshop.psm1') -Force
$temp = Join-Path $root ('_build/steam-workshop-checks/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
function Assert($value, $message) { if (-not $value) { throw $message } }
function Reject([scriptblock]$action) {
    $rejected = $false
    try { & $action } catch { $rejected = $true }
    Assert $rejected 'Expected rejection.'
}
$vdfPath = Join-Path $temp 'test.vdf'
$fields = [ordered]@{appid='4348910';publishedfileid='0';description="中文 `"引号`"`r`n下一行`t缩进";changenote="更新说明`n第二行";contentfolder='D:\mods\test'}
$vdf = ConvertTo-VdfText $fields
Assert ($vdf.Contains("中文 \`"引号\`"`r`n下一行`t缩进")) 'VDF must preserve actual description line breaks and tabs.'
Assert ($vdf.Contains("更新说明`n第二行")) 'VDF must preserve actual changenote line breaks.'
$textVdf = ConvertTo-VdfText @{description=$fields.description;changenote=$fields.changenote}
Assert (-not $textVdf.Contains('\n') -and -not $textVdf.Contains('\r') -and -not $textVdf.Contains('\t')) 'VDF introduced visible control-character escapes in text.'
Assert ($vdf.Contains('D:\\mods\\test')) 'VDF path escaping failed.'
Set-Content $vdfPath $vdf
Assert ((Read-WorkshopIdentity $vdfPath) -eq '0') 'New identity failed.'
foreach ($id in @('1234567890', '18446744073709551615')) {
    $fields.publishedfileid = $id
    Set-Content $vdfPath (ConvertTo-VdfText $fields)
    Assert ((Read-WorkshopIdentity $vdfPath) -eq $id) 'Existing identity failed.'
}
$fields.publishedfileid = '18446744073709551616'
Set-Content $vdfPath (ConvertTo-VdfText $fields)
Reject { Read-WorkshopIdentity $vdfPath }
$fields.appid = '4349200'
Set-Content $vdfPath (ConvertTo-VdfText $fields)
Reject { Read-WorkshopIdentity $vdfPath }
Set-Content $vdfPath ($vdf + "`n" + '"publishedfileid" "123"')
Reject { Read-WorkshopIdentity $vdfPath }
$log = Join-Path $temp 'change.log'
Set-Content $log "2.0.0 - today`nCurrent fix`n`n1.0.0 - yesterday`nOld fix"
$note = Get-WorkshopChangeNote $log '2.0.0'
Assert ($note.Contains('Current fix') -and -not $note.Contains('Old fix')) 'Changelog leaked historical changes.'
Reject { Get-WorkshopChangeNote $log '3.0.0' }
Assert-WorkshopResult 0 'Success. Published item 123.' '0' '123'
Assert-WorkshopResult 0 'Success. Updated item 123.' '123' '123'
# Actual SteamCMD output reports the item ID before its commit-success message.
$committedNew = "Create new workshop item ( PublishFileID 3828918378).`nPreparing update...`nPreparing content...`nUploading content...`nUploading preview image...`nCommitting update...Success.Unloading Steam API...OK"
Assert-WorkshopResult 0 $committedNew '0' '3828918378'
Assert-WorkshopResult 0 'Committing update...Success.Unloading Steam API...OK' '123' '123'
Reject { Assert-WorkshopResult 0 ($committedNew.Replace('3828918378', '456')) '0' '3828918378' }
Reject { Assert-WorkshopResult 0 ($committedNew.Replace('Committing update...Success.', 'Committing update...ERROR!')) '0' '3828918378' }
Reject { Assert-WorkshopResult 1 $committedNew '0' '3828918378' }
Reject { Assert-WorkshopResult 0 'Committing update...Success.' '0' '3828918378' }
Reject { Assert-WorkshopResult 0 'PublishFileID 456) Committing update...Success.' '123' '123' }
Reject { Assert-WorkshopResult 0 'ERROR upload failed' '123' '123' }
Reject { Assert-WorkshopResult 1 'Success. Updated item 123.' '123' '123' }
Reject { Assert-WorkshopResult 0 'Success. Updated item 456.' '123' '456' }
Reject { Assert-WorkshopResult 0 'Success. Published item 123.' '0' '0' }
# A rejected login must allow a retry; uncertain creation must remain guarded.
$pendingLogin = Join-Path $temp 'login.pending'
$loginFailure = "Logging in user 'fixture' to Steam Public...ERROR (Invalid Password)`nUnloading Steam API...OK"
Set-Content $pendingLogin 'creation started'
try { Complete-WorkshopUpload 0 $loginFailure '0' '0' $pendingLogin; throw 'Failed login was accepted.' } catch {
    Assert ($_.Exception.Message -like 'Steam login failed (Invalid Password).*') 'Login failure did not report its cause.'
}
Assert (-not (Test-Path -LiteralPath $pendingLogin)) 'Rejected login left a stale creation guard.'
foreach ($uncertain in @('ERROR upload failed', ($loginFailure + "`nPreparing update..."))) {
    Set-Content $pendingLogin 'creation started'
    Reject { Complete-WorkshopUpload 1 $uncertain '0' '0' $pendingLogin }
    Assert (Test-Path -LiteralPath $pendingLogin) 'Uncertain upload lost its creation guard.'
}
Set-Content $pendingLogin 'creation started'
Reject { Complete-WorkshopUpload 0 $loginFailure '0' '123' $pendingLogin }
Assert (Test-Path -LiteralPath $pendingLogin) 'A returned item ID was treated as a login-only failure.'
Complete-WorkshopUpload 0 $committedNew '0' '3828918378' $pendingLogin
Assert (-not (Test-Path -LiteralPath $pendingLogin)) 'Confirmed upload left its creation guard.'
# Exercise publication guards in an isolated fixture, without Steam or a real build.
$legacy = Join-Path $temp '.steam-workshop'
$local = Join-Path $temp '.local/steam-workshop'
Assert ((Get-WorkshopStateDirectory $temp) -eq $local) 'New checkouts must use local Workshop state.'
New-Item -ItemType Directory -Path $legacy | Out-Null
Assert ((Get-WorkshopStateDirectory $temp) -eq $legacy) 'Legacy bindings must still be discovered.'
New-Item -ItemType Directory -Path $local -Force | Out-Null
Reject { Get-WorkshopStateDirectory $temp }
Remove-Item -LiteralPath $legacy
Copy-Item (Join-Path $root 'Publish-Steam.ps1') $temp
$fixtureTools = Join-Path $temp 'tools/publish'
New-Item -ItemType Directory -Path $fixtureTools, (Join-Path $temp 'mods/demo') -Force | Out-Null
Copy-Item (Join-Path $root 'tools/publish/Publish-Steam.ps1'), (Join-Path $root 'tools/publish/SteamWorkshop.psm1') $fixtureTools
Set-Content (Join-Path $temp 'mods/demo/manifest.xml') '<Mod><ID>demo</ID><ModVersion>1.0.0</ModVersion></Mod>'
Set-Content (Join-Path $temp '.local/steam-workshop/demo.pending') 'unknown outcome'
$entry = Join-Path $temp 'Publish-Steam.ps1'
try { & $entry -Mod demo -NewItem; throw 'Guard did not reject.' } catch {
    Assert ($_.Exception.Message -like '*Previous creation outcome is unknown*') 'Pending creation guard failed.'
}
$fields.appid = '4348910'; $fields.publishedfileid = '123'
Set-Content (Join-Path $temp '.local/steam-workshop/demo.vdf') (ConvertTo-VdfText $fields)
try { & $entry -Mod demo -PublishedFileId 456; throw 'Guard did not reject.' } catch {
    Assert ($_.Exception.Message -like '*already bound*') 'Rebinding guard failed.'
}
# A normal content update must preserve a description edited on the Workshop page,
# even when a local description exists. Only creation or an explicit sync sends it.
Set-Content (Join-Path $temp 'mods/demo/manifest.xml') '<Mod><ID>demo</ID><Name>Demo</Name><ModVersion>1.0.0</ModVersion><Description>Short manifest description</Description></Mod>'
Set-Content (Join-Path $temp 'mods/demo/change.log') "1.0.0 - today`nFixture change"
$description = "[h1]完整说明[/h1]`n[list][*]功能与快捷键[/list]"
Set-Content (Join-Path $temp 'mods/demo/workshop-description.txt') $description
Set-Content (Join-Path $temp 'Build-Mods.ps1') @'
param([string]$Mod, [string]$GameDir, [switch]$NoInstall, [switch]$PassThru)
$fixturePackage = Join-Path $PSScriptRoot 'packages/demo'
New-Item -ItemType Directory -Path $fixturePackage -Force | Out-Null
[pscustomobject]@{Root=$fixturePackage;Version='1.0.0';Manifest=[pscustomobject]@{Id='demo'}}
'@
$preparedPath = Join-Path $temp 'packages/demo.workshop.vdf'
& $entry -Mod demo -PrepareOnly
$prepared = Get-Content -LiteralPath $preparedPath -Raw
Assert ($prepared -notmatch '(?m)^\s*"description"\s') 'Routine update would overwrite the online description.'
Assert ((Read-WorkshopIdentity (Join-Path $temp '.local/steam-workshop/demo.vdf')) -eq '123') 'Preparing an update changed its saved identity.'
& $entry -Mod demo -PrepareOnly -UpdateDescription
$prepared = Get-Content -LiteralPath $preparedPath -Raw
Assert ($prepared.Contains($description)) 'Explicit description sync did not preserve Steam BBCode and actual newlines.'
Assert ($prepared.Contains("1.0.0 - today`nFixture change")) 'Prepared changenote lost its actual newline.'
Set-Content (Join-Path $temp 'mods/demo/manifest.xml') '<Mod><ID>demo</ID><Name>演示 / Demo</Name><ModVersion>1.0.0</ModVersion><Description>Short manifest description</Description></Mod>'
Set-Content (Join-Path $temp 'mods/demo/workshop-description.txt') "[h1]Demo[/h1]`nEnglish description."
& $entry -Mod demo -PrepareOnly -UpdateDescription
$localized = Get-Content -LiteralPath $preparedPath -Raw
Assert ($localized.Contains('"title" "Demo"')) 'English Workshop title was replaced by the bilingual manifest name.'
Assert ($localized.Contains('[h1]Demo[/h1]') -and -not $localized.Contains('演示')) 'Default Workshop details mixed the manifest language into the English page.'
Set-Content (Join-Path $temp 'mods/demo/workshop-description.txt') $description
$fields.publishedfileid = '0'
Set-Content (Join-Path $temp '.local/steam-workshop/demo.vdf') (ConvertTo-VdfText $fields)
Remove-Item -LiteralPath (Join-Path $temp '.local/steam-workshop/demo.pending')
& $entry -Mod demo -PrepareOnly
$prepared = Get-Content -LiteralPath $preparedPath -Raw
Assert ($prepared.Contains('[h1]完整说明[/h1]')) 'New publication omitted its local description.'
Remove-Item -LiteralPath (Join-Path $temp 'mods/demo/workshop-description.txt')
& $entry -Mod demo -PrepareOnly
$prepared = Get-Content -LiteralPath $preparedPath -Raw
Assert ($prepared.Contains('"description" "Short manifest description"')) 'New publication without a local description lost its manifest fallback.'
Write-Host 'Workshop offline checks passed (escaping, identity, changelog, upload result, login failure recovery, pending creation, rebinding and description preservation/sync).'
