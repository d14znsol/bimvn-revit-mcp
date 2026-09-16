[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027','All')][string]$RevitVersion,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$ApiDirectory
)

$ErrorActionPreference = 'Stop'
if ($ApiDirectory -and $RevitVersion -eq 'All') { throw 'ApiDirectory requires one explicit RevitVersion; never reuse one API bundle for multiple years.' }
$root = Split-Path -Parent $PSScriptRoot
$allVersions = @('2019','2020','2021','2022','2023','2024','2025','2026','2027')
$tfmByVersion = @{
    2019 = 'net47'; 2020 = 'net47'
    2021 = 'net48'; 2022 = 'net48'; 2023 = 'net48'; 2024 = 'net48'
    2025 = 'net8.0-windows'; 2026 = 'net8.0-windows'
    2027 = 'net10.0-windows'
}
$versions = if ($RevitVersion -eq 'All') {
    @($allVersions | Where-Object { Test-Path -LiteralPath "C:\Program Files\Autodesk\Revit $_\RevitAPI.dll" })
} else { @($RevitVersion) }
if ($versions.Count -eq 0) { throw 'No installed Revit API was detected. Pass an explicit installed year or install Revit first.' }
if ($RevitVersion -eq 'All') {
    $missingVersions = @($allVersions | Where-Object { $_ -notin $versions })
    if ($missingVersions.Count -gt 0) {
        Write-Warning ("Revit API not installed; skipped explicitly: {0}" -f ($missingVersions -join ', '))
    }
}

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}

# Validate the actual binary identity before any build. API reference bundles
# may be provided locally by an authorized Revit owner; they are not shipped.
foreach ($version in $versions) {
    $apiRoot = if ($ApiDirectory) { [IO.Path]::GetFullPath($ApiDirectory) } else { "C:\Program Files\Autodesk\Revit $version" }
    foreach ($name in @('RevitAPI.dll','RevitAPIUI.dll','NewtonSoft.Json.dll')) {
        $dependency = Join-Path $apiRoot $name
        if (!(Test-Path -LiteralPath $dependency)) { throw "Revit $version dependency missing: $dependency. Supply a licensed matching API bundle using -ApiDirectory." }
        if ($name -like 'RevitAPI*') {
            $identity = [Reflection.AssemblyName]::GetAssemblyName($dependency)
            if ($identity.Name -ne [IO.Path]::GetFileNameWithoutExtension($name) -or $identity.Version.Major -ne ([int]$version - 2000)) {
                throw "Wrong Revit API binary for $version : $identity"
            }
        }
    }
}

$contractsProject = Join-Path $root 'contracts\DSCons.RevitMcp.Contracts.csproj'
$loaderProject = Join-Path $root 'MCP\DSCons.RevitMcp.csproj'
$runtimeProject = Join-Path $root 'MCP.CoreRuntime\DSCons.RevitMcp.CoreRuntime.csproj'
$serverProject = Join-Path $root 'MCP-Server'
Invoke-Dotnet @('build', $contractsProject, '-c', $Configuration, '--nologo')
if (!(Get-Command npm -ErrorAction SilentlyContinue)) { throw 'npm is required to build the Node/TypeScript MCP server.' }
Push-Location $serverProject
try {
    if (!(Test-Path 'node_modules')) { & npm install --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' } }
    & npm run build; if ($LASTEXITCODE -ne 0) { throw 'npm run build failed.' }
}
finally { Pop-Location }

$contractsDll = Join-Path $root ("contracts\bin\{0}\netstandard2.0\DSCons.RevitMcp.Contracts.dll" -f $Configuration)
$manifestSource = Join-Path $root 'MCP\DSCons.RevitMcp.addin'

foreach ($version in $versions) {
    # Build sequentially: the same csproj switches target framework by RevitVersion,
    # so parallel builds would race on obj/project.assets.json.
    $tfm = $tfmByVersion[[int]$version]
    $revitRoot = if ($ApiDirectory) { [IO.Path]::GetFullPath($ApiDirectory) } else { "C:\Program Files\Autodesk\Revit $version" }
    $apiPath = Join-Path $revitRoot 'RevitAPI.dll'
    $apiUiPath = Join-Path $revitRoot 'RevitAPIUI.dll'
    $newtonsoftPath = Join-Path $revitRoot 'NewtonSoft.Json.dll'
    foreach ($requiredPath in @($apiPath, $apiUiPath, $newtonsoftPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) { throw "Revit $version dependency is missing: $requiredPath" }
    }
    $commonProps = @("-p:RevitVersion=$version", "-p:RevitTargetFramework=$tfm", "-p:RevitApiPath=$apiPath", "-p:RevitApiUiPath=$apiUiPath")
    $loaderArgs = @('build', $loaderProject, '-c', $Configuration) + $commonProps + @('--nologo')
    $runtimeArgs = @('build', $runtimeProject, '-c', $Configuration) + $commonProps + @("-p:NewtonsoftPath=$newtonsoftPath", '--nologo')
    Invoke-Dotnet $loaderArgs
    Invoke-Dotnet $runtimeArgs

    $output = Join-Path $root "artifacts\Revit$version"
    $artifactBoundary = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (![IO.Path]::GetFullPath($output).StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Artifact output escaped workspace artifacts directory.' }
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    $runtimeOutput = Join-Path $output 'runtime'
    New-Item -ItemType Directory -Force -Path $runtimeOutput | Out-Null

    $loaderSource = Join-Path $root "MCP\bin\$Configuration\$tfm"
    $coreSource = Join-Path $root "MCP.CoreRuntime\bin\$Configuration\$tfm"
    Copy-Item (Join-Path $loaderSource 'DSCons.RevitMcp.dll') $output -Force
    Copy-Item $contractsDll $output -Force
    Copy-Item (Join-Path $coreSource 'DSCons.RevitMcp.CoreRuntime.dll') $runtimeOutput -Force

    Copy-Item $manifestSource (Join-Path $output 'DSCons.RevitMcp.addin') -Force
}

Write-Host 'Build complete.'
Write-Host '  MCP Server: MCP-Server\build\index.js'
Write-Host ("  Revit loader/runtime: artifacts\{0}" -f (($versions | ForEach-Object { "Revit$_" }) -join ', '))
Write-Host '  Protocol smoke test: node tests\mcp_protocol_smoke.mjs'
