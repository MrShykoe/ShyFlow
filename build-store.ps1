# Шаг 75: сборка пакета MSIX для Microsoft Store. Ничего ставить не нужно, кроме .NET 8 SDK:
# инструменты (makeappx, makepri, signtool) скрипт сам берёт из бесплатного пакета Microsoft.Windows.SDK.BuildTools на nuget.org.
#
#   1) Впиши в Package\store.json три значения из Partner Center (Product management -> Product identity).
#   2) Запусти:  powershell -ExecutionPolicy Bypass -File build-store.ps1
#      Готовый файл: out\store\ShyFlow-<версия>.msix  -> его и загружаешь в Partner Center (подписывать не нужно, Store подпишет сам).
#   3) Проверить пакет у себя до отправки:  powershell -ExecutionPolicy Bypass -File build-store.ps1 -TestSign
#      (подпишет тестовым сертификатом; для установки этот сертификат надо один раз добавить в доверенные, см. сообщение в конце)
param([switch]$TestSign)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = $PSScriptRoot
$pkg = Join-Path $root 'Package'
$out = Join-Path $root 'out\store'
$tools = Join-Path $pkg '.tools'

# ---------- данные из Partner Center ----------
$cfg = Get-Content (Join-Path $pkg 'store.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($k in 'IdentityName', 'Publisher', 'PublisherDisplayName') {
    if ([string]::IsNullOrWhiteSpace($cfg.$k) -or $cfg.$k -like 'ВСТАВЬ*') {
        Write-Host "Заполни Package\store.json: $k (Partner Center -> Product identity)" -ForegroundColor Red
        exit 1
    }
}

# ---------- версия из Island.csproj: Store требует четыре числа, последнее 0 ----------
[xml]$proj = Get-Content (Join-Path $root 'Island\Island.csproj') -Raw -Encoding UTF8
$ver = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$parts = @($ver.Split('.') + @('0', '0', '0'))[0..2]
$msixVer = ($parts -join '.') + '.0'
Write-Host "Версия пакета: $msixVer"

# ---------- инструменты Windows SDK ----------
if (-not (Test-Path (Join-Path $tools 'makeappx.exe'))) {
    Write-Host 'Скачиваю инструменты Windows SDK (один раз)...'
    New-Item -ItemType Directory -Force $tools | Out-Null
    $idx = Invoke-RestMethod 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/index.json' -UseBasicParsing
    $v = $idx.versions | Where-Object { $_ -notmatch '-' } | Select-Object -Last 1
    $zip = Join-Path $tools 'buildtools.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools/$v/microsoft.windows.sdk.buildtools.$v.nupkg" -OutFile $zip -UseBasicParsing
    Expand-Archive $zip (Join-Path $tools 'bt') -Force
    $exe = Get-ChildItem (Join-Path $tools 'bt\bin') -Recurse -Filter makeappx.exe | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $exe) { throw 'В пакете BuildTools не нашёлся makeappx.exe' }
    Copy-Item (Join-Path $exe.Directory.FullName '*') $tools -Recurse -Force
}
$makeappx = Join-Path $tools 'makeappx.exe'
$makepri = Join-Path $tools 'makepri.exe'
$signtool = Join-Path $tools 'signtool.exe'

# ---------- сборка программы: папка со всем внутри, .NET не нужен на компьютере пользователя ----------
$layout = Join-Path $out 'layout'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
Write-Host 'Собираю ShyFlow...'
dotnet publish (Join-Path $root 'Island\Island.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $layout
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish не прошёл' }

# ---------- манифест и значки ----------
Copy-Item (Join-Path $pkg 'Assets') (Join-Path $layout 'Assets') -Recurse -Force
$man = Get-Content (Join-Path $pkg 'Package.appxmanifest') -Raw -Encoding UTF8
$esc = { param($s) [Security.SecurityElement]::Escape($s) }
$man = $man.Replace('__PUBLISHER_DISPLAY__', (& $esc $cfg.PublisherDisplayName))
$man = $man.Replace('__IDENTITY_NAME__', (& $esc $cfg.IdentityName))
$man = $man.Replace('__PUBLISHER__', (& $esc $cfg.Publisher))
$man = $man.Replace('__VERSION__', $msixVer)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $man, (New-Object Text.UTF8Encoding $false))

# ---------- resources.pri: чтобы Windows брал значки нужного размера ----------
$priCfg = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priCfg /dq en-US /pv 10.0.0 /o | Out-Null
& $makepri new /pr $layout /cf $priCfg /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri не прошёл' }

# ---------- пакет ----------
$msix = Join-Path $out "ShyFlow-$msixVer.msix"
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx не прошёл' }

if ($TestSign) {
    # тестовая подпись: только чтобы поставить пакет у себя. В Store загружается неподписанный, его подписывает Microsoft
    $pfx = Join-Path $pkg 'ShyFlow-test.pfx'
    $pfxPass = 'shyflow-test'
    if (-not (Test-Path $pfx)) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $cfg.Publisher -KeyUsage DigitalSignature -FriendlyName 'ShyFlow test' `
            -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Export-PfxCertificate -Cert $cert -FilePath $pfx -Password (ConvertTo-SecureString $pfxPass -AsPlainText -Force) | Out-Null
        Export-Certificate -Cert $cert -FilePath (Join-Path $pkg 'ShyFlow-test.cer') | Out-Null
    }
    & $signtool sign /fd SHA256 /a /f $pfx /p $pfxPass $msix
    if ($LASTEXITCODE -ne 0) { throw 'signtool не прошёл' }
    Write-Host ''
    Write-Host 'Чтобы установить тестовый пакет, один раз добавь сертификат в доверенные (PowerShell от администратора):' -ForegroundColor Yellow
    Write-Host "  Import-Certificate -FilePath '$(Join-Path $pkg 'ShyFlow-test.cer')' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    Write-Host 'Потом просто открой .msix двойным кликом. Перед этим закрой обычный ShyFlow.'
}

Write-Host ''
Write-Host "Готово: $msix" -ForegroundColor Green
