[CmdletBinding()]
param([switch]$ConfirmConfigure)

$ErrorActionPreference = 'Stop'
if (!$ConfirmConfigure) { throw 'Safety stop. Re-run with -ConfirmConfigure to update Claude Desktop MCP configuration.' }
$root = Split-Path -Parent $PSScriptRoot
$server = Join-Path $root 'MCP-Server\build\index.js'
if (!(Test-Path $server)) { throw 'Build MCP-Server first: .\scripts\build-mcp.ps1' }
$path = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
$directory = Split-Path $path -Parent
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$config = if (Test-Path $path) { Get-Content -Raw $path | ConvertFrom-Json -AsHashtable } else { @{} }
if (!$config.ContainsKey('mcpServers')) { $config.mcpServers = @{} }
$config.mcpServers.Remove('mcp-server-for-revit')
$config.mcpServers['dscons-revit-mcp'] = @{ command = 'node'; args = @($server); env = @{} }
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
Write-Host "Claude Desktop now points to DSCons MCP: $path. Fully quit and reopen Claude Desktop."
