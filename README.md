# Disk Health Advisor

Read-only диагностика дисков для Windows: SMART, ресурс SSD/TBW, наблюдение в трее, журнал записи процессов и дневная сводка по дискам.

Disk Health Advisor is a Windows desktop utility for read-only disk diagnostics: SMART, SSD TBW/resource tracking, tray monitoring, process write journal, and daily disk activity overview.

## Русский

Disk Health Advisor помогает понять состояние дисков человеческим языком. Программа не меняет данные на дисках: она только читает информацию Windows, SMART/smartctl, локальную историю, счетчики записи процессов и локальную базу TBW для SSD.

### Возможности

- Понятная сводка здоровья дисков
- Просмотр ресурса SSD/TBW и локальная база TBW
- Поиск TBW в интернете и ручное сохранение значения
- Наблюдение в трее
- Легкие проверки процессов каждые 10 секунд
- Полный опрос дисков реже, чтобы не нагружать компьютер
- Вкладка "День диска" с событиями, температурой, всплесками записи и советами
- Расследования подозрительных изменений диска
- Журнал: кто и когда заметно писал на диск
- Темы оформления
- Установщик Windows, собранный через Inno Setup

### Скачать

Обычному пользователю лучше скачивать установщик со страницы **Releases**:

`DiskHealthAdvisorSetup.exe`

Скрипт установщика лежит здесь:

`Installer/DiskHealthAdvisorSetup.iss`

### Сборка из исходников

Требования:

- Windows 10/11 x64
- .NET 9 SDK
- Опционально: Inno Setup 7, если нужно собрать установщик
- Опционально: smartmontools/smartctl, чтобы получать больше SMART/NVMe-данных

Debug-сборка:

```powershell
dotnet build
```

Запуск:

```text
bin\Debug\net9.0-windows\DiskHealthAdvisor.exe
```

Публикация self-contained x64-версии:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish\win-x64
```

Сборка установщика:

```powershell
& "C:\Program Files\Inno Setup 7\ISCC.exe" "Installer\DiskHealthAdvisorSetup.iss"
```

Готовый установщик:

```text
Installer\Output\DiskHealthAdvisorSetup.exe
```

### Заметки

- Некоторые SMART/NVMe-поля могут быть недоступны без smartctl или прав администратора.
- Счетчики процессов Windows показывают общую активность чтения/записи процесса и не всегда точно привязывают процесс к физическому диску.
- Наблюдение сделано легким: частые проверки читают счетчики процессов, а полный опрос дисков выполняется реже.

## English

Disk Health Advisor helps understand disk health in plain language. The app does not modify disk data. It reads Windows data, SMART/smartctl, local history, process write counters, and a local SSD TBW database.

### Features

- Human-readable disk health summary
- SSD resource/TBW view with local TBW database
- Online TBW lookup and manual TBW saving
- Tray monitoring
- Lightweight 10-second process checks
- Slower full disk refresh to avoid unnecessary load
- "Disk Day" overview with events, temperatures, write spikes, and next steps
- Investigations for suspicious disk changes
- Process write journal: who wrote a lot and when
- Custom themes
- Self-contained Windows installer built with Inno Setup

### Download

For normal use, download the installer from the GitHub **Releases** page:

`DiskHealthAdvisorSetup.exe`

The installer script is stored here:

`Installer/DiskHealthAdvisorSetup.iss`

### Build From Source

Requirements:

- Windows 10/11 x64
- .NET 9 SDK
- Optional: Inno Setup 7, if you want to build the installer
- Optional: smartmontools/smartctl, for better SMART/NVMe data

Build Debug:

```powershell
dotnet build
```

Run from:

```text
bin\Debug\net9.0-windows\DiskHealthAdvisor.exe
```

Publish self-contained x64 build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish\win-x64
```

Build installer:

```powershell
& "C:\Program Files\Inno Setup 7\ISCC.exe" "Installer\DiskHealthAdvisorSetup.iss"
```

Installer output:

```text
Installer\Output\DiskHealthAdvisorSetup.exe
```

### Notes

- Some NVMe/SATA SMART fields may be unavailable without smartctl or administrator rights.
- Windows process counters show general process read/write activity and do not always map a process to an exact physical disk.
- Monitoring is designed to be lightweight: frequent checks read process counters, while full disk scans happen less often.

## GitHub Release Flow

1. Build the app with `dotnet publish`.
2. Build the installer with Inno Setup.
3. Create a GitHub release, for example `v1.0.0`.
4. Attach `Installer\Output\DiskHealthAdvisorSetup.exe` to the release.
5. Keep `bin/`, `obj/`, `publish/`, and `Installer/Output/` out of Git commits.

## License

No license has been selected yet.
