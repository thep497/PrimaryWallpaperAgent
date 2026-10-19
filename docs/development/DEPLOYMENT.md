# Startup and distribution

## Single-file preview

Publish-Release.ps1 creates a self-contained win-x64 .NET 10 build with PublishSingleFile and IncludeNativeLibrariesForSelfExtract enabled, trimming disabled, and debug symbols excluded. The app, runtime, Thai resources and icons are bundled into one EXE.

Release assets contain only PrimaryWallpaperAgent.exe and SHA256SUMS.txt, with no developer settings/photos/snapshots/trial plans. The build is unsigned.

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
