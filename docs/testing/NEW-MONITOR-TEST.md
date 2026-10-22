# New monitor wallpaper recovery

Development fix, 22 October 2026. Pending user verification on physical monitors.

## Cause and change

The tray policy previously treated an unfamiliar wallpaper on any attached monitor as a user selection, including Windows restoring a remembered wallpaper while a monitor was being connected.

The agent now retains the topology from its last successful policy check in memory. After two matching complete snapshots, an added, removed or replaced active monitor ID in the same session permits a fresh policy application using the existing saved picture and the current Windows Primary Display. Every other active monitor receives black.

This applies even when Windows has restored unfamiliar paths on existing monitors during the same transition. Those paths are not adopted as the selected image, added to trusted image aliases or saved in recent history. A successful check consumes the transition; a later unfamiliar picture on that same monitor set still pauses. Settings bytes remain unchanged.

Primary-only changes, resizing and enumeration order do not authorize unfamiliar images. Known-image primary changes continue to work. Picture/Fill, slideshow, complete mapping, fresh-snapshot checks, readback/rollback and repair-rate limits remain enforced. Detached cache entries are not write targets.

The observed monitor baseline is not persisted. On a cold start with entirely unfamiliar assignments and no accepted baseline, the agent still asks for an explicit image choice. If already paused, select the existing saved thumbnail once, then allow the agent to reach Running before testing connections. Startup and manual pause do not gain automatic image adoption.

Windows does not identify the cause of each returned wallpaper path in this logic. A manual wallpaper change concurrent with a monitor-set transition will be treated as part of that transition. The exception is limited to the observed transition rather than a general time-based wallpaper override.

## Automated verification

The hotplug suite uses generated image assets and fake wallpaper/startup backends. It covers 1-3-1-3 with restored unfamiliar paths, notebook and external primary, same-count replacement, five active displays, detached cache entries, no repeated writes, exact settings/history preservation, cold-start/session boundaries, primary-only/resize/order changes, incomplete/unstable mapping, mode changes and rollback on setter failure.

A real AgentContext message loop verifies automatic recovery without Settings interaction, followed by manual image-change pause on the unchanged set. No native wallpaper setters or startup registry writers are used.

Run scripts/Test.ps1 (15 suites) or the test executable with hotplug.
Evidence is ignored under artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/hotplug-test-*/.

## User retest

1. Exit the previous instance, launch the review EXE, and restore the saved thumbnail once if it is currently paused. Wait for Running.
2. Connect an external monitor not previously used with the app. After Windows settles and two polling samples agree, only the Windows primary should have the selected image.
3. Disconnect/reconnect, test another Windows primary and, if possible, replace a monitor while keeping the same count.
4. With the same monitor set, change the image in Windows. The agent should pause rather than replace that deliberate choice.
5. If recovery fails, use View log and report the section around HOTPLUG_REBASE / PAUSED. Physical results have not been claimed by automated fixtures.

The older guard/physical test records remain historical evidence.

## Result on 22 October 2026

- Build: 0 warnings/errors; all 15 suites passed.
- Agent integration evidence: ignored runtime/hotplug-test-33611ad3.
- Full single-file review build 0.4.4-review passed fresh/reopened read-only package checks, with zero native wallpaper calls and registry writes.
- Review output: artifacts/review/hotplug-20261022/PrimaryWallpaperAgent.Tray.exe.
- SHA-256: D2B449C3A26032F0BDF049EC01667B6DE7C567193EC95B5225CEA2D03497F165.
- Source remains uncommitted pending user retest; public release remains v0.4.3.

## Release preparation

22 October 2026: this fix is included with the user-approved Settings/diagnostics changes in v0.4.5 Preview. The historical review results above remain unchanged. The full suite now has 17 suites; physical-monitor checklist items remain open unless separately confirmed.
