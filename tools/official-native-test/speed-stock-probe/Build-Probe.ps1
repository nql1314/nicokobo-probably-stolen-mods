[CmdletBinding()]
param([string]$GameDirectory = 'F:\SteamLibrary\steamapps\common\Probably Stolen')
$ErrorActionPreference = 'Stop'
$managed = Join-Path ([IO.Path]::GetFullPath($GameDirectory)) 'Probably Stolen_Data/Managed'
if (!(Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'Formal Mono game assemblies are missing.' }
& dotnet build (Join-Path $PSScriptRoot 'Probe.csproj') -c Release "-p:GameManaged=$managed" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Speed/stock probe build failed.' }
$package = Join-Path $PSScriptRoot 'package'
New-Item -ItemType Directory -Path $package -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/NativeSpeedStockProbe.dll') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination $package -Force
Write-Output $package
