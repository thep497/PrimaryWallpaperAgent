# Primary Wallpaper Agent

A Windows 11 tray app that keeps a picture on the current Windows primary display and makes other extended displays black. It follows primary changes and monitor connections automatically.

แสดงภาพบน Windows Primary Display เพียงจอเดียว จออื่นที่ Extend เป็นสีดำ รองรับภาษาไทยและอังกฤษ

## Download and first launch

Download **PrimaryWallpaperAgent.exe** from [GitHub Releases](https://github.com/thep497/PrimaryWallpaperAgent/releases). The v0.3.1 preview is a self-contained **Windows x64** single-file build; a separate .NET installation is not required.

1. Keep the EXE in a permanent local folder. Exit any older running instance before launching it.
2. Set Windows background to **Picture** and fit to **Fill**. Spotlight/slideshow are not supported.
3. On first launch, Settings opens and waits for an explicit image choice. Existing wallpaper remains untouched until that choice.
4. Choose a JPG/PNG/BMP, or select **Use current primary image**. The image applies immediately to the current primary; other extended displays become black.
5. Optionally select Thai/English, a 0.5/1/2/5 second check interval, or **Start with Windows**. Changes save immediately.

Settings has no scroll area; click **?** for explanations. **Undo image** restores the previous successful image selection, retaining other preferences. Undo is one level and lasts until app exit. Close/Esc retains successful changes. Display checking continues while Settings is open.

Start with Windows uses a per-user Run registry entry and the actual EXE path. Copying the EXE to another PC does not enable startup there. If moved/renamed after opting in, toggle this option off/on from its new location. See [deployment details](docs/development/DEPLOYMENT.md).

Open the EXE again to show Settings in the existing v0.3.1 instance. The version/About link opens version information, GitHub Releases and the current data folder. Exit older versions once before the first v0.3.1 launch.

Managed logs/diagnostics are retained for 30 days with a target total limit of 50 MiB; active/locked files are preserved and retried. Text logs rotate at 2 MiB. Cleanup does not touch settings or wallpaper assets.

## Data and updates

New installations create settings, cached images and logs in **%LOCALAPPDATA%\PrimaryWallpaperAgent**. Exit the app before replacing its EXE. Keep the same path to preserve the startup command; user data is separate.

Older installations with settings.json beside the EXE continue using that folder. No silent migration or import of private trial assets occurs. Exit keeps the current wallpaper assignments.

One static image is shared across Windows Virtual Desktops. Unknown wallpaper changes pause the agent for explicit adoption through Settings. Changing the image in Windows alone does not automatically adopt it.

## Build and tests

Windows, PowerShell 7 and .NET SDK **10.0.402** (see global.json):

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1 -SkipBuild
```

Tests use generated images and fake display/startup backends. Window tests display only app fixtures. These tests make no real wallpaper or startup registry writes.

Build and verify a distributable EXE:

```powershell
.\scripts\Publish-Release.ps1
.\scripts\Test-Release.ps1
```

Output: artifacts/releases/v0.3.1/win-x64/PrimaryWallpaperAgent.exe and SHA256SUMS.txt. Publish downloads Microsoft runtime packs from nuget.org into ignored .local/release-nuget. No third-party app packages, global NuGet source changes or PATH changes are required. The script refuses to overwrite an existing release destination.

Publish-Tray.ps1 and Start-WallpaperTray.ps1 remain legacy helpers for the private trial package, rather than public installation steps.

Read-only display probe after a Debug build:

```powershell
.\artifacts\bin\PrimaryWallpaperAgent.Probe\debug\PrimaryWallpaperAgent.Probe.exe --json
```

Probe exit 0 means complete; 1 means fatal; 2 means some API entries were unreadable/unmatched. Cached entries can yield exit 2 even when active displays are mapped.

## Source layout

- src/PrimaryWallpaperAgent.Core — display APIs, policy and engine.
- src/PrimaryWallpaperAgent.Tray — tray, Settings, first-run and persistence.
- src/PrimaryWallpaperAgent.Probe — read-only console and explicit diagnostics.
- tests/PrimaryWallpaperAgent.Tests — 11 automated suites.
- scripts — build, test, publish and diagnostics.
- assets/branding — icons and reproducible source artwork.
- docs/development and docs/testing — implementation and evidence.
- artifacts and .local — ignored packages, output and local evidence.

Private settings, photos, monitor snapshots and trial plans are excluded from source and release assets.

## Preview limitations and evidence

The user previously verified primary switching, hotplug 3→1→3 and policy persistence across Virtual Desktops. On 20 October the user reported normal launch and expected wallpaper behavior. Detailed scenario results are still being collected. Physical-console mapping/recovery, projector, more than three physical displays, DPI transitions and startup after logon still need validation. Tests with 1/3/5 simulated displays do not establish hardware results.

- [v0.3.1 release notes](docs/releases/v0.3.1.md)
- [Deployment and startup](docs/development/DEPLOYMENT.md)
- [v0.3.1 activation / retention / About](docs/testing/MAINTENANCE-TEST.md)
- [Testing index](docs/testing/README.md)
- [Single-file / first-run verification](docs/testing/RELEASE-TEST.md)
- [Immediate settings / Undo / tray](docs/testing/INSTANT-SETTINGS-TEST.md)
- [Compact Settings / click-help](docs/testing/COMPACT-SETTINGS-TEST.md)
- [Pending physical-console checks](docs/testing/PENDING-TESTS.md)
- [Branding preview](assets/branding/Preview.png)
