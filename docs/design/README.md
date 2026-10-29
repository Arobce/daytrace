# DayTrace dashboard design

These files are the source of the dashboard design made in Claude Design.
Live canvas (owner's account only): https://claude.ai/artifact/Bq3Jpw8YjZReux3N3Q197r

| File | Screen | Route |
|---|---|---|
| `today.dc.html` | Today | `#/today` |
| `week.dc.html` | Week | `#/week` |
| `apps.dc.html` | Apps & Categories | `#/apps` |
| `settings.dc.html` | Settings | `#/settings` |
| `tray.dc.html` | Tray menu and tray icon states | WinForms `NotifyIcon` menu (not web) |

## How to read them (for Claude Code)

The files are **reference, not code to ship**. They won't render on their own: they need the Claude Design runtime (`support.js`), which isn't included. Read them as source:

- **Layout and styling** are the inline `style="…"` attributes on plain HTML elements. Copy the values (sizes, spacing, colors, radii, font sizes and weights) into `wwwroot/app.css` as CSS classes and custom properties. Don't keep inline styles in the real dashboard.
- **`<x-dc>`** wraps the screen markup and **`<helmet>`** holds page-level styles. Ignore both wrappers.
- **`{{name}}`** is a placeholder filled with data. **`<sc-for list="{{items}}" as="x">`** is a loop and **`<sc-if value="{{cond}}">`** is a conditional. In the real app these become JS rendering from the API in PRD §7.
- **The `<script type="text/x-dc">` block** at the bottom only generates **sample data** for the mockup. Use it to understand the shape and formatting of values (for example `1h 52m` or `58%`), never as real data or logic.
- **Google Fonts links** are for the mockup only. The real dashboard vendors IBM Plex Sans locally (PRD §4.3).
- **Timeline and Week chart:** the mockup draws them with positioned `<div>`s. Build the timeline the same way. The Week chart uses Chart.js (PRD §8.3), styled to match the mockup: same colors, bar width, gridlines and labels.

If the design and the PRD disagree, the PRD wins for behavior and data, and the design wins for look and layout. Ask the user if unsure.
