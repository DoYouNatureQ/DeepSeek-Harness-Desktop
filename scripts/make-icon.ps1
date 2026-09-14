# 生成 DeepSeek Harness Desktop 应用图标(与手机 App 一致的白底蓝鲸)
# 路径均相对仓库根目录解析,可在任意位置克隆后直接运行。
param(
    [string]$SvgPath,
    [string]$OutIco,
    # 输出预览图目录(便于重新生成后查看效果)
    [string]$PreviewDir = (Join-Path $PSScriptRoot '..\.dsh-icon-preview'),
    [string]$Background = '#FFFFFFFF',   # 底色:白色
    [string]$Border = '#14000000',       # 浅描边,避免白底在浅色任务栏/背景上"隐身"
    # 注意:不要命名为 $Whale —— PowerShell 内置自动变量 $Whale(当前管道对象)会遮蔽它
    [string]$WhaleColor = '#4D6BFE'      # DeepSeek 品牌蓝
)

$ErrorActionPreference = 'Stop'

function ConvertTo-Argb([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 6) { $h = 'FF' + $h }
    if ($h.Length -ne 8) { throw "无效颜色(需 #RRGGBB 或 #AARRGGBB): $hex" }
    return [System.Windows.Media.Color]::FromArgb(
        [Convert]::ToInt32($h.Substring(0, 2), 16),
        [Convert]::ToInt32($h.Substring(2, 2), 16),
        [Convert]::ToInt32($h.Substring(4, 2), 16),
        [Convert]::ToInt32($h.Substring(6, 2), 16))
}

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

    # 圆角方形白色底(与手机 App 图标同款)
    $rect = New-Object System.Windows.Rect(0, 0, $size, $size)
    $radius = [Math]::Round($size * 0.225)
    $bg = New-Object System.Windows.Media.SolidColorBrush (ConvertTo-Argb $Background)
    # 16px 下 1px 描边会占满整个图标,直接省略,让鲸鱼保持清晰
    $borderColor = ConvertTo-Argb $Border
    $pen = if ($size -le 16) { $null } else { New-Object System.Windows.Media.Pen (New-Object System.Windows.Media.SolidColorBrush $borderColor), ([Math]::Max(1, $size / 128)) }
    $dc.DrawRoundedRectangle($bg, $pen, $rect, $radius, $radius)

    # 品牌蓝鲸鱼居中;小尺寸放大占比,避免细笔画塌陷成浅色
    $ratio = if ($size -le 20) { 0.74 } elseif ($size -le 32) { 0.68 } else { 0.60 }
    $targetW = $size * $ratio
    $scale = $targetW / $bounds.Width
    $whale = $geometry.Clone()
    $transform = New-Object System.Windows.Media.TransformGroup
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform(-$bounds.X, -$bounds.Y)))
    $transform.Children.Add((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $offsetX = ($size - $bounds.Width * $scale) / 2
    $offsetY = ($size - $bounds.Height * $scale) / 2
    $transform.Children.Add((New-Object System.Windows.Media.TranslateTransform($offsetX, $offsetY)))
    $whale.Transform = $transform
    $dc.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush (ConvertTo-Argb $WhaleColor)), $null, $whale)
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return , $ms.ToArray()
}

# 预览图:256px 主图 + 多尺寸拼版(在浅色/深色背景上确认可见性)
if (-not (Test-Path $PreviewDir)) { New-Item -ItemType Directory -Path $PreviewDir -Force | Out-Null }
$mainPreview = Join-Path $PreviewDir 'icon-256.png'
[System.IO.File]::WriteAllBytes($mainPreview, (Render-IconPng 256))
Write-Host "preview: $mainPreview"

$sheetSizes = @(256, 64, 48, 32, 16)
$sheet = New-Object System.Windows.Media.DrawingVisual
$sdc = $sheet.RenderOpen()
# 前半浅色底、后半深色底,检查白底图标在两种任务栏上的表现
$sdc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xF3, 0xF4, 0xF6))), $null,
    (New-Object System.Windows.Rect(0, 0, 512, 300)))
$sdc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x1C, 0x1C, 0x1E))), $null,
    (New-Object System.Windows.Rect(512, 0, 512, 300)))
$slotX = 210
$bigSize = 256
$bigTop = 22
$bigX = 26
$darkColX = 512
$darkBigX = $darkColX + $bigX

function Get-IconFrame([int]$size) {
    $png = Render-IconPng $size
    $ms = New-Object System.IO.MemoryStream(, $png)
    $decoder = New-Object System.Windows.Media.Imaging.PngBitmapDecoder($ms, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    return $decoder.Frames[0]
}

# 左侧 256px 主图(只画一次),右侧小尺寸竖直排列并按 256 的垂直中心对齐
$bigImg = Get-IconFrame $bigSize
$sdc.DrawImage($bigImg, (New-Object System.Windows.Rect($bigX, $bigTop, $bigSize, $bigSize)))
$sdc.DrawImage($bigImg, (New-Object System.Windows.Rect($darkBigX, $bigTop, $bigSize, $bigSize)))

foreach ($s in @(64, 48, 32, 16)) {
    $img = Get-IconFrame $s
    $top = $bigTop + ($bigSize - $s) / 2
    $darkSlotX = $darkColX + $slotX
    $sdc.DrawImage($img, (New-Object System.Windows.Rect($slotX, $top, $s, $s)))
    $sdc.DrawImage($img, (New-Object System.Windows.Rect($darkSlotX, $top, $s, $s)))
    $slotX += $s + 18
}
$sdc.Close()
$sheetBmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(1024, 300, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$sheetBmp.Render($sheet)
$sheetEnc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$sheetEnc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($sheetBmp))
$sheetPath = Join-Path $PreviewDir 'icon-sizes.png'
$sheetStream = [System.IO.File]::Create($sheetPath)
$sheetEnc.Save($sheetStream)
$sheetStream.Dispose()
Write-Host "preview sheet: $sheetPath"

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
