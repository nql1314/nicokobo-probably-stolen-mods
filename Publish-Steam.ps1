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
    [string]$GameDir
)

& (Join-Path $PSScriptRoot 'tools/publish/Publish-Steam.ps1') @PSBoundParameters
