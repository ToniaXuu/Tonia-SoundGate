<#
.SYNOPSIS
    Tonia-SoundGate 一键打包：读取 csproj 版本 → 自包含发布 → 编译 NSIS 安装包。

.DESCRIPTION
    版本号唯一真源是 Tonia-SoundGate.csproj 的 <Version>。本脚本读取它，
    注入到 NSIS 的 /DAPP_VERSION，确保"程序内部版本"与"安装包版本"永不漂移。
    发版只需改 csproj 里的 <Version> 一处，然后运行本脚本。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File packaging\build.ps1
#>

$ErrorActionPreference = 'Stop'

# 脚本位于 packaging\，项目根目录是其上一级
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'Tonia-SoundGate.csproj'
$publishDir = Join-Path $PSScriptRoot 'publish'
$nsiScript = Join-Path $PSScriptRoot 'setup.nsi'

# ---------- 1. 读取版本真源 ----------
[xml]$proj = Get-Content -LiteralPath $csproj -Encoding UTF8
$versionNode = $proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.Version)) {
    throw "无法从 $csproj 读取 <Version>，请检查项目文件。"
}
$version = $versionNode.Version.Trim()
Write-Host "版本真源 (csproj): $version" -ForegroundColor Cyan

# ---------- 2. 自包含单文件发布 ----------
Write-Host "正在发布到 $publishDir ..." -ForegroundColor Cyan
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

# ---------- 3. 定位 makensis ----------
$makensis = Get-Command makensis -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $makensis) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
        "$env:ProgramFiles\NSIS\makensis.exe"
    )
    $makensis = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makensis) {
    throw "未找到 makensis.exe。请安装 NSIS 3，或将其目录加入 PATH。"
}

# ---------- 4. 编译安装包，注入版本 ----------
# NSIS 的 OutFile 不会自动创建目标目录；CI 全新 checkout 里 dist 不存在，
# 必须显式创建，否则 makensis 报 "Can't open output file" 后退出码 1。
$distDir = Join-Path $PSScriptRoot 'dist'
if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    Write-Host "已创建输出目录: $distDir" -ForegroundColor DarkGray
}

Write-Host "正在编译安装包 (APP_VERSION=$version) ..." -ForegroundColor Cyan
Push-Location $PSScriptRoot
try {
    & $makensis "/DAPP_VERSION=$version" /INPUTCHARSET UTF8 $nsiScript
    if ($LASTEXITCODE -ne 0) { throw "makensis 失败，退出码 $LASTEXITCODE" }
}
finally {
    Pop-Location
}

# ---------- 5. 汇报产物 ----------
$setup = Join-Path $PSScriptRoot "dist\Tonia-SoundGate-Setup-$version.exe"
if (Test-Path $setup) {
    $sizeMB = [math]::Round((Get-Item $setup).Length / 1MB, 1)
    Write-Host "打包完成: $setup ($sizeMB MB)" -ForegroundColor Green
}
else {
    throw "安装包未按预期生成: $setup"
}
