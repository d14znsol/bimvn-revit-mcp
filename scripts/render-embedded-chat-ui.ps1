param(
    [string]$RevitInstallPath = (Join-Path $env:ProgramFiles 'Autodesk\Revit 2023'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\ui-preview'),
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\artifacts\Revit2023\DSCons.RevitMcp.dll'),
    [string]$ContractsAssemblyPath = (Join-Path $PSScriptRoot '..\artifacts\Revit2023\DSCons.RevitMcp.Contracts.dll')
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactPath = [IO.Path]::GetFullPath($AssemblyPath)
$dependencyPaths = @(
    (Join-Path $RevitInstallPath 'RevitAPI.dll'),
    (Join-Path $RevitInstallPath 'RevitAPIUI.dll'),
    (Join-Path $RevitInstallPath 'NewtonSoft.Json.dll'),
    [IO.Path]::GetFullPath($ContractsAssemblyPath)
)

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml
foreach ($dependencyPath in $dependencyPaths) {
    if (-not (Test-Path -LiteralPath $dependencyPath)) { throw "Missing UI preview dependency: $dependencyPath" }
    [void][Reflection.Assembly]::LoadFrom($dependencyPath)
}
if (-not (Test-Path -LiteralPath $artifactPath)) { throw "Build Revit 2023 before rendering the UI preview." }
$assembly = [Reflection.Assembly]::LoadFrom($artifactPath)
$controlType = $assembly.GetType('DSCons.RevitMcp.Core.EmbeddedChat.EmbeddedChatPaneControl', $true)
$activityType = $assembly.GetType('DSCons.RevitMcp.Core.EmbeddedChat.ChatToolActivity', $true)
$addMessage = $controlType.GetMethod('AddMessage', [Reflection.BindingFlags]'Instance, NonPublic')
$addActivity = $controlType.GetMethod('AddToolActivity', [Reflection.BindingFlags]'Instance, NonPublic')
$dispose = $controlType.GetMethod('Dispose', [Reflection.BindingFlags]'Instance, Public')

if (-not (Test-Path -LiteralPath $OutputDirectory)) { [void](New-Item -ItemType Directory -Path $OutputDirectory) }

function Render-ChatPreview {
    param([int]$Width, [int]$Height, [int]$Dpi, [string]$Name, [bool]$WithMessages = $true, [bool]$WithActivity = $false)

    $control = [Activator]::CreateInstance($controlType, $true)
    if ($WithMessages) {
        [void]$addMessage.Invoke($control, @('assistant', 'Minh da doc dung project va view hien tai. Ban mo ta tuyen ong hoac cong viec can lam, minh se kiem tra du lieu truoc khi Preview.', $false))
        [void]$addMessage.Invoke($control, @('user', 'Ve giup toi mot doan ong gio 600x500, dai 2 m, cao do tim 2000 mm.', $false))
        [void]$addMessage.Invoke($control, @('system', 'Preview PASS - dang cho xac nhan. Chat dong y de thuc hien hoac huy de dung.', $false))
    }
    if ($WithActivity) {
        $complete = [Activator]::CreateInstance($activityType, $true)
        $complete.Tool = 'document_info'; $complete.Text = 'complete'; $complete.IsComplete = $true
        [void]$addActivity.Invoke($control, @($complete))
        $running = [Activator]::CreateInstance($activityType, $true)
        $running.Tool = 'bim_changeset_preview'; $running.Text = 'started'; $running.IsComplete = $false
        [void]$addActivity.Invoke($control, @($running))
        $toolField = $controlType.GetField('_toolDetails', [Reflection.BindingFlags]'Instance, NonPublic')
        $toolField.GetValue($control).IsExpanded = $true
    }

    $window = [Windows.Window]::new()
    $window.Width = $Width
    $window.Height = $Height
    $window.Content = $control
    $window.Background = [Windows.Media.Brushes]::White
    $window.WindowStyle = [Windows.WindowStyle]::None
    $window.ResizeMode = [Windows.ResizeMode]::NoResize
    $window.ShowInTaskbar = $false
    $window.ShowActivated = $false
    $window.Left = -10000
    $window.Top = -10000
    $window.Show()
    $window.UpdateLayout()

    $scale = $Dpi / 96.0
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]($Width * $scale), [int]($Height * $scale), $Dpi, $Dpi, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($window)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $outputPath = Join-Path $OutputDirectory "$Name.png"
    $stream = [IO.File]::Open($outputPath, [IO.FileMode]::Create)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    $window.Close()
    [void]$dispose.Invoke($control, @())
    $outputPath
}

$outputs = @(
    (Render-ChatPreview -Width 440 -Height 900 -Dpi 96 -Name 'chat-welcome-100' -WithMessages $false),
    (Render-ChatPreview -Width 440 -Height 900 -Dpi 120 -Name 'chat-tools-125' -WithActivity $true),
    (Render-ChatPreview -Width 320 -Height 900 -Dpi 96 -Name 'chat-compact-100'),
    (Render-ChatPreview -Width 440 -Height 900 -Dpi 120 -Name 'chat-docked-125'),
    (Render-ChatPreview -Width 680 -Height 900 -Dpi 144 -Name 'chat-wide-150')
)
$outputs | ForEach-Object { Write-Host "Rendered: $_" }
