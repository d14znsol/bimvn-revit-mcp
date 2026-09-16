[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027')]
    [string[]]$RevitVersions = @('2019','2020','2021','2022','2023','2024','2025','2026','2027'),
    [string]$ApiBundleRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "OutputRoot already exists; choose a new release directory to avoid overwriting learner data: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$requestedRevitVersions = @($RevitVersions | Sort-Object -Unique)
if ($requestedRevitVersions.Count -eq 0) { throw 'At least one Revit version is required.' }

function Get-ApiRoot([string]$year) {
    if ($ApiBundleRoot) { return Join-Path ([IO.Path]::GetFullPath($ApiBundleRoot)) $year }
    return "C:\Program Files\Autodesk\Revit $year"
}
function Get-ApiBundleInfo([string]$year) {
    $apiRoot = Get-ApiRoot $year
    $required = @('RevitAPI.dll','RevitAPIUI.dll','NewtonSoft.Json.dll')
    $missing = @($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $apiRoot $_)) })
    if ($missing.Count -gt 0) {
        return [pscustomobject]@{ status='skipped'; reason='missing_dependency'; missingDependencies=$missing; targetFramework=$null; revitApiVersion=$null; revitApiUiVersion=$null; newtonsoftVersion=$null }
    }
    $apiIdentity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $apiRoot 'RevitAPI.dll'))
    $apiUiIdentity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $apiRoot 'RevitAPIUI.dll'))
    $jsonIdentity = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $apiRoot 'NewtonSoft.Json.dll'))
    $expectedMajor = [int]$year - 2000
    if ($apiIdentity.Name -ne 'RevitAPI' -or $apiIdentity.Version.Major -ne $expectedMajor -or $apiUiIdentity.Name -ne 'RevitAPIUI' -or $apiUiIdentity.Version.Major -ne $expectedMajor) {
        return [pscustomobject]@{ status='skipped'; reason='wrong_assembly_identity'; missingDependencies=@(); targetFramework=$null; revitApiVersion=$apiIdentity.Version.ToString(); revitApiUiVersion=$apiUiIdentity.Version.ToString(); newtonsoftVersion=$jsonIdentity.Version.ToString() }
    }
    $tfm = switch ([int]$year) { {$_ -le 2020} { 'net47'; break } {$_ -le 2024} { 'net48'; break } {$_ -le 2026} { 'net8.0-windows'; break } default { 'net10.0-windows' } }
    return [pscustomobject]@{ status='available'; reason=$null; missingDependencies=@(); targetFramework=$tfm; revitApiVersion=$apiIdentity.Version.ToString(); revitApiUiVersion=$apiUiIdentity.Version.ToString(); newtonsoftVersion=$jsonIdentity.Version.ToString() }
}

$apiInfoByYear = @{}
foreach ($year in $requestedRevitVersions) { $apiInfoByYear[$year] = Get-ApiBundleInfo $year }
$availableRevitVersions = @($requestedRevitVersions | Where-Object { $apiInfoByYear[$_].status -eq 'available' })
$skippedRevitVersions = @($requestedRevitVersions | Where-Object { $apiInfoByYear[$_].status -ne 'available' })
if ($availableRevitVersions.Count -eq 0) { throw 'No requested Revit version has a complete matching API bundle.' }
if ($skippedRevitVersions.Count -gt 0) {
    Write-Warning ("Skipped Revit versions without a complete matching API bundle: {0}" -f ($skippedRevitVersions -join ', '))
}

foreach ($year in $availableRevitVersions) {
    $buildOptions = @{ RevitVersion=$year; Configuration=$Configuration }
    if ($ApiBundleRoot) { $buildOptions.ApiDirectory = Get-ApiRoot $year }
    & (Join-Path $PSScriptRoot 'build-mcp.ps1') @buildOptions
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Revit $year." }
}

function Copy-ItemSafe([string]$Relative, [string]$Destination) {
    $source = Join-Path $root $Relative
    if (!(Test-Path -LiteralPath $source)) { throw "Release source is missing: $Relative" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $Destination -Recurse -Force
}
foreach ($year in $availableRevitVersions) { Copy-ItemSafe "artifacts\Revit$year" (Join-Path $output "artifacts\Revit$year") }
Copy-ItemSafe 'MCP-Server\build' (Join-Path $output 'MCP-Server\build')
Copy-ItemSafe 'MCP-Server\package.json' (Join-Path $output 'MCP-Server\package.json')
Copy-ItemSafe 'MCP-Server\package-lock.json' (Join-Path $output 'MCP-Server\package-lock.json')
Copy-ItemSafe 'MCP-Server\assets\ocr\README.md' (Join-Path $output 'MCP-Server\assets\ocr\README.md')
Copy-ItemSafe 'MCP-Server\assets\ocr\ocr-assets.manifest.json' (Join-Path $output 'MCP-Server\assets\ocr\ocr-assets.manifest.json')
Push-Location (Join-Path $output 'MCP-Server')
try { & npm ci --omit=dev --ignore-scripts --no-audit --no-fund; if ($LASTEXITCODE -ne 0) { throw 'npm ci for the production learner release failed.' } }
finally { Pop-Location }
Copy-ItemSafe 'scripts\sync-ocr-assets.ps1' (Join-Path $output 'scripts\sync-ocr-assets.ps1')
& (Join-Path $output 'scripts\sync-ocr-assets.ps1') -McpServerRoot (Join-Path $output 'MCP-Server')
if ($LASTEXITCODE -ne 0) { throw 'The pinned offline OCR assets could not be copied and verified.' }
foreach ($script in @('student-setup.ps1','preflight-mcp.ps1','install-mcp.ps1','uninstall-mcp.ps1','configure-antigravity.ps1','configure-claude.ps1','configure-claude-desktop.ps1','configure-codex.ps1')) { Copy-ItemSafe "scripts\$script" (Join-Path $output "scripts\$script") }
foreach ($doc in @(
    'START_HERE.md',
    'README.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'docs\COMPATIBILITY.md',
    'docs\COMBINE-SHOP-COPILOT-v1.md',
    'docs\COMBINE-ISSUE-REPORT-TEMPLATE.md',
    'docs\COMBINE-RUNTIME-TEST.md',
    'docs\FAMILY-EVIDENCE-V2.md',
    'docs\FAMILY-BUILD-REPORT-V2.md',
    'docs\FAMILY-BUILD-REPORT-TEMPLATE.md',
    'docs\FAMILY-PLATFORM-QUICK-START.md',
    'docs\safety-and-audit-standard-v1.md',
    'docs\personal-quick-start-vi.md',
    'docs\learning\README.md',
    'docs\learning\LEARNER_GUIDE.md',
    'docs\learning\60-MINUTE-MCP-PILOT.md',
    'docs\FAMILY-TEMPLATE-AUTO-RESOLUTION.md',
    'docs\MCP-RIBBON-CONTROLS.md',
    'docs\learning\templates'
)) { Copy-ItemSafe $doc (Join-Path $output $doc) }

Copy-ItemSafe 'docs\MULTIVERSION-ROLLOUT.md' (Join-Path $output 'docs\MULTIVERSION-ROLLOUT.md')
foreach ($year in $availableRevitVersions) {
    foreach ($required in @('DSCons.RevitMcp.dll','DSCons.RevitMcp.Contracts.dll','DSCons.RevitMcp.addin','runtime\DSCons.RevitMcp.CoreRuntime.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $output "artifacts\Revit$year\$required"))) { throw "Incomplete learner artifact: $year/$required" }
    }
}
if (!(Test-Path -LiteralPath (Join-Path $output 'docs\learning\LEARNER_GUIDE.md'))) { throw 'Missing standalone learner guide.' }
$files = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName
$buildRecords = @($requestedRevitVersions | ForEach-Object {
    $info = $apiInfoByYear[$_]
    [ordered]@{ revitVersion=$_; targetFramework=$info.targetFramework; status=if($info.status -eq 'available'){'built'}else{'skipped'}; revitApiVersion=$info.revitApiVersion; revitApiUiVersion=$info.revitApiUiVersion; newtonsoftVersion=$info.newtonsoftVersion; missingDependencies=@($info.missingDependencies); reason=$info.reason }
})
$manifest = [ordered]@{ product='DSCons Revit MCP learner release'; generatedUtc=(Get-Date).ToUniversalTime().ToString('o'); configuration=$Configuration; requestedRevitVersions=$requestedRevitVersions; revitVersions=$availableRevitVersions; skippedRevitVersions=$skippedRevitVersions; revitBuilds=$buildRecords; nodeEntrypoint='MCP-Server/build/index.js'; files=@($files | ForEach-Object { [ordered]@{ path=$_.FullName.Substring($output.Length+1).Replace('\','/'); sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash; bytes=$_.Length } }) }
[IO.File]::WriteAllText((Join-Path $output 'release-manifest.json'), ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
Write-Host "Student release created: $output"
