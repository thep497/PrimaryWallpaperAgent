# Startup and distribution

## Single-file preview

Publish-Release.ps1 creates Full (self-contained) and Slim (framework-dependent) win-x64 .NET 10 builds with PublishSingleFile and IncludeNativeLibrariesForSelfExtract enabled, trimming disabled, and debug symbols excluded. Both variants bundle app resources into one EXE. Full includes the runtime; Slim uses an installed compatible .NET 10 Windows Desktop Runtime x64.

Release assets contain Full/Slim EXEs, update-manifest.json, update-manifest.sig and SHA256SUMS.txt, with no developer settings/photos/snapshots/trial plans. The manifest is ECDSA-signed; EXEs remain unsigned by Authenticode.

Single-file describes the download; the app creates user data on disk. Microsoft native runtime libraries can extract to the normal per-user temporary bundle cache. Test-Release.ps1 redirects that cache into the ignored project folder.

## First launch and data

New installations use Environment.SpecialFolder.LocalApplicationData/PrimaryWallpaperAgent for settings.json, assets and runtime logs. FirstRun creates a hash-checked black PNG, used only as an initial preview and black policy asset, with HasChosenImage=false and startup disabled. The watcher does not capture/repair wallpaper while unconfigured.

Settings opens on first launch. Only a successful explicit image selection activates the policy. Preference edits alone cannot activate it. Failed first image transactions roll back and remain inactive. Undo cannot restore the placeholder.

Existing settings are preserved, including invalid files that must be reported rather than silently reset. Schema 1/2/3 settings without HasChosenImage remain configured for compatibility.

An adjacent settings.json selects legacy folder mode. No old-package scan or automatic migration occurs. The existing local package is left in its current folder.

## Start with Windows

- Key: HKCU\Software\Microsoft\Windows\CurrentVersion\Run
- Value: PrimaryWallpaperAgent
- Data: "<actual Environment.ProcessPath>" --startup
- Opt-in through Settings, with immediate effect for the current account.
- Runs after sign-in; no service, scheduled task or administrator elevation.
- The actual EXE path is quoted; the Run-command length check remains.
- Renaming/copying before enabling startup works; the filename is not hardcoded.
- Moving/renaming after enabling leaves the registered old path. Toggle off/on from the new location.
- Copying the EXE does not transfer/create another user's registration.
- The UI reads this entry, rather than every startup-disable override in Windows.

Quit before replacing the EXE. New installations keep data separate across updates. Legacy users must retain settings/assets with the existing binary location until a separately verified migration is implemented.

## Package verification

Test-Release.ps1 copies only the EXE to a folder with spaces and Thai characters, renames it, disables shared .NET runtime lookup and redirects extraction into the project.

The --package-check diagnostic creates isolated data, verifies actual Thai/English resources, constructs Settings and loads icons. It has no native desktop backend, startup writer or writer lease. This verifies the bundle on this Windows machine, rather than physical mapping, logon startup or clean-VM coverage.

## References

- [Microsoft: Run and RunOnce](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [Microsoft: single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## v0.3.1 activation and retention

A second v0.3.1 launch asks the existing process to show Settings using a same-user/session named pipe. A duplicate startup launch exits quietly. The wallpaper-writer mutex still protects older tools; exit v0.3.0 once when upgrading.

Managed runtime logs/diagnostics have 30-day retention and a best-effort 50 MiB target. Text logs rotate at 2 MiB with one predecessor. Current/locked/unknown files and junction targets are preserved; settings/assets are untouched. See [test details](../testing/MAINTENANCE-TEST.md).

The Settings header version link opens About with the actual version/data location and Releases/data-folder actions. Automatic updating remains separate future work.

- [Microsoft: PipeOptions.CurrentUserOnly](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0)
- [Microsoft: foreground activation permission](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-allowsetforegroundwindow)

## v0.4.0 updates

Public release output now includes separate Full/Slim single-file EXEs and an ECDSA-signed manifest. Full includes the runtime; Slim requires an installed compatible .NET 10 Windows Desktop Runtime x64. Updates preserve flavor and the existing EXE path; downloads/installations require a user click. See [auto-update design and evidence](AUTO-UPDATE.md).

Versions before v0.4.0 need one manual upgrade. Update signing uses the ignored DPAPI-protected key and the committed public verification key. EXEs remain unsigned by Authenticode.
