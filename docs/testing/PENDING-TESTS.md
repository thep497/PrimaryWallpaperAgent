# Pending user tests

## 20 October 2026 — physical console after Remote session

User confirmed on 19 October that the session was Remote. Native diagnostics reported WinDisc without display endpoints and could not match the physical monitors. Remote is a plausible explanation; verify at the physical console before concluding.

Approved development chunks are complete: source layout, Thai/English UI, configurable polling, and app/tray icon design. Physical display, Remote recovery, icon-state interaction and DPI tests below remain open.

### 1. Capture before changing agent state

At the physical Windows console, note the tray status first. The latest local build continues checking while Settings is open; explicitly test that behavior as well. Capture using the existing read-only helper:

```powershell
Set-Location -LiteralPath 'D:\Programming\PrivateProjects\PrimaryWallpaperAgent'
.\scripts\Capture-DesktopSnapshot.ps1 -Label Local-BeforeResume
```

This saves a new JSON file in snapshots; it does not set wallpaper or change display settings. Label is supplied by the tester, not an automatically detected Virtual Desktop ID.

Check for the physical GDI displays, exactly one Primary, stable topology and exact device-path mapping for active attached entries. Cached/unknown entries can still cause exit 2; distinguish those from missing active-display mappings.

Record whether the existing agent recovers automatically after Remote. If it remains paused, record that first, then try Resume from Tray. Do not describe recovery as automatic if a manual Resume was required.

### 2. Test one scenario at a time

Wait for the tray to report Running after each topology change. Current saved polling may be 5 seconds; two stable samples are still required. Record actual results and capture each state:

| Scenario | Snapshot label | Visual expectation |
|---|---|---|
| Original three displays | Local3-A | Image only on the Windows primary |
| Switch Windows primary | Local3-NewPrimary | Image follows the new primary; other displays black |
| Switch primary with Settings open | Local3-SettingsOpen | Checking continues and the image follows primary |
| Virtual Desktop 1 / 2 / 3 | Local-VD1 / Local-VD2 / Local-VD3 | Primary-only policy remains visible on each desktop |
| Disconnect to notebook only | Local-Notebook | Notebook shows the selected image |
| Reconnect three displays | Local3-Reconnect | Current primary has image; other displays black |
| Return from a later Remote session | Local-AfterRemote | Record automatic recovery versus Resume required |

Example capture after completing one scenario:

```powershell
.\scripts\Capture-DesktopSnapshot.ps1 -Label Local3-NewPrimary
```

Compare API assignments with what is actually visible. A path in the API alone is not proof of what a Virtual Desktop displays.

### 3. UI and packaging follow-up

- Verify image selection applies immediately and Undo restores only the image, retaining language/poll/startup. Undo is available until app exit.
- Recheck Thai/English and saved polling at the physical console. The user already confirmed both features work in the Remote session.
- Verify Tray Pause shows amber pause bars; an error/action-required state shows an exclamation mark.
- Check Config/Explorer/tray icons on the actual taskbar theme and display scaling. User approved the icon design; high-DPI transitions have not been verified.
- Test startup after logon separately if the user enables the existing Config opt-in.
- Projector and more than three physical displays remain untested hardware cases.

Record actual observations before closing any checklist item. Keep snapshots and personal image paths in ignored local evidence folders.
