# =====================================================
#  build.ps1 — Запусти и получи Z-UI-Setup.exe
#  Использование: .\build.ps1
#  Опционально:   .\build.ps1 -Version "1.2.0"
# =====================================================

param(
    [string]$Version = "1.0.0",
    [string]$ProjectFile = ".\Z-UI\Z-UI.csproj",
    [string]$InnoSetup = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"

# --- Цвета для вывода ---
function Info  { param($msg) Write-Host "  $msg" -ForegroundColor Cyan }
function OK    { param($msg) Write-Host "  OK  $msg" -ForegroundColor Green }
function Fail  { param($msg) Write-Host "  ERR $msg" -ForegroundColor Red; Write-Host "`nНажми Enter для выхода..." -ForegroundColor DarkGray; Read-Host | Out-Null; exit 1 }
function Step  { param($msg) Write-Host "`n[ $msg ]" -ForegroundColor Yellow }

Clear-Host
Write-Host "============================================" -ForegroundColor DarkGray
Write-Host "   Z-UI Build Script  v$Version" -ForegroundColor White
Write-Host "============================================" -ForegroundColor DarkGray

# ── Шаг 1: Проверки ──────────────────────────────────
Step "Проверка окружения"

if (-not (Test-Path $ProjectFile)) {
    Fail "Не найден проект: $ProjectFile`n  Запусти скрипт из корня репозитория."
}
OK "Проект найден: $ProjectFile"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail ".NET SDK не установлен или не в PATH"
}
OK ".NET SDK: $(dotnet --version)"

if (-not (Test-Path $InnoSetup)) {
    Fail "Inno Setup не найден: $InnoSetup`n  Скачай с https://jrsoftware.org/isinfo.php"
}
OK "Inno Setup найден"

# ── Шаг 2: Обновить версию в .iss ────────────────────
Step "Обновление версии в setup.iss → $Version"

$issPath = ".\setup.iss"
if (Test-Path $issPath) {
    (Get-Content $issPath) `
        -replace '#define MyAppVersion ".*"', "#define MyAppVersion `"$Version`"" |
        Set-Content $issPath
    OK "Версия обновлена"
} else {
    Info "setup.iss не найден, версия не обновлена"
}

# ── Шаг 3: Очистка старой сборки ─────────────────────
Step "Очистка"

@(".\publish", ".\installer") | ForEach-Object {
    if (Test-Path $_) {
        Remove-Item $_ -Recurse -Force
        Info "Удалено: $_"
    }
}
OK "Чисто"

# ── Шаг 4: Публикация приложения ─────────────────────
Step "dotnet publish"

Info "Собираю Release / win-x64 / self-contained..."
dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o .\publish `
    /p:Version=$Version

if ($LASTEXITCODE -ne 0) { Fail "dotnet publish завершился с ошибкой" }

# Копируем resources.pri если не был скопирован target'ом в .csproj
$priSrc = ".\Z-UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\Z-UI.pri"
$priDst = ".\publish\resources.pri"
if (-not (Test-Path $priDst) -and (Test-Path $priSrc)) {
    Copy-Item $priSrc $priDst
    Info "resources.pri скопирован вручную"
}
if (-not (Test-Path $priDst)) { Fail "resources.pri не найден — приложение не запустится" }

OK "Публикация готова → .\publish\"

# ── Шаг 5: Сборка ZUI.Worker (Native AOT) ─────────────
Step "Сборка ZUI.Worker (Native AOT)"

$workerProject = ".\ZUI.Worker\ZUI.Worker.csproj"
if (Test-Path $workerProject) {
    Info "Публикация ZUI.Worker (Native AOT, win-x64)..."

    dotnet publish $workerProject `
        -c Release `
        -r win-x64 `
        -o .\publish\ZUI.Worker

    if ($LASTEXITCODE -ne 0) { Fail "dotnet publish ZUI.Worker завершился с ошибкой" }

    $workerExe = ".\publish\ZUI.Worker\ZUI.Worker.exe"
    if (-not (Test-Path $workerExe)) { Fail "ZUI.Worker.exe не создан" }

    OK "ZUI.Worker.exe скопирован в publish\ZUI.Worker\"
} else {
    Fail "ZUI.Worker проект не найден: $workerProject"
}

# ── Шаг 6: Сборка zui-broker ───────────────────────────
Step "Сборка zui-broker (Rust)"

$brokerPath = ".\zui-broker"
if (Test-Path "$brokerPath\Cargo.toml") {
    Info "Собираю zui-broker..."
    Push-Location $brokerPath
    cargo build --release
    Pop-Location
    
    if ($LASTEXITCODE -ne 0) { Fail "cargo build завершился с ошибкой" }
    
    $brokerExe = ".\zui-broker\target\release\zui-broker.exe"
    if (-not (Test-Path $brokerExe)) { Fail "zui-broker.exe не создан" }
    
    Copy-Item $brokerExe ".\publish\zui-broker.exe" -Force
    OK "zui-broker.exe скопирован в publish"
} else {
    Info "zui-broker не найден, пропускаю"
}

# ── Шаг 7: Windows App Runtime ───────────────────────
Step "Windows App Runtime Redistributable"

$rtInstaller = ".\WindowsAppRuntimeInstall-x64.exe"

if (-not (Test-Path $rtInstaller)) {
    Fail "Не найден: $rtInstaller`n Скачай experimental Runtime вручную с:`n https://github.com/microsoft/WindowsAppSDK/releases/tag/2.0.0-experimental6n и положи в корень проекта."
}
OK "Runtime найден: $rtInstaller"

# ── Шаг 8: Сборка установщика ────────────────────────
Step "Inno Setup → Z-UI-Setup.exe"

Info "Компилирую setup.iss..."
& $InnoSetup $issPath

if ($LASTEXITCODE -ne 0) { Fail "ISCC.exe завершился с ошибкой" }

$output = ".\installer\Z-UI-Setup.exe"
if (-not (Test-Path $output)) { Fail "Установщик не создан — проверь setup.iss" }

$size = [math]::Round((Get-Item $output).Length / 1MB, 1)
OK "Установщик готов: $output ($size MB)"

# ── Готово ───────────────────────────────────────────
Write-Host ""
Write-Host "============================================" -ForegroundColor DarkGray
Write-Host "   Готово!  installer\Z-UI-Setup.exe" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Нажми Enter для выхода..." -ForegroundColor DarkGray
Read-Host | Out-Null
