<#
.SYNOPSIS
    Публикует мод RussianLocalization в Steam Workshop через SteamCMD.

.DESCRIPTION
    Скрипт сам доустанавливает SteamCMD, собирает workshop_item.vdf из workshop.json
    и manifest.json и запускает публикацию.

    ПЕРВЫЙ ЗАПУСК — только вход, чтобы пройти Steam Guard:
        .\Publish-Workshop.ps1 -Account ваш_логин -LoginOnly

    Steam запомнит сессию, дальше публикация идёт без ввода кода:
        .\Publish-Workshop.ps1 -Account ваш_логин

    Посмотреть, что будет отправлено, ничего не публикуя:
        .\Publish-Workshop.ps1 -DryRun

.NOTES
    Теги в .vdf намеренно НЕ пишутся: SteamCMD обновляет только переданные поля,
    поэтому уже выставленные на странице мода теги сохранятся. В workshop.json
    поле Tags — строка, а игра ждёт массив, так что трогать его лишний раз незачем.
#>
[CmdletBinding()]
param(
    # Логин Steam. Не нужен только для -DryRun.
    [string]$Account,

    # Только войти в аккаунт (пройти Steam Guard) и выйти. Ничего не публикует.
    [switch]$LoginOnly,

    # Собрать .vdf, показать его и остановиться.
    [switch]$DryRun,

    # Заметка к изменениям на странице мода. По умолчанию — "Обновление <версия>".
    [string]$ChangeNote,

    # Папка мода. По умолчанию — RussianLocalization в корне репозитория.
    [string]$Source
)

$ErrorActionPreference = 'Stop'

$AppId = '333640'   # Caves of Qud
$SteamCmdUrl = 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Source) { $Source = Join-Path $Root 'RussianLocalization' }
$SteamCmdDir = Join-Path $Root '_tools\steamcmd'
$SteamCmd = Join-Path $SteamCmdDir 'steamcmd.exe'
$VdfPath = Join-Path $PSScriptRoot 'workshop_item.vdf'

function Write-Step($message) { Write-Host "==> $message" -ForegroundColor Cyan }
function Write-Ok($message) { Write-Host "    $message" -ForegroundColor Green }

# --- Проверка содержимого мода -------------------------------------------------

Write-Step 'Проверяю папку мода'
if (-not (Test-Path $Source)) { throw "Папка мода не найдена: $Source" }

foreach ($required in 'manifest.json', 'workshop.json', 'preview.png') {
    if (-not (Test-Path (Join-Path $Source $required))) {
        throw "В папке мода нет обязательного файла $required ($Source)"
    }
}

$manifest = Get-Content (Join-Path $Source 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$workshop = Get-Content (Join-Path $Source 'workshop.json') -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not $workshop.WorkshopId -or $workshop.WorkshopId -eq 0) {
    throw 'В workshop.json не задан WorkshopId. Публикация создала бы НОВЫЙ элемент вместо обновления существующего.'
}
if (-not $ChangeNote) { $ChangeNote = "Обновление $($manifest.Version)" }

$sizeMb = [math]::Round((Get-ChildItem $Source -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Ok "$($manifest.Name) v$($manifest.Version), WorkshopId $($workshop.WorkshopId), $sizeMb МБ"

# --- SteamCMD ------------------------------------------------------------------

if (-not (Test-Path $SteamCmd)) {
    Write-Step 'SteamCMD не найден, скачиваю'

    # Разворачиваем ТОЛЬКО в пустую папку. Если распаковать свежий steamcmd.exe поверх
    # остатков старой установки (в этом репозитории лежал заброшенный bin/steamservice.*),
    # он виснет намертво на "Downloading update (0 of 43,472 KB)": процесс не ест CPU,
    # ничего не читает и оставляет рядом маркер .crash.
    if (Test-Path $SteamCmdDir) {
        Get-Process -Name steamcmd -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        Write-Host '    Нашлась старая установка SteamCMD, удаляю её перед распаковкой' -ForegroundColor Yellow
        Remove-Item $SteamCmdDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $SteamCmdDir -Force | Out-Null
    $zip = Join-Path ([System.IO.Path]::GetTempPath()) 'steamcmd.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $SteamCmdUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $SteamCmdDir -Force
    Remove-Item $zip -Force
    if (-not (Test-Path $SteamCmd)) { throw "SteamCMD не распаковался в $SteamCmdDir" }
    Write-Ok 'SteamCMD установлен'
} else {
    Write-Ok 'SteamCMD на месте'
}

# --- Вход ----------------------------------------------------------------------

if ($LoginOnly) {
    if (-not $Account) { throw 'Для входа нужен -Account' }
    Write-Step "Вход в Steam под аккаунтом $Account"
    Write-Host '    Введите пароль и код Steam Guard, когда SteamCMD попросит.' -ForegroundColor Yellow
    & $SteamCmd +login $Account +quit
    if ($LASTEXITCODE -ne 0) { throw "SteamCMD завершился с кодом $LASTEXITCODE — вход не выполнен" }
    Write-Ok 'Вход выполнен, сессия сохранена. Теперь можно публиковать без -LoginOnly.'
    return
}

# --- Сборка .vdf ---------------------------------------------------------------

function ConvertTo-VdfString($value) {
    if ($null -eq $value) { return '' }
    $text = [string]$value
    $text = $text.Replace('\', '\\')
    $text = $text.Replace('"', '\"')
    $text = $text.Replace("`r`n", '\n').Replace("`n", '\n').Replace("`r", '\n')
    $text = $text.Replace("`t", '\t')
    return $text
}

Write-Step 'Собираю workshop_item.vdf'

$contentFolder = (Resolve-Path $Source).Path
$previewFile = (Resolve-Path (Join-Path $Source 'preview.png')).Path
$visibility = if ($null -ne $workshop.Visibility) { [string]$workshop.Visibility } else { '0' }

$vdf = @"
"workshopitem"
{
	"appid"			"$AppId"
	"publishedfileid"	"$($workshop.WorkshopId)"
	"contentfolder"		"$(ConvertTo-VdfString $contentFolder)"
	"previewfile"		"$(ConvertTo-VdfString $previewFile)"
	"visibility"		"$visibility"
	"title"			"$(ConvertTo-VdfString $workshop.Title)"
	"description"		"$(ConvertTo-VdfString $workshop.Description)"
	"changenote"		"$(ConvertTo-VdfString $ChangeNote)"
}
"@

# SteamCMD ждёт UTF-8 без BOM — иначе кириллица в описании приедет мусором.
[System.IO.File]::WriteAllText($VdfPath, $vdf, (New-Object System.Text.UTF8Encoding($false)))
Write-Ok "Файл готов: $VdfPath"

if ($DryRun) {
    Write-Step 'Режим -DryRun, публикации не будет. Содержимое .vdf:'
    Write-Host ''
    Write-Host $vdf
    Write-Host ''
    Write-Host 'Заголовок : ' -NoNewline; Write-Host $workshop.Title -ForegroundColor White
    Write-Host 'Заметка   : ' -NoNewline; Write-Host $ChangeNote -ForegroundColor White
    Write-Host 'Видимость : ' -NoNewline; Write-Host "$visibility (0 = публичный)" -ForegroundColor White
    Write-Host 'Описание  : ' -NoNewline; Write-Host "$($workshop.Description.Length) символов" -ForegroundColor White
    return
}

# --- Публикация ----------------------------------------------------------------

if (-not $Account) { throw 'Нужен -Account. Если ещё не входили — сначала запустите с -LoginOnly.' }

Write-Step "Публикую в Workshop (элемент $($workshop.WorkshopId))"
Write-Host '    Заливается ~' -NoNewline
Write-Host "$sizeMb МБ, это может занять несколько минут." -ForegroundColor Yellow

& $SteamCmd +login $Account +workshop_build_item $VdfPath +quit
$code = $LASTEXITCODE

if ($code -ne 0) {
    throw @"
SteamCMD завершился с кодом $code — публикация НЕ выполнена.
Частые причины:
  * не выполнен вход      -> .\Publish-Workshop.ps1 -Account $Account -LoginOnly
  * протухла сессия       -> то же самое, войдите заново
  * нет прав на элемент   -> WorkshopId $($workshop.WorkshopId) должен принадлежать этому аккаунту
"@
}

Write-Ok 'Готово. Страница мода:'
Write-Host "    https://steamcommunity.com/sharedfiles/filedetails/?id=$($workshop.WorkshopId)" -ForegroundColor White
