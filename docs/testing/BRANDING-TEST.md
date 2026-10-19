# App and tray icons — 19 October 2026

Status: user approved the icon design and authorized commit/continuation on 19 October 2026. Native icon-state interactions and physical DPI/theme follow-up remain in PENDING-TESTS.md.

## Artwork and product identity

- Official display/product name: Primary Wallpaper Agent.
- Large icon: a blue/teal landscape on the dominant primary screen with black secondary screens.
- At small sizes, the mark simplifies to two screens. The drawing does not bind policy to a center screen, a brand or a display number.
- Tray Running: cyan screen; Paused by user: amber pause bars; Attention/error: amber exclamation mark.
- Status text and notifications still explain the state. The icon is an additional signal.
- Transparent artwork uses a pale outer rim and navy inner rim for visibility on light/dark taskbars.
- Artwork is text-free and shared across Thai/English.

## Source and integration

assets/branding contains committed ICO files, an editable SVG review master and a PNG preview. scripts/Generate-Branding.ps1 is the reproducible geometry source. It uses installed Windows System.Drawing without downloading packages. Production builds consume the committed ICO files and do not execute the generator.

Application.ico contains 16/20/24/32/40/48/64/128/256 px PNG frames. Each tray variant contains 16/20/24/32/40/48/64 px frames.

ApplicationIcon embeds the main icon into the apphost executable. Embedded managed resources supply owned/cloned Icon instances for Config and Tray, so loose icon files are not required at runtime. Icons are disposed when their owners close.

AssemblyTitle and Product are the spaced official name. Executable identity, namespace, startup value, package directory and user-data paths are unchanged.

## Verification

- Debug solution build: zero warnings and errors.
- Existing policy/config/localization/polling/window regression suites pass.
- Branding tests validate all ICO directory entries and decoded frame dimensions, 32-bit alpha/transparency, distinct tray-state bitmaps, native icon handles after stream disposal and NotifyIcon state assignment.
- Extracted Debug executable icon differs from the generic application icon; executable ProductName and FileDescription equal the official name.
- Native hidden-start window tests reveal Config twice while using the new tray-state assets.
- Generator rerun produced identical SHA-256 hashes for all branding assets on this machine.
- Preview was visually inspected on light/dark backgrounds, including actual 16–40 px tray samples.
- Release executable icon was extracted to the local evidence folder. Existing user settings/current photo/black image hashes were preserved during publish.

The session remains Remote and reports WinDisc. Native read-only smoke cannot establish physical display mapping, hotplug or real-monitor DPI transitions. Physical-console follow-up remains in PENDING-TESTS.md for 20 October.

Release read-only smoke passed: 4 samples, 25.05 seconds, zero repairs, exit 0, English, existing 5000 ms interval. Native snapshots matched in every field except CapturedAt; user settings/image hashes were unchanged. The actual package reopened Config with nativeVisible=True and a nonzero native window-icon handle.

## User review

- Check the new tray icon and Config window icon.
- Pause from Tray: amber pause bars should appear.
- Resume if the display environment is valid; an amber exclamation mark means the agent needs attention. Read the tray status/log for the reason.
- Review assets/branding/Preview.png for the large application mark and all three tray states.
- Test actual light/dark taskbar themes and high-DPI displays at the physical console when available.

## Local evidence (ignored)

- .local/branding-20261019/package-before.json
- .local/branding-20261019/native-before.json and native-after.json
- .local/branding-20261019/smoke-result.json
- .local/branding-20261019/extracted-package-icon.png
- artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/branding-self-test-*/result.json
