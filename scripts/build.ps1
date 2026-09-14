# 构建 DeepSeek Harness Desktop
# 用法: powershell -ExecutionPolicy Bypass -File scripts\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host "==> 构建 WPF 客户端 (Release, self-contained)…" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\DeepSeekHarness.Desktop\DeepSeekHarness.Desktop.csproj') `
    -c Release -r win-x64 --self-contained true -o (Join-Path $root 'app')
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

Write-Host "==> 运行自检…" -ForegroundColor Cyan
$out = Join-Path $env:TEMP 'dsh-build-selftest.txt'
Remove-Item $out -ErrorAction SilentlyContinue
$p = Start-Process -FilePath (Join-Path $root 'app\DeepSeekHarness.exe') `
    -ArgumentList '--selftest', $out, '--with-server' -PassThru
$p.WaitForExit(360000) | Out-Null
Get-Content $out
if ($p.ExitCode -ne 0) { throw "自检未通过" }

Write-Host "==> 完成。运行 app\DeepSeekHarness.exe 启动。" -ForegroundColor Green
