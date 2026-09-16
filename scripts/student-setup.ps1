[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Check','Install','Uninstall')][string]$Mode,
    [Parameter(Mandatory)][ValidateSet('2019','2020','2021','2022','2023','2024','2025','2026','2027')][string]$RevitVersion,
    [Parameter(Mandatory)][ValidateSet('Antigravity','ClaudeCode','Codex')][string]$Client,
    [switch]$ConfirmInstall,
    [switch]$ConfirmConfigure,
    [switch]$ConfirmUninstall
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dataRoot = Join-Path $env:LOCALAPPDATA 'DSCons\RevitMcp\student-setup'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$ownershipPath = Join-Path $dataRoot ("ownership-{0}-{1}.json" -f $RevitVersion,$Client)
$artifact = Join-Path $root "artifacts\Revit$RevitVersion"
$server = Join-Path $root 'MCP-Server\build\index.js'
$serverRoot = Join-Path $root 'MCP-Server'
$packageJson = Join-Path $serverRoot 'package.json'
$packageLock = Join-Path $serverRoot 'package-lock.json'
$productionDependencyMarkers = @(
    (Join-Path $serverRoot 'node_modules\@modelcontextprotocol\sdk\package.json'),
    (Join-Path $serverRoot 'node_modules\pdfjs-dist\package.json'),
    (Join-Path $serverRoot 'node_modules\tesseract.js\package.json'),
    (Join-Path $serverRoot 'node_modules\@napi-rs\canvas\package.json')
)

function Write-Report([string]$Status, [string[]]$Rows) {
    Write-Host "DSCons Revit MCP - $Mode ($RevitVersion / $Client)" -ForegroundColor Cyan
    Write-Host "Trang thai: $Status"
    $Rows | ForEach-Object { Write-Host "- $_" }
}
function ExistsText([string]$Ok, [string]$Missing, [bool]$Condition) { if ($Condition) { return $Ok } return $Missing }
function Get-ConfigureScript {
    switch ($Client) {
        'Antigravity' { Join-Path $PSScriptRoot 'configure-antigravity.ps1' }
        'ClaudeCode' { Join-Path $PSScriptRoot 'configure-claude.ps1' }
        'Codex' { Join-Path $PSScriptRoot 'configure-codex.ps1' }
    }
}
function Get-ClientBackupPaths {
    switch ($Client) {
        'Antigravity' { return @((Join-Path $env:USERPROFILE '.gemini\config\mcp_config.json'),(Join-Path $env:USERPROFILE '.gemini\antigravity-ide\mcp_config.json'),(Join-Path $env:USERPROFILE '.gemini\antigravity\mcp_config.json')) }
        'ClaudeCode' { return @((Join-Path $env:USERPROFILE '.claude.json')) }
        'Codex' { return @((Join-Path $env:USERPROFILE '.codex\config.toml')) }
    }
}
function Backup-Path([string]$Source, [string]$DestinationRoot) {
    if (Test-Path -LiteralPath $Source) {
        New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
        Copy-Item -LiteralPath $Source -Destination (Join-Path $DestinationRoot ([IO.Path]::GetFileName($Source))) -Recurse -Force
    }
}
function Invoke-ClientCheck { & (Get-ConfigureScript) -Check }
function Test-ProductionNodeDependencies {
    foreach ($marker in $productionDependencyMarkers) {
        if (!(Test-Path -LiteralPath $marker)) { return $false }
    }
    return $true
}
function Ensure-NodeRuntime {
    if (!(Get-Command node -ErrorAction SilentlyContinue) -or !(Get-Command npm -ErrorAction SilentlyContinue)) {
        throw 'Node.js/npm is missing. Install a supported Node.js LTS release, then run Check again.'
    }
    if ((Test-ProductionNodeDependencies) -and (Test-Path -LiteralPath $server)) { return }
    if (!(Test-Path -LiteralPath $packageJson) -or !(Test-Path -LiteralPath $packageLock)) {
        throw 'MCP-Server package.json/package-lock.json is missing; dependencies cannot be installed safely.'
    }
    Push-Location $serverRoot
    try {
        & npm ci --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Automatic Node dependency installation failed.' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Automatic Node MCP build failed.' }
        & npm prune --omit=dev --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Production dependency cleanup failed.' }
    }
    finally { Pop-Location }
    if (!(Test-ProductionNodeDependencies) -or !(Test-Path -LiteralPath $server)) {
        throw 'Node MCP runtime is incomplete after automatic setup.'
    }
}

if ($Mode -eq 'Check') {
    $rows = [System.Collections.Generic.List[string]]::new()
    $nodeOk = $null -ne (Get-Command node -ErrorAction SilentlyContinue)
    $rows.Add((ExistsText 'Node: tim thay.' 'Node: THIEU.' $nodeOk))
    $npmOk = $null -ne (Get-Command npm -ErrorAction SilentlyContinue)
    $rows.Add((ExistsText 'npm: tim thay.' 'npm: THIEU.' $npmOk))
    $dependenciesOk = Test-ProductionNodeDependencies
    $rows.Add((ExistsText 'Node dependencies: san sang.' 'Node dependencies: THIEU; Install co the tu cai sau khi duoc xac nhan.' $dependenciesOk))
    $apiRoot = "C:\Program Files\Autodesk\Revit $RevitVersion"
    $apiPaths = @((Join-Path $apiRoot 'RevitAPI.dll'), (Join-Path $apiRoot 'RevitAPIUI.dll'), (Join-Path $apiRoot 'NewtonSoft.Json.dll'))
    $apiOk = @($apiPaths | Where-Object { !(Test-Path -LiteralPath $_) }).Count -eq 0
    if ($apiOk) {
        try {
            $apiOk = ([Reflection.AssemblyName]::GetAssemblyName($apiPaths[0]).Version.Major -eq ([int]$RevitVersion - 2000)) -and
                ([Reflection.AssemblyName]::GetAssemblyName($apiPaths[1]).Version.Major -eq ([int]$RevitVersion - 2000))
        } catch { $apiOk = $false }
    }
    $rows.Add((ExistsText ("Revit {0}: API/APIUI/Newtonsoft dung va dung nam." -f $RevitVersion) ("Revit {0}: API bundle thieu hoac sai nam." -f $RevitVersion) $apiOk))
    $familyTemplateRoot = "C:\ProgramData\Autodesk\RVT $RevitVersion\Family Templates"
    $mechanicalTemplate = @(Get-ChildItem -LiteralPath $familyTemplateRoot -Recurse -File -Filter '*.rft' -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('Metric Mechanical Equipment.rft','Mechanical Equipment.rft') } | Select-Object -First 1)
    $genericTemplate = @(Get-ChildItem -LiteralPath $familyTemplateRoot -Recurse -File -Filter '*.rft' -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('Metric Generic Model.rft','Generic Model.rft') } | Select-Object -First 1)
    if ($mechanicalTemplate.Count -gt 0) { $rows.Add(("Family template: tim thay Mechanical Equipment dung Revit {0}." -f $RevitVersion)) }
    elseif ($genericTemplate.Count -gt 0) { $rows.Add(("Family template: se dung Metric Generic Model Revit {0} va tu doi Family Category truoc khi tao hinh." -f $RevitVersion)) }
    else { $rows.Add(("Family template: THIEU ca Mechanical Equipment va Generic Model dung Revit {0}; can repair/install content." -f $RevitVersion)) }
    $serverOk = [bool](Test-Path -LiteralPath $server)
    $rows.Add((ExistsText 'MCP Server artifact: san sang.' 'MCP Server artifact: THIEU; DSCons can build release.' $serverOk))
    $artifactPaths = @(
        (Join-Path $artifact 'DSCons.RevitMcp.dll'),
        (Join-Path $artifact 'DSCons.RevitMcp.Contracts.dll'),
        (Join-Path $artifact 'DSCons.RevitMcp.addin'),
        (Join-Path $artifact 'runtime\DSCons.RevitMcp.CoreRuntime.dll')
    )
    $artifactOk = @($artifactPaths | Where-Object { !(Test-Path -LiteralPath $_) }).Count -eq 0
    $rows.Add((ExistsText ("Artifact Revit {0}: san sang." -f $RevitVersion) ("Artifact Revit {0}: THIEU." -f $RevitVersion) $artifactOk))
    $revit = @(Get-Process -Name Revit -ErrorAction SilentlyContinue)
    if ($revit.Count) {
        $pids = ($revit | Select-Object -ExpandProperty Id) -join ', '
        $rows.Add(("Revit dang mo (PID: {0}); Install/Uninstall se bi chan." -f $pids))
    } else { $rows.Add('Revit dang dong.') }
    try { Invoke-ClientCheck; $rows.Add(("Client {0}: entrypoint/config hop le." -f $Client)) }
    catch { $rows.Add(("Client {0}: CAN XU LY - {1}" -f $Client, $_.Exception.Message)) }
    Write-Report 'CHI-DOC - khong thay doi add-in hay cau hinh.' $rows
    return
}

if ($Mode -eq 'Install') {
    if (!$ConfirmInstall -or !$ConfirmConfigure) { throw 'Safety stop. Install requires both -ConfirmInstall and -ConfirmConfigure.' }
    if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Safety stop. Close every Revit process before Install.' }
    if (Test-Path -LiteralPath $ownershipPath) { throw 'Safety stop. Existing ownership record: reviewed upgrade or uninstall required; backup will not be overwritten.' }
    Ensure-NodeRuntime
    $backup = Join-Path $dataRoot ("backup-$stamp-$RevitVersion-$Client")
    $addinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
    Backup-Path (Join-Path $addinRoot 'DSCons.RevitMcp.addin') (Join-Path $backup 'addin')
    Backup-Path (Join-Path $addinRoot 'DSConsRevitMcp') (Join-Path $backup 'addin')
    $clientBackups = @()
    $backupIndex = 0
    Get-ClientBackupPaths | ForEach-Object {
        $slot = Join-Path $backup "client\$backupIndex"
        Backup-Path $_ $slot
        $clientBackups += [pscustomobject]@{ path=$_; existed=(Test-Path -LiteralPath $_); backup=(Join-Path $slot ([IO.Path]::GetFileName($_))) }
        $backupIndex++
    }
    & (Join-Path $PSScriptRoot 'install-mcp.ps1') -RevitVersion $RevitVersion -ConfirmInstall
    & (Get-ConfigureScript) -ConfirmConfigure
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null
    $owned = [ordered]@{ revitVersion=$RevitVersion; client=$Client; createdUtc=(Get-Date).ToUniversalTime().ToString('o'); backup=$backup; manifest=(Join-Path $addinRoot 'DSCons.RevitMcp.addin'); addinDirectory=(Join-Path $addinRoot 'DSConsRevitMcp'); artifactSha256=(Get-FileHash (Join-Path $artifact 'DSCons.RevitMcp.dll') -Algorithm SHA256).Hash }
    $owned.schemaVersion = 2
    $owned.manifestSha256 = (Get-FileHash -LiteralPath $owned.manifest -Algorithm SHA256).Hash
    $owned.clientBackups = @($clientBackups | ForEach-Object { $_ | Add-Member -NotePropertyName installedSha256 -NotePropertyValue $(if (Test-Path -LiteralPath $_.path) { (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash } else { $null }) -PassThru })
    $owned.files = @(Get-ChildItem -LiteralPath $owned.addinDirectory -Recurse -File | ForEach-Object { [pscustomobject]@{path=$_.FullName; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
    [IO.File]::WriteAllText($ownershipPath, ($owned | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Write-Report 'DA CAI - hay mo Revit va chay prompt kiem tra ket noi.' @("Backup: $backup", "Ownership: $ownershipPath", 'Khong co Project/model nao bi Save hoac Sync.')
    return
}

if (!$ConfirmUninstall) { throw 'Safety stop. Uninstall requires -ConfirmUninstall.' }
if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Safety stop. Close every Revit process before Uninstall.' }
if (!(Test-Path -LiteralPath $ownershipPath)) { throw 'Safety stop. No DSCons student ownership record exists for this Revit version/client; nothing was removed.' }
$owned = Get-Content -LiteralPath $ownershipPath -Raw | ConvertFrom-Json
if ($owned.schemaVersion -ne 2) { throw 'Safety stop. Legacy ownership needs reviewed migration; nothing removed.' }
$expectedAddinRoot = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
if ($owned.addinDirectory -ne (Join-Path $expectedAddinRoot 'DSConsRevitMcp') -or $owned.manifest -ne (Join-Path $expectedAddinRoot 'DSCons.RevitMcp.addin')) { throw 'Safety stop. Ownership target mismatch.' }
if (!(Test-Path -LiteralPath $owned.manifest) -or (Get-FileHash -LiteralPath $owned.manifest -Algorithm SHA256).Hash -ne $owned.manifestSha256) { throw 'Safety stop. Manifest changed; nothing removed.' }
$allowedClientPaths = @(Get-ClientBackupPaths)
foreach ($entry in $owned.clientBackups) {
    if ($entry.path -notin $allowedClientPaths) { throw 'Safety stop. Unexpected client restore target.' }
    $currentHash = if (Test-Path -LiteralPath $entry.path) { (Get-FileHash -LiteralPath $entry.path -Algorithm SHA256).Hash } else { $null }
    if ($currentHash -ne $entry.installedSha256) { throw 'Safety stop. Client configuration changed after install; reviewed entry-level restore required. Nothing removed.' }
    if ($entry.existed -and !(Test-Path -LiteralPath $entry.backup)) { throw 'Safety stop. Client backup missing; nothing removed.' }
}
$currentFiles = @(Get-ChildItem -LiteralPath $owned.addinDirectory -Recurse -File)
if ($currentFiles.Count -ne @($owned.files).Count) { throw 'Safety stop. Add-in file inventory changed; nothing removed.' }
foreach ($file in $currentFiles) {
    $entry = @($owned.files | Where-Object path -eq $file.FullName)
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $entry[0].sha256) { throw 'Safety stop. Add-in file ownership changed; nothing removed.' }
}
& (Join-Path $PSScriptRoot 'uninstall-mcp.ps1') -RevitVersion $RevitVersion -ConfirmUninstall
foreach ($entry in $owned.clientBackups) {
    if ($entry.existed) { Copy-Item -LiteralPath $entry.backup -Destination $entry.path -Force }
    elseif (Test-Path -LiteralPath $entry.path) { Remove-Item -LiteralPath $entry.path -Force }
}
if (Test-Path -LiteralPath (Join-Path $owned.backup 'addin')) { Get-ChildItem -LiteralPath (Join-Path $owned.backup 'addin') -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion") -Recurse -Force } }
Remove-Item -LiteralPath $ownershipPath -Force
Write-Report 'DA GO trong pham vi DSCons - entry/add-in khong lien quan duoc giu nguyen.' @("Da khoi phuc backup neu co: $($owned.backup)", 'Khong co Project/model nao bi thay doi.')
