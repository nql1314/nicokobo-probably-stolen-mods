[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bundle = Join-Path $repo 'mods/mechcore-protocol'
$sources = @('mechcore-protocol-synthesis-expansion', 'mechcore-protocol-module-matrix', 'aug-mechanical-ascension')
$assets = @{}
function Test-SameResource([IO.FileInfo]$Source, [IO.FileInfo]$Destination) {
    return $Destination.Exists -and $Source.Length -eq $Destination.Length -and [Linq.Enumerable]::SequenceEqual[byte](
        [IO.File]::ReadAllBytes($Source.FullName), [IO.File]::ReadAllBytes($Destination.FullName))
}

foreach ($name in $sources) {
    $source = Join-Path $repo ('mods/' + $name)
    $textures = Join-Path $source 'Textures'
    if (Test-Path -LiteralPath $textures) {
        foreach ($file in Get-ChildItem -LiteralPath $textures -Recurse -File) {
            if ($file.Name -in @('README', 'README.md', 'README.txt', 'change.log')) { continue }
            $relative = 'Textures/' + [IO.Path]::GetRelativePath($textures, $file.FullName).Replace('\', '/')
            if ($assets.ContainsKey($relative) -and !(Test-SameResource $assets[$relative] $file)) {
                throw "Conflicting component resource: $relative"
            }
            $assets[$relative] = $file
        }
    }
}
$expected = @{}
$boundary = [IO.Path]::GetFullPath($bundle).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($relative in ($assets.Keys | Sort-Object)) {
    $target = [IO.Path]::GetFullPath((Join-Path $bundle $relative))
    if (!$target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Resource target escapes the bundle' }
    $expected[$target] = $true
    $file = [IO.FileInfo]::new($target)
    if (!(Test-SameResource $assets[$relative] $file)) {
        $file.Directory.Create()
        Copy-Item -LiteralPath $assets[$relative].FullName -Destination $target -Force
    }
}
# Bundle textures are generated. Source assets remain in the components.
$generated = [IO.Path]::GetFullPath((Join-Path $bundle 'Textures'))
if (!$generated.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected generated directory' }
if (Test-Path -LiteralPath $generated) {
    foreach ($file in Get-ChildItem -LiteralPath $generated -Recurse -File) {
        if (!$expected.ContainsKey($file.FullName)) { Remove-Item -LiteralPath $file.FullName }
    }
}
Write-Host ("Mechcore resources: {0} texture files." -f $assets.Count)
