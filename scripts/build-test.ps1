# build-test.ps1 - Скрипт сборки Z-UI 2.0 для тестирования
# Требования: Visual Studio 2022, .NET 10 SDK

param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [switch]$Clean = $false,
    [switch]$Restore = $true,
    [switch]$Build = $true,
    [switch]$Test = $false
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path $PSScriptRoot -Parent
$SolutionFile = Join-Path $ProjectDir "Z-UI.sln"
$ProjectFile = Join-Path $ProjectDir "Z-UI\Z-UI.csproj"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Z-UI 2.0 Build Script for VS 2022" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Platform: $Platform" -ForegroundColor Yellow
Write-Host ""

# Проверка наличия MSBuild
$MSBuildPath = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue
if (-not $MSBuildPath) {
    $MSBuildPath = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue
}
if (-not $MSBuildPath) {
    $MSBuildPath = & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue
}

if (-not $MSBuildPath) {
    Write-Error "MSBuild не найден. Установите Visual Studio 2022."
    exit 1
}

Write-Host "MSBuild: $MSBuildPath" -ForegroundColor Green

# Проверка наличия dotnet SDK
$DotnetVersion = dotnet --version
if (-not $DotnetVersion) {
    Write-Error ".NET SDK не найден. Установите .NET 10 SDK."
    exit 1
}

Write-Host "Dotnet SDK: $DotnetVersion" -ForegroundColor Green

# Очистка (если требуется)
if ($Clean) {
    Write-Host "`n[1/5] Cleaning..." -ForegroundColor Cyan
    if (Test-Path "$ProjectDir\bin") {
        Remove-Item -Recurse -Force "$ProjectDir\bin" -ErrorAction SilentlyContinue
    }
    if (Test-Path "$ProjectDir\obj") {
        Remove-Item -Recurse -Force "$ProjectDir\obj" -ErrorAction SilentlyContinue
    }
    Write-Host "Clean completed" -ForegroundColor Green
}

# Restore NuGet пакетов
if ($Restore) {
    Write-Host "`n[2/5] Restoring NuGet packages..." -ForegroundColor Cyan
    
    # Проверяем NuGet.config
    $NuGetConfig = Join-Path $ProjectDir "NuGet.config"
    if (-not (Test-Path $NuGetConfig)) {
        Write-Host "Creating NuGet.config..." -ForegroundColor Yellow
        @'<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="microsoft" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/index.json" />
  </packageSources>
</configuration>
'@ | Out-File -FilePath $NuGetConfig -Encoding UTF8
    }
    
    dotnet restore $SolutionFile --configfile $NuGetConfig
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Restore failed"
        exit 1
    }
    Write-Host "Restore completed" -ForegroundColor Green
}

# Сборка
if ($Build) {
    Write-Host "`n[3/5] Building project..." -ForegroundColor Cyan
    
    $BuildArgs = @(
        $SolutionFile
        "/p:Configuration=$Configuration"
        "/p:Platform=$Platform"
        "/p:WindowsTargetFramework=net10.0-windows10.0.19041.0"
        "/p:WindowsAppSDKSelfContained=true"
        "/p:WindowsPackageType=None"
        "/p:EnableMsixTooling=false"
        "/restore:false"
        "/verbosity:minimal"
        "/nr:false"
    )
    
    & $MSBuildPath @BuildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Host "Build completed" -ForegroundColor Green
}

# Проверка выходных файлов
Write-Host "`n[4/5] Checking output..." -ForegroundColor Cyan
$OutputDir = Join-Path $ProjectDir "Z-UI\bin\$Platform\$Configuration\net10.0-windows10.0.19041.0"
if (Test-Path $OutputDir) {
    $ExePath = Join-Path $OutputDir "Z-UI.exe"
    if (Test-Path $ExePath) {
        Write-Host "Output: $ExePath" -ForegroundColor Green
        $FileInfo = Get-Item $ExePath
        Write-Host "Size: $([math]::Round($FileInfo.Length / 1MB, 2)) MB" -ForegroundColor Green
    } else {
        Write-Warning "Z-UI.exe not found in output directory"
    }
} else {
    Write-Warning "Output directory not found: $OutputDir"
}

# Проверка наличия winws.exe
Write-Host "`n[5/5] Checking winws.exe..." -ForegroundColor Cyan
$WinwsPath = Join-Path $ProjectDir "Z-UI\zapret\winws.exe"
if (Test-Path $WinwsPath) {
    Write-Host "winws.exe found: $WinwsPath" -ForegroundColor Green
} else {
    Write-Warning "winws.exe not found. Download from zapret-discord-youtube repository."
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "`nTo run the application:" -ForegroundColor Yellow
Write-Host "  $ExePath" -ForegroundColor White
