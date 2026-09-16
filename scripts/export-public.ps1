[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputRoot)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) {
    throw "OutputRoot already exists; choose a new export directory: $output"
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

# This is the publication boundary. Keep it explicit: private ledgers, runtime
# evidence, customer inputs, generated binaries and local client state are not
# inferred from .gitignore and are never copied by this exporter.
$allow = @(
    '.github',
    '.gitignore',
    'AGENTS.md',
    'CLAUDE.md',
    'GEMINI.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'README.md',
    'START_HERE.md',
    'PROJECT.md',
    'global.json',
    'DSCons.RevitMcp.sln',
    'contracts',
    'domain',
    'docs\ARCHITECTURE.md',
    'docs\CLIENT-SETUP.md',
    'docs\COMPATIBILITY.md',
    'docs\GIOI-THIEU-MCP.md',
    'docs\REFERENCE-ALIGNMENT.md',
    'docs\SAFETY.md',
    'docs\MIGRATION.md',
    'docs\RUNTIME-TEST.md',
    'docs\TROUBLESHOOTING.md',
    'docs\MULTIVERSION-ROLLOUT.md',
    'docs\PACKAGING-CHECKLIST-VI.md',
    'docs\COMBINE-SHOP-COPILOT-v1.md',
    'docs\COMBINE-ISSUE-REPORT-TEMPLATE.md',
    'docs\COMBINE-RUNTIME-TEST.md',
    'docs\CAD-CHANGESET-REPORT-TEMPLATE.md',
    'docs\PDF-CAD-EVIDENCE-TEMPLATE.md',
    'docs\safety-and-audit-standard-v1.md',
    'docs\personal-quick-start-vi.md',
    'docs\FAMILY-EVIDENCE-V2.md',
    'docs\FAMILY-BUILD-REPORT-V2.md',
    'docs\FAMILY-BUILD-REPORT-TEMPLATE.md',
    'docs\FAMILY-PLATFORM-QUICK-START.md',
    'docs\FAMILY-PLATFORM-WAVE-1.md',
    'docs\FAMILY-QUALITY-PLATFORM.md',
    'docs\FAMILY-TEMPLATE-AUTO-RESOLUTION.md',
    'docs\MCP-RIBBON-CONTROLS.md',
    'docs\learning\README.md',
    'docs\learning\LEARNER_GUIDE.md',
    'docs\learning\60-MINUTE-MCP-PILOT.md',
    'docs\learning\templates',
    'integrations',
    'installer\README.md',
    'MCP-Server\README.md',
    'MCP-Server\src',
    'MCP-Server\package.json',
    'MCP-Server\package-lock.json',
    'MCP-Server\tsconfig.json',
    'MCP-Server\assets\ocr',
    'MCP\README.md',
    'MCP\Application.cs',
    'MCP\Commands',
    'MCP\Core',
    'MCP\DSCons.RevitMcp.addin',
    'MCP\DSCons.RevitMcp.csproj',
    'MCP.CoreRuntime\RevitMcpCoreRuntime.cs',
    'MCP.CoreRuntime\DSCons.RevitMcp.CoreRuntime.csproj',
    'scripts',
    'tests\DSCons.RevitMcp.Contracts.Tests',
    'tests\mcp_protocol_smoke.mjs',
    'tests\family_static_contract.py',
    'tests\family_platform_static_contract.py',
    'tests\family_evidence_test.mjs',
    'tests\v2_contract_check.py',
    'tests\combine_static_contract.py',
    'tests\knowledge_search_test.mjs',
    'tests\ribbon_toggle_static_contract.py',
    'tests\demo_workflow_static_contract.py',
    'tests\source_format_validation.py',
    'tests\multiversion_build_guards.ps1',
    'tests\learner_package_check.mjs'
) | Select-Object -Unique

$allowedExtensions = @('.cs','.csproj','.ts','.json','.md','.ps1','.py','.mjs','.toml','.addin','.example','.yml','.yaml')
foreach ($relative in $allow) {
    $source = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $source)) {
        throw "Public export allowlist source is missing: $relative"
    }
    $destination = Join-Path $output $relative
    if ((Get-Item -LiteralPath $source).PSIsContainer) {
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $rel = $file.FullName.Substring($source.Length + 1)
            if ($rel -match '(?i)(^|\\)(bin|obj|node_modules|build|__pycache__)(\\|$)') { continue }
            if ($file.Extension -notin $allowedExtensions) { continue }
            $destFile = Join-Path $destination $rel
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destFile) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destFile
        }
    }
    else {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

$forbiddenContent = '(?i)([A-Z]:\\Users\\[^\\\s]+|E:\\0\.|sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{20,}|github_pat_[a-zA-Z0-9_]{20,}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)'
$pathHits = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $relativePath = $_.FullName.Substring($output.Length + 1)
    $relativePath -match '(?i)(^|\\)(node_modules|bin|obj|inputs|knowledge|course-content|runtime|audit|artifacts|\.agents|\.agent|\.DSCons)(\\|$)' -or
    $relativePath -match '(?i)(^|\\)\.mcp\.json$' -or
    $relativePath -match '(?i)(RevitAPI|RevitAPIUI|Newtonsoft\.Json)\.dll$' -or
    $relativePath -match '(?i)\.(rvt|rfa)$'
}
if ($pathHits) {
    throw ('Public export contains forbidden files: ' + (($pathHits | ForEach-Object FullName) -join ', '))
}

$oversized = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object Length -gt 100MB
if ($oversized) {
    throw ('Public export contains files larger than 100 MB: ' + (($oversized | ForEach-Object FullName) -join ', '))
}

$scanFiles = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -ne '.gitignore' }
$hits = $scanFiles | Select-String -Pattern $forbiddenContent
if ($hits) {
    $hits | ForEach-Object { Write-Error ("Public export scan failed: " + $_.Path + ':' + $_.LineNumber) }
    throw 'Public export contains a forbidden secret, private key or local absolute path.'
}

$bom = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $bytes = [IO.File]::ReadAllBytes($_.FullName)
    $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
}
if ($bom) { throw 'Public export contains UTF-8 BOM; rewrite text files as UTF-8 without BOM.' }

foreach ($required in @(
    'LICENSE',
    'THIRD-PARTY-NOTICES.md',
    'PROJECT.md',
    '.github\workflows\source-ci.yml',
    'MCP-Server\src\index.ts',
    'MCP-Server\README.md',
    'MCP\README.md',
    'MCP\Commands\FamilyCommands.cs',
    'contracts\McpContracts.cs',
    'MCP\Core\CoreRuntimeManager.cs',
    'MCP\Core\McpRibbon.cs',
    'tests\demo_workflow_static_contract.py'
)) {
    if (!(Test-Path -LiteralPath (Join-Path $output $required))) {
        throw "Incomplete public source export: $required"
    }
}

$files = Get-ChildItem -LiteralPath $output -Recurse -File | Sort-Object FullName
$manifest = [ordered]@{
    product = 'DSCons Revit MCP public source export'
    generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    license = 'DSCons Revit MCP Educational Source License 1.0'
    excluded = @('.agents','.agent','.mcp.json','inputs','artifacts','bin','obj','node_modules','runtime/audit/session state','Revit API DLLs','models/RFA','course/reference archives')
    files = @($files | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($output.Length + 1).Replace('\','/')
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
            bytes = $_.Length
        }
    })
}
[IO.File]::WriteAllText(
    (Join-Path $output 'export-manifest.json'),
    ($manifest | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false)
)
Write-Host "Public source export created: $output"
