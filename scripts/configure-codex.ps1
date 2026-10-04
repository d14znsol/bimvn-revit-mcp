[CmdletBinding()]
param([switch]$ConfirmConfigure, [switch]$Check, [switch]$Remove)
$ErrorActionPreference = 'Stop'
if (!$ConfirmConfigure -and !$Check) { throw 'Safety stop. Re-run with -Check to inspect, or -ConfirmConfigure to update/remove Codex global MCP configuration.' }
$root = Split-Path -Parent $PSScriptRoot; $server = Join-Path $root 'MCP-Server\build\index.js'
if (!(Test-Path $server)) { throw 'Build MCP-Server first: .\scripts\build-mcp.ps1' }
if (!(Get-Command codex -ErrorAction SilentlyContinue)) { throw 'Codex CLI not found.' }
if ($Check) {
    # `codex mcp list` only proves that an entry can be parsed. Inspect the
    # DSCons entry itself so a moved/deleted project path cannot yield a false
    # positive from student-setup's read-only Check mode.
    $raw = & codex mcp get dscons-revit-mcp --json 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Codex DSCons MCP entry is missing or could not be inspected.' }
    try { $entry = ($raw -join [Environment]::NewLine) | ConvertFrom-Json }
    catch { throw "Codex DSCons MCP entry returned invalid JSON: $($_.Exception.Message)" }
    $args = @($entry.transport.args)
    if ($entry.transport.type -ne 'stdio' -or $entry.transport.command -ne 'node' -or $args.Count -ne 1) {
        throw 'Codex DSCons MCP entry has an invalid transport. Expected: node <MCP-Server\\build\\index.js>.'
    }
    $configuredServer = [IO.Path]::GetFullPath([string]$args[0])
    $expectedServer = [IO.Path]::GetFullPath($server)
    if (!(Test-Path -LiteralPath $configuredServer)) {
        throw "Codex DSCons MCP entrypoint does not exist: $configuredServer"
    }
    if ($configuredServer -ine $expectedServer) {
        throw "Codex DSCons MCP entrypoint is stale. Expected: $expectedServer"
    }
    Write-Host "PASS Codex DSCons MCP entry: node $expectedServer"
    return
}
if ($Remove) { & codex mcp remove dscons-revit-mcp; if ($LASTEXITCODE -ne 0) { throw 'Codex DSCons MCP removal failed.' }; Write-Host 'Codex DSCons MCP entry removed.'; return }
& codex mcp remove dscons-revit-mcp 2>$null; & codex mcp add dscons-revit-mcp -- node $server
if ($LASTEXITCODE -ne 0) { throw 'Codex MCP registration failed.' }
Write-Host 'Codex now points to the DSCons MCP-Server. Restart the Codex session.'
