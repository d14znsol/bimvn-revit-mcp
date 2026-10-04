[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027')][string]$RevitVersion, [switch]$ConfirmUninstall, [switch]$RestoreLegacy)
$ErrorActionPreference = 'Stop'
if (!$ConfirmUninstall) { throw 'Safety stop. Re-run with -ConfirmUninstall only after every Revit window is closed.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close every Revit process before uninstalling MCP.' }
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
Remove-Item -LiteralPath (Join-Path $addinRoot 'DSCons.RevitMcp.addin') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $addinRoot 'DSConsRevitMcp') -Force -Recurse -ErrorAction SilentlyContinue
if ($RestoreLegacy) { $backup = Join-Path $env:LOCALAPPDATA "DSCons\RevitMcp\migration-backup\$RevitVersion"; foreach ($legacy in @('mcp-servers-for-revit.addin','revit-mcp.addin','revit_mcp_plugin')) { $source = Join-Path $backup $legacy; if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination $addinRoot -Recurse -Force } } }
Write-Host "DSCons MCP removed for Revit $RevitVersion. Restored legacy MCP: $RestoreLegacy"
