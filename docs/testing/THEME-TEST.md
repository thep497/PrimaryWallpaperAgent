# Theme and compact image selection verification

v0.4.3 change, 20 October 2026. The user approved the local review build. This is not a physical Windows 10 retest.

Settings uses only the four recent-picture thumbnails. The selected picture moves to the first position; its filename appears below that thumbnail and the full name is available on hover. The separate large preview is removed.

Theme offers System (default for existing settings), Light and Dark in Thai and English. It applies and persists immediately. A failed settings transaction restores the prior preference and colors. Image selection and Undo retain the chosen theme. System follows Windows app-color notifications while the app is open; notification handlers and form registrations are removed on disposal. High Contrast uses system colors.

The app styles Settings, About/update controls, click-help and the tray menu. It reads Windows app-color preference without writing it. Native file/message dialogs follow Windows. The explicit dark title-bar attribute is restricted to Windows 11 build 22000+; the app body uses its own palette on Windows 10. That implementation choice does not establish physical Windows 10 behavior.

Automated theme fixtures cover legacy defaults, invalid values, immediate persistence/reopen, failed-save rollback, preservation through image/Undo, all open windows, marshalled system notifications, High Contrast, contrast ratios and compact Thai/English layout. Generated images and fake wallpaper/startup backends are used. No real wallpaper or startup/system preference writes occur.

Run: scripts/Test.ps1 (14 suites including theme and native window visibility).
Ignored screenshots/evidence: artifacts/bin/PrimaryWallpaperAgent.Tests/debug/runtime/theme-test-*/.

Official references:
- [.NET 10 WinForms themes](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100)
- [SetColorMode platform and live-change limitations](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setcolormode?view=windowsdesktop-10.0)
- [DWM title-bar attribute and minimum Windows version](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [System preference notifications](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents.userpreferencechanged?view=windowsdesktop-10.0)

Current verification result:
- Full 14-suite run passed on 20 October 2026; build reported 0 warnings and 0 errors.
- The first combined run encountered a sharing violation during a fixture settings replacement. A repeat of the same build passed all suites. The locking source was not identified; no Windows configuration or unrelated storage behavior was changed.
- Theme evidence: ignored runtime/theme-test-fa81f37a; reviewed both compact layout and Dark controls, including native ComboBox chrome.
- Local self-contained x64 review EXE: artifacts/review/theme-settings-20261020/PrimaryWallpaperAgent.Tray.exe, version 0.4.3-review. Package verification passed on fresh/reopened fixture data with unchanged settings, zero native wallpaper calls and zero registry writes.
- Review EXE SHA-256: BC91A225FFB62002EBF96E2F66A86B95736C4A2067D80EAF43DB0F9B5AA3618B.
- The user approved the review build and authorized commit/publication as v0.4.3 Preview on 20 October 2026.