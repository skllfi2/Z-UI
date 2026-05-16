# Zapret Binary Files

This folder should contain the zapret-discord-youtube binaries.

## Required Files

Download from [zapret-discord-youtube releases](https://github.com/Flowseal/zapret-discord-youtube/releases):

```
zapret/
├── winws.exe          <- Main DPI bypass executable
├── WinDivert.dll      <- Required by winws.exe  
├── WinDivert64.sys    <- Kernel driver (x64)
├── strategies/        <- Strategy files (already included)
├── lists/             <- Domain lists (already included)
└── version.txt        <- Version info
```

## Installation

1. Download latest release from: https://github.com/Flowseal/zapret-discord-youtube/releases
2. Extract `winws.exe`, `WinDivert.dll`, `WinDivert64.sys` to this folder
3. Run Z-UI

## Alternative: Use existing installation

If you already have zapret installed, set the path in Z-UI settings.
