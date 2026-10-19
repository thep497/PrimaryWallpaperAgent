# Compact Settings and click hints — 19 October 2026

Status: approved to continue and publish by the user; included in v0.3.0 preview.

- Removed persistent explanatory paragraphs and the scroll container from Settings.
- Four ? buttons reveal localized click-help for wallpaper/image commands, language, polling and Start with Windows.
- Popup supports its close button and dismisses via normal dropdown outside-click/Escape behavior. Accessible names/descriptions are localized.
- Main window keeps status, preview, image filename, action controls, preference controls and feedback.
- Undo image is in the footer with View log and Close.
- Long errors have their own ? details button; the main feedback stays compact.
- Existing immediate-save behavior, image-only Undo and checking while Settings stays open are preserved.
- Startup implementation and publish mode are unchanged; see ../development/DEPLOYMENT.md.

## Verified

- Debug build: zero warnings/errors.
- Full policy/config/localization/polling/branding/instant/compact/window suites passed.
- Compact UI tests cover Thai/English at client sizes 700x580 and 640x560.
- No horizontal/vertical scrollbars; interactive controls fit within the client area, and preview does not overlap actions.
- All four click-help topics open with the expected localized content and close successfully; opening help invokes zero setting changes.
- Screenshots of both main languages and Thai image help were visually inspected.
- All tests use generated images and fake desktop/startup; no real wallpaper setters or registry writes.
- Release native read-only smoke passed: 4 samples, 25.06 seconds, zero repairs, exit 0. Existing settings/current image/black image hashes and native snapshot (except CapturedAt) matched before/after.
- Actual package log confirmed CONFIG_VISIBLE; nativeVisible=True, followed by a normal CONFIG_CLOSED event. The window was already closed before external control inspection, so actual-window scrollbar inspection was not claimed; the no-scroll verification is from the automated UI fixtures.

Extreme text scaling or smaller windows outside the checked sizes are not claimed verified. Physical monitor and DPI-transition checks remain pending in PENDING-TESTS.md.

## Evidence (ignored)

- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/compact-self-test-*/result.json and config-*.png/help-*.png
- .local/compact-20261019/package-before.json
- .local/compact-20261019/native-before.json and native-after.json
- .local/compact-20261019/smoke-result.json
