[CmdletBinding()]
param([switch]$ConfirmConfigure, [switch]$Check, [switch]$Remove)
$ErrorActionPreference = 'Stop'
if (!$ConfirmConfigure -and !$Check) { throw 'Safety stop. Re-run with -Check to inspect, or -ConfirmConfigure to update/remove Claude Code global MCP configuration.' }
$root = Split-Path -Parent $PSScriptRoot; $server = Join-Path $root 'MCP-Server\build\index.js'
if (!(Test-Path $server)) { throw 'Build MCP-Server first: .\scripts\build-mcp.ps1' }
if (!(Get-Command claude -ErrorAction SilentlyContinue)) { throw 'Claude Code CLI not found.' }
if ($Check) {
    $configPath = Join-Path $root '.mcp.json'
    if (!(Test-Path -LiteralPath $configPath)) { throw "Claude Code project MCP config is missing: $configPath" }
    $bytes = [IO.File]::ReadAllBytes($configPath)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw 'Claude Code project MCP config must be UTF-8 without BOM.' }
    try { $config = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json }
    catch { throw "Claude Code project MCP config is invalid JSON: $($_.Exception.Message)" }
    $entry = $config.mcpServers.'dscons-revit-mcp'
    if ($null -eq $entry) { throw 'Claude Code project MCP config is missing dscons-revit-mcp.' }
    if ($entry.command -ne 'node' -or @($entry.args).Count -lt 1) { throw 'Claude Code DSCons MCP command/args are invalid.' }
    $entrypoint = [IO.Path]::GetFullPath((Join-Path $root ([string]@($entry.args)[0])))
    if ($entrypoint -ne [IO.Path]::GetFullPath($server)) { throw "Claude Code DSCons MCP entrypoint mismatch: $entrypoint" }
    Write-Host "PASS Claude Code DSCons MCP entry: node $entrypoint"
    return
}
if ($Remove) { & claude mcp remove dscons-revit-mcp; if ($LASTEXITCODE -ne 0) { throw 'Claude Code DSCons MCP removal failed.' }; Write-Host 'Claude Code DSCons MCP entry removed.'; return }
& claude mcp remove dscons-revit-mcp 2>$null; & claude mcp add dscons-revit-mcp -- node $server
if ($LASTEXITCODE -ne 0) { throw 'Claude Code MCP registration failed.' }
Write-Host 'Claude Code now points to the DSCons MCP-Server. Start a new session.'
