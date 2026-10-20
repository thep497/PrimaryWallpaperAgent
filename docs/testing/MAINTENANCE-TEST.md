# v0.3.1 maintenance and activation — 20 October 2026

## Implemented

### Existing-instance activation

InstanceCoordinator uses a per-user/per-Windows-session mutex and named pipe. PipeOptions.CurrentUserOnly restricts the pipe to the same user/elevation. Only two single-byte requests are accepted: show Settings and quiet startup. Requests queue onto the WinForms UI thread; no new desktop writer is created.

The caller grants foreground activation permission to the verified pipe server process when Windows permits it. A bounded acknowledgment handshake ensures Windows does not discard the reply at Disconnect. Broken/invalid/disconnected clients are reset so later activation still works. Connect/read operations have timeouts and shutdown cancels pending I/O.

The existing global wallpaper-writer mutex is retained to protect older tray versions and console tools. If an older instance owns it, a localized message asks the user to exit that instance. An old v0.3.0 process cannot receive the new protocol.

### Log retention

Only runtime/startup-error.log, its previous file, and recognized direct files inside recognized agent session directories are managed. Cleanup never recursively traverses or deletes. Reparse directories/files, nested contents and unknown files are skipped. Settings and assets are outside the managed tree.

Policy: 30 days; target 50 MiB total managed files. Text logs rotate before exceeding 2 MiB and retain one previous file; oversized single text entries are summarized. The active log and its session directory are protected. Locked files are preserved; cleanup is best effort rather than force deletion.

Cleanup runs at session startup, after diagnostic saves/text-log rotation and hourly while running. Retention values are fixed maintenance defaults, rather than new settings controls.

### About

A version/About link fits in the existing Settings header without adding rows or scrolling. About shows the running assembly version and actual data root. The two actions open the fixed project Releases URL and the current data folder. About is modal, while the existing guard timer continues through the normal modal message loop. No preferences or wallpaper are changed by these actions.

## Verification

- Debug build: zero warnings/errors.
- All 11 suites: policy, config, localization, polling, branding, instant, compact, release, maintenance, instance, window.
- Cross-process fixture verifies the mutex blocks a second agent, quiet startup, malformed/disconnected client recovery, show Settings, restore minimized Settings, reopen closed Settings and unchanged settings/wallpaper/startup.
- Retention fixture verifies age and oldest-first budget together, active and empty-current-session preservation, unknown/nested data preservation, unchanged settings/assets, locked-file retry and bounded rotation.
- A Windows junction is created in an ignored isolated project fixture. The regular managed log is removed while the junction target sentinel remains unchanged.
- About fixture verifies both languages, actual version/data path, intended action targets, controls fitting the window and no preference saves.
- Thai/English About and Settings screenshots were inspected. Existing compact tests also cover 700x580 and 640x560 Settings without scrolling.
- A popup test was rerun sequentially after a transient focus dismissal during overlapping test process activity; the final full suite runs sequentially.
- The self-contained release is copied alone to a renamed EXE under a path containing spaces/Thai characters; isolated first-run/reopen, both languages, Settings/About and icons are verified with shared runtime lookup disabled.

The test backends do not call real wallpaper setters or write the startup registry. Different accounts/elevation, Virtual Desktop foreground-switch policy and physical DPI transitions are not claimed tested.

## Evidence (ignored)

- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/maintenance-self-test-*/about/*.png
- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/instance-self-test-*
- .local/retention-junction-*
- .local/release-verification-*
- .local/release-download-check-v0.3.1/verification.json

Physical-console follow-up remains in PENDING-TESTS.md; the user's general successful-use report does not imply each individual scenario was tested.
