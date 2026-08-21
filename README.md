# Master Tasks

A small always-available Windows task list — a translucent (acrylic), tabbed panel that lives in
the system tray while you work.

## Features

- **Custom tabs** — create/rename/delete as many task lists ("regions of work") as you like.
- **Archive** — ticking a task off files it under an Archive tab, grouped by the list it came from,
  with restore/permanent-delete per item and a one-click export to a `.docx` report.
- **Due dates & urgency** — optionally set a due date per task. Anything due within a week (or
  overdue) is bumped under an "Urgent" heading; everything else sits under "General". A tray
  reminder fires once per task as it crosses into "due soon", plus a once-a-week (Friday) nudge to
  export the archive if there's anything in it.
- **Tray-first** — closes to the tray instead of quitting; left-click the tray icon to bring the
  panel to the front over whatever else is on screen. Single-instance: launching it again (e.g. a
  pinned taskbar icon) just brings the existing panel forward.
- Acrylic blur-behind, rounded corners, a custom crystal-lattice icon, all in one palette
  (`#264D59` `#43978D` `#F9E07F` `#F9AD6A` `#D46C4E`).

## Running it

Requires the .NET 8 desktop runtime (ships with Windows or via the .NET SDK).

```
dotnet build -c Release
```

Then run `bin\Release\net8.0-windows\TaskPanel.exe` (or `dotnet build` for a Debug build during
development). Data is stored at `%AppData%\TaskPanel\data.json`.
