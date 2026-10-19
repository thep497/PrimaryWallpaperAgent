# Primary Wallpaper Agent branding

Original vector artwork created for this project. No external image, font outline or third-party icon is embedded in the mark.

- Application.svg: editable vector review master, transparent background.
- Application.ico: apphost/Explorer/Config icon. PNG frames: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px.
- TrayRunning.ico / TrayPaused.ico / TrayAttention.ico: simplified two-monitor mark with 16, 20, 24, 32, 40, 48 and 64 px frames.
- Preview.png: actual-size tray marks and large application mark on light/dark backgrounds.
- ../../scripts/Generate-Branding.ps1: canonical geometry and reproducible asset generator using Windows System.Drawing. No downloads or packages required.

The large mark shows a colorful primary screen with black secondary screens. Small app frames and tray marks simplify to two screens to preserve clarity. The arrangement communicates the policy; it does not imply that a fixed physical monitor or the center monitor must be primary.

Running: cyan screen. Paused by user: amber pause bars. Error/action required: amber exclamation mark. Status text and notifications remain the authoritative explanation, so color is not the only signal.

A light outer rim and dark inner rim provide contrast on both taskbar themes without detecting theme settings or writing the registry. The artwork has no text, so it is shared across Thai and English.

Regenerate from the project root:
```powershell
.\scripts\Generate-Branding.ps1
```

The generator overwrites these derived assets, including the SVG review master. Make durable geometry changes in the generator. Builds use committed ICO assets and do not run the generator automatically.

The application embeds icon resources and sets ApplicationIcon for the executable. Runtime does not depend on loose ICO files. Display/product name is Primary Wallpaper Agent; executable, namespace, startup value and package path retain their existing identities.
