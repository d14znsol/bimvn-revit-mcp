[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$legacyDirectories = @('source', 'manifests', 'addin') | ForEach-Object { Join-Path $root $_ }
foreach ($directory in $legacyDirectories) {
    if (!(Test-Path -LiteralPath $directory)) { continue }
    $files = Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
    if ($files.Count -gt 0) { throw "Legacy scaffold contains non-generated files and was not touched: $directory" }
    if ($PSCmdlet.ShouldProcess($directory, 'Remove empty legacy scaffold')) { Remove-Item -LiteralPath $directory -Recurse -Force }
}
Write-Host 'Legacy scaffold cleanup complete.'
