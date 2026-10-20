# Updates and Full/Slim distribution

## Scope

Windows x64 single-file C#/WinForms .NET 10. Full embeds the runtime; Slim uses a compatible installed .NET 10 Windows Desktop Runtime x64. Neither package modifies a system runtime. An existing runtime is not substituted into Full.

v0.4.0 is the first updater-capable release and must be downloaded manually. The updater preserves the actual installed filename/path and embedded flavor. It does not migrate data, change startup registration or switch Full/Slim automatically.

## UI and checking

Update controls live in About. Automatic checks default on, Preview defaults off. Successful checks are cached for 24 hours; failed checks have a one-hour retry interval. The first timer tick is about one minute after launch. Manual checks bypass that cache.

The feed reads the latest 30 GitHub release entries for the fixed public repository thep497/PrimaryWallpaperAgent. Drafts, disallowed previews, nonnumeric three-part tags, nonnewer versions and entries without the signed update assets are excluded. No credentials or GitHub login are required by the app.

Only one check/install runs at a time. A found release is announced once per version. Download/install requires the explicit Update and restart action. User settings save immediately in updates/preferences.json, separate from wallpaper settings.json.

## Trust and download

Release assets:
- PrimaryWallpaperAgent-Full-win-x64.exe
- PrimaryWallpaperAgent-Slim-win-x64.exe
- update-manifest.json
- update-manifest.sig
- SHA256SUMS.txt

The manifest includes schema, version, stable/preview channel, and each flavor's exact filename/size/SHA-256. Its raw UTF-8 bytes are signed with ECDSA P-256/SHA-256. The embedded public key verifies that signature before the manifest is trusted. Hash alone is not used as publisher authentication.

Versions and asset names are strictly validated. URLs are constructed for the fixed repository/tag rather than accepted from arbitrary release metadata. HTTPS redirects are bounded and limited to the GitHub API/download hosts used by the release service. Metadata and asset sizes are bounded; incomplete downloads are rejected.

The signing private key is DPAPI-protected for the developer's Windows account at .local/release-signing/update-key.dpapi. Only assets/updates/update-public-key.pem goes into Git or the EXEs. Publish refuses a missing/mismatched key and never silently generates a replacement. Losing the private key/profile can require a manual trust-key migration; preserve it using an independently approved backup procedure.

Update manifest signing is distinct from Authenticode EXE signing. No Authenticode certificate is configured.

## Installation transaction

1. Prove the existing EXE directory is writable; do not request elevation.
2. Download the same flavor into updates/staging/<random operation ID>.
3. Verify signed size/hash and run --update-preflight. This constructs resource/UI fixtures without native wallpaper, registry or writer lease.
4. Copy the current EXE as the temporary helper. Copy the verified candidate beside the target for same-volume File.Replace.
5. The helper validates the ticket, signature, original hash/version/flavor, exact parent PID/start time/path, data root and candidate hash.
6. A same-user/elevation named pipe completes an acknowledgment handshake. Only then does the app exit.
7. Wait for that parent to exit. Atomically replace the target and retain the old EXE backup.
8. Start the new EXE with a validated post-update ticket. It loads existing configuration, creates the agent/UI loop, preserves pause and reports a nonce/PID/version/flavor health acknowledgment on the first idle event.
9. If startup fails, stop only that new child, restore the original and attempt to reopen it. Unexpected externally changed target files are neither replaced nor launched.

No recursive delete is used. Staging cleanup removes only known direct files in recognized operation directories older than a day, skipping redirecting links (junctions/symlinks). One previous successful EXE backup is retained; the preceding owned backup is removed only after another healthy update. Locked files can remain for later cleanup. Logs are bounded separately.

The running guard continues during network checks/download/preflight. It stops briefly during process handoff; existing wallpaper assignments remain, while topology following resumes with the new process.

## Verification

- scripts/Test.ps1: 12 standard suites, including signed metadata, tampering, channel selection, flavor-specific downloads, truncation, transaction success/rollback, changed-target refusal, preference persistence and bilingual Update UI.
- scripts/Test-UpdateProcesses.ps1: publishes isolated 0.3.9/0.4.0 executable fixtures for both flavors. The real controller/helper/file replacement/health protocol is exercised for Full/Slim success and forced startup failure. Fixtures inject an ephemeral test public key and a local HTTP transport; the production private key is never used to sign test executable payloads.
- Process fixtures bypass native wallpaper/startup backends; actual processes and file transactions still run. Settings bytes and original/new EXE hashes are checked.
- scripts/Test-Release.ps1: verifies the production manifest signature and all asset hashes, copies each product EXE alone under a renamed path with spaces/Thai characters, checks fresh/reopened data and update preflight. Full disables shared runtime lookup; Slim uses the installed compatible runtime.
- Thai/English About screenshots are visually inspected.

Local evidence is ignored under artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/update-* and .local/release-verification-*.

These tests do not establish clean Windows VM dependency coverage, Windows 10 compatibility, physical monitor behavior or every possible antivirus/filesystem policy. Healthy startup is not a guarantee of every feature in an arbitrary future release. A protected install directory requires manual updating; no elevation or installer service is added.

## References

- [GitHub Releases API](https://docs.github.com/en/rest/releases/releases)
- [Microsoft single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Microsoft ECDSA verification](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.ecdsa.verifydata?view=net-10.0)

v0.4.1 distinguishes actual links via FileSystemInfo.LinkTarget from non-link cloud-sync reparse metadata. This was checked on an existing synced directory (ReparsePoint, no link target) and a real test junction (rejected). Log-retention classification is unchanged.
