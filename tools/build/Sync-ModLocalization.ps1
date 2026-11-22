[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Package,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '../../mods'
$manifest = [xml](Get-Content -LiteralPath (Join-Path $root "$Package/manifest.xml") -Raw)
$table = Join-Path $root 'Localization.csv'
$rows = @(Import-Csv -LiteralPath $table -Encoding utf8 | Where-Object Package -CEQ $Package | Sort-Object Key -CaseSensitive)
if ($rows.Count -eq 0) { throw "No translations in $table for $Package" }

$requiredLocales = @('en', 'zh', 'ru', 'de', 'ja', 'fr', 'es', 'pl', 'zh-TW')
$columns = @($rows[0].PSObject.Properties.Name)
$locales = @($columns | Where-Object { $_ -cnotin @('Package', 'Key') })
$missing = @($requiredLocales | Where-Object { $_ -cnotin $locales })
if ($missing.Count) { throw "Missing localization columns: $($missing -join ', ')" }
$aliases = [ordered]@{ 'zh-CN' = 'zh'; 'zh-Hans' = 'zh'; 'zh-Hant' = 'zh-TW' }
foreach ($locale in $locales) {
    if ($locale -cnotmatch '^[a-zA-Z0-9]+(?:-[a-zA-Z0-9]+)*$') { throw "Invalid locale code: $locale" }
}
$locales = @($locales | Where-Object {
    $locale = $_
    $requiredLocales -ccontains $locale -or ($rows | Where-Object { ![string]::IsNullOrWhiteSpace($_.$locale) } | Select-Object -First 1)
})
$localeFiles = @($locales + @($aliases.Keys | Where-Object { $_ -notin $locales }))
$keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$formatPattern = '(?<!\{)\{[0-9]+(?:,-?[0-9]+)?(?::[^{}]*)?\}'
foreach ($row in $rows) {
    if ($row.Key -cnotmatch '^[a-zA-Z0-9_.-]+$' -or !$keys.Add($row.Key)) {
        throw "Invalid or duplicate localization key: $Package/$($row.Key)"
    }
    $expectedFormats = @([regex]::Matches($row.en, $formatPattern).Value | Sort-Object -Unique) -join ' '
    foreach ($locale in $locales) {
        $value = [string]$row.$locale
        if ($locale -cnotin $requiredLocales -and [string]::IsNullOrWhiteSpace($value)) { continue }
        if ([string]::IsNullOrWhiteSpace($value) -or $value.Contains([char]10) -or $value.Contains([char]13)) {
            throw "Empty or multiline translation: $Package/$($row.Key) ($locale). Use \n for line breaks."
        }
        $formats = @([regex]::Matches($value, $formatPattern).Value | Sort-Object -Unique) -join ' '
        if ($formats -cne $expectedFormats) { throw "Changed format arguments: $Package/$($row.Key) ($locale)" }
        if ($expectedFormats) {
            try { [void][Text.CompositeFormat]::Parse($value) }
            catch { throw "Invalid format string: $Package/$($row.Key) ($locale)" }
        }
    }
}

function Write-GeneratedFile([string]$Path, [string]$Text) {
    if (!(Test-Path -LiteralPath $Path) -or [IO.File]::ReadAllText($Path) -cne $Text) {
        [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
    }
}

$localization = Join-Path $OutputDirectory 'Localization'
New-Item -ItemType Directory -Path $localization -Force | Out-Null
foreach ($locale in $localeFiles) {
    $lines = foreach ($row in $rows) {
        $value = [string]$row.$locale
        if ([string]::IsNullOrWhiteSpace($value) -and $aliases.Contains($locale)) {
            $value = [string]$row.($aliases[$locale])
        }
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        # The native loader splits on the first comma and decodes quoted values.
        $row.Key + ',"' + $value.Replace('"', '""') + '"'
    }
    Write-GeneratedFile (Join-Path $localization "$locale.csv") (($lines -join [char]10) + [char]10)
}
# Some registrations need their labels before ModLoader has loaded the CSVs.
# The in-assembly copy comes from the same table as the native resources.
$source = [Text.StringBuilder]::new()
$jsonOptions = [Text.Json.JsonSerializerOptions]::new()
[void]$source.AppendLine('// Generated from mods/Localization.csv.')
[void]$source.AppendLine('#nullable enable')
[void]$source.AppendLine('using System;')
[void]$source.AppendLine('using System.Collections.Generic;')
[void]$source.AppendLine('namespace Nicokobo.ModUtilities;')
[void]$source.AppendLine('internal static partial class ModText')
[void]$source.AppendLine('{')
[void]$source.AppendLine('    static partial void FindAuthored(string modId, string key, string locale, ref string? value)')
[void]$source.AppendLine('    {')
[void]$source.AppendLine('        if (modId != "' + $manifest.Mod.ID + '") return;')
[void]$source.AppendLine('        if (AuthoredTables.TryGetValue(locale, out var texts) && texts.TryGetValue(key, out var text)) value = text;')
[void]$source.AppendLine('    }')
[void]$source.AppendLine('    private static readonly Dictionary<string, Dictionary<string, string>> AuthoredTables = new(StringComparer.OrdinalIgnoreCase)')
[void]$source.AppendLine('    {')
foreach ($locale in $locales) {
    [void]$source.AppendLine('        ["' + $locale + '"] = new()')
    [void]$source.AppendLine('        {')
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.$locale)) { continue }
        $literal = [Text.Json.JsonSerializer]::Serialize($row.$locale.Replace('\n', [string][char]10), [type][string], $jsonOptions)
        [void]$source.AppendLine('            ["' + $row.Key + '"] = ' + $literal + ',')
    }
    [void]$source.AppendLine('        },')
}
[void]$source.AppendLine('    };')
[void]$source.AppendLine('}')
Write-GeneratedFile (Join-Path $OutputDirectory 'ModText.g.cs') $source.ToString()
