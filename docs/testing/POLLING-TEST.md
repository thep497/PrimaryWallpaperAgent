# Configurable polling — 19 October 2026

Status: user confirmed the polling feature passed and authorized commit/next chunk on 19 October 2026.

## Behavior

- Config supports 0.5, 1, 2 and 5 seconds. Existing settings default to 1 second.
- Save applies the new interval to the running WinForms timer and persists PollIntervalMs. No restart is needed.
- Changing only polling/language/startup preferences uses Save settings without applying a wallpaper plan.
- Cancel leaves the saved interval unchanged. Switching UI language preserves the interval selected in the preview.
- Explicit image adoption still uses Save and apply image and preserves the selected polling preference.
- Unsupported manually edited interval values fail validation before the agent starts its timer.

## Timing and tradeoffs

A shorter interval schedules more display checks; a longer interval schedules fewer checks and can delay response to topology changes. The default remains 1 second. No measured CPU/battery improvement is claimed.

The existing guard still requires matching fingerprints in two consecutive samples before repair. Under an otherwise idle, stable environment, detection is approximately one to two polling intervals after a change, plus API/work time; this is not a response-time guarantee.

The repair limit remains eight repairs in a rolling 60-second window. The existing read-error policy remains ten consecutive failed captures; a longer interval can therefore take longer to enter the paused state. Saving only a preference preserves the prior paused/error state and does not resume a policy conflict.

The read-only smoke duration scales with the configured interval so even the 5-second choice can collect multiple samples.

## Verified

- Debug build: zero warnings and errors.
- Policy, config, localization, polling and hidden-window suites passed.
- Polling tests cover legacy settings, invalid intervals, all four selections, persisted/reopened selection, cancel, translated labels, explicit image adoption and exact-byte rollback after an injected settings-write failure.
- Poll-only transactions make zero desktop API calls and zero startup writes in the fake backends.
- Existing A-B-A image-adoption and policy/rollback tests pass.
- Thai and English screenshots were visually inspected: polling control, hints and footer buttons fit.
- Release package was built while the verified old Tray process was stopped. Settings/current image/black image hashes were preserved.

## Native smoke / Remote limitation

Read-only native smoke tests use isolated Release-package copies with fixture settings for each interval. They read the actual Windows state and never call wallpaper setters. Runtime summaries/logs record the interval actually assigned to the timer.

The user confirmed this session is Remote. Windows currently reports WinDisc without usable real-monitor endpoints. These tests can establish safe execution and timer cadence; they do not validate real-monitor mapping or physical response time.

Physical-console follow-up is scheduled in the project checklist for 20 October: [PENDING-TESTS.md](PENDING-TESTS.md). No external notification has been scheduled.

| Interval | Language | Samples | Repairs | Elapsed |
|---|---|---:|---:|---:|
| 500 ms | th | 9 | 0 | 5.07 s |
| 1000 ms | en | 4 | 0 | 5.06 s |
| 2000 ms | th | 4 | 0 | 10.06 s |
| 5000 ms | en | 4 | 0 | 25.06 s |

All four native smoke processes exited 0. Native snapshots matched except CapturedAt; user settings and image hashes were unchanged. The actual package reopened Config with nativeVisible=True and loaded the existing 1000 ms default.

## User review

1. Choose 2 or 5 seconds and Save settings.
2. Reopen Config and confirm the choice persists.
3. Select another interval and Cancel; reopening should show the saved choice.
4. Return to 1 second if preferred.
5. Check primary/hotplug/Virtual Desktop response at the physical console tomorrow.

## Local evidence (ignored)

- .local/polling-20261019/package-before.json
- .local/polling-20261019/native-before.json and native-after.json
- .local/polling-20261019/smoke-results.json
- .local/polling-20261019/smoke-{500,1000,2000,5000}/runtime/
- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/polling-self-test-*/result.json and config-th.png/config-en.png
