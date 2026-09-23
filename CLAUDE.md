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
- WPF-UI (lepoco/wpfui) for the Fluent Design shell, Mica backdrop, and system
  light/dark + accent theme sync (`Wpf.Ui.Appearance.SystemThemeWatcher`/
  `ApplicationThemeManager`). Acrylic was evaluated and dropped 2026-09-23 — the user
  compared both live and preferred Mica as closer to native Windows look; only Mica is
  supported now (see `ThemePreference`/theme notes below, there's no backdrop setting).
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
section of the Settings window (it renders off the catalog, one row per registered
action).

### Settings window architecture (rewritten 2026-09-23)

`SettingsWindow` is **one single scrollable `FluentWindow`, no `NavigationView`, no
separate `Page` classes**. All content (Shortcuts, General, About) lives directly in
`SettingsWindow.xaml` as sections inside one `ScrollViewer` > `StackPanel`, using
`ui:TextBlock FontTypography="BodyStrong"` as section headers and `ui:CardControl`/
`ui:CardExpander` as individual setting rows — this mirrors the official WPF-UI Gallery's
own `SettingsPage.xaml` pattern exactly (fetched from
[lepoco/wpfui](https://github.com/lepoco/wpfui) on GitHub as a reference before writing
this). All Shortcuts/General/About logic (hotkey capture wiring, autostart toggle, theme
combo, About version text) lives together in `SettingsWindow.xaml.cs`.

This replaced an earlier `NavigationView`-based sidebar design from Phase 1. Two reasons,
both from direct user feedback 2026-09-23: (1) a sidebar felt oversized for an app with
only 3 small sections ("nevhodný pro tenhle typ aplikace"), and (2) removing
`NavigationView` incidentally fixed the live-theme-switch corruption bug (see Known
issues) since that bug is specifically triggered by `NavigationView`. Don't reintroduce
`NavigationView` here without re-checking whether lepoco/wpfui#1639 has been fixed
upstream first.

Key non-obvious technical constraints — these are by design, don't try to "fix" them:
- **Win+letter/number cannot be captured in `HotkeyCaptureControl`, and don't try to fix
  this with a global `WH_KEYBOARD_LL` hook again.** Win+function-key (e.g. Win+F12) works
  fine via plain `GetAsyncKeyState` (see `HotkeyCaptureControl.xaml.cs`), but Win+letter/
  number is claimed by the shell before a normal WPF app ever sees the keydown (that's
  why it opens Start Menu/Explorer/etc. instead). We tried fixing this properly on
  2026-09-23 with a temporary low-level keyboard hook (installed while the capture button
  has focus, suppressing the event before the shell saw it) — it technically worked for
  detecting the combo, but a capture that didn't produce a valid result (e.g. modifiers
  read as empty — a real bug in that path, never fully root-caused) left the button still
  focused, which left the hook installed, which **suppressed all keyboard input
  system-wide, including Alt+Tab**, until the user killed the whole process. Scoped to
  while Centertized was running (killing the process removes its hooks automatically,
  confirmed), not a lasting OS-level issue, but still a real "your keyboard stopped
  working" incident during testing. Reverted at the user's explicit request. If this is
  ever revisited, it needs a hard safety net *before* shipping again — e.g. a watchdog
  timer that force-unhooks after N seconds regardless of focus state, and it should be
  tested far more thoroughly than "it builds and the happy path works" before touching a
  real keyboard with it.
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
- WPF-UI controls (`FluentWindow`, `CardControl`, ...) render/work only if `App.xaml`'s
  `Application.Resources` merges `<ui:ThemesDictionary Theme="Light" />` +
  `<ui:ControlsDictionary />` (see `App.xaml`). Without this they have no control
  template at all. Found this the hard way in Phase 1; don't remove it.
  `SystemThemeWatcher.Watch(window)`/`ApplicationThemeManager.Apply(...)` only handle
  *live* theme syncing on top of this, they don't replace the initial merge.
- **`WindowBackdropType` must be set directly on the `FluentWindow` instance**
  (`WindowBackdropType = WindowBackdropType.Mica;` in code, or the XAML attribute) —
  passing a backdrop type to `ApplicationThemeManager.Apply(theme, backdrop, ...)` alone
  does *not* turn on the actual DWM Mica compositing on that window; it only affects
  which color resources get picked. Skip the direct property and Mica silently never
  renders at all (found this 2026-09-23 — the window just showed a flat WPF-drawn
  background, no blur, no accent tint).
- **Use `<ui:TextBlock>` (with `FontTypography="..."`), not plain `<TextBlock>`, for any
  primary text in WPF-UI windows.** Plain `TextBlock` doesn't inherit a theme-aware
  foreground from anywhere in this setup and renders as plain black text in dark mode —
  looks fine in Light, silently broken in Dark. For secondary/muted text, set
  `Foreground="{ui:ThemeResource TextFillColorSecondaryBrush}"` explicitly (WPF-UI's own
  markup extension, not plain `{DynamicResource ...}` — that's what the library's own
  Gallery app uses). Found this 2026-09-23 from a user screenshot showing genuinely
  black (not just low-contrast) text in dark mode on every page using bare `<TextBlock>`.
- `IWin32WindowService.IsEligibleForActions` must **not** exclude windows belonging to
  Centertized's own process. It's tempting to add that exclusion (early versions did,
  reasoning "don't act on our own Settings window") but it's wrong: SettingsWindow is a
  normal visible top-level window and the user reasonably expects to be able to center/
  maximize it like anything else. The only Centertized-owned window that should never
  match is the hidden message-only hotkey sink, and that's already filtered out by the
  `IsWindowVisible` check earlier in the same method — no separate process-id check is
  needed. Removed 2026-09-23 after the user asked "why can't I center this window with
  its own shortcut?" and the answer was "no good reason."

## Known issues

- Tray context menu (`App.xaml`'s `ContextMenu`/`MenuItem` on the `TaskbarIcon`) renders
  with plain default Windows styling (white) even in dark mode — WPF-UI doesn't seem to
  theme it automatically the way it themes windows/pages. Likely fix: either explicit
  WPF-UI-aware styling on the menu, or switch the tray icon to the `WPF-UI.Tray` package
  (its `NotifyIcon`/menu is theme-synced out of the box) — see the Stack section above,
  this was flagged as a tradeoff when H.NotifyIcon.Wpf was chosen. Still open.
- ~~Switching the Windows light/dark theme while SettingsWindow is open produces a
  visually broken half-and-half state~~ **RESOLVED 2026-09-23** — root cause confirmed
  as a real, still-open upstream bug ([lepoco/wpfui#1639](https://github.com/lepoco/wpfui/issues/1639)):
  `ApplicationThemeManager.Apply()`/`ApplySystemTheme()` breaks a `NavigationView`'s pane
  and content when called on an already-open window. Fix was architectural, not a
  workaround: **`SettingsWindow` no longer uses `NavigationView` at all** — see
  "Settings window architecture" below. Without `NavigationView`, live theme switching
  via `SystemThemeWatcher`/`ApplicationThemeManager.Apply()` on the open window works
  correctly, confirmed by the user switching System/Light/Dark live with no reopen and
  no visual corruption.

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
position" idea from `TODO.md` as the real second action (`ToggleMaximizeAction`) rather
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
- The "auto-center every newly-opened window" idea from `TODO.md` is still open and
  needs a different mechanism than the hotkey/`IWindowAction` pattern (something like a
  `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` watcher toggled from a settings
  checkbox, not a catalog action) — worth designing separately when picked up.

## Session 2026-09-23: visual polish pass — done

Picked up the 2026-09-22 TODO (Mica/Acrylic wasn't rendering correctly, wanted a
backdrop picker). What actually shipped, after several rounds of live feedback with
screenshots from the user's real screen:

- **Theme picker** (System default / Light / Dark) on the General section —
  `ThemePreference` enum in `Centertized.Core/Settings`, persisted in `AppSettings`.
  No backdrop picker — Acrylic got dropped entirely after a direct side-by-side
  comparison (see Stack section).
- **Root-caused why Mica wasn't rendering**: `WindowBackdropType` has to be set on the
  `FluentWindow` instance directly, not just passed to `ApplicationThemeManager.Apply()`
  — see Known issues / technical constraints above.
- **Root-caused why dark mode had black text**: bare `<TextBlock>` doesn't pick up a
  theme-aware foreground in this setup; needed `<ui:TextBlock>` /
  `{ui:ThemeResource TextFillColorSecondaryBrush}` everywhere, matching the WPF-UI
  Gallery's own reference XAML (fetched from GitHub for this).
- **Removed `NavigationView` entirely** — user feedback was that a sidebar felt wrong
  for a 3-section settings window, and this happened to also be the exact trigger for
  the confirmed-upstream live-theme-switch bug (lepoco/wpfui#1639). One architectural
  change fixed both: see "Settings window architecture" above. Live theme switching
  (System/Light/Dark) now works with **no window reopen and no visible refresh**,
  confirmed by the user.
- **Startup behavior flipped**: the Settings window now shows by default when the app
  starts (previously: tray-only, silent). Added a "Start minimized" toggle
  (`AppSettings.StartMinimized`) for users who want the old tray-only behavior back.
- **Tray icon double-click** now opens Settings too (`TrayLeftMouseDoubleClick` on the
  `TaskbarIcon` in `App.xaml`) — standard tray-app convention that was missing.
- **Fixed a real usability bug the user caught by trying it**: `IsEligibleForActions`
  used to exclude Centertized's own windows, which meant the center/maximize hotkeys
  silently did nothing when the Settings window itself was focused. No good reason for
  that exclusion existed — removed (see technical constraints above).

All of the above was verified either by the user directly (screenshots, live testing)
or by this session using `PrintWindow`+`SetProcessDpiAwarenessContext` to screenshot the
running window and inspect it directly via the Read tool — a reusable pattern for
visually checking WPF rendering without needing the user, documented here since it
proved genuinely useful: capture with `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`
(flag `0x2`) into a `Bitmap`, find the target `hwnd` by enumerating windows for the
process and matching on title (`FindWindow` alone was unreliable in testing).

Still open, unchanged from before: tray context menu theming (Known issues), a
first-run tray balloon, elevated-window (UIPI) behavior unverified live, and the
"auto-center every newly-opened window" idea from `TODO.md` (needs a
`SetWinEventHook`-based watcher, a different mechanism than the hotkey/action pattern).
