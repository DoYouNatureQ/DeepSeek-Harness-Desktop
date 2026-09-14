# 生成 DeepSeek Harness Desktop 应用图标(与手机 App 一致的蓝底白鲸)
# 路径均相对仓库根目录解析,可在任意位置克隆后直接运行。
param(
    [string]$SvgPath,
    [string]$OutIco,
    [string]$PreviewPath = "$env:TEMP\dsh-icon-preview.png"
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot = <repo>\scripts,向上一级即仓库根
$root = Split-Path -Parent $PSScriptRoot
if (-not $SvgPath) { $SvgPath = Join-Path $root 'src\DeepSeekHarness.Desktop\Assets\deepseek.svg' }
if (-not $OutIco) { $OutIco = Join-Path $root 'src\DeepSeekHarness.Desktop\Assets\app.ico' }

if (-not (Test-Path $SvgPath)) { throw "未找到图标源 SVG: $SvgPath" }
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$svg = [System.IO.File]::ReadAllText($SvgPath)
if ($svg -notmatch '\sd="([^"]+)"') { throw "未在 SVG 中找到路径" }
$pathData = $matches[1]

$geometry = [System.Windows.Media.Geometry]::Parse($pathData)
$bounds = $geometry.Bounds
Write-Host "whale bounds: $($bounds.Width) x $($bounds.Height)"

function Render-IconPng([int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()

    # 圆角方形蓝色底(手机 App 图标风格)
    $rect = New-Object System.Windows.Rect(0, 0, $size, $size)
    $radius = [Math]::Round($size * 0.225)
    $bg = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x4D, 0x6B, 0xFE))
    $dc.DrawRoundedRectangle($bg, $null, $rect, $radius, $radius)

    # 白色鲸鱼居中
    $targetW = $size * 0.60
    $scale = $targetW / $bounds.Width
    $whale = $geometry.Clone()
    $transform = New-Object System.Windows.Media.TransformGroup
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform(-$bounds.X, -$bounds.Y)))
    $transform.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $offsetX = ($size - $bounds.Width * $scale) / 2
    $offsetY = ($size - $bounds.Height * $scale) / 2
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform($offsetX, $offsetY)))
    $whale.Transform = $transform
    $dc.DrawGeometry([System.Windows.Media.Brushes]::White, $null, $whale)
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return , $ms.ToArray()
}

# 预览图
[System.IO.File]::WriteAllBytes($PreviewPath, (Render-IconPng 256))
Write-Host "preview: $PreviewPath"

# 多尺寸 ICO
$sizes = @(256, 128, 64, 48, 32, 16)
$images = @()
foreach ($s in $sizes) { $images += , (Render-IconPng $s) }

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([UInt16]0)          # reserved
$writer.Write([UInt16]1)          # type: icon
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $images[$i]
    $writer.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $writer.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $writer.Write([Byte]0)        # palette
    $writer.Write([Byte]0)        # reserved
    $writer.Write([UInt16]1)      # planes
    $writer.Write([UInt16]32)     # bit count
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $writer.Write($data) }
$writer.Flush()

[System.IO.File]::WriteAllBytes($OutIco, $ico.ToArray())
$writer.Dispose()
Write-Host "icon written: $OutIco ($((Get-Item $OutIco).Length) bytes)"
