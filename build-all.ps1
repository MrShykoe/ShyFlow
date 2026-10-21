# Собирает ShyFlow.exe для тестов и установщик для пользователей. Нужны .NET 8 SDK и Inno Setup 6.3+.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

[xml]$proj = Get-Content "$root\Island\Island.csproj" -Encoding UTF8
$ver = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $ver) { throw 'В Island.csproj не найден тег Version' }

# 1) ShyFlow.exe для разработчиков (лёгкий, нужен .NET 8 Desktop Runtime)
$dev = "$root\out\dev"
if (Test-Path $dev) { Remove-Item $dev -Recurse -Force }
dotnet publish "$root\Island\Island.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o $dev
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

# 2) установщик через Inno Setup
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Host ''
    Write-Host 'Inno Setup не найден. Поставь его с https://jrsoftware.org/isdl.php и запусти build-all.bat ещё раз.' -ForegroundColor Yellow
    Write-Host "Тестовый exe уже готов: $dev\ShyFlow.exe"
    exit 1
}

& $iscc "/DAppVersion=$ver" "/DSourceExe=$dev\ShyFlow.exe" "$root\ShyFlow.iss"
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup вернул ошибку' }

Write-Host ''
Write-Host "Готово. Версия $ver" -ForegroundColor Green
Write-Host "  Для разработчиков (тест):  out\dev\ShyFlow.exe"
Write-Host "  Для пользователей:         out\installer\ShyFlow-Setup.exe"
