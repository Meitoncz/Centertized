# Centertized

Native Windows tray utility. First feature: a global hotkey (user-configurable, with
best-effort conflict detection) centers the active/foreground window on its current
monitor. Built to be extended with more window-management features over time.

## Language convention (user request, 2026-09-22)

- All conversational replies to the user: **Czech**.
- All comments inside the application source code: **Czech**.
- All user-facing app UI strings (menu items, window/page titles, button labels,
  in-app messages): **English** for now — Czech localization may be added later as an
  explicit feature, not by default. Don't translate UI strings to Czech unless asked.
- This file (CLAUDE.md) and any other Claude-internal notes: English is fine.

## Stack

- C# / .NET 10 (`net10.0-windows`), WPF.
- WPF-UI (lepoco/wpfui) for the Fluent Design shell, Mica/Acrylic backdrop, and system
  light/dark + accent theme sync (`Wpf.Ui.Appearance.SystemThemeWatcher`).
- H.NotifyIcon.Wpf for the tray icon (`WPF-UI.Tray` was considered as a first-party
  alternative — less mature at evaluation time; revisit if H.NotifyIcon causes friction).
- Serilog for file logging under `%AppData%\Centertized\logs` — this is a tray-only
  background app with no console, so file logging is the only diagnostic path.

## Architecture

Three projects:
- `src/Centertized` — WPF app: tray icon/menu, settings window, all UI/XAML.
- `src/Centertized.Core` — plain class library, **no WPF reference**. Win32 interop
  (hotkey registration, window/monitor geometry), the `IWindowAction` extensibility
  contract, settings persistence. Kept WPF-free so it's testable with plain
  `dotnet test` (no STA thread / display needed).
- `src/Centertized.Tests` — xUnit tests, references `Centertized.Core` only.

Extensibility contract for new hotkey-triggered features: implement `IWindowAction` in
`Centertized.Core/Actions/`, register it in `WindowActionCatalog`. That should be the
only change needed — no touching `HotkeyActionRegistry`, the tray, or the Shortcuts
settings page (it renders off the catalog, one row per registered action).

Key non-obvious technical constraints — these are by design, don't try to "fix" them:
- Hotkey conflict detection (`RegisterHotKey` failing with `ERROR_HOTKEY_ALREADY_REGISTERED`)
  only catches other apps that also use `RegisterHotKey`. Apps using a low-level keyboard
  hook instead are undetectable in advance — there is no Win32 API to query that.
- A window running elevated (as administrator) cannot be moved by this app while the app
  itself runs non-elevated — that's UIPI, an OS security boundary. v1 runs non-elevated
  by design (avoids UAC friction); an elevated opt-in mode is a possible future feature,
  not a bug to silently work around.
- Centering must read the window's bounds via
  `DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, ...)`, not `GetWindowRect` —
  the latter includes invisible resize-border padding, so naive centering looks visibly
  off-center by a few pixels.
- DPI awareness must be Per-Monitor V2 (declared in `app.manifest`). Verify this via Task
  Manager's "DPI Awareness" column after any TargetFramework/SDK change — this has
  silently regressed before in the WPF ecosystem despite a correct manifest.
- WPF-UI controls (`FluentWindow`, `NavigationView`, ...) render/work only if
  `App.xaml`'s `Application.Resources` merges `<ui:ThemesDictionary Theme="Light" />` +
  `<ui:ControlsDictionary />` (see `App.xaml`). Without this they have no control
  template at all — internal named parts stay null and things like
  `NavigationView.Navigate(...)` throw `NullReferenceException` deep inside WPF-UI
  rather than failing obviously. Found this the hard way in Phase 1; don't remove it.
  `SystemThemeWatcher.Watch(window)` only handles *live* theme syncing on top of this,
  it doesn't replace the initial merge.
- `NavigationView.Navigate(...)` (and similar calls needing the control's template)
  must not run directly in a window's constructor — the template isn't applied yet at
  that point. Hook `RootNavigation.Loaded` and navigate from there instead
  (see `SettingsWindow.xaml.cs`).

## Known issues (deferred to Phase 4 polish)

- Tray context menu (`App.xaml`'s `ContextMenu`/`MenuItem` on the `TaskbarIcon`) renders
  with plain default Windows styling (white) even in dark mode — WPF-UI doesn't seem to
  theme it automatically the way it themes windows/pages. Likely fix: either explicit
  WPF-UI-aware styling on the menu, or switch the tray icon to the `WPF-UI.Tray` package
  (its `NotifyIcon`/menu is theme-synced out of the box) — see the Stack section above,
  this was flagged as a tradeoff when H.NotifyIcon.Wpf was chosen.
- Switching the Windows light/dark theme *while* SettingsWindow is open produces a
  visually broken half-and-half state: the content area (right side) picks up the new
  theme's brush correctly, but the NavigationView pane / Mica backdrop (left side, title
  bar) stays on the old (dark) look. Looks like a sync gap between WPF-UI's resource
  dictionary swap and the DWM-level Mica dark-mode attribute. Not yet root-caused —
  needs investigation before relying on live theme switching looking correct.

Full rationale/tradeoffs (library comparisons, phased roadmap, verification steps) came
from the architecture-planning session on 2026-09-22 — ask the user if you need that
history and it isn't in conversation context anymore.

## Commands

- Build: `dotnet build`
- Run the app: `dotnet run --project src/Centertized`
- Test: `dotnet test src/Centertized.Tests`

## Current status

Phase 0 (scaffold, tray icon, single instance) and Phase 1 (Settings window shell:
FluentWindow + NavigationView + Mica + live theme sync) are done and manually verified
by the user. Two cosmetic issues found during Phase 1 verification are tracked above
under "Known issues" rather than blocking progress. Next up: Phase 2 (hotkey capture +
registration + conflict detection) — see git log for exact commits, and the phased
roadmap (Phase 0 → Phase 5) for what's still ahead.
