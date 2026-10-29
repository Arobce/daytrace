# DayTrace — Product Requirements Document (Iteration 1)

> Working name: **DayTrace**. Rename freely; the name appears only in project names, the data folder and the tray tooltip.

| | |
|---|---|
| Status | Ready to build |
| Platform | Windows 10 (22H2) and Windows 11, x64 |
| Stack | C# / .NET 10 (LTS), WinForms tray app, ASP.NET Core minimal API, SQLite, vanilla HTML/CSS/JS dashboard |
| Owner | You |
| Builder | Claude Code, milestone by milestone (see §11) |

---

## 1. Summary

DayTrace is a personal, local-only time tracker for Windows. A small tray app runs from login, records which application (and window title) is in the foreground and for how long, and notices when you're idle, locked or asleep. A local web dashboard, opened from the tray icon, shows where your day went: a timeline, totals by category, top apps, and a simple productivity score. You assign apps to categories, and the dashboard recalculates everything, including past days.

Iteration 1 is about **trustworthy raw data and a clear daily picture**. No goals, nudges, browser URLs or sync yet.

## 2. Goals and non-goals

### Goals (v1)
1. Record foreground app sessions accurately (±2 s) all day without the user thinking about it.
2. Exclude idle, locked, sleeping and paused time from "active" time.
3. Categorize apps (by exe name) and window titles (by "title contains" rules), editable at any time, applied retroactively.
4. Show Today, Week, and Apps & Categories views in a local dashboard.
5. Never lose more than 30 s of data on a crash, kill or power loss.
6. Stay invisible in cost: < 1 % average CPU, < 60 MB RAM for the tracker.
7. Keep all data on the machine. No network traffic other than `127.0.0.1`.

### Non-goals (v1)
- Browser URL tracking (needs an extension → v2)
- Goals, focus sessions, alerts, notifications
- Multiple machines, sync, accounts, cloud
- Media-aware idle (watching a video with no input counts as idle in v1; see §13)
- Installer/MSI, auto-update, code signing
- Windows Service mode (a service cannot see the user's desktop windows; see §6.1)

## 3. Users and core scenarios

Single user, on their own PC, technically comfortable.

| # | Scenario | Expected outcome |
|---|---|---|
| S1 | I start my PC and work all day | Tracker starts at login silently; at day's end the dashboard shows an accurate timeline |
| S2 | I step away for lunch without locking | After 3 min of no input, time stops counting; the gap appears as "Away (idle)" on the timeline |
| S3 | I lock with Win+L / close the lid | Time stops immediately; gap shows as "Locked" / "Asleep" |
| S4 | I spend 40 min on YouTube in Chrome | Counted as Entertainment (title rule), not as generic Browsing |
| S5 | I notice "notion.exe" is Uncategorized | I assign it to Productive from the Apps page; today's and all past totals update immediately |
| S6 | I want an hour off the record | Tray → Pause → 1 hour; nothing is recorded; gap shows as "Paused" |
| S7 | I want to see this week | Week view shows active time per day, stacked by category |

## 4. Architecture

```
┌──────────────────────────────┐        ┌──────────────────────────────┐
│ DayTrace.Tracker.exe         │        │ DayTrace.Web.exe             │
│ (WinForms tray, no window)   │ starts │ (ASP.NET Core, 127.0.0.1)    │
│                              │──────▶ │                              │
│ ForegroundSource (poll 1 s)  │        │ /api/*  JSON endpoints       │
│ IdleMonitor                  │        │ /       static dashboard     │
│ SystemStateMonitor           │        │         (wwwroot, no build)  │
│   (lock/unlock, sleep/wake)  │        │                              │
│ SessionRecorder (pure logic) │        │                              │
└──────────────┬───────────────┘        └──────────────┬───────────────┘
               │ writes: sessions, away_periods         │ reads: everything
               │ reads: settings                        │ writes: categories, rules, settings
               ▼                                        ▼
        ┌─────────────────────────────────────────────────────┐
        │ %LOCALAPPDATA%\DayTrace\daytrace.db (SQLite, WAL)    │
        └─────────────────────────────────────────────────────┘
                     ▲
                     │ shared code: schema, migrations, repositories,
                     │ categorization, aggregation
              DayTrace.Core (class library)
```

### 4.1 Solution layout

```
DayTrace.sln
src/
  DayTrace.Core/          net10.0          models, DB access, migrations, seed data,
                                           categorizer, aggregation queries, IClock
  DayTrace.Tracker/       net10.0-windows  WinForms tray app; Native/ (P/Invoke),
                                           ForegroundSource, IdleMonitor,
                                           SystemStateMonitor, SessionRecorder host,
                                           TrayIcon, Autostart, WebLauncher
  DayTrace.Web/           net10.0          minimal API + wwwroot/ (index.html, css, js,
                                           vendor/chart.umd.min.js)
tests/
  DayTrace.Core.Tests/    xUnit            recorder state machine, categorizer,
                                           aggregation, migrations
docs/
  PRD.md
  design/                                  Claude Design source files (read-only reference):
                                           README.md, today/week/apps/settings/tray.dc.html
```

`SessionRecorder` (the state machine) lives in **Core**, with no Win32 dependency, so it can be unit tested with a fake clock and fake inputs. The Tracker project only adapts Win32 signals into calls on it.

### 4.2 Process model
- **Tracker** is the always-on process. Single instance (named mutex `Local\DayTrace.Tracker`).
- **Web** is started by the tracker on demand ("Open dashboard") and stopped when the tracker exits. If it crashes, tracking is unaffected.
- Both open the same SQLite file in **WAL mode** with `busy_timeout = 5000`. Each table has exactly one writer process (see §5), so contention is minimal.

### 4.3 Dependencies (keep to this list; ask before adding)
- `Microsoft.Data.Sqlite`
- `Dapper`
- `Serilog`, `Serilog.Sinks.File` (rolling daily logs, 7-day retention)
- Tests: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` (optional)
- Dashboard: **Chart.js 4.x** and the **IBM Plex Sans** font (OFL, woff2) vendored into `wwwroot/vendor/` (no CDN; must work offline). No frontend build step.

## 5. Data model

Location: `%LOCALAPPDATA%\DayTrace\daytrace.db`. Logs: `%LOCALAPPDATA%\DayTrace\logs\`.

**All timestamps are stored as INTEGER Unix epoch milliseconds in UTC.** Conversion to local time happens only in the dashboard API (using the machine's local time zone) and the browser.

Schema version is tracked with `PRAGMA user_version`; migrations are ordered C# scripts in Core, applied at startup by whichever process opens the DB first (wrapped in a transaction; idempotent).

```sql
-- Written by: Tracker
CREATE TABLE sessions (
  id              INTEGER PRIMARY KEY,
  started_at      INTEGER NOT NULL,          -- UTC ms
  ended_at        INTEGER NOT NULL,          -- UTC ms; always set (heartbeat-updated)
  exe_name        TEXT    NOT NULL,          -- lower-case, e.g. 'chrome.exe'
  exe_path        TEXT,                      -- full path if resolvable, else NULL
  app_name        TEXT,                      -- FileVersionInfo.FileDescription, e.g. 'Google Chrome'
  window_title    TEXT    NOT NULL DEFAULT '' -- trimmed, max 512 chars
);
CREATE INDEX ix_sessions_started ON sessions(started_at);
CREATE INDEX ix_sessions_exe     ON sessions(exe_name);

-- Written by: Tracker
CREATE TABLE away_periods (
  id          INTEGER PRIMARY KEY,
  started_at  INTEGER NOT NULL,
  ended_at    INTEGER NOT NULL,
  reason      TEXT NOT NULL CHECK (reason IN ('idle','locked','asleep','paused'))
);
CREATE INDEX ix_away_started ON away_periods(started_at);

-- Written by: Web
CREATE TABLE categories (
  id            INTEGER PRIMARY KEY,
  name          TEXT NOT NULL UNIQUE,
  color         TEXT NOT NULL,                -- '#RRGGBB'
  productivity  TEXT NOT NULL CHECK (productivity IN ('productive','neutral','distracting')),
  sort_order    INTEGER NOT NULL DEFAULT 0,
  is_system     INTEGER NOT NULL DEFAULT 0    -- 1 for 'Uncategorized' (cannot delete/rename)
);

-- Written by: Web
CREATE TABLE rules (
  id           INTEGER PRIMARY KEY,
  category_id  INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
  match_type   TEXT NOT NULL CHECK (match_type IN ('exe','title_contains')),
  pattern      TEXT NOT NULL,                 -- exe: 'chrome.exe' (lower-case); title: substring
  exe_filter   TEXT,                          -- title_contains only: restrict to this exe (optional)
  priority     INTEGER NOT NULL DEFAULT 0,    -- higher wins among same match_type
  created_at   INTEGER NOT NULL,
  UNIQUE (match_type, pattern, exe_filter)
);

-- Written by: Web (tracker reads every 60 s)
CREATE TABLE settings (
  key    TEXT PRIMARY KEY,
  value  TEXT NOT NULL
);
```

### 5.1 Settings (defaults seeded)
| key | default | meaning |
|---|---|---|
| `poll_interval_ms` | `1000` | foreground polling interval |
| `min_switch_seconds` | `2` | debounce: a new window must be stable this long to become a session |
| `idle_threshold_seconds` | `180` | no input for this long → idle |
| `heartbeat_seconds` | `30` | how often the open session's `ended_at` is saved |
| `day_start_hour` | `0` | local hour a "day" starts (0 = midnight; 4 suits night owls) |
| `web_port` | `5217` | dashboard port on 127.0.0.1 |

### 5.2 Categorization (computed at read time, never stored on sessions)
For each session, the category is resolved in this order:
1. **Title rules** (`title_contains`), case-insensitive substring on `window_title`, honoring `exe_filter` if set. Highest `priority` wins; ties → most recently created.
2. **Exe rule** matching `exe_name` exactly (case-insensitive).
3. Otherwise **Uncategorized**.

Implement as a `Categorizer` in Core that loads rules once per request and resolves in memory (data volume is small: ~2,000 sessions/day). Distinct (exe, title) pairs can be memoized per request.

### 5.3 Seed data (first run)
Categories (name — color — productivity):
- Productive — `#2F9E6E` — productive
- Communication — `#3B7DD8` — neutral
- Browsing — `#8A7FD1` — neutral
- Entertainment — `#E0654F` — distracting
- Social — `#E39B3A` — distracting
- System — `#9AA1AC` — neutral
- Uncategorized — `#C8CDD4` — neutral — `is_system = 1`

Exe rules:
- Productive: `code.exe`, `devenv.exe`, `rider64.exe`, `idea64.exe`, `windowsterminal.exe`, `powershell.exe`, `pwsh.exe`, `cmd.exe`, `winword.exe`, `excel.exe`, `powerpnt.exe`, `onenote.exe`, `notion.exe`, `obsidian.exe`, `figma.exe`, `claude.exe`
- Communication: `slack.exe`, `ms-teams.exe`, `teams.exe`, `outlook.exe`, `olk.exe`, `zoom.exe`, `discord.exe`
- Browsing: `chrome.exe`, `msedge.exe`, `firefox.exe`, `brave.exe`, `opera.exe`, `arc.exe`
- Entertainment: `spotify.exe`, `steam.exe`, `vlc.exe`
- System: `explorer.exe`, `systemsettings.exe`, `taskmgr.exe`, `searchhost.exe`, `shellexperiencehost.exe`, `startmenuexperiencehost.exe`

Title rules (`title_contains`, no exe filter, priority 10):
- Entertainment: `YouTube`, `Netflix`, `Twitch`, `Prime Video`, `Disney+`
- Social: `Reddit`, ` / X`, `Facebook`, `Instagram`, `LinkedIn`
- Productive: `GitHub`, `Stack Overflow`, `Google Docs`, `Google Sheets`
- Communication: `Gmail`, `Outlook`, `WhatsApp`

## 6. Tracker requirements

### 6.1 Process and lifecycle
- **T-1** WinForms app with **no main window**; `ApplicationContext` subclass owns a `NotifyIcon`. This provides the Win32 message loop needed by `SystemEvents` and future event hooks.
- **T-2** Must run as a normal user process in the interactive session. Must **not** be a Windows Service (services run in session 0 and cannot see the user's windows).
- **T-3** Single instance via named mutex; a second launch exits silently (optionally signals the first to open the dashboard).
- **T-4** "Start with Windows" writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DayTrace` = `"<path>\DayTrace.Tracker.exe" --autostart`. Enabled by default on first run.
- **T-5** On exit (menu Exit, `SessionEnding`, `Application.ApplicationExit`): close the open session at "now", stop the web process, flush logs.
- **T-6** Unhandled exceptions are logged; the polling loop catches per-tick exceptions and continues (one bad window must never stop tracking).

### 6.2 Foreground detection (`ForegroundSource`)
- **T-10** Poll every `poll_interval_ms` with a `System.Windows.Forms.Timer` (runs on the UI thread, so all state is single-threaded).
- **T-11** `GetForegroundWindow()`. If it returns `IntPtr.Zero` (focus in transition), skip the tick.
- **T-12** `GetWindowThreadProcessId` → PID. Resolve the exe path with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName` (works for most elevated processes too). On failure, fall back to `Process.GetProcessById(pid).ProcessName + ".exe"`; on failure, `exe_name = "unknown.exe"`, `exe_path = NULL`.
- **T-13** **UWP apps:** if the exe is `applicationframehost.exe`, enumerate child windows (`EnumChildWindows`) and use the first child whose PID differs from the frame's PID. If none found, keep `applicationframehost.exe`.
- **T-14** Window title via `GetWindowTextLength` + `GetWindowText`, trimmed, truncated to 512 chars.
- **T-15** `app_name` from `FileVersionInfo.GetVersionInfo(path).FileDescription`, cached per exe path for the process lifetime.
- **T-16** Lock screen (`lockapp.exe`) is never recorded as a session; lock handling is in §6.4.
- **T-17** Polling sits behind an `IForegroundSource` interface so it can later be replaced by `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` without touching the recorder.

### 6.3 Session recording (`SessionRecorder`, in Core)
A pure state machine. Inputs: `Observe(ForegroundInfo, now)`, `GoAway(reason, at)`, `Return(now)`, `Heartbeat(now)`, `Shutdown(now)`. Output: calls on an `ISessionStore` (`Insert`, `UpdateEnd`, `InsertAway`, `UpdateAwayEnd`). Uses `IClock` for time.

States: **Active(session)**, **Pending(candidate, firstSeenAt)** layered on Active, **Away(reason, since)**.

- **T-20 Identity.** Two observations are "the same" when `exe_path` (or `exe_name` if path is null) and `window_title` are equal.
- **T-21 Debounce.** When an observation differs from the current session, it becomes a *candidate* with `firstSeenAt`. If the same candidate is observed continuously for ≥ `min_switch_seconds`, it is committed: the current session ends at `firstSeenAt`, and a new session starts at `firstSeenAt` (backdated). If a different observation arrives first, the candidate is replaced. If the current window reappears, the candidate is discarded. Effect: alt-tab flicker is absorbed into the surrounding session.
- **T-22 First observation** (no current session, not away) starts a session immediately, no debounce.
- **T-23 Persist on start.** A new session is inserted immediately with `ended_at = started_at`.
- **T-24 Heartbeat.** Every `heartbeat_seconds`, `ended_at` of the open session (or open away period) is updated to now. Worst-case data loss on crash is one heartbeat.
- **T-25 Away.** `GoAway(reason, at)` ends the open session at `at` (may be in the past, e.g. last input time for idle), discards any candidate, and inserts an `away_periods` row starting at `at`.
- **T-26 Return.** `Return(now)` closes the away period at `now`. The next `Observe` starts a session immediately (T-22 rules).
- **T-27 Clock safety.** If any computed `ended_at < started_at` (clock moved backwards), clamp `ended_at = started_at`. If now jumps forward > 2 × heartbeat while Active without a heartbeat having fired (e.g. undetected sleep), end the session at the last heartbeat time and start fresh.
- **T-28 Zero-length sessions** (`ended_at == started_at` at close) are deleted.

### 6.4 Idle, lock, sleep, pause (`IdleMonitor`, `SystemStateMonitor`)
- **T-30 Idle.** Every 5 s, read `GetLastInputInfo`. Compute idle ms as `unchecked((uint)Environment.TickCount - lii.dwTime)` (handles the 49.7-day tick wraparound). If idle ≥ `idle_threshold_seconds` and Active → `GoAway("idle", now - idleMs)`. If Away(idle) and idle < 5 s → `Return(now)`.
- **T-31 Lock.** `SystemEvents.SessionSwitch`: `SessionLock` / `ConsoleDisconnect` / `RemoteDisconnect` → `GoAway("locked", now)`; `SessionUnlock` / reconnect → `Return(now)`. Lock takes precedence over idle (if already Away(idle), convert: close idle period at now, open locked period).
- **T-32 Sleep.** `SystemEvents.PowerModeChanged`: `Suspend` → `GoAway("asleep", now)`; `Resume` → `Return(now)` (if the session is still locked, the lock handler will immediately go away again; that's fine).
- **T-33 Pause.** Tray menu: Pause for 15 min / 1 hour / Until I resume. → `GoAway("paused", now)`; a timer resumes automatically. While paused, idle/lock/sleep events are ignored except that resume-from-pause re-checks idle and lock state.
- **T-34** `SystemEvents` handlers must be unsubscribed on exit (they are static and leak otherwise).

### 6.5 Tray UI
- **T-40** Icon and menu: match `docs/design/tray.dc.html`. The icon is a dark rounded square with a white pulse line. When paused, it turns grey with an amber pause badge. Render it as `.ico` at 16/20/24/32 px.
- **T-41** Tooltip (≤ 63 chars): `DayTrace · 5h 12m today · Code` or `DayTrace · Paused until 14:30`.
- **T-42** Menu:
  - **Open dashboard** (default action; also on double-click)
  - Today: 5h 12m (disabled, informational)
  - ---
  - **Pause** ▸ 15 minutes / 1 hour / Until I resume — or **Resume tracking** when paused
  - ---
  - ✓ Start with Windows
  - Open data folder
  - ---
  - Exit
- **T-43 Open dashboard:** if `GET http://127.0.0.1:{port}/api/health` fails, start `DayTrace.Web.exe` (same folder) as a child process with `--port {port} --db "<path>"`, wait up to 5 s for health, then open the default browser at `http://127.0.0.1:{port}/`. If the port is taken by something else (health returns non-DayTrace payload), show a balloon tip with the error.

### 6.6 Logging
- Serilog to `%LOCALAPPDATA%\DayTrace\logs\tracker-.log`, daily rolling, 7 files.
- Info: start/stop, away/return transitions, settings reload. Debug (off by default): every committed session. Never log at per-tick frequency at Info.
- **Window titles must not be written to logs at Info level** (privacy).

### 6.7 Developer console mode
`DayTrace.Tracker.exe --console` attaches a console (`AllocConsole`) and prints each committed session, away/return, and heartbeat. Essential for manual testing (M2).

## 7. Web API requirements

ASP.NET Core minimal API. Binds **only** to `http://127.0.0.1:{port}` (never `0.0.0.0` / `localhost` wildcard). Serves `wwwroot` as static files with `index.html` fallback. JSON uses camelCase; durations are **seconds** (integers); timestamps are UTC ms.

Day boundaries: a "day" `YYYY-MM-DD` is `[local date at day_start_hour, next day at day_start_hour)` in the machine's local time zone. Sessions overlapping a boundary are **clipped**, never dropped.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/health` | `{ "app": "DayTrace", "version": "1.0.0" }` |
| GET | `/api/status` | `{ trackerRunning, paused, current: { exeName, appName, title, category, since } \| null }`. `trackerRunning` = newest `ended_at` across sessions/away within 2 × heartbeat |
| GET | `/api/day?date=YYYY-MM-DD` | Everything for the Today view (below) |
| GET | `/api/week?start=YYYY-MM-DD` | 7 days: `[{ date, activeSeconds, byCategory: [{ categoryId, seconds }] }]`, plus `totalSeconds`, `dailyAverageSeconds`, `weekdayAverageSeconds`, `bestDay`, and `productivityScore` (computed over the whole week's totals, same formula as §7.1, not an average of daily scores) |
| GET | `/api/apps?from=YYYY-MM-DD&to=YYYY-MM-DD` | Per exe: `{ exeName, appName, seconds, sessionCount, lastSeen, categoryId, matchedRuleId, isUncategorized }` sorted by seconds desc |
| GET | `/api/categories` | All categories with `exeRuleCount` and `titleRuleCount` (shown as "16 apps · 5 title rules") |
| POST/PUT/DELETE | `/api/categories[/{id}]` | Create / edit (name, color, productivity, sortOrder) / delete. Deleting moves its rules' apps to Uncategorized (cascade deletes rules). `is_system` categories return 409 on delete/rename |
| GET | `/api/rules` | All rules with category name |
| POST/PUT/DELETE | `/api/rules[/{id}]` | Create / edit / delete a rule. Validates: pattern non-empty; exe patterns lower-cased and must end in `.exe` |
| PUT | `/api/apps/{exeName}/category` | Body `{ categoryId }`. Upserts the exe rule for that app (the one-click assign used by the Apps page). `categoryId` of Uncategorized deletes the exe rule |
| GET/PUT | `/api/settings` | Read/update settings in §5.1 (validate ranges: poll 250–5000 ms, min_switch 0–10 s, idle 30–1800 s, day_start 0–6). GET also returns read-only `dbPath`, `dbSizeBytes`, `trackingSince` (earliest session start) |
| POST | `/api/settings/reset` | Restore the §5.1 defaults |

### 7.1 `/api/day` response
```json
{
  "date": "2026-10-28",
  "activeSeconds": 18720,
  "awaySeconds": 3600,
  "productivityScore": 72,
  "switches": 143,
  "longestFocus": { "seconds": 3120, "exeName": "code.exe", "appName": "Visual Studio Code", "start": 1761652800000 },
  "firstActivity": 1761640200000,
  "lastActivity": 1761675600000,
  "byCategory": [ { "categoryId": 1, "name": "Productive", "color": "#2F9E6E", "productivity": "productive", "seconds": 11000 } ],
  "topApps": [
    { "exeName": "code.exe", "appName": "Visual Studio Code", "categoryId": 1, "seconds": 9000, "sessionCount": 41, "categoryBreakdown": [] },
    { "exeName": "chrome.exe", "appName": "Google Chrome", "categoryId": 3, "seconds": 6720, "sessionCount": 64,
      "categoryBreakdown": [ { "categoryId": 3, "seconds": 2760 }, { "categoryId": 4, "seconds": 2280 }, { "categoryId": 5, "seconds": 840 }, { "categoryId": 1, "seconds": 840 } ] }
  ],
  "timeline": [
    { "kind": "session", "start": 0, "end": 0, "exeName": "", "appName": "", "title": "", "categoryId": 1 },
    { "kind": "away", "start": 0, "end": 0, "reason": "idle" }
  ]
}
```

Definitions:
- **activeSeconds** = sum of clipped session durations.
- **productivityScore** = `round(100 × productive / (productive + distracting))`; `null` if both are 0. Neutral time is excluded so that browsing/system time doesn't dilute the score.
- **switches** = number of sessions in the day whose exe differs from the previous session's exe (app switches, not title changes).
- **longestFocus** = longest run of consecutive sessions in the same **category** with no away period between, gaps < 60 s merged.
- **topApps** are grouped by exe. `categoryId` is the app's own category (its exe rule, else Uncategorized), which is what the category chip shows and edits. When title rules send some of that app's time to other categories, `categoryBreakdown` lists every category's share (empty when all time is in one category); the dashboard shows it as a muted line ("Split by title rules: 46m Browsing · 38m Entertainment …"). Filtering Top apps by a category (§8.2) shows apps with any time in that category, with the time column showing only that category's share.
- **timeline** = sessions and away periods in chronological order. For days with > 1,500 sessions, merge adjacent sessions of the same exe whose gap is < 5 s before returning.

## 8. Dashboard requirements

Static `wwwroot/index.html` + `app.css` + `app.js` (ES modules allowed, no bundler). Client-side hash routing: `#/today`, `#/week`, `#/apps`, `#/settings`. Light and dark themes via `prefers-color-scheme`; the design shows light mode, so derive dark mode from the same tokens.

### 8.0 Design source
The visual design lives in **`docs/design/`** as Claude Design source files, one per screen. **Read `docs/design/README.md` first**, then the file for the screen you're building, and match its layout, spacing, colors and type exactly.

| Screen | Design file |
|---|---|
| Today (§8.2) | `docs/design/today.dc.html` |
| Week (§8.3) | `docs/design/week.dc.html` |
| Apps & Categories (§8.4) | `docs/design/apps.dc.html` |
| Settings (§8.5) | `docs/design/settings.dc.html` |
| Tray menu (§6.5) | `docs/design/tray.dc.html` |

Live canvas for humans (private to the owner's claude.ai account; Claude Code can't open it): https://claude.ai/artifact/Bq3Jpw8YjZReux3N3Q197r. If the design changes, re-export the files into `docs/design/`; the files in the repo are what gets built.

Precedence: **PRD wins on behavior and data, design wins on look and layout.** Sample numbers in the design files are illustrative only.

Design tokens (light): font IBM Plex Sans (vendored woff2, no Google Fonts at runtime), tabular numbers; page `#F5F6F8`, surface `#FFFFFF`, border `#E3E6EB`, divider `#F0F1F4`, text `#16191F`, secondary text `#5B6270`, nav text `#3B414C`, active nav background `#ECEEF2`, primary button `#16191F` on white text; radius 12 px cards, 8 px controls; 44 px minimum touch targets; category colors from §5.3.

### 8.1 Shell
- Left sidebar (collapses to a top bar < 900 px): logo/name, nav (Today, Week, Apps & Categories, Settings), and a **live status pill** at the bottom: green dot "Tracking · Visual Studio Code", amber "Paused", grey "Tracker not running". Polls `/api/status` every 10 s.

### 8.2 Today (`#/today`, default)
- Header: date with ‹ › arrows (› disabled on today) and a "Today" button when viewing another day; date picker on click.
- **Stat cards (4):** Active time (sub: first–last activity and total away) · Productivity score out of 100 (with a thin productive/neutral/distracting bar and "4h 32m productive · 52m distracting") · Longest focus (duration, app, start–end) · App switches (sub: switches per active hour).
- **Day timeline:** horizontal band from first to last activity (padded to whole hours), colored blocks by category, hatched light-grey for away (tooltip shows reason). Hover a block → app, title, start–end, duration. Hour ticks below.
- **By category:** horizontal bar list (color swatch, name, duration, % of active), sorted desc. Clicking a category filters Top apps.
- **Top apps:** table — app name (+ exe in muted text, + the "Split by title rules" line when `categoryBreakdown` is non-empty), category chip (click to reassign via dropdown → `PUT /api/apps/{exe}/category`), time, sessions. Top 10, "Show all" expands.
- Empty state: "No activity recorded for this day."

### 8.3 Week (`#/week`)
- Header: week range with ‹ › (weeks start Monday) and a "This week" button.
- Stat cards: total active · daily average (sub: weekday average) · most active day · productivity score for the week.
- Stacked bar chart (Chart.js): one bar per day, stacked by category colors, day name and total under each bar.
- Category totals table: category, type, share bar with %, total time, per-day average.

### 8.4 Apps & Categories (`#/apps`)
- **Uncategorized callout** at top if any apps from the last 7 days are uncategorized: "4 apps need a category" with inline dropdowns.
- **Apps table** (last 30 days by default; range select 7/30/90 days): app, exe, time, last seen, category dropdown. Search box filters.
- **Categories panel:** list with color dot, name, productivity type (Productive / Neutral / Distracting segmented control), app count, edit/delete. "+ New category" opens an inline form (name, color from a 10-swatch palette, type).
- **Title rules panel:** list of rules ("Window title contains `YouTube` → Entertainment", optional "only in chrome.exe"), add/edit/delete. Helper text explains that title rules override app rules.
- Every change re-fetches and visibly updates; no page reload.

### 8.5 Settings (`#/settings`)
- Idle threshold (select: 1, 2, 3, 5, 10, 15 min), Min switch time (0–5 s), Day starts at (00:00–06:00), Poll interval (advanced, collapsed).
- Each setting is a row: plain-language label, one-line explanation, control on the right.
- Data: shows DB path, size and "tracking since" date; "Open data folder" is tray-only in v1 (show hint).
- Footer note: "All data stays on this PC. DayTrace makes no network connections."
- Buttons: Reset to defaults (`POST /api/settings/reset`) and Save changes.

### 8.6 Formatting rules
- Durations: `< 1 min` → `45s`; `< 1 h` → `23m`; otherwise `5h 12m`.
- Times: local, 24-hour or 12-hour per browser locale (`Intl.DateTimeFormat`).
- App display: `appName` if present, else exe without `.exe`.

## 9. Non-functional requirements

| Area | Requirement |
|---|---|
| Accuracy | Session boundaries within ±(poll interval + 1 s) of real switches |
| CPU | Tracker < 1 % average on a 4-core machine; no allocations-heavy work per tick |
| Memory | Tracker < 60 MB working set; Web < 120 MB |
| Durability | ≤ 30 s data loss on hard kill (heartbeat) |
| Storage | ≈ 0.5–2 MB per month at normal use |
| Startup | Tracker recording within 3 s of launch; dashboard first paint < 1 s on localhost |
| Privacy | No outbound network; Web bound to 127.0.0.1; titles not in Info logs |
| Compatibility | Windows 10 22H2+, Windows 11; standard user (no admin) |
| Resilience | Tracker survives Web crash and vice versa; DB locked → retry, never crash |

## 10. Testing

### 10.1 Unit tests (required; Core)
`SessionRecorder` with `FakeClock` and an in-memory `ISessionStore`:
1. First observation starts a session immediately.
2. Switch held ≥ min_switch → previous ends at `firstSeenAt`, new starts at `firstSeenAt`.
3. Flicker A→B (1 s)→A produces one session A.
4. A→B (1 s)→C (held) produces A then C, with C starting when C was first seen.
5. Idle `GoAway` backdates the session end to last input.
6. Lock while idle converts the away reason correctly.
7. Return then Observe starts immediately (no debounce).
8. Heartbeat updates `ended_at`; Shutdown closes session.
9. Clock moves backwards → clamped, no negative durations.
10. Forward time jump > 2× heartbeat → session closed at last heartbeat.
11. Zero-length sessions are deleted.

`Categorizer`: title rule beats exe rule; `exe_filter` respected; case-insensitivity; priority and tie-break; fallback to Uncategorized.

Aggregation: day clipping across midnight and across `day_start_hour`; productivity score with zero denominators; switches count; longest focus merge rules.

Migrations: fresh DB → current version; running twice is a no-op.

### 10.2 Manual test checklist (M3/M6)
- [ ] Alt-tab rapidly across 4 windows → no sub-2 s sessions recorded
- [ ] Settings app / Calculator show as their real exe, not ApplicationFrameHost
- [ ] Task Manager (elevated) recorded with name
- [ ] No input for idle threshold → away(idle) starting at last input
- [ ] Win+L for 2 min → away(locked) ≈ 2 min
- [ ] Sleep/resume → away(asleep)
- [ ] Pause 15 min auto-resumes
- [ ] Kill tracker in Task Manager → last session ends within 30 s of kill time
- [ ] Second launch does not create a second tray icon
- [ ] Start with Windows toggle survives reboot
- [ ] Dashboard works with Wi-Fi off

## 11. Milestones (build in order; each ends green: build + tests)

**M1 — Foundation**
Solution, three projects + tests, `Directory.Build.props` (nullable, warnings as errors, `LangVersion latest`), Core: models, `Db.Open()` (WAL, busy_timeout, foreign_keys ON), migrations, seed data, repositories, `IClock`.
✅ Tests for migrations and seed pass.

**M2 — Recording core**
`SessionRecorder` + `ISessionStore` (SQLite impl) + full unit test suite (§10.1 items 1–4, 8–11). Tracker: `ForegroundSource` (polling, process resolution, UWP fix, titles), wire into recorder, `--console` mode.
✅ Run `--console`, switch apps, see correct sessions in console and DB.

**M3 — Away states and tray**
`IdleMonitor`, `SystemStateMonitor`, pause, tray menu/tooltip/icon, single instance, autostart, logging, settings reload every 60 s. Tests §10.1 items 5–7.
✅ Manual checklist items for idle/lock/sleep/pause/kill/single-instance.

**M4 — Web API**
Minimal API, `Categorizer`, aggregation service, all endpoints in §7, validation, tests for categorizer and aggregation. Tracker "Open dashboard" launches Web.
✅ `curl` each endpoint against a DB with real data.

**M5 — Dashboard**
Shell, Today, Week, Apps & Categories, Settings per §8, built one screen at a time from its design file (§8.0). Vendored Chart.js and IBM Plex Sans.
✅ All views render with real data, light + dark, at 1440 px and 800 px widths.

**M6 — Packaging**
`dotnet publish` both apps `-r win-x64 --self-contained -p:PublishSingleFile=true` into one `dist/DayTrace/` folder; `README.md` with install (copy folder, run Tracker once) and uninstall (Exit, untick autostart, delete folder and `%LOCALAPPDATA%\DayTrace`).
✅ Fresh-user test on a clean Windows account.

## 12. Iteration 2 candidates (not now)
1. Browser extension for real URLs/domains (Chrome/Edge), categorized by domain
2. Media-aware idle (don't go idle while audio/video plays in the foreground app)
3. Replace polling with `SetWinEventHook` (foreground + name change)
4. Daily goals ("≤ 1 h Entertainment"), end-of-day summary notification
5. Privacy rules: exclude apps or redact titles for specific exes
6. Export CSV/JSON; delete a date range
7. Focus mode timer with distraction alerts

## 13. Known limitations (accepted for v1)
- Watching a video without touching input for > idle threshold counts as idle.
- Browser time is per tab *title*, not URL; sites with generic titles may be miscategorized.
- Time in fullscreen games that capture input normally still counts (input is detected).
- Multiple users on one PC each get their own data (per-user `%LOCALAPPDATA%`), which is desirable but not merged.

## 14. Open questions
- Should "System" time (Explorer, Settings) be hidden from Top apps by default? (Proposed: shown, but sorted normally.)
- Should the Web process shut itself down after N minutes with no requests to save memory? (Proposed: no for v1.)
