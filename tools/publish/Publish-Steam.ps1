#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Mod,
    [string]$SteamUser,
    [string]$SteamCmdPath,
    [ValidatePattern('^[1-9][0-9]{0,19}$')][string]$PublishedFileId,
    [switch]$NewItem,
    [ValidateSet('Public','FriendsOnly','Hidden')][string]$Visibility,
    [string]$ChangeNote,
    [string]$PreviewFile,
    [switch]$UpdateDescription,
    [switch]$PrepareOnly,
    [string]$GameDir = $env:PROBABLY_STOLEN_GAME_DIR
)

$ErrorActionPreference = 'Stop'
# SteamCMD writes UTF-8 even under a legacy Windows console code page.
# PowerShell uses Console.OutputEncoding to decode native pipeline output.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
Import-Module (Join-Path $PSScriptRoot 'SteamWorkshop.psm1') -Force
$interactive = -not $Mod
$projects = @(Get-ChildItem (Join-Path $repoRoot 'mods') -Directory | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.xml')
})
if (-not $Mod) {
    Write-Host 'Select one Mod to publish/update:'
    for ($i = 0; $i -lt $projects.Count; $i++) { Write-Host "$($i + 1). $($projects[$i].Name)" }
    $choice = Read-Host 'Number'
    if ($choice -notmatch '^[1-9][0-9]*$' -or [long]$choice -gt $projects.Count) { throw 'Invalid selection.' }
    $Mod = $projects[[int]$choice - 1].Name
}
$project = @($projects | Where-Object { $_.Name -ceq $Mod })
if ($project.Count -ne 1) { throw "Unknown official Mod folder: $Mod" }
$source = $project[0].FullName
[xml]$manifest = Get-Content -LiteralPath (Join-Path $source 'manifest.xml') -Raw
$modId = [string]$manifest.Mod.ID
if ($modId -notmatch '^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}$') { throw 'Invalid manifest ID.' }
if ($NewItem -and $PublishedFileId) { throw 'Choose NewItem or PublishedFileId, not both.' }

# Durable identity lives outside disposable build/dist folders. Never store passwords here.
$stateRoot = Get-WorkshopStateDirectory $repoRoot
New-Item -ItemType Directory -Path $stateRoot -Force | Out-Null
$vdfPath = Join-Path $stateRoot "$modId.vdf"
$pendingPath = Join-Path $stateRoot "$modId.pending"
$settingsPath = Join-Path $stateRoot 'account.json'
$lock = [IO.File]::Open((Join-Path $stateRoot "$modId.lock"), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    $id = if (Test-Path -LiteralPath $vdfPath) { Read-WorkshopIdentity $vdfPath } else { '0' }
    if ($id -ne '0') {
        if ($NewItem -or ($PublishedFileId -and $PublishedFileId -cne $id)) {
            throw "This Mod is already bound to Workshop item $id. Refusing to replace its identity."
        }
    } elseif ($PublishedFileId) {
        $id = $PublishedFileId
    } elseif (Test-Path -LiteralPath $pendingPath) {
        throw "Previous creation outcome is unknown. Check your Workshop items and bind the returned ID with -PublishedFileId. Do not delete $pendingPath to retry blindly."
    } elseif (-not $NewItem -and -not $PrepareOnly) {
        if (-not $interactive) { throw 'First publication: use -NewItem, or bind an existing item with -PublishedFileId.' }
        $answer = Read-Host 'Existing Workshop item ID, or NEW to create one'
        if ($answer -ceq 'NEW') { $NewItem = $true }
        elseif ($answer -match '^[1-9][0-9]{0,19}$') { $id = $answer }
        else { throw 'No valid publication target selected.' }
    }

    if (-not $ChangeNote) { $ChangeNote = Get-WorkshopChangeNote (Join-Path $source 'change.log') ([string]$manifest.Mod.ModVersion) }
    if ($PreviewFile) { $PreviewFile = (Resolve-Path -LiteralPath $PreviewFile).Path }
    elseif (Test-Path -LiteralPath (Join-Path $source 'preview.png')) { $PreviewFile = Join-Path $source 'preview.png' }
    if ($PreviewFile -and (Get-Item -LiteralPath $PreviewFile).Length -ge 1MB) { throw 'Use a preview image smaller than 1 MB.' }

    $descriptionPath = Join-Path $source 'workshop-description.txt'
    $description = if (Test-Path -LiteralPath $descriptionPath) {
        Get-Content -LiteralPath $descriptionPath -Raw -Encoding utf8
    } else { [string]$manifest.Mod.Description }
    # SteamCMD updates the default English page; its title belongs to the same
    # localized document, while the game manifest keeps its bilingual name.
    $heading = [regex]::Match($description, '\A\s*\[h1\]([^\r\n]+?)\[/h1\]')
    $workshopTitle = if ($heading.Success) { $heading.Groups[1].Value } else { [string]$manifest.Mod.Name }

    # Upload only this fresh build snapshot, never a possibly stale dist directory.
    $built = @(& (Join-Path $repoRoot 'Build-Mods.ps1') -Mod $Mod -GameDir $GameDir -NoInstall -PassThru)
    if ($built.Count -ne 1) { throw 'Build did not return exactly one package.' }
    $package = $built[0]
    if ($package.Manifest.Id -cne $modId -or $package.Version -cne [string]$manifest.Mod.ModVersion) {
        throw 'Source identity/version changed during the build; prepare again.'
    }
    $fields = [ordered]@{
        appid = '4348910'
        publishedfileid = $id
        contentfolder = $package.Root
        changenote = $ChangeNote
    }
    # Omitted VDF fields keep their current Workshop values. Preserve page edits
    # on routine updates, even when a local description file is present.
    if ($id -eq '0' -or $UpdateDescription) {
        $fields.title = $workshopTitle
        $fields.description = $description
    }
    if ($PreviewFile) { $fields.previewfile = $PreviewFile }
    # Omit visibility during routine updates so an existing public item is not hidden.
    if ($Visibility) { $fields.visibility = @{Public='0';FriendsOnly='1';Hidden='2'}[$Visibility] }
    elseif ($id -eq '0') { $fields.visibility = '2' }
    $vdf = ConvertTo-VdfText $fields
    if ($PrepareOnly) {
        $previewVdf = Join-Path (Split-Path $package.Root -Parent) "$Mod.workshop.vdf"
        Set-Content -LiteralPath $previewVdf -Value $vdf -Encoding utf8NoBOM
        Write-Host "Prepared only; no Steam login/upload or game installation: $previewVdf"
        return
    }

    if (-not $SteamUser -and (Test-Path -LiteralPath $settingsPath)) {
        $SteamUser = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).SteamUser
    }
    if (-not $SteamUser) { $SteamUser = Read-Host 'Steam login name (password / Steam Guard are entered in SteamCMD)' }
    if ($SteamUser -notmatch '^[A-Za-z0-9_]{2,64}$' -or $SteamUser -eq 'anonymous') { throw 'Use a valid Steam account that owns the game.' }
    if (-not $SteamCmdPath) {
        $command = Get-Command steamcmd.exe -ErrorAction SilentlyContinue
        if ($command) { $SteamCmdPath = $command.Source }
        else { $SteamCmdPath = Get-WorkshopSteamCmdPath $repoRoot }
    }
    if (-not (Test-Path -LiteralPath $SteamCmdPath -PathType Leaf)) {
        throw 'SteamCMD not found. Extract the official SteamCMD Windows ZIP to .local/steamcmd, or pass -SteamCmdPath. See docs/STEAM_PUBLISHING.md.'
    }
    $SteamCmdPath = (Resolve-Path -LiteralPath $SteamCmdPath).Path
    if ([IO.Path]::GetFileName($SteamCmdPath) -ine 'steamcmd.exe') { throw 'SteamCmdPath must point to steamcmd.exe.' }
    @{SteamUser=$SteamUser} | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8NoBOM
    Set-Content -LiteralPath $vdfPath -Value $vdf -Encoding utf8NoBOM
    # Validate the persisted target before starting an external mutation.
    $null = Read-WorkshopIdentity $vdfPath
    if ($id -eq '0') { Set-Content -LiteralPath $pendingPath -Value 'Creation started; recover its Workshop ID before any retry.' }
    Write-Host "Uploading $modId v$($package.Version), target=$id. SteamCMD may request password / Steam Guard."
    & $SteamCmdPath '+login' $SteamUser '+workshop_build_item' $vdfPath '+quit' 2>&1 | Tee-Object -Variable steamOutput | Out-Host
    $exitCode = $LASTEXITCODE
    $output = @($steamOutput)
    $afterId = Read-WorkshopIdentity $vdfPath
    Complete-WorkshopUpload $exitCode ($output -join "`n") $id $afterId $pendingPath
    Write-Host "SteamCMD confirmed upload: https://steamcommunity.com/sharedfiles/filedetails/?id=$afterId"
    Write-Host 'Check the Workshop page and accept its agreement if prompted. Public visibility/subscriber download were not independently verified.'
} finally { $lock.Dispose() }
