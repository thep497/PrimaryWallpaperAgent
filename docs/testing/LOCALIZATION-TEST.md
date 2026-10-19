# Thai / English UI — 19 October 2026

Status: user confirmed Thai/English behavior on 19 October 2026 and authorized commit/next chunk.

## Behavior

- Config provides ไทย (Thai) and English. Selecting a language previews the form immediately.
- Save persists the selection and updates tray menus/status/notifications without restarting.
- Cancel discards the preview. Existing settings without Language open in Thai.
- Changing only language/startup preferences saves settings without applying a wallpaper plan. Selecting an image explicitly retains the normal Save and apply image transaction.
- Language is additive to the existing settings schema. Image references, current-source alias, hashes and accepted formats are preserved.
- UI text uses English neutral resources plus a Thai satellite assembly, with 59 matching keys.
- Technical logs remain English and retain exception details. Data serialization and process culture are unchanged.
- Display name is Primary Wallpaper Agent. Physical executable/namespace/startup identity remain compatible.

## Verified

- Debug solution build: zero warnings and errors.
- Policy, config, localization and hidden-window regression suites passed on the final full run.
- Localization tests cover resource parity/fallback, legacy defaults, persistence, preview/cancel, localized errors, image-adoption requests and unchanged culture.
- A forbidden desktop fake verifies zero Capture/SetWallpaper calls on preference-only save. Startup fake verifies zero registry writes on language-only save.
- Injected settings-write failure restores the exact previous bytes.
- Thai and English Config screenshots were inspected for overlap and clipping.
- Release package contains th/PrimaryWallpaperAgent.Tray.resources.dll.
- Actual package smoke: Thai, 4 samples, 5.07 seconds, zero repairs, exit 0.
- Isolated copy of Release package with English settings: 4 samples, 5.09 seconds, zero repairs, exit 0. User settings were not changed to run this check.
- Native snapshots before/after the two smoke runs match in every field except CapturedAt.
- Publish preserved SHA-256 of user settings, current photo and black image.
- Actual Config opened with a nonzero window handle, title Primary Wallpaper Agent · ตั้งค่า, and CONFIG_VISIBLE; nativeVisible=True.

## Observations and limits

One initial config-suite run hit a File.Replace IOException. Its rollback ran; a separate rerun and the later complete suite passed. The underlying filesystem cause is unconfirmed; no production retry or configuration change was added.

The live environment reports one GDI display named WinDisc with no display endpoints; wallpaper API entries are unmatched. Config capture therefore reports incomplete/unstable active mapping and displays the waiting status. The cause of this environment state was not established. Config opened successfully, but the native smoke results establish safe read-only execution only, not successful active-monitor mapping or a fresh physical-monitor/Virtual Desktop regression test.

User confirmed the language feature works as intended. Physical-console tests after Remote are tracked in PENDING-TESTS.md. Poll remains 1000 ms with two stable samples. Custom icons, configurable polling and Spotlight are outside this chunk.

## User review

1. Choose English in Config, then Save settings.
2. Check tray labels and reopen Config: English should remain selected.
3. Preview Thai and Cancel: reopening should retain English.
4. Save Thai if preferred. Language-only changes should leave the displayed image unchanged.

## Local evidence (ignored)

- .local/localization-20261019/package-before.json
- .local/localization-20261019/native-before.json
- .local/localization-20261019/native-after.json
- .local/localization-20261019/smoke-results.json
- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/localization-self-test-*/result.json and config-th.png/config-en.png
- Timestamped runtime folders inside the actual package and isolated english-smoke copy
