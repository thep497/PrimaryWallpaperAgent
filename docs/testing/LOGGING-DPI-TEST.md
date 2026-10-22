# Logging, support export and Settings DPI review

22 October 2026. Development/review build; pending user Remote and physical-console retests. Public release remains v0.4.3.

## Behavior

- Serilog 4.4.0 and Serilog.Sinks.File 7.0.0 write under runtime/logs/agent-YYYYMMDD.log. Restarts append; size rolls use numbered chunks at about 2 MiB. Dates follow local time using invariant filename formatting. Entries include offset, severity, event, version and run ID.
- Existing session evidence and legacy startup logs remain readable. Shared whitelist-based retention covers both layouts, targets 30 days/50 MiB, skips active/locked files and never traverses links or unrelated files. Actual cleanup may lag while files are locked.
- Each saved snapshot has an independent timestamp/event ID and diagnostic envelope. Failed repairs cannot overwrite an earlier before-N file. Repair before/after records share an attempt ID.
- Mapping failures capture state when it changes, throttle duplicate notices and log recovery. Cached API entries remain separate from active mapping validation. Existing mapping/mode and manual-pause guards remain enforced.
- About offers Open log folder and Export diagnostics in Thai/English. Export creates a new ZIP with seven days of legacy/new logs, a sorted timeline, selected display states, a manifest and third-party notices. It omits images/full settings backups and tokenizes paths/device IDs. Limits: 500 input files, 4 MiB/file, 50 MiB input. Existing output files are never overwritten.
- Settings reserves its footer outside the content viewport. Design DPI is applied after construction; buttons/selectors are measured after DPI changes. Opening fits the compact content within the working area. A separate minimum viewport allows vertical/horizontal resizing; only the content viewport scrolls when the current window is too small, while the footer remains available. Thumbnail and single-line caption rows have explicit DPI-scaled heights. UI telemetry records actual DPI/client/working-area measurements.

## Evidence

All 17 suites passed with 0 build warnings/errors. Diagnostics tests additionally cover concurrent/restart appends, live-file export, bounded messages, age pruning, unique failed-attempt snapshots, legacy timelines, privacy and destination preservation. The DPI fixture sends native WM_DPICHANGED messages for 96/120/144/192 DPI and checks controls, button text, selector widths and short-screen footer visibility. Synthetic DPI messages do not establish real Remote behavior.

A private copy of the supplied archive produced the same 263 timeline events from seven app runs. Export contained 53 selected source files and no full settings backups; the source archive checksum was unchanged. Raw user paths/monitor IDs and the archive are excluded from source control.

The 21 October 09:02-09:07 hotplug case is reconstructed with synthetic IDs and a remembered old-trial image path on a new 4K secondary. The prior primary stays selected and only the new secondary becomes black; the old path is not trusted on later unchanged-topology changes.

Bundled Full/Slim package checks also load the logger and generate a support ZIP in explicit fixture data. No actual wallpaper/startup changes occur.

## Build and retest

NuGet.Config enables only nuget.org for this repository. Common.ps1 points the process package cache to ignored .local/release-nuget; no global NuGet/PATH configuration is changed. Third-party notices are embedded in the single-file app and included in support exports.

1. Exit the old app and launch the review EXE.
2. Open Settings immediately, reconnect Remote, change its resolution/DPI and reopen Settings. Check footer, image-action text and selectors.
3. About -> Export diagnostics, choose a new ZIP filename, then send that one file.
4. Restart the app and export again: prior runs must remain present.
5. Retest a new physical external monitor and current-primary behavior separately.

References:
- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/forms/autoscale
- https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview
- https://github.com/serilog/serilog-sinks-file#rolling-policies

## Earlier review package result (superseded)

- Version: 0.4.5-review; Full/Slim each passed fresh and reopened standalone checks. Full was checked with DOTNET_ROOT pointing to a nonexistent fixture runtime. Both loaded Serilog/file sink and created a support ZIP from explicit fixture data, with unchanged settings and zero wallpaper/startup registry calls.
- Full: artifacts/review/diagnostics-dpi-20261022/full/PrimaryWallpaperAgent.Tray.exe; SHA-256 0305A75E1A8DE4C54FE3F43F606FA27F0776918F3ACBABEE669EF9287CBC1FDD.
- Slim: artifacts/review/diagnostics-dpi-20261022/slim/PrimaryWallpaperAgent.Tray.exe; SHA-256 44C4388A623923758B4C0EEE1C683CC91DFFE1C5F060A78E09525627E9DF832E.
- All-suite evidence: hotplug-test-869d87b2, diagnostics-test-c19c5afa, dpi-test-e057a292. Final live-export/privacy suite: diagnostics-test-574913fd.
- Real supplied-archive export check: ignored .local/runtime-export-check-20261022/support-redacted.zip; 263/263 events, 53 included source files, 56 ZIP entries including timeline/manifest/notices. Original archive unchanged.
- Source remains uncommitted pending user review. The real running app and original user data were not stopped, replaced or modified by this development run.
## Settings regression correction - review.2

The supplied 22 October support ZIP reproduced exactly in a fixture at native 144 DPI (150%), working area 1920x1128, three recent entries and a 64-character cached-image filename. Before correction: required client height 1362, actual client height 1072, recent strip height 752 and caption height 598. The old sizing helper also promoted the inflated preferred height to MinimumSize, preventing vertical shrinking. Earlier tests did not cover this combination.

- The recent-image strip now has explicit thumbnail/caption rows and GrowAndShrink sizing, with a single ellipsized filename below the first thumbnail. Long filenames no longer expand the last row.
- Settings fits its content on first open. A separate 600x320 logical client minimum permits user resizing. Resizing does not expand the window back to its preferred height. Only the content viewport scrolls when needed; footer actions stay visible.
- Live resize updates scrolling without logging each mouse movement. Open, completed resize, DPI/display and content-change telemetry includes minimum and content sizes.
- The identical fixture now requires 1050x799 client pixels with no scrollbar. Recent strip height is 189, caption height 35. Shrinking to 649 or the 480-pixel minimum client height works; the last preference remains reachable by scrolling. Expanding restores the scrollbar-free layout.
- English/Thai screenshots and geometry were inspected. All 17 suites passed (0 warnings/errors); final DPI regression evidence is artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/dpi-test-acbae787. Native WM_DPICHANGED round trips cover 96/120/144/192 DPI. These checks do not replace the user's real Remote/display retest.
- Review version 0.4.5-review.2 is kept separately under artifacts/review/settings-layout-20261022-r2/{full,slim}. The old review artifacts, running application and user settings remain untouched. No commit or GitHub release until user review.

Full/Slim review.2 fresh/reopened standalone package checks passed; settings unchanged, zero wallpaper/registry calls. Hashes and fixture evidence are in the ignored review package-check.json. Minimize/restore also preserves the compact window size.

## User approval and release preparation

22 October 2026: the user tested review.2 and reported OK, then authorized commit and GitHub publication. This confirms the reviewed Settings behavior; it does not close every physical hotplug/Remote reconnection checklist item. These reviewed changes are included in v0.4.5 Preview. Earlier review notes above remain historical evidence.
