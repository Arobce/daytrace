# CLAUDE.md — DayTrace

DayTrace is a local-only Windows time tracker: a tray app records which app/window is in the foreground, and a local web dashboard shows where the day went. **The full spec is `docs/PRD.md` — read it before starting any milestone, and treat it as the source of truth.** The visual design is in `docs/design/` (see its README).

## Stack
- C# / **.NET 10** (LTS). Do not target .NET 8/9 (end of support Nov 2026).
- `src/DayTrace.Core` (net10.0) — models, SQLite access, migrations, `SessionRecorder`, `Categorizer`, aggregation
- `src/DayTrace.Tracker` (net10.0-windows, WinForms) — tray app, Win32 interop, monitors
- `src/DayTrace.Web` (net10.0) — ASP.NET Core minimal API + static dashboard in `wwwroot/`
- `tests/DayTrace.Core.Tests` — xUnit
- SQLite via `Microsoft.Data.Sqlite` + `Dapper`; logging via Serilog file sink; Chart.js and IBM Plex Sans vendored in `wwwroot/vendor/`
- Dashboard look: before building any screen (M5) or the tray menu (M3), read `docs/design/README.md` and that screen's `.dc.html` file (mapping in PRD §8.0). They are Claude Design source files: copy layout and style values from them into `app.css`, but don't copy their template syntax or sample-data scripts. PRD wins on behavior and data; the design wins on look and layout.

## Commands (PowerShell, from repo root)
```powershell
dotnet build                                        # whole solution
dotnet test                                         # all tests
dotnet run --project src/DayTrace.Tracker -- --console   # tracker with live console output
dotnet run --project src/DayTrace.Web -- --port 5217     # dashboard at http://127.0.0.1:5217
dotnet publish src/DayTrace.Tracker -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/DayTrace
dotnet publish src/DayTrace.Web     -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist/DayTrace
```
Data lives in `%LOCALAPPDATA%\DayTrace\` (`daytrace.db`, `logs\`). For development you can point both apps at a scratch DB with `--db <path>`.

## How to work in this repo
- Build **milestone by milestone** (PRD §11). Finish each with `dotnet build` and `dotnet test` green, then stop and summarize what was done and what to test manually.
- Reference requirement IDs (e.g. `T-21`) in commit messages and code comments where the logic is non-obvious.
- Ask before adding any NuGet package or JS library not listed in PRD §4.3.
- Ask before changing the DB schema, API shapes, or anything marked as a decision in the PRD. If the PRD is ambiguous, ask rather than guess.
- Write the unit tests for `SessionRecorder` and `Categorizer` alongside the code, not afterwards.

## Architecture rules (do not break)
1. **One writer per table.** Tracker writes `sessions`, `away_periods`. Web writes `categories`, `rules`, `settings`. Never cross this.
2. **All stored times are UTC Unix milliseconds (INTEGER).** Convert to local time only in the Web aggregation layer and the browser. Never call `DateTime.Now` in Core — use `IClock`.
3. **Categories are never stored on sessions.** They are resolved at read time by `Categorizer` so rule changes apply retroactively.
4. **`SessionRecorder` stays pure** (no Win32, no timers, no SQLite types). It receives events and calls `ISessionStore`. All Win32 code lives in `DayTrace.Tracker/Native/`.
5. **Tracker is single-threaded.** All tracking work runs on the WinForms UI thread via `System.Windows.Forms.Timer` and `SystemEvents` callbacks marshalled to it. No locks needed; don't introduce background threads for tracking.
6. **Web binds to `http://127.0.0.1:{port}` only.** Never `0.0.0.0`, `*`, or `localhost` wildcards. No outbound network calls anywhere. No CDNs in the dashboard.
7. **Never crash the tracker.** Catch and log per-tick exceptions; one weird window must not stop tracking.

## Code conventions
- `Nullable` enabled, `TreatWarningsAsErrors` true, file-scoped namespaces, `sealed` by default, records for immutable models.
- P/Invoke with `[LibraryImport]` (source-generated), `SetLastError = true` where errors matter; wrap handles in `SafeHandle`.
- SQL lives next to the repository that uses it, as `const string`. Always parameterized.
- Dashboard: plain ES modules (`wwwroot/js/*.js`), no framework, no build step. CSS custom properties for colors, light + dark via `prefers-color-scheme`. Fetch helpers in one `api.js`.
- Keep files small and named after what they contain (`IdleMonitor.cs`, `ForegroundSource.cs`).

## Windows gotchas (read before touching the tracker)
- **Not a Windows Service.** Services run in session 0 and can't see the user's windows. It's a per-user tray app started via `HKCU\...\Run`.
- **Message loop required.** `SystemEvents.SessionSwitch` / `PowerModeChanged` (and any future `SetWinEventHook`) only fire on a thread pumping messages. The WinForms `ApplicationContext` provides it — don't run tracking from a console `Main` loop without one. Unsubscribe `SystemEvents` handlers on exit (static events leak).
- **UWP apps** show as `ApplicationFrameHost.exe`; resolve the real process via a child window with a different PID (PRD T-13).
- **Elevated processes:** use `PROCESS_QUERY_LIMITED_INFORMATION` + `QueryFullProcessImageName`; don't require admin.
- **Idle time:** `GetLastInputInfo` tick counts wrap after 49.7 days — compute with `unchecked((uint)Environment.TickCount - lii.dwTime)`.
- **GetForegroundWindow can return 0** during focus transitions — skip that tick.
- **SQLite:** open with `Journal Mode=WAL`, `PRAGMA busy_timeout=5000`, `PRAGMA foreign_keys=ON`. Two processes share the file.
- **Privacy:** never log window titles at Info level.

## Testing
- Unit tests in `tests/DayTrace.Core.Tests`; use `FakeClock` and an in-memory `ISessionStore` for recorder tests, and a temp-file SQLite DB for repository/aggregation tests.
- The required test cases are listed in PRD §10.1 — all must exist and pass.
- Win32 code isn't unit tested; verify it with `--console` mode and the manual checklist in PRD §10.2. When you finish M2/M3, tell the user which checklist items to run.
