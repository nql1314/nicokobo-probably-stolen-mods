param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$')]
    [string]$Label,
    [string]$BaselineSources = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repo '_build/review-20261031'))
$runRoot = [IO.Path]::GetFullPath((Join-Path $outputRoot ('perf-' + $Label)))
if (-not $runRoot.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Performance output must stay under the review directory.'
}
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
if ($BaselineSources) { $BaselineSources = (Resolve-Path -LiteralPath $BaselineSources).ProviderPath }
$utf8 = New-Object Text.UTF8Encoding($false)
function EscapeXml([string]$value) { [Security.SecurityElement]::Escape($value) }
function ReplaceExactly([string]$source, [string]$before, [string]$after) {
    if ($source.IndexOf($before, [StringComparison]::Ordinal) -lt 0 -or $source.IndexOf($before, $source.IndexOf($before, [StringComparison]::Ordinal) + 1, [StringComparison]::Ordinal) -ge 0) {
        throw ('Fixture signature changed: ' + $before)
    }
    $source.Replace($before, $after)
}

foreach ($kind in @('Synthesis', 'Matrix')) {
    $directory = if ($kind -eq 'Synthesis') { 'synthesis-official-checks' } else { 'matrix-official-checks' }
    $project = Join-Path $repo ('tools/' + $directory + '/' + $kind + '.Official.Checks.csproj')
    $raw = & dotnet msbuild $project '-getItem:Compile,Reference' '-getProperty:TargetFramework,GameManaged'
    if ($LASTEXITCODE -ne 0) { throw ('MSBuild item evaluation failed for ' + $kind) }
    $evaluated = ($raw -join "`n") | ConvertFrom-Json
    $work = Join-Path $runRoot $kind
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $items = New-Object Text.StringBuilder
    $hashes = @()
    foreach ($item in $evaluated.Items.Compile) {
        $sourcePath = $item.FullPath
        if ($sourcePath -eq (Join-Path (Split-Path $project -Parent) 'Program.cs')) { continue }
        if ($BaselineSources -and (Split-Path $sourcePath -Leaf) -in @('ManufacturedEffectsRuntime.cs', 'MatrixTurbo.cs')) {
            $sourcePath = Join-Path $BaselineSources (Split-Path $sourcePath -Leaf)
        }
        $hashes += [PSCustomObject]@{ path = $sourcePath; sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash }
        if ((Split-Path $sourcePath -Leaf) -in @('DevicePorts.cs', 'Fixtures.cs', 'NativePorts.cs')) {
            $source = [IO.File]::ReadAllText($sourcePath)
            if ($kind -eq 'Synthesis' -and (Split-Path $sourcePath -Leaf) -eq 'DevicePorts.cs') {
                $source = ReplaceExactly $source 'public List<GameItem> FindAllItem() => Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Distinct().ToList();' 'public List<GameItem> FindAllItem() => PerfCounters.Capture(Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Distinct().ToList());'
            }
            if ($kind -eq 'Synthesis' -and (Split-Path $sourcePath -Leaf) -eq 'Fixtures.cs') {
                $source = ReplaceExactly $source 'public static bool IsItemOwned(GameItem item) => item.GetTagReadonly("owned").GetBool();' 'public static bool IsItemOwned(GameItem item) => PerfCounters.Owned(item.GetTagReadonly("owned").GetBool());'
            }
            if ($kind -eq 'Matrix' -and (Split-Path $sourcePath -Leaf) -eq 'NativePorts.cs') {
                $source = ReplaceExactly $source 'public List<GameItem> FindAllItem(bool isOwn = true) => Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Where(item => !isOwn || item.Owned).Distinct().ToList();' 'public List<GameItem> FindAllItem(bool isOwn = true) => PerfCounters.Capture(Items.SelectMany(item => new[] { item }.Concat(item.FindAllChildItems())).Where(item => !isOwn || item.Owned).Distinct().ToList());'
                $source = ReplaceExactly $source 'public static bool IsItemOwned(GameItem item) => item.Owned;' 'public static bool IsItemOwned(GameItem item) => PerfCounters.Owned(item.Owned);'
            }
            $sourcePath = Join-Path $work (Split-Path $sourcePath -Leaf)
            [IO.File]::WriteAllText($sourcePath, $source, $utf8)
        }
        [void]$items.AppendLine(('    <Compile Include="' + (EscapeXml $sourcePath) + '" />'))
    }
    foreach ($name in @('PerfCounters.cs', ($kind + 'Perf.cs'))) {
        [void]$items.AppendLine(('    <Compile Include="' + (EscapeXml (Join-Path $PSScriptRoot $name)) + '" />'))
    }
    foreach ($reference in $evaluated.Items.Reference) {
        [void]$items.AppendLine(('    <Reference Include="' + (EscapeXml $reference.Identity) + '"><HintPath>' + (EscapeXml $reference.HintPath) + '</HintPath></Reference>'))
    }
    $xml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>$($evaluated.Properties.TargetFramework)</TargetFramework>
    <LangVersion>latest</LangVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><CheckEolTargetFramework>false</CheckEolTargetFramework>
    <AssemblyName>$kind.Perf</AssemblyName><UseSharedCompilation>false</UseSharedCompilation>
    <TieredCompilation>false</TieredCompilation>
  </PropertyGroup>
  <ItemGroup>
$items
  </ItemGroup>
</Project>
"@
    $generatedProject = Join-Path $work ($kind + '.Perf.csproj')
    [IO.File]::WriteAllText($generatedProject, $xml, $utf8)
    $hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $work 'source-hashes.json') -Encoding UTF8
    $build = & dotnet build $generatedProject -c Release --nologo -v minimal 2>&1
    $build | Set-Content -LiteralPath (Join-Path $work 'build.log') -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { $build | Write-Output; throw ('Performance build failed for ' + $kind) }
    $dll = Join-Path $work ('bin/Release/' + $evaluated.Properties.TargetFramework + '/' + $kind + '.Perf.dll')
    & dotnet $dll (Join-Path $runRoot ($kind.ToLowerInvariant() + '.json'))
    if ($LASTEXITCODE -ne 0) { throw ('Performance/state checks failed for ' + $kind) }
}
Write-Output ('Source-linked performance results: ' + $runRoot)
