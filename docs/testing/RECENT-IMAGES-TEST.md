# Recent picture recovery

## Behavior

Settings displays up to four distinct pictures, including the current one, ordered by most recent successful selection. The current thumbnail has an accent border. Hover/focus accessibility descriptions identify filenames/current selection. A click explicitly applies the picture using the existing settings transaction, obeys the current primary, and resumes a paused agent on success.

Records refer only to app-owned cached assets with SHA-256 verification. They persist as an optional RecentImages property in additive schema-3 settings; old settings without that property remain readable and expose the current configured image. The unconfigured first-run placeholder is excluded.

No old asset-folder scan or automatic import occurs. Repeated selections move an existing item to the front. Failed transactions restore exact settings/history bytes. Preference-only changes do not reorder history. Undo retains its existing one-level behavior and updates recency after a successful undo.

Only four references are listed; this change does not delete historical asset files or alter existing backup/retention rules. History entries do not add trusted wallpaper aliases. Unknown external pictures still pause the agent until a deliberate selection.

The current saved picture can be reapplied without calling GetWallpaper or reopening the original source file. If Windows points at a missing file, the UI explains the unavailable source and directs the user to Recent pictures or Browse. The original exception remains in CONFIG_UI_ERROR for diagnostics.

## Verification

scripts/Test.ps1 now runs 13 suites. The recent suite uses generated pictures, isolated settings, and fake desktop/startup backends.

- First-run placeholder exclusion; four-entry ordering/deduplication; current cached image for pre-upgrade settings.
- Failed image/settings writes restore history and exact prior bytes.
- Relaunch and selection after deleting fixture original files.
- Actual v0.4.1 Full EXE --package-check reads new history-bearing settings without changing their bytes (rollback compatibility).
- Asset path/hash validation, changed cache rejection on click, and disabled damaged-history thumbnails.
- Thai/English controls fit at normal/minimum sizes with no scrolling or preview overlap.
- Agent timer integration: three configured displays change to one reporting a missing Windows cache; guard pauses; Use primary image reports the missing file; current cached thumbnail reapplies and resumes; three displays/new primary are followed afterward.

Run the focused suite after Build.ps1:

```powershell
.\artifacts\bin\PrimaryWallpaperAgent.Tests\debug\PrimaryWallpaperAgent.Tests.exe recent
```

Ignored evidence is generated under artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/recent-images-test-* (result.json, recent-en.png, recent-th.png, and isolated agent logs).

## Remaining manual check

In the Windows 10 Hyper-V Enhanced Session that exposed the issue, repeat the multi-monitor/full-screen transition, then select the current thumbnail after pause. Confirm visible wallpaper, resumed operation and history after restart. Automated fixtures establish the recovery path; they do not reproduce the guest Windows/driver behavior.
