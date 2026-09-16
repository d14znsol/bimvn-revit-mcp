[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027')][string]$RevitVersion, [ValidateSet('Debug','Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tfm = if ([int]$RevitVersion -le 2020) { 'net47' } elseif ([int]$RevitVersion -le 2024) { 'net48' } elseif ([int]$RevitVersion -le 2026) { 'net8.0-windows' } else { 'net10.0-windows' }
$revitRoot = "C:\Program Files\Autodesk\Revit $RevitVersion"
$apiPath = Join-Path $revitRoot 'RevitAPI.dll'; $apiUiPath = Join-Path $revitRoot 'RevitAPIUI.dll'; $newtonsoftPath = Join-Path $revitRoot 'NewtonSoft.Json.dll'
dotnet build (Join-Path $root 'MCP.CoreRuntime\DSCons.RevitMcp.CoreRuntime.csproj') -c $Configuration -p:RevitVersion=$RevitVersion -p:RevitTargetFramework=$tfm -p:RevitApiPath=$apiPath -p:RevitApiUiPath=$apiUiPath -p:NewtonsoftPath=$newtonsoftPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Revit MCP CoreRuntime build failed.' }
$target = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\DSConsRevitMcp"
if (!(Test-Path (Join-Path $target 'DSCons.RevitMcp.dll'))) { throw 'DSCons Revit MCP is not installed for this Revit version. Use install-mcp only after explicit approval.' }
$source = Join-Path $root "MCP.CoreRuntime\bin\$Configuration\$tfm\DSCons.RevitMcp.CoreRuntime.dll"
if (!(Test-Path $source)) { throw "Missing CoreRuntime build output: $source" }
$runtimeTarget = Join-Path $target 'runtime'
New-Item -ItemType Directory -Force -Path $runtimeTarget | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $runtimeTarget 'DSCons.RevitMcp.CoreRuntime.dll') -Force
# Old V2 deployments copied Newtonsoft.Json 13 into this folder. Revit owns
# the compatible assembly (12 for 2023; 13 for 2025), so retaining that copy
# causes a startup binding conflict in Revit 2023.
$obsoleteNewtonsoft = Join-Path $runtimeTarget 'Newtonsoft.Json.dll'
if (Test-Path $obsoleteNewtonsoft) { Remove-Item -LiteralPath $obsoleteNewtonsoft -Force }
Write-Host "Published CoreRuntime for Revit $RevitVersion. The running add-in detects this changed runtime automatically; no Revit restart or ribbon click is required. 'Cập nhật Code' remains a manual fallback."
