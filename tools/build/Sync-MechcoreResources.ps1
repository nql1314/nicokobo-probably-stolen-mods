[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bundle = Join-Path $repo 'mods/mechcore-protocol'
$sources = @('mechcore-protocol-synthesis-expansion', 'mechcore-protocol-module-matrix', 'aug-mechanical-ascension')
$assets = @{}
foreach ($name in $sources) {
    $source = Join-Path $repo ('mods/' + $name)
    $textures = Join-Path $source 'Textures'
    if (Test-Path -LiteralPath $textures) {
        foreach ($file in Get-ChildItem -LiteralPath $textures -Recurse -File) {
            if ($file.Name -in @('README', 'README.md', 'README.txt', 'change.log')) { continue }
            $relative = 'Textures/' + [IO.Path]::GetRelativePath($textures, $file.FullName).Replace('\', '/')
            if ($assets.ContainsKey($relative) -and ![Collections.StructuralComparisons]::StructuralEqualityComparer.Equals(
                [IO.File]::ReadAllBytes($assets[$relative]), [IO.File]::ReadAllBytes($file.FullName))) {
                throw "Conflicting component resource: $relative"
            }
            $assets[$relative] = $file.FullName
        }
    }
}
$expected = @{}
function Resolve-GeneratedFile([string]$Relative) {
    $target = [IO.Path]::GetFullPath((Join-Path $bundle $Relative))
    $boundary = [IO.Path]::GetFullPath($bundle).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Resource target escapes the bundle' }
    $null = New-Item -ItemType Directory -Path (Split-Path $target) -Force
    $expected[$target] = $true
    return $target
}
foreach ($relative in ($assets.Keys | Sort-Object)) {
    $target = Resolve-GeneratedFile $relative
    Copy-Item -LiteralPath $assets[$relative] -Destination $target -Force
}
# Bundle textures are generated. Source assets remain in the components.
$generated = [IO.Path]::GetFullPath((Join-Path $bundle 'Textures'))
$boundary = [IO.Path]::GetFullPath($bundle).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$generated.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected generated directory' }
if (Test-Path -LiteralPath $generated) {
    foreach ($file in Get-ChildItem -LiteralPath $generated -Recurse -File) {
        if (!$expected.ContainsKey($file.FullName)) { Remove-Item -LiteralPath $file.FullName }
    }
}
Write-Host ("Mechcore resources: {0} texture files." -f $assets.Count)
