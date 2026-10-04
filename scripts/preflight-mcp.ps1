[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027','All')][string]$RevitVersion
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$allVersions = @('2019','2020','2021','2022','2023','2024','2025','2026','2027')
$versions = if ($RevitVersion -eq 'All') {
    @($allVersions | Where-Object { Test-Path -LiteralPath "C:\Program Files\Autodesk\Revit $_\RevitAPI.dll" })
} else { @($RevitVersion) }
if ($versions.Count -eq 0) { throw 'No installed Revit API was detected for the requested preflight.' }
if ($RevitVersion -eq 'All') {
    $missingVersions = @($allVersions | Where-Object { $_ -notin $versions })
    if ($missingVersions.Count -gt 0) {
        Write-Warning ("Revit API not installed; not checked: {0}" -f ($missingVersions -join ', '))
    }
}
$failed = $false

function Check-Path([string]$Path, [string]$Label) {
    if (Test-Path -LiteralPath $Path) { Write-Host "PASS  $Label" -ForegroundColor Green }
    else { Write-Host "MISS  $Label ($Path)" -ForegroundColor Red; $script:failed = $true }
}
function Check-ApiBundle([string]$Version) {
    $rootPath = "C:\Program Files\Autodesk\Revit $Version"
    $paths = @((Join-Path $rootPath 'RevitAPI.dll'), (Join-Path $rootPath 'RevitAPIUI.dll'), (Join-Path $rootPath 'NewtonSoft.Json.dll'))
    $missing = @($paths | Where-Object { !(Test-Path -LiteralPath $_) })
    if ($missing.Count -gt 0) { Write-Host "MISS  Revit $Version API bundle: $($missing -join ', ')" -ForegroundColor Red; $script:failed = $true; return }
    try {
        $expectedMajor = [int]$Version - 2000
        $api = [Reflection.AssemblyName]::GetAssemblyName($paths[0]); $ui = [Reflection.AssemblyName]::GetAssemblyName($paths[1])
        if ($api.Name -ne 'RevitAPI' -or $ui.Name -ne 'RevitAPIUI' -or $api.Version.Major -ne $expectedMajor -or $ui.Version.Major -ne $expectedMajor) { throw "assembly identity $($api.FullName) / $($ui.FullName)" }
        Write-Host "PASS  Revit $Version API bundle ($($api.Version), $($ui.Version))" -ForegroundColor Green
    } catch { Write-Host "MISS  Revit $Version API bundle identity: $($_.Exception.Message)" -ForegroundColor Red; $script:failed = $true }
}

Write-Host 'DSCons Revit MCP preflight (read-only)' -ForegroundColor Cyan
$dotnet10 = if (Get-Command dotnet -ErrorAction SilentlyContinue) { @(& dotnet --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }).Count -gt 0 } else { $false }
if ($dotnet10) { Write-Host 'PASS  .NET 10 SDK' -ForegroundColor Green } else { Write-Host 'MISS  .NET 10 SDK (required by the 2027 build baseline)' -ForegroundColor Red; $failed = $true }
$revit = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
if ($revit.Count -gt 0) {
    Write-Host "INFO  Revit process(es) running: $($revit.Id -join ', ')" -ForegroundColor Yellow
    Write-Host 'INFO  This preflight does not install, uninstall, close or modify Revit.' -ForegroundColor Yellow
} else { Write-Host 'INFO  No Revit process is running.' -ForegroundColor Yellow }

Check-Path (Join-Path $root 'MCP-Server\build\index.js') 'Node MCP Server artifact'
foreach ($version in $versions) {
    Check-ApiBundle $version
    $artifact = Join-Path $root "artifacts\Revit$version"
    Check-Path (Join-Path $artifact 'DSCons.RevitMcp.addin') "Revit $version manifest artifact"
    Check-Path (Join-Path $artifact 'DSCons.RevitMcp.dll') "Revit $version loader artifact"
    Check-Path (Join-Path $artifact 'DSCons.RevitMcp.Contracts.dll') "Revit $version internal loader contract artifact"
    Check-Path (Join-Path $artifact 'runtime\DSCons.RevitMcp.CoreRuntime.dll') "Revit $version CoreRuntime artifact"

    $addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$version"
    $manifest = Join-Path $addinRoot 'DSCons.RevitMcp.addin'
    if (Test-Path -LiteralPath $manifest) {
        Write-Host "INFO  Revit $version DSCons manifest is installed." -ForegroundColor Yellow
        Check-Path (Join-Path $addinRoot 'DSConsRevitMcp\DSCons.RevitMcp.dll') "Revit $version installed loader"
        Check-Path (Join-Path $addinRoot 'DSConsRevitMcp\runtime\DSCons.RevitMcp.CoreRuntime.dll') "Revit $version installed CoreRuntime"
    } else {
        Write-Host "INFO  Revit $version DSCons manifest is not installed." -ForegroundColor Yellow
    }
    foreach ($legacy in @('mcp-servers-for-revit.addin','revit-mcp.addin','revit_mcp_plugin')) {
        if (Test-Path -LiteralPath (Join-Path $addinRoot $legacy)) {
            Write-Host "INFO  Revit $version legacy artifact present: $legacy" -ForegroundColor Yellow
        }
    }
}

$session = Join-Path $env:LOCALAPPDATA 'DSCons\RevitMcp\session.json'
if (Test-Path -LiteralPath $session) {
    try {
        $data = Get-Content -LiteralPath $session -Raw | ConvertFrom-Json
        $pidAlive = Get-Process -Id ([int]$data.processId) -ErrorAction SilentlyContinue
        if (!$pidAlive) { Write-Host 'WARN  Session file exists but its process is no longer alive.' -ForegroundColor Yellow }
        else { Write-Host "INFO  Active DSCons session: PID $($data.processId), port $($data.port)." -ForegroundColor Yellow }
    } catch { Write-Host "WARN  Session file is not valid JSON: $session" -ForegroundColor Yellow }
} else { Write-Host 'INFO  No active DSCons session.json.' -ForegroundColor Yellow }

if ($failed) { Write-Error 'Preflight failed: required build artifacts are missing.' }
else { Write-Host 'Preflight passed.' -ForegroundColor Green }
