# Tempo

A calm, premium reminder app for **Windows 11**, built as a native **WinUI 3** desktop application in **C# / .NET 10**. Tempo runs offline, needs no account, and keeps your reminders in a local SQLite database.

> *A little more present.* — Set it. Forget it. Be there when it matters.

---

## Highlights

- **Simple core flow** — set a time, write the reminder, choose how it repeats, save.
- **Repetition** — Once, Daily, Weekly (pick weekdays), or a Custom elapsed interval (every N minutes / hours / days), with an optional end date or occurrence limit.
- **Real Windows integration** — Windows toast notifications with **Snooze** and **Dismiss** actions, a polished in-app alert window, a **system tray** icon (Open / Exit), optional **Start with Windows**, and single-instance protection.
- **Reliable, honest scheduling** — a DST-aware calendar engine distinct from elapsed intervals, catch-up for reminders missed while the app was closed/asleep, and clear in-app documentation of exactly when reminders do and don't fire.
- **Accessible & adaptive** — keyboard navigation, visible focus, automation names, Windows display-scaling (Per-Monitor-v2 DPI), 12/24-hour clock following Windows, and a layout that reflows for smaller desktop windows (no mobile UI).

---

## Project layout

```
AlertApp/
├─ Tempo.sln
├─ src/
│  ├─ Tempo.Core/            # Pure logic: models, recurrence math, firing engine, SQLite store (no UI)
│  └─ Tempo/                 # WinUI 3 app: views, services, Windows integration
├─ tests/
│  └─ Tempo.Core.Tests/      # xUnit tests for recurrence, missed/snooze policy, limits, persistence
├─ installer/
│  └─ Tempo.iss              # Inno Setup installer script
├─ build-installer.ps1       # Publish (self-contained) + build the installer
├─ build-portable.ps1        # Publish + package the no-install portable ZIP
└─ README.md
```

The scheduling brain lives in **`Tempo.Core`** with **no UI dependency**, so it is fully unit-tested headlessly. The WinUI project consumes it.

---

## Requirements

- Windows 11 (or Windows 10 1809+). Tempo targets `net10.0-windows10.0.19041.0`, min 10.0.17763.
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build. (`winget install Microsoft.DotNet.SDK.10`)
- No Visual Studio or MSIX workload required — Tempo is an **unpackaged** app and builds with the `dotnet` CLI.
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) *(optional)* to produce the installer. (`winget install JRSoftware.InnoSetup`)

---

## Build & run

```powershell
# from the repo root
dotnet build src/Tempo/Tempo.csproj -c Debug -p:Platform=x64

# run it
dotnet run --project src/Tempo/Tempo.csproj -c Debug -p:Platform=x64
```

Run the tests:

```powershell
dotnet test tests/Tempo.Core.Tests/Tempo.Core.Tests.csproj
```

### Produce a self-contained build + installer

```powershell
# Windows PowerShell 5.1 or PowerShell 7 both work:
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
#   -> publish/Tempo/                     (self-contained app, no .NET needed on the target PC)
#   -> dist/Tempo-Setup-1.0.0.exe         (per-user installer, no admin rights required)
```

`build-installer.ps1 -NoInstaller` publishes the app folder only. If Inno Setup isn't installed,
the script still produces the self-contained `publish/Tempo/` folder and tells you how to get the compiler.

### Portable build (no install)

```powershell
powershell -ExecutionPolicy Bypass -File .\build-portable.ps1
#   -> dist/Tempo-Portable-1.0.0.zip
```

Unzip anywhere **writable** (a USB stick, Downloads, a project folder) and run `Tempo\Tempo.exe` — no
installation, no admin. A `Tempo.portable` marker file next to the exe switches the app into **portable
mode**: it stores its reminders and settings in a `Data` folder *beside the executable* instead of under
`%LOCALAPPDATA%`, so the whole app travels with its data. Delete the marker to make that copy use per-user
data instead. (If the folder it's run from is read-only, Tempo falls back to `%LOCALAPPDATA%\Tempo`.)

Portable mode affects only where Tempo keeps its own files; toast notifications still use the per-user
registration Windows requires, and "Start with Windows" (if you enable it) records the exe's current path.

The published app is **self-contained** (`WindowsAppSDKSelfContained` + `SelfContained`), so the target machine needs neither the .NET runtime nor the Windows App Runtime installed.

---

## How it works

### Data & settings
Everything is stored locally in SQLite at `%LOCALAPPDATA%\Tempo\tempo.db` (alerts + a key/value settings table). No network, no account.

### Scheduling approach
Tempo uses an **in-process scheduler**: a 1-second dispatcher timer evaluates every alert against the current time. This is the appropriate model for a desktop reminder app that lives in the tray, and it is robust to clock changes, sleep/resume, and time-zone/DST shifts because every tick re-reads the real time and the current zone. Tempo also listens for Windows **time-change** and **power-resume** events to re-evaluate immediately.

**Calendar vs. elapsed schedules are kept distinct:**
- *Daily / Weekly* are **calendar** schedules evaluated in your local wall-clock time, so "Daily at 09:00" stays at 09:00 across daylight-saving changes.
- *Custom* is an **elapsed** interval measured as an absolute duration from an anchor instant, so "every 24 hours" keeps constant spacing (its wall-clock time may shift by an hour across DST — correct for an elapsed interval).

### What's guaranteed in which state  *(shown in-app under Settings → "How reminders run")*
| State | Reminders fire? |
|---|---|
| Main window **closed**, Tempo running in the tray | **Yes** — this is the default; closing the window is safe. |
| Tempo **fully exited** | No — not at the scheduled instant. Caught up on next launch. |
| PC **asleep** or **shut down** | No — not at the scheduled instant. Caught up on resume/next launch. |

Tempo does **not** claim to fire reminders while fully exited or asleep. When it next runs (including immediately on resume), it delivers what was missed according to your **Missed reminders** setting:
- **Show one catch-up reminder** (default) — one reminder for the most recent missed occurrence; the backlog is skipped so you aren't flooded.
- **Skip missed reminders** — silently advance to the next future occurrence.

### Duplicate-delivery prevention
Each alert records the exact scheduled slot it last delivered (`LastFiredOccurrenceUtc`). The next occurrence is always computed strictly after that slot, so a given occurrence fires once — across restarts too. A named-instance **single-instance** guard (Windows App SDK `AppInstance`) ensures two Tempo processes never schedule the same alerts; a second launch surfaces the existing window.

### Notifications
Delivery uses the Windows App SDK `AppNotificationManager` (supports unpackaged apps) with Snooze/Dismiss buttons and a snooze-length selector. If the app is in the foreground, Tempo shows its own in-app alert window instead of a background toast; if notifications are unavailable, it falls back to the in-app window and surfaces a clear message in Settings.

---

## Verified behavior

Verified on Windows 11 Pro (26100) with .NET 10.0.403:

- **Automated tests — 21/21 passing** (`Tempo.Core.Tests`): DST spring-forward handling, daily/weekly next-occurrence, elapsed-interval spacing, end-date and max-occurrence limits, missed catch-up (ShowOnce delivers one + skips the backlog; Skip advances silently), snooze (delivered but not counted as an occurrence), pause behaviour, duplicate-fire prevention, one-time completion, and a full SQLite round-trip of every field.
- **Delivery** — a reminder scheduled ~25 s out fired exactly on time; its occurrence count incremented once (no duplicate) and the state persisted. When the app was in the background it delivered a Windows toast; when foreground it showed the in-app alert window with Snooze/Dismiss.
- **Persistence & editing** — alerts and settings survive restarts; editing re-arms the schedule; pause/resume and delete/undo work against the live store.
- **Upcoming sorting** — the Upcoming list ordered correctly by true next occurrence (Today 14:30 → Today 20:00 → Tomorrow 09:00 for a weekday-only alert), and paused alerts were excluded.
- **Tray & single instance** — closing the main window kept the process running in the tray (default); a second launch redirected to the first instance and exited, leaving exactly one process.
- **Settings** — close behaviour, clock format, missed-reminder policy, snooze length, sound, and Start-with-Windows persist; a clean install shows correct defaults.
- **Installer** — a silent per-user install, launch of the installed app, and silent uninstall all completed with exit code 0, leaving no files behind.

## Limitations / notes

- Reminders fire only while Tempo is running (foreground or tray) — see the table above. This is stated honestly rather than relying on a background service or the Windows Task Scheduler.
- "Custom interval in days" is treated as an **elapsed** 24h×N duration (by design, to contrast with calendar Daily).
- The app is locked to a light theme to match Tempo's calm visual identity.
- Built and tested for **x64**; an `ARM64` configuration is included but was not exercised in this environment.
