# Immediate settings and simplified tray — 19 October 2026

Status: approved to continue and publish by the user; included in v0.3.0 preview.

## User-facing behavior

- Language, polling and Start with Windows apply and persist as soon as their controls change.
- Choose and use image opens the file picker; confirming a valid file applies it immediately.
- Use primary image explicitly adopts the image currently assigned to the primary display.
- Undo image restores the previous successful image selection. It keeps the current language/poll/startup preferences and applies the primary-only policy for the current topology.
- Undo is one level, survives closing/reopening Settings, and is cleared on use or app exit. It is not a persistent history or redo feature.
- Close/Esc/title-bar close retain successful changes. There is no Save/Apply/Cancel command.
- Settings remains open after edits and shows success/error feedback.
- The initial scrolling layout was superseded by compact Settings without scrollbars. Explanations are available through click-help; see COMPACT-SETTINGS-TEST.md.
- Check wallpaper again has been removed from Tray. Remaining items: status, Settings, Pause/Resume, activity log and Exit.

## Runtime and transactions

The guard continues sampling while Settings is open, including native modal dialog message loops. Only a settings transaction temporarily suppresses guard ticks. Existing user/error pauses still apply.

LiveSettings holds current config bytes and the rolling expected startup state. Each successful change advances that baseline; successive on/off startup toggles no longer compare against a stale value captured when the window opened.

Preference changes never invoke desktop Capture/SetWallpaper through the settings transaction. Polling changes update the running timer without restarting the app. They preserve pause state and do not reset the repair-rate limit.

Explicit image adoption/Undo retain the prior behavior of resuming the agent after success. Undo uses validated cached assets, not a fresh read of an original external image path. Changing preferences alone cannot adopt or overwrite an image.

Each transaction writes unique local evidence names. On failure, the existing transaction restores prior settings/startup/wallpaper as applicable. Controls revert to committed values. Failed changes do not replace preview or undo history. Incomplete recovery or externally edited config pauses the agent for intervention.

Image read failures and display-mapping failures have separate localized messages. Remote mapping failure is not presented as a corrupt image file.

## Verification

- Final Debug build: zero warnings/errors.
- Policy/config/localization/polling/branding/instant/window suites passed.
- New tests use generated image fixtures, a fake desktop and fake startup registration; no real wallpaper setters or registry writes.
- Confirmed: immediate language/poll, repeated startup on/off, A-B-A image adoption, Close/reopen persistence, Undo across window reopen, image-only Undo, failed Undo preserving history, unchanged image originals, settings-write rollback, startup-write rollback, control reversion, external config preservation and invalid image rejection.
- Integration test runs the actual AgentContext timer and Config window with injected fake desktop/startup. It repairs secondary monitors, preserves a user pause through preference edits, resumes, and follows a primary switch while Config remains visible.
- Integration test checks exactly the five intended tray items, with no manual Check action.
- Hidden-start window reveal regression still passes.
- Release native read-only smoke passed: 4 samples, 25.05 seconds, zero repairs, exit 0, existing English/5000 ms preferences. Native snapshots matched in every field except CapturedAt and settings/current photo/black image hashes were unchanged before relaunch.
- Actual package reopened Config with nativeVisible=True. Physical display behavior remains pending user validation; the fake-desktop integration establishes checking while Settings stays open.

This session is Remote. Native smoke establishes read-only execution, not physical monitor mapping or actual wallpaper/Undo appearance. Physical-console follow-up remains in PENDING-TESTS.md for 20 October.

## Review steps

1. Change language/poll and close/reopen Settings; edits should persist immediately.
2. Check that Tray no longer has Check wallpaper again.
3. At the physical console, select a different image and use Undo image; other preferences should stay unchanged.
4. Leave Settings open and switch primary/hotplug/Virtual Desktop; automatic checking should continue.
5. Startup toggle changes the user's registration immediately. Use it only when actually opting in/out.

## Local evidence (ignored)

- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/instant-self-test-*/result.json and config-th.png/config-en.png
- .local/instant-20261019/package-before.json
- .local/instant-20261019/native-before.json and native-after.json
- .local/instant-20261019/smoke-result.json

Native inspection of the verified package window confirmed Close/Undo image controls and no Save/Apply/Cancel command. Cross-process control text was queried with WM_GETTEXT; GetWindowText alone was insufficient for these controls.
The actual Settings status reached Running while the window stayed open. This refers to the current remote display provider and does not substitute for physical-console testing.
