# Primary Wallpaper Agent

A Windows 11 tray app that keeps a picture on the current Windows primary display and makes other extended displays black. It follows primary changes and monitor connections automatically.

แสดงภาพบน Windows Primary Display เพียงจอเดียว จออื่นที่ Extend เป็นสีดำ รองรับภาษาไทยและอังกฤษ

## Download and first launch

Choose **PrimaryWallpaperAgent-Full-win-x64.exe** (recommended, includes runtime) or **PrimaryWallpaperAgent-Slim-win-x64.exe** (requires .NET 10 Windows Desktop Runtime x64) from [GitHub Releases](https://github.com/thep497/PrimaryWallpaperAgent/releases). Both v0.4.1 packages are **Windows x64** single-file apps. Full needs no separate .NET installation. Slim uses the compatible installed desktop runtime.

1. Keep the EXE in a permanent local folder. Exit any older running instance before launching it.
2. Set Windows background to **Picture** and fit to **Fill**. Spotlight/slideshow are not supported.
3. On first launch, Settings opens and waits for an explicit image choice. Existing wallpaper remains untouched until that choice.
4. Choose a JPG/PNG/BMP, or select **Use current primary image**. The image applies immediately to the current primary; other extended displays become black.
5. Optionally select Thai/English, a 0.5/1/2/5 second check interval, or **Start with Windows**. Changes save immediately.

Settings has no scroll area; click **?** for explanations. **Undo image** restores the previous successful image selection, retaining other preferences. Undo is one level and lasts until app exit. Close/Esc retains successful changes. Display checking continues while Settings is open.

Start with Windows uses a per-user Run registry entry and the actual EXE path. Copying the EXE to another PC does not enable startup there. If moved/renamed after opting in, toggle this option off/on from its new location. See [deployment details](docs/development/DEPLOYMENT.md).

Open the EXE again to show Settings in the existing running instance. The version/About link opens version information, GitHub Releases and the current data folder. Exit older versions once before launching this release for the first time.

Managed logs/diagnostics are retained for 30 days with a target total limit of 50 MiB; active/locked files are preserved and retried. Text logs rotate at 2 MiB. Cleanup does not touch settings or wallpaper assets.

## Automatic updates

About contains automatic checks, optional Preview releases, release notes and Update and restart. Checks are daily after success; downloads/installations require a click. Full stays Full and Slim stays Slim. The signed manifest, hash/size verification, preflight and startup/rollback protocol protect the update transaction.

Default channel is Stable; enable Preview to receive later previews. Versions before v0.4.0 must be upgraded manually once. See [update details](docs/development/AUTO-UPDATE.md).

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

Output: Full/Slim EXEs, signed update-manifest.json/update-manifest.sig and SHA256SUMS.txt under artifacts/releases/v0.4.1/win-x64/. Publish requires the existing local DPAPI update-signing key matching the public key in assets/updates. It downloads Microsoft runtime packs from nuget.org into ignored .local/release-nuget. No third-party app packages, global NuGet source changes or PATH changes are required. The script refuses to overwrite an existing release destination.

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
- tests/PrimaryWallpaperAgent.Tests — 12 automated suites.
- scripts — build, test, publish and diagnostics.
- assets/branding — icons and reproducible source artwork.
- docs/development and docs/testing — implementation and evidence.
- artifacts and .local — ignored packages, output and local evidence.

Private settings, photos, monitor snapshots and trial plans are excluded from source and release assets.

## Preview limitations and evidence

The user previously verified primary switching, hotplug 3→1→3 and policy persistence across Virtual Desktops. On 20 October the user reported normal launch and expected wallpaper behavior. Detailed scenario results are still being collected. Physical-console mapping/recovery, projector, more than three physical displays, DPI transitions and startup after logon still need validation. Tests with 1/3/5 simulated displays do not establish hardware results.

- [v0.4.1 release notes](docs/releases/v0.4.1.md)
- [Deployment and startup](docs/development/DEPLOYMENT.md)
- [Updates and distribution variants](docs/development/AUTO-UPDATE.md)
- [v0.3.1 activation / retention / About](docs/testing/MAINTENANCE-TEST.md)
- [Testing index](docs/testing/README.md)
- [Single-file / first-run verification](docs/testing/RELEASE-TEST.md)
- [Immediate settings / Undo / tray](docs/testing/INSTANT-SETTINGS-TEST.md)
- [Compact Settings / click-help](docs/testing/COMPACT-SETTINGS-TEST.md)
- [Pending physical-console checks](docs/testing/PENDING-TESTS.md)
- [Branding preview](assets/branding/Preview.png)
