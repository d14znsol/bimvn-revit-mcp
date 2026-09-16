[CmdletBinding()]
param([Parameter(Mandatory)][string]$McpServerRoot)

$ErrorActionPreference = 'Stop'
$server = [IO.Path]::GetFullPath($McpServerRoot)
$manifestPath = Join-Path $server 'assets\ocr\ocr-assets.manifest.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw "OCR manifest is missing: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$target = Join-Path $server 'assets\ocr'
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($asset in $manifest.assets) {
    $packageRoot = Join-Path $server ('node_modules\' + $asset.package.Split('@')[-1])
    if ($asset.package -like '@*/*@*') {
        $parts = $asset.package.Substring(1).Split('@')[0].Split('/')
        $packageRoot = Join-Path $server ('node_modules\@' + $parts[0] + '\' + $parts[1])
    }
    $source = Join-Path $packageRoot $asset.package_path
    if (!(Test-Path -LiteralPath $source)) { throw "Pinned OCR dependency asset is missing: $source. Run npm ci in MCP-Server first." }
    $actual = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ($actual -ne $asset.sha256) { throw "OCR checksum mismatch for $($asset.file). Lockfile/dependency is not the approved asset." }
    Copy-Item -LiteralPath $source -Destination (Join-Path $target $asset.file) -Force
    $copied = (Get-FileHash -LiteralPath (Join-Path $target $asset.file) -Algorithm SHA256).Hash
    if ($copied -ne $asset.sha256) { throw "OCR copy verification failed for $($asset.file)." }
}
Write-Host "OCR assets synchronized and checksum-verified: $target"
