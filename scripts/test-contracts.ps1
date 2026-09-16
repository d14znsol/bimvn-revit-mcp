$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $projectRoot 'tests\DSCons.RevitMcp.Contracts.Tests\DSCons.RevitMcp.Contracts.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Contract tests failed.' }
