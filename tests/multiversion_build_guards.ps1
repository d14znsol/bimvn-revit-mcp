$ErrorActionPreference = 'Stop'
$builder = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\build-mcp.ps1'
try {
    & $builder -RevitVersion All -ApiDirectory $PSScriptRoot
    throw 'FAIL: ambiguous bundle accepted'
} catch { if ($_.Exception.Message -notmatch 'requires one explicit RevitVersion') { throw } }
try {
    & $builder -RevitVersion 2020 -ApiDirectory $PSScriptRoot
    throw 'FAIL: missing bundle accepted'
} catch { if ($_.Exception.Message -notmatch 'dependency missing') { throw } }
$text = [IO.File]::ReadAllText($builder)
foreach ($year in 2019..2027) {
    if ($text -notmatch "$year = 'net") { throw "Missing target mapping: $year" }
}
$expected = @{
    2019='net47'; 2020='net47'; 2021='net48'; 2022='net48'; 2023='net48'; 2024='net48';
    2025='net8.0-windows'; 2026='net8.0-windows'; 2027='net10.0-windows'
}
foreach ($pair in $expected.GetEnumerator()) {
    if ($text -notmatch "$($pair.Key) = '$($pair.Value)'") { throw "Wrong target mapping: $($pair.Key)" }
}
$sourceRoot = Split-Path -Parent $PSScriptRoot
$combine = [IO.File]::ReadAllText((Join-Path $sourceRoot 'MCP\Commands\CombineCommands.cs'))
if ($combine -match '\.ToHashSet\(') { throw 'Combine source uses Enumerable.ToHashSet, which is unavailable on the Revit 2019/2020 net47 target.' }
Write-Output 'PASS 2019-2027 build mappings and fail-closed bundle guards. No runtime claim.'
