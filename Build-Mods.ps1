[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$NoInstall,
    [string[]]$Mod,
    [switch]$PassThru
)

& (Join-Path $PSScriptRoot 'tools/build/Build-Mods.ps1') @PSBoundParameters
