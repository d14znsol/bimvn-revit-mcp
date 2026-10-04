[CmdletBinding()]
param([switch]$ConfirmConfigure, [switch]$Check, [switch]$Remove)
$ErrorActionPreference = 'Stop'
if (!$ConfirmConfigure -and !$Check) { throw 'Safety stop. Re-run with -Check to validate, or -ConfirmConfigure to update/remove Google Antigravity MCP configuration.' }
$root = Split-Path -Parent $PSScriptRoot; $server = Join-Path $root 'MCP-Server\build\index.js'
if (!(Test-Path $server)) { throw 'Build MCP-Server first: .\scripts\build-mcp.ps1' }
$paths = @(
    (Join-Path $env:USERPROFILE '.gemini\config\mcp_config.json'),
    (Join-Path $env:USERPROFILE '.gemini\antigravity-ide\mcp_config.json'),
    (Join-Path $env:USERPROFILE '.gemini\antigravity\mcp_config.json')
)

function Test-AntigravityConfig {
    param([string]$Path)
    if (!(Test-Path $Path)) { throw "Missing Antigravity MCP configuration: $Path" }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "Invalid UTF-8 BOM in $Path. Antigravity's Node JSON.parse rejects this file; rerun with -ConfirmConfigure."
    }
    try { $config = ([System.Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json) }
    catch { throw "Invalid JSON in ${Path}: $($_.Exception.Message)" }
    $entry = $config.mcpServers.'dscons-revit-mcp'
    if ($null -eq $entry) { throw "Missing dscons-revit-mcp entry in $Path" }
    if ($entry.command -ne 'node' -or $entry.args.Count -ne 1 -or $entry.args[0] -ne $server) {
        throw "Stale or invalid dscons-revit-mcp entry in $Path. Expected: node $server"
    }
    Write-Host "PASS $Path"
}

if ($Check) {
    foreach ($path in $paths) { Test-AntigravityConfig -Path $path }
    Write-Host 'Antigravity MCP configuration is valid. Refresh MCP Servers or restart Antigravity.'
    return
}

foreach ($path in $paths) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    $content = if (Test-Path $path) { Get-Content -Raw $path } else { '' }
    $config = if ([string]::IsNullOrWhiteSpace($content)) { [PSCustomObject]@{} } else { try { $content | ConvertFrom-Json } catch { [PSCustomObject]@{} } }
    if ($null -eq $config) { $config = [PSCustomObject]@{} }
    if ($null -eq $config.mcpServers) {
        $config | Add-Member -NotePropertyName 'mcpServers' -NotePropertyValue ([PSCustomObject]@{}) -Force
    }
    if ($Remove) {
        if ($config.mcpServers.PSObject.Properties['dscons-revit-mcp']) {
            $config.mcpServers.PSObject.Properties.Remove('dscons-revit-mcp')
        }
        $json = $config | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
        Write-Host "Removed DSCons MCP entry from $path"
        continue
    }
    if ($config.mcpServers.PSObject.Properties['dscons-revit-mcp']) {
        $config.mcpServers.PSObject.Properties.Remove('dscons-revit-mcp')
    }
    $entry = [PSCustomObject]@{ command = 'node'; args = @($server) }
    $config.mcpServers | Add-Member -NotePropertyName 'dscons-revit-mcp' -NotePropertyValue $entry -Force
    # Windows PowerShell's Set-Content -Encoding UTF8 emits a BOM. Antigravity
    # uses Node JSON.parse, which rejects that leading character.
    $json = $config | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Updated $path"
}
if ($Remove) { Write-Host 'Google Antigravity DSCons MCP entry removed; unrelated entries were preserved.' }
else { Write-Host 'Google Antigravity now points to the DSCons MCP-Server. Reload MCP Servers.' }
