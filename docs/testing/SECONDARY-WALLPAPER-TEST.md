# Primary and secondary wallpaper verification - v0.5.0

23 October 2026. The user approved the corrected 0.5.0 review and authorized v0.5.0 Preview publication. Local review results below remain evidence; production packages are verified separately.

## Behavior

- Settings edits two roles: Primary display and All secondary displays. Switching the viewed role never writes settings or wallpaper. The same recent-image strip is reused; only one selector row is added.
- Each role has one selected picture. SecondaryImage=null means the permanent black default. Every non-primary active display uses the secondary selection; assignments follow Windows primary changes and monitor connections rather than stored monitor IDs.
- Primary retains Use primary image from Windows. Secondary offers Use black. Selections apply/save immediately. The secondary success message also covers preconfiguration before a monitor is connected.
- One four-picture recent library is shared. Each role's current picture is independently referenced and appears first when viewing it, including after falling out of the library. Black is a permanent choice outside image history. Cached files are content-addressed and original files are not needed after selection.
- Each role has one Undo until process exit, surviving Settings close/reopen. Undo preserves the other role and current language/theme/poll/startup preferences.
- Selecting a secondary picture before first primary selection saves it without activating the watcher. With only one display it can be prepared without rewriting the primary. Editing secondaries preserves an unrelated Windows primary image; an unmatched full policy is not resumed by that edit.
- Known complete monitor transitions retain the hotplug rebase. Unknown wallpaper on the unchanged monitor set still pauses; history is not trusted as current aliases. Fill and the same settings across Virtual Desktops remain in effect.
- Per-monitor apply skips assignments already correct, retaining topology/readback and rollback checks. Failure injection tests target actual changed assignments.

## Settings compatibility and backup

Schemas 1-3 read with the existing primary and black secondary default, without rewriting them on open. An explicit image edit writes schema 4 with an optional SecondaryImage record. Primary fields and the actual black asset remain separate. The record contains one cached file/hash/label/source alias, not per-monitor preferences.

Before the first upgrade write, SettingsStore preserves the exact original bytes as settings-before-schema4.json beside settings.json. This backup is not rotated with logs or exported in support ZIPs. It is never overwritten. It remains useful even when a failed upgrade transaction restores the old settings.

v0.4.5 cannot open schema 4. To return to it, first exit the running app, preserve the schema-4 settings separately, and restore the saved pre-upgrade settings.json. New secondary selections are not represented in the old format. Do not run two app versions at once.

## Automated verification

The secondary suite uses generated pictures, fake startup/desktop backends and real ConfigForm/AgentContext message loops. No native wallpaper setters, startup registry writers, Windows DPI changes or system theme changes are called.

Coverage: legacy defaults and exact backup; first-run secondary preconfiguration/Undo; one, three and five displays; role swaps and hotplug; no redundant setters; shared history with pinned current selections; independent Undo/preferences; restart/cache-only recovery after source deletion; malformed secondary metadata; setter/save rollback; preserving unknown primary wallpaper while editing secondaries; history-alias rejection; Thai/English light/dark UI; black thumbnail, shared recent selection and actual watcher integration.

The DPI suite covers native 96/120/144/192 messages, bounded long captions, complete controls/footer at the reported 150% working area, vertical/horizontal resizing and a manually reduced viewport remaining reduced after DPI changes. Small native rounding after a DPI round trip is allowed. Simulated messages do not establish real Remote or hardware results.

## User review

1. Exit v0.4.5 from its tray, then launch the 0.5.0 review EXE. Existing primary and black secondaries should remain as before.
2. Open Settings, switch between Primary display and All secondary displays: viewing alone must not alter wallpaper.
3. Select a secondary picture from Browse or recent pictures. Every secondary should use it; primary stays on its selected picture. Try Use black and Undo.
4. Change a primary picture, then a secondary picture, and Undo each role: the other role must retain its selection.
5. Switch Windows primary and connect/disconnect a monitor. Only the current primary uses the primary picture; every other active monitor uses the secondary picture.
6. If convenient, disconnect to one monitor, prepare a secondary picture, and reconnect. Check Virtual Desktop switches and Remote separately.
7. Check Thai/English, Light/Dark/System and window resizing. Export diagnostics from About if a problem occurs.

Local review outputs: artifacts/review/secondary-wallpaper-20261023/{full,slim}/PrimaryWallpaperAgent.Tray.exe. Full includes runtime; Slim requires .NET 10 Windows Desktop Runtime x64. Package checks run only on isolated generated data.
## Local verification result

All 18 suites passed with zero build warnings/errors. Targeted secondary/config/instant/hotplug tests also passed after the final trusted-primary transition guard; secondary/config tests passed after atomic pre-upgrade backup handling. Final secondary evidence: secondary-test-09eac6d9; full-suite DPI evidence: dpi-test-fc9cba7a. Full/Slim standalone checks passed fresh/reopened data and copied generated schema-4 data with a custom secondary picture, unchanged settings and zero native wallpaper/registry calls. Exact package hashes and evidence directory are in the ignored review package-check.json.

## User approval and release preparation

23 October 2026: the user reported the review looked OK and authorized commit and publication. The approved feature is included in v0.5.0 Preview. Earlier local-review results above remain historical evidence; this approval does not close every physical/Remote checklist item.

Release validation on 23 October: the current 0.5.0 source passed all 18 suites. One earlier recent-picture transaction test encountered a Windows sharing violation; the unchanged suite passed twice on fresh generated fixtures before packaging. No runtime change was made to hide that failure.
