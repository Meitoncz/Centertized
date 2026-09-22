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

Phase 0 (scaffold, tray icon, single instance), Phase 1 (Settings window shell:
FluentWindow + NavigationView + Mica + live theme sync), and Phase 2 (hotkey capture,
registration, conflict detection, persistence) are done. Two cosmetic issues found
during Phase 1 verification are tracked above under "Known issues" rather than blocking
progress.

Phase 2 core pipeline (JSON settings -> Hotkey.TryParse -> HotkeyActionRegistry.TryBind
-> real RegisterHotKey -> WM_HOTKEY -> Dispatch -> IWindowAction.ExecuteAsync) was
verified end-to-end without any GUI interaction: pre-seeded `settings.json` with a
binding, launched the app, and used `keybd_event` (P/Invoke from PowerShell) to
simulate the actual key combo — confirmed via `%AppData%\Centertized\logs\activity.log`
that the action fired. This pattern (simulate real global input via `keybd_event`,
check a log file for the effect) is reusable for testing hotkey-related behavior
without needing a human to click/press anything. The one thing NOT covered this way is
the live capture UI itself (`HotkeyCaptureControl` on the Shortcuts page reacting to
an actual click + keypress) — that still needs a human, since it's real GUI interaction.

Phase 3 (real window centering via `Win32WindowService` + `WindowCenteringCalculator`)
is also done. Verified for real (not just unit tests) using the same no-GUI approach as
Phase 2, extended with a real second window: launched Notepad via PowerShell, moved it
off-center with `MoveWindow`, made it foreground with `SetForegroundWindow`, fired the
configured hotkey via `keybd_event`, then read its resulting position back. Two things
worth knowing for next time:
- The verifying PowerShell process is **not** DPI-aware by default, so its own
  `GetWindowRect`/`Screen.WorkingArea` readings are DPI-*virtualized* and differ from
  Centertized's (Per-Monitor-V2 aware) real physical coordinates by the monitor's scale
  factor (seen directly on this machine: a consistent 1.25x gap on a 125%-scaled
  monitor). To verify Centertized's actual behavior from PowerShell, call
  `SetProcessDpiAwarenessContext((IntPtr)(-4))` **before** any window/monitor P/Invoke
  calls in that script — then the numbers match exactly. Confirmed this way: the
  window's real DWM extended-frame-bounds ended up centered on the real physical work
  area to within ~1px on one axis, exactly as `WindowCenteringCalculator` intends.
- Maximize → hotkey correctly restores first, then centers the restored size (verified
  `IsZoomed` flips true -> false and the restored window lands centered).
- Elevated-window handling (UIPI) was **not** verified live — this dev machine has UAC
  disabled, which means there may be no real integrity-level split to test against
  (everything could already be running elevated). Needs a real check on a machine with
  normal UAC settings before trusting the documented elevated-window limitation as
  confirmed rather than just "expected per Win32 docs."

Phase 4 is mostly done: Serilog now backs all logging (`%AppData%\Centertized\logs\
centertized-<date>.log`, rolling daily, 14-day retention) via `Microsoft.Extensions.
Logging.ILogger` passed through `WindowActionContext`/`HotkeyActionRegistry` — Core
only depends on the logging *abstractions* package, never Serilog directly, so it's
still WPF/Serilog-free. `Services/TrayNotificationSink.cs` is a custom Serilog sink that
turns any Warning+ log event into a tray balloon automatically — so
`CenterActiveWindowAction` just logs normally (e.g. when `SetWindowPos` fails on a
likely-elevated window) and the notification happens as a side effect of logging, no
separate notification plumbing needed. Verified for real: made another process hold the
same hotkey combo via `RegisterHotKey(IntPtr.Zero, ...)` (no window needed for that
call), confirmed Centertized logs the expected Warning on startup rebind failure and
keeps running (the old ad-hoc `WriteCrashLog`/`crash.log` is gone, fully replaced). The
tray balloon's actual on-screen appearance was not visually confirmed (no screen
access) — worth a human glance next time a Warning-level event fires for real.
`AutostartService` (HKCU Run key, `Services/AutostartService.cs`) is wired into a
`ToggleSwitch` on the General page; the registry read/write/delete mechanics were
verified directly, but the toggle's click path itself wasn't (same "needs a human" gap
as the hotkey capture UI). About page shows the real assembly version now.

Icon done: `src/Centertized/Resources/icon.ico` (multi-size, PNG-in-ICO — 16 through 256
px), a simple "viewfinder corners + center dot" design generated programmatically
(System.Drawing/GDI+, source not kept in the repo — it was a scratch one-off, see git
log if it needs regenerating/tweaking). Wired via `<ApplicationIcon>` in
`Centertized.csproj` (so it's the .exe's own file icon too) and the tray icon loads it
via `Icon.ExtractAssociatedIcon(...)` on the running exe rather than duplicating the
file — one source of truth. User picked this as a placeholder in the "user's own logo
later" sense — they may swap in their own icon eventually, but this one is a real,
finished icon, not a stub. Verified by extracting the icon straight from the built .exe
and inspecting it (a Read tool can view images directly — no human needed for this
particular check, unlike most of the UI verification elsewhere in this file).

Still open from Phase 4: a first-run tray balloon to help users discover the icon
exists.

Phase 5 (extensibility proof) is done, using the "toggle maximize/restore to previous
position" idea from `IDEAS.md` as the real second action (`ToggleMaximizeAction`) rather
than a throwaway demo. Confirmed the success criterion from the plan: adding it touched
exactly `Centertized.Core/Actions/ToggleMaximizeAction.cs` (new file) and one line in
`App.xaml.cs`'s `ActionCatalog = new WindowActionCatalog([...])` — nothing in
`HotkeyActionRegistry`, the tray, `SettingsWindow`, or `ShortcutsPage.xaml` needed
touching; the Shortcuts page picked up the new row automatically since it's catalog-
driven. It DID require adding two new members to `IWin32WindowService`
(`Maximize`, `TrySetBounds`) — that's expected and fine, the success criterion is about
the hotkey/tray/Settings plumbing not needing changes, not about the window-management
API surface being frozen; it's meant to grow as real actions need it.

Verified for real with the same Notepad + `keybd_event` approach as Phases 2-3: moved
Notepad to known bounds, fired the hotkey once (maximized, `IsZoomed` true), fired it
again (restored to the exact original `GetWindowRect`, confirmed from a DPI-aware
PowerShell reading — see the Phase 3 DPI note above, same gotcha applies here since each
new PowerShell process needs its own `SetProcessDpiAwarenessContext` call).

One incidental finding: if you read the Serilog log file from PowerShell, use
`Get-Content ... -Encoding UTF8` — without it, Czech diacritics come out garbled. The
log file itself is correctly UTF-8 encoded; it's purely a Windows PowerShell 5.1
`Get-Content` default-encoding-detection quirk, not a bug in the app.

Remaining open items (not blocking, just not done yet):
- First-run tray balloon so users discover the icon exists.
- Everything flagged above as "needs a human": visual check of the Settings
  window/tray-menu theming issues (see Known Issues), the hotkey capture UI's actual
  click+keypress interaction, the toggle switch's click path, and the tray balloon's
  on-screen appearance.
- Elevated-window (UIPI) behavior still unverified live (this dev machine has UAC off).
- The "auto-center every newly-opened window" idea from `IDEAS.md` is still open and
  needs a different mechanism than the hotkey/`IWindowAction` pattern (something like a
  `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` watcher toggled from a settings
  checkbox, not a catalog action) — worth designing separately when picked up.

## Session end 2026-09-22: visual polish pass needed next

User did a hands-on pass and reported: core functionality (tray, hotkey, centering,
Settings shell) works, but the **visual polish needs real work** — specifically the
feeling that Mica/Acrylic isn't being applied correctly. Two concrete asks for next
session (also in `IDEAS.md`):

1. **Add a backdrop-type picker to Settings (General page)** — let the user choose
   Mica / Acrylic / None instead of the hardcoded `SystemThemeWatcher.Watch(this)`
   default in `SettingsWindow.xaml.cs`. Implementation sketch: add a
   `WindowBackdropType` (or a simple string) field to `AppSettings`
   (`Centertized.Core/Settings/AppSettings.cs`), a picker control on `GeneralPage`, and
   call `SystemThemeWatcher.Watch(this, chosenBackdrop)` (it takes a
   `Wpf.Ui.Controls.WindowBackdropType` as its second param, confirmed in Phase 1) with
   the saved choice instead of the implicit default. Apply it on `SettingsWindow`
   construction and whenever the setting changes while the window is open.
2. **General Mica/Acrylic correctness pass** — this is likely the same root cause as
   the already-documented Known Issue above (live theme-switch leaves the NavigationView
   pane / title bar area out of sync with the content area). Worth investigating
   properly this time (WPF-UI GitHub issues/discussions for known Mica bugs, whether
   `ExtendsContentIntoTitleBar` or `WindowCornerPreference` interact badly with the
   backdrop, whether it's specific to this Windows build) rather than deferring again —
   the user explicitly flagged this as the top priority for the next session.

Do these two together as one pass, not separately — they're the same subsystem.
