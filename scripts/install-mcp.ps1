[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027')][string]$RevitVersion, [switch]$ConfirmInstall)

$ErrorActionPreference = 'Stop'
if (!$ConfirmInstall) { throw 'Safety stop. Re-run with -ConfirmInstall only after every Revit window is closed.' }
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close every Revit process before MCP migration.' }
$root = Split-Path -Parent $PSScriptRoot
$artifact = Join-Path $root "artifacts\Revit$RevitVersion"
if (!(Test-Path (Join-Path $artifact 'DSCons.RevitMcp.dll'))) { throw "Build first: .\scripts\build-mcp.ps1 -RevitVersion $RevitVersion" }
if (!(Test-Path (Join-Path $artifact 'DSCons.RevitMcp.addin'))) { throw "Build artifact is missing the canonical DSCons.RevitMcp.addin manifest." }
if (!(Test-Path (Join-Path $artifact 'runtime\DSCons.RevitMcp.CoreRuntime.dll'))) { throw "Build artifact is missing the CoreRuntime DLL." }
$addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$target = Join-Path $addinRoot 'DSConsRevitMcp'
$backup = Join-Path $env:LOCALAPPDATA "DSCons\RevitMcp\migration-backup\$RevitVersion"
New-Item -ItemType Directory -Force -Path $target,$backup | Out-Null

# Exact migration scope; it never enumerates or alters other add-ins/years.
foreach ($legacy in @('mcp-servers-for-revit.addin','revit-mcp.addin','revit_mcp_plugin')) {
    $source = Join-Path $addinRoot $legacy
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $backup -Recurse -Force; Remove-Item -LiteralPath $source -Recurse -Force }
}
Get-ChildItem -LiteralPath $artifact -Force | Where-Object { $_.Name -ne 'DSCons.RevitMcp.addin' } | Copy-Item -Destination $target -Recurse -Force
Copy-Item -LiteralPath (Join-Path $artifact 'DSCons.RevitMcp.addin') -Destination (Join-Path $addinRoot 'DSCons.RevitMcp.addin') -Force
if ($RevitVersion -in @('2023','2025')) {
    $serverEntrypoint = [IO.Path]::GetFullPath((Join-Path $root 'MCP-Server\build\index.js'))
    $capabilityManifest = Join-Path $target 'embedded-capabilities.json'
    if (!(Test-Path -LiteralPath $serverEntrypoint)) { throw "Revit $RevitVersion Chat AI requires MCP-Server\build\index.js. Build the Node server first." }
    if (!(Test-Path -LiteralPath $capabilityManifest)) { throw "Revit $RevitVersion Chat AI requires the generated embedded-capabilities.json in the build artifact." }
    $codexCommand = Get-Command codex -ErrorAction SilentlyContinue
    $claudeCommand = Get-Command claude -ErrorAction SilentlyContinue
    $antigravityCommand = Get-Command agy -ErrorAction SilentlyContinue
    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue
    $chatConfig = [ordered]@{
        schemaVersion = 2
        enabled = $true
        verifiedCodexCliVersion = '0.154.0-alpha.6.2'
        mcpServerEntrypoint = $serverEntrypoint
        codexCliPath = if ($codexCommand -and (Test-Path -LiteralPath $codexCommand.Source)) { [IO.Path]::GetFullPath($codexCommand.Source) } else { $null }
        claudeCliPath = if ($claudeCommand -and (Test-Path -LiteralPath $claudeCommand.Source)) { [IO.Path]::GetFullPath($claudeCommand.Source) } else { $null }
        antigravityCliPath = if ($antigravityCommand -and (Test-Path -LiteralPath $antigravityCommand.Source)) { [IO.Path]::GetFullPath($antigravityCommand.Source) } else { $null }
        nodeExecutablePath = if ($nodeCommand -and (Test-Path -LiteralPath $nodeCommand.Source)) { [IO.Path]::GetFullPath($nodeCommand.Source) } else { $null }
        capabilityManifestPath = [IO.Path]::GetFullPath($capabilityManifest)
    }
    [IO.File]::WriteAllText((Join-Path $target 'DSCons.RevitMcp.chat.json'), ($chatConfig | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
}
Write-Host "Installed DSCons Revit MCP for Revit $RevitVersion. Legacy MCP backup: $backup"
