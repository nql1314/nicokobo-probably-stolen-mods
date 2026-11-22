[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentPath,
    [Parameter(Mandatory)][string]$ProbePackagePath,
    [switch]$DeveloperMode
)
& (Join-Path $PSScriptRoot 'Update-IsolatedPackage.ps1') -EnvironmentPath $EnvironmentPath -PackagePath $ProbePackagePath -Role Probe -DeveloperMode:$DeveloperMode
