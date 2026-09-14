# 构建 DeepSeek Harness Desktop
# 用法: powershell -ExecutionPolicy Bypass -File scripts\build.ps1
# 注意: 本文件为 UTF-8 with BOM,请勿另存为无 BOM(Windows PowerShell 5.1 会按 ANSI 解码导致中文损坏)。
$ErrorActionPreference = 'Stop'
# $PSScriptRoot = <repo>\scripts,向上一级即仓库根;因此本脚本与调用时的当前目录无关。
$root = Split-Path -Parent $PSScriptRoot

# 前置检查:应用在运行时会锁定 app\ 下的 dll/exe,dotnet publish 会以 MSB3021/MSB3027 失败。
$running = @(Get-Process -Name 'DeepSeekHarness' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "==> 检测到 DeepSeek Harness Desktop 正在运行 (PID $($running.Id -join ', '))。" -ForegroundColor Yellow
    Write-Host "    构建需要写入 app\ ,请先关闭该应用再重试(关闭窗口即可)。" -ForegroundColor Yellow
    throw "应用正在运行,无法覆盖 app\ 。"
}

Write-Host "==> 构建 WPF 客户端 (Release, self-contained)…" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\DeepSeekHarness.Desktop\DeepSeekHarness.Desktop.csproj') `
    -c Release -r win-x64 --self-contained true -o (Join-Path $root 'app')
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

Write-Host "==> 运行自检…" -ForegroundColor Cyan
Write-Host "    自检会按 appsettings.json 的端口拉起 dsh 服务,请确保该端口未被占用。" -ForegroundColor DarkGray
$out = Join-Path $env:TEMP 'dsh-build-selftest.txt'
Remove-Item $out -ErrorAction SilentlyContinue
$p = Start-Process -FilePath (Join-Path $root 'app\DeepSeekHarness.exe') `
    -ArgumentList '--selftest', $out, '--with-server' -PassThru
$p.WaitForExit(360000) | Out-Null
Get-Content $out
if ($p.ExitCode -ne 0) { throw "自检未通过" }

Write-Host "==> 完成。运行 app\DeepSeekHarness.exe 启动。" -ForegroundColor Green
