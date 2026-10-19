# v0.3.0 single-file and first-run verification

## Automated coverage

Generated assets and fake display/startup backends only:

- Fresh launch is inactive; startup is opt-in.
- Black asset decodes and passes its hash check.
- Reopen preserves settings; new copies use stable per-user data.
- Explicit adjacent settings retain legacy folder mode.
- Startup quotes the actual renamed executable path.
- Preferences before first image cannot activate wallpaper.
- Actual AgentContext timer stays inactive over multiple first-run ticks.
- Failed first image adoption rolls back and remains inactive.
- Successful first image adoption follows current primary-only policy.
- The preview placeholder cannot become an Undo target.
- Legacy settings remain configured; invalid settings are never silently reset.

## Packaged EXE

Run scripts/Test-Release.ps1 after scripts/Publish-Release.ps1.

Only the EXE is copied to a new directory with spaces/Thai characters and renamed. Shared runtime lookup points to nonexistent locations; bundle extraction stays inside the project. The EXE --package-check diagnostic creates fresh data, verifies Thai differs from English, loads icons and constructs Settings. It has no native wallpaper backend, registry writer or writer lease. A second run verifies reopen; the release checksum must match.

Passing this check proves the bundled EXE runs on this Windows machine with shared .NET lookup disabled. It is not a clean Windows VM or physical-monitor/logon test. The existing running package and startup entry are untouched.

## Results

Debug build: zero warnings/errors. All nine suites pass: policy, config, localization, polling, branding, instant, compact, release and window. Single-file release publish and two packaged EXE checks pass, with zero native wallpaper calls and registry writes.

Local evidence stays under ignored artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/release-self-test-* and .local/release-verification-* directories. Upload/download checksum evidence is also local and ignored.

Physical-console/Remote recovery, projector, >3 physical displays, DPI transitions and startup after logon remain pending in PENDING-TESTS.md.
