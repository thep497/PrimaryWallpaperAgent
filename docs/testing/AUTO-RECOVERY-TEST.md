# Automatic recovery and Windows primary import

Development change, 29 October 2026. Physical display verification remains pending.

## Behavior

- An observed primary-role/display/session/bounds transition retains saved role images, waits for complete matching samples and at least two seconds since the observed topology change, then restores the current primary and shared secondary roles. It remains a transition until repaired assignments have also settled for two seconds.
- Outside a display transition, a new readable primary picture stable for at least two seconds is validated and copied into owned assets. A fresh display/primary/path check and file-hash check precede settings commit. Secondary selection, preferences and startup registration are preserved. Undo can restore the previous primary.
- Importing the observed primary does not write wallpaper or startup registration. The verified Windows path may remain on primary. A later role change uses the durable cached image. Byte-identical aliases do not rewrite settings; external file size/last-write metadata makes same-path replacements observable.
- Secondary-only Windows changes are not imported. All secondary displays return to the app's selected shared image or black. Windows set-for-all imports only the primary role and restores secondary policy.
- Capture/mapping, unsupported-mode and setter/readback failures retain monitoring and retry with exponential backoff from one second to a maximum of 30 seconds. Each failed attempt requires fresh matching samples. Eight successful repairs in a rolling minute cause a cooldown until the budget permits another repair.
- Manual Pause retains its original meaning. No display writes occur with incomplete or unstable active mapping. Unsupported modes are not changed globally. Explicit settings recovery failures retain the existing intervention behavior.
- Timeline events include DISPLAY_TRANSITION_BEGIN/END, WINDOWS_PRIMARY_CANDIDATE/ADOPTED, MAPPING_WAIT/RECOVERED, MODE_WAIT, RECOVERY_RETRY/RESUMED and REPAIR_COOLDOWN. Snapshots are saved at changed failures and periodic retry notices, not on every polling tick. Existing rotation, retention and export apply.

## Boundaries

The current read APIs expose state, not an authenticated origin of a wallpaper change. A picture change concurrent with an observed display transition is handled as transition recovery. Stable primary changes afterward are imported regardless of whether Windows, another program or a person caused them. Changes entirely between polling samples cannot always be attributed. Cold startup has no prior topology baseline; a primary matching the saved secondary before the first accepted check is rebased to the saved primary, rather than imported. A different unfamiliar cold-start picture still cannot be attributed to a cause. These are explicit limits, not claims of perfect user-action detection.

The app keeps one primary image and one shared secondary image across Virtual Desktops; it does not introduce per-desktop settings. Same-path changes rely on external file size/last-write metadata to invalidate the decoded image cache; deliberately preserving both metadata values can conceal a replacement. Different encodings of the same visual picture are not assumed to have equal byte hashes.

## Automated checks

Run `scripts/Test.ps1`, or the test executable with `recovery` for focused deterministic-clock checks. Tests generate their own pictures and use fake display/startup backends. No native wallpaper writes or startup registry writes are made.

The recovery suite covers delayed unfamiliar paths during primary-only switching, primary-role following, primary-only and all-display Windows image changes, secondary-only override, same-path content replacement, equal-byte aliases, durable caching, more than ten mapping failures, setter failure and recovery, mode recovery, repair cooldown, unreadable images becoming available, manual Pause/Resume, startup isolation, stale-primary rejection, exact settings rollback and preservation of externally edited settings. The existing hotplug message-loop test covers 1-3-1-3 and same-count replacement, then automatic retry on an unreadable primary.

## Physical retest

1. Exit the older instance and launch the review EXE. Wait for Running.
2. Switch Windows primary/secondary several times, including while Settings is open. The saved images should follow roles and no automatic Pause should latch.
3. With displays settled, change only the primary picture in Windows. Wait for import; reopen Settings and verify the new primary thumbnail. Switch primary again and verify the imported image follows it.
4. Change a secondary picture in Windows; verify every secondary retains the app's configured shared image. Set one image for all displays in Windows and verify only primary adopts it.
5. Disconnect/reconnect, return from Remote, and switch Virtual Desktops. Record actual results separately; simulated checks do not establish these hardware outcomes.
6. Export diagnostics if behavior differs. Do not reset settings first: transition/candidate/retry events are the useful evidence.

## Verification result, 29 October 2026

- Final Debug build: zero warnings/errors.
- Focused recovery tests passed, including missing imported source/alias files and a role swap before the first polling sample.
- Final collected run: **19 of 20 suites passed**. All wallpaper, hotplug, recovery, configuration, recent-image, localization, theme, diagnostics, release/bootstrap, maintenance, activation, update and native-window suites passed.
- The remaining DPI suite fails at `User can shrink the window vertically`. The unchanged HEAD source was exported and built under `artifacts/review/recovery-baseline-20261029/source`; its DPI test fails at the identical assertion with identical reported layout dimensions. This is a confirmed baseline failure in this environment, left outside the wallpaper change. DPI is not claimed as passed.
- Per-suite logs and machine-readable final results: `artifacts/review/recovery-20261029-final-tests/test-results.json`.
- Full single-file review EXE: `artifacts/review/recovery-20261029-final/PrimaryWallpaperAgent.Tray.exe`, product version `0.5.2-review`, 116790125 bytes.
- SHA-256: `661B39816809D9FF3C65F81EAB478E953BB6D4B057A2678116BD786172300E3F`.
- Fresh/reopened read-only package checks both passed with separate fixture data, unchanged settings, Thai/English resources, zero native wallpaper calls and zero startup registry writes. Reports and checksums accompany the EXE. These review checks did not replace the running installation.
- These checks exercise the proposed causes and recovery behavior using generated inputs. No log from the user's original failing runtime was read, so they do not identify the exact trigger of that past incident. Physical switching remains for the user's retest.
