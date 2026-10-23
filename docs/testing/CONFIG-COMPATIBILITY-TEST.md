# Settings compatibility verification - v0.5.1

23 October 2026. The user chose silent, simple downgrade handling and authorized commit/publication after automated verification. Unknown fields may be discarded on the next explicit settings edit. No compatibility or recovery window is added.

## Contract

- Wallpaper settings.json accepts any positive integer schema marker and ignores fields not represented by this reader, including nested additions. Known legacy schemas retain their migration behavior; higher schemas normalize in memory to the current schema (4).
- Missing optional fields use constructor defaults. Missing secondary selection means black. Unknown saved theme/poll choices use System/1000 ms; new requests from the UI still validate supported choices.
- Opening/settings preview does not rewrite the original bytes. A successful explicit settings edit writes the current reader's schema/known fields and drops unknown fields. Lost newer selections do not reappear on upgrading; the newer reader must supply its defaults.
- Failed transactions restore the original byte sequence, including the UTF-8 BOM and unknown fields. The permanent pre-schema-4 backup is never overwritten.
- Malformed JSON, incompatible types for known fields, missing required metadata, nonpositive schema values and cached-image boundary/hash failures are still errors. This is tolerance for additive settings, not a guarantee of compatibility after changing existing field meanings/types.
- The signed update manifest and separate updater preferences retain their own validation. Existing 0.4.x EXEs keep their original upper-schema restriction; this patch cannot change code already distributed.

## Reproduce

```powershell
.\scripts\Build.ps1
.\artifacts\bin\PrimaryWallpaperAgent.Tests\debug\PrimaryWallpaperAgent.Tests.exe compatibility
.\scripts\Test.ps1 -SkipBuild
.\scripts\Publish-Release.ps1 -Version 0.5.1
.\scripts\Test-Release.ps1 -Version 0.5.1
```

The compatibility suite writes generated evidence under artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/compatibility-test-<id>. Its package-future fixture contains only generated cached images and synthetic schema/fields. Full/Slim --package-check can open that fixture under an isolated data path; it does not access the user's actual settings or native wallpaper/startup writers.

## Automated coverage

1. Schemas 1/2/3/4/5/int.MaxValue with nested future fields, unsupported theme/poll/language choices and valid cached primary/secondary pictures.
2. Missing optional preferences/history/secondary selection; black secondary and unchanged primary defaults.
3. Original bytes retained after opening, exact future/BOM bytes restored after an injected save-after-write failure, successful preferences edits dropping unknown fields and saving schema 4, followed by another edit/reopen.
4. Existing pre-upgrade backup retained; no real wallpaper or startup writes. Reject broken JSON, known-field type changes, invalid required metadata and invalid asset paths/hashes.
5. Full regression suites and standalone signed Full/Slim distribution checks.

## Verified results

- Debug build completed with zero warnings/errors. All 19 regression suites passed; compatibility evidence is compatibility-test-eb9e572b and DPI evidence is dpi-test-45105ad0.
- The first regression attempt stopped in the existing secondary UI fixture because a button lookup found no control. That unchanged suite passed in isolation and in the subsequent complete run. The cause of the first failure is unconfirmed; no production UI change was made for it.
- Full/Slim signed release verification passed: renamed standalone executable, fresh/reopened data, isolated shared-runtime lookup for Full, preflight identity, bilingual/icon/UI resources, SHA-256/size and pinned ECDSA signature. Evidence: .local/release-verification-f7079858.
- Both production EXEs read generated future-schema/BOM/nested-extra-field data and data missing optional preferences/secondary selection, with identical settings and asset bytes afterward. Both rejected broken JSON without resetting settings. The unchanged 0.5.0 Full EXE rejected that same future fixture as the negative control. Evidence: .local/compatibility-release-a69cb894.
- Tests used generated images/data and fake desktop/startup backends or the read-only --package-check path. Actual user settings, wallpaper and startup registration were not changed.

Physical display tests remain separate because this patch does not change wallpaper policy or display mapping.
