# Pending user tests

## 20 October 2026 — physical console after Remote session

User confirmed on 19 October that the current session is Remote. Native display diagnostics reported WinDisc with no display endpoints and could not match the real monitors. Remote is a plausible explanation; confirm on the physical machine before concluding.

- Exit/disconnect Remote as appropriate and test at the actual Windows console.
- Check that notebook, LG and portrait monitor map correctly to IDesktopWallpaper and that the intended primary is identified.
- Verify image follows primary, all secondary screens remain black, hotplug 3→1→3, and switching the three Virtual Desktops.
- Recheck Thai/English Config and the new polling preference on real displays.
- Compare remote reconnect/disconnect behavior and whether the agent recovers or requires Resume. Do not claim Remote support from UI-only tests.
- Record actual results before closing this item.

Language UI: user confirmed working as intended and authorized commit/next chunk.