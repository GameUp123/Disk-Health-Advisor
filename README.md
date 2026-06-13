# Disk Health Advisor

Disk Health Advisor is a Windows desktop utility for read-only disk diagnostics. It shows disk health in plain language, watches disk activity in the tray, tracks notable write spikes, and helps understand what happened to disks during the day.

The app does not change disk data. It reads Windows, SMART/smartctl, local history, process write counters, and a local SSD TBW database.

## Features

- Human-readable disk health summary
- SSD resource/TBW view with local TBW database
- Online TBW lookup and manual TBW saving
- Tray monitoring with lightweight 10-second process checks
- Full disk refresh on a slower interval
- "Disk Day" daily overview with events, temperatures, write spikes, and next steps
- Investigations for suspicious disk changes
- Process write journal: who wrote a lot and when
- Custom themes and compact WPF interface
- Self-contained Windows installer built with Inno Setup

## Download

For normal use, download the installer from the GitHub Releases page:

`DiskHealthAdvisorSetup.exe`

The installer is built from:

`Installer/DiskHealthAdvisorSetup.iss`

## Build From Source

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

## GitHub Release Flow

1. Build the app with `dotnet publish`.
2. Build the installer with Inno Setup.
3. Create a GitHub release, for example `v1.0.0`.
4. Attach `Installer\Output\DiskHealthAdvisorSetup.exe` to the release.
5. Keep `bin/`, `obj/`, `publish/`, and `Installer/Output/` out of Git commits.

## Notes

- Some NVMe/SATA SMART fields may be unavailable without smartctl or administrator rights.
- Windows process counters show general process read/write activity and do not always map a process to an exact physical disk.
- Monitoring is designed to be lightweight: frequent checks read process counters, while full disk scans happen less often.

## License

No license has been selected yet.
