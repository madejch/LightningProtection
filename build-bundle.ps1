# Собирает плагин под AutoCAD 2024, 2025–2026 и 2027 и складывает пакет автозагрузки
# в dist\LightningProtection.bundle. С ключом -Install копирует пакет в
# %APPDATA%\Autodesk\ApplicationPlugins, откуда AutoCAD загружает его при запуске.
# -OutDir — другая папка для пакета (например, если AutoCAD держит открытым dist).
# -Installer — дополнительно собрать установщик LightningProtection-Setup-<версия>.exe (Inno Setup 6).
param([switch]$Install, [switch]$Installer, [string]$OutDir = 'dist')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\LightningProtection.Acad\LightningProtection.Acad.csproj'
$bundle = Join-Path (Join-Path $root $OutDir) 'LightningProtection.bundle'

dotnet build $project -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }

if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $bundle 'Contents') | Out-Null
Copy-Item (Join-Path $root 'bundle\PackageContents.xml') $bundle

$targets = @{ '2024' = 'net48'; '2025' = 'net8.0-windows'; '2027' = 'net10.0-windows' }
foreach ($version in $targets.Keys) {
    $source = Join-Path $root "src\LightningProtection.Acad\bin\Release\$($targets[$version])"
    $destination = Join-Path $bundle "Contents\$version"
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item "$source\*" $destination -Exclude '*.pdb'
}
# Шрифт GOST Common (прямой и наклонный): из папки fonts репозитория или из шрифтов Windows
# на машине сборки. Плагин пишет наклонным и установит шрифт пользователю, у которого его нет.
foreach ($fontName in 'GOST Common.ttf', 'GOST Common Italic.ttf') {
    $fontSource = @((Join-Path $root "fonts\$fontName"), (Join-Path $env:WINDIR "Fonts\$fontName"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Fonts\$fontName")) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($fontSource) {
        New-Item -ItemType Directory -Force (Join-Path $bundle 'Contents\Fonts') | Out-Null
        Copy-Item $fontSource (Join-Path $bundle "Contents\Fonts\$fontName")
    } else {
        Write-Warning "Шрифт $fontName не найден — пакет собран без него."
    }
}

Write-Host "Пакет: $bundle"

if ($Installer) {
    $iscc = @((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Не найден Inno Setup 6 (ISCC.exe). Установите его: winget install JRSoftware.InnoSetup' }
    $version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Encoding UTF8)).Project.PropertyGroup.Version
    & $iscc /Q "/DAppVersion=$version" "/DBundleDir=$bundle" "/O$(Join-Path $root $OutDir)" (Join-Path $root 'installer\LightningProtection.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Сборка установщика не удалась.' }
    Write-Host "Установщик: $(Join-Path (Join-Path $root $OutDir) "LightningProtection-Setup-$version.exe")"
}

if ($Install) {
    $plugins = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
    New-Item -ItemType Directory -Force $plugins | Out-Null
    $target = Join-Path $plugins 'LightningProtection.bundle'
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $bundle $plugins -Recurse
    Write-Host "Установлено в $target. Перезапустите AutoCAD."
}
