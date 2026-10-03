# Centertized

Native Windows tray utility. First feature: a global hotkey (user-configurable, with
best-effort conflict detection) centers the active/foreground window on its current
monitor. Built to be extended with more window-management features over time.

## Language convention (user request, 2026-09-22)

- All conversational replies to the user: **Czech**.
- All comments inside the application source code: **Czech**.
- All user-facing app UI strings (menu items, window/page titles, button labels,
  in-app messages): **localized through resource dictionaries** (`Resources/Strings.en.xaml`
  is the base/fallback, `Strings.cs.xaml` overlays it; `Services/Loc.cs`). Never hard-code
  UI text: add the key to BOTH dictionaries, use `{DynamicResource Key}` in XAML and
  `Loc.Get/Format` in code (updated 2026-09-23 when Czech localization shipped). Log
  messages and code comments stay Czech.
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
  itself runs non-elevated — that's UIPI, an OS security boundary. The app runs non-elevated
  by default (avoids UAC friction); the opt-in "Run as administrator" setting (see the
  2026-10-03 entry) is the supported way around it, not a hack to add elsewhere.
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

- ~~Tray context menu renders wrong~~ **RESOLVED 2026-09-23** (native Win32 popup menu, see the
  last session entry). Original note kept for history: the tray context menu rendered
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

Icon: the original programmatic placeholder was replaced 2026-09-23 by the user's own
custom logo (`Resources/centertized_icon.png`, 1085x1085, plus the Affinity source
`centertized_icon.af`, kept in the repo as the editable master). `Resources/icon.ico`
is generated from that PNG (PNG-in-ICO, sizes 16/20/24/32/40/48/64/128/256, bicubic
downscale via System.Drawing) — **regenerate icon.ico from the PNG if the logo changes**,
don't hand-edit the .ico. It's used in three places: `<ApplicationIcon>` in
`Centertized.csproj` (the .exe's file icon), as a WPF `<Resource>` for the Settings
window's `ui:TitleBar.Icon` (`ui:ImageIcon`), and for the tray icon via
`App.LoadTrayIcon()`, which loads the ICO resource at a DPI-appropriate size (16 * DPI
scale) — `Icon.ExtractAssociatedIcon` was dropped because it only ever returns 32x32,
which looks soft in the tray on high-DPI displays.

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
- Everything flagged above as "needs a human": visual check of the Settings
  window/tray-menu theming issues (see Known Issues), the hotkey capture UI's actual
  click+keypress interaction, the toggle switch's click path, and the tray balloon's
  on-screen appearance.
- Elevated-window (UIPI) behavior still unverified live (this dev machine has UAC off).

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

Still open, unchanged from before: tray context menu theming (Known issues) and
elevated-window (UIPI) behavior unverified live. First-run tray balloon and
auto-center-new-windows shipped later the same day — see the next session entry below.

## Session 2026-09-23 (cont'd): auto-center new windows + first-run tray balloon

Picked up the two remaining `TODO.md` items.

**First-run tray balloon**: `AppSettings.HasShownTrayHint` flag, shown once from
`SettingsWindow.OnClosing` via `App.ShowTrayHintIfNeeded()` the first time the window is
hidden (not closed) — the moment the app "disappears" into the tray for the first time.

**Auto-center new windows** (`NewWindowWatcher.cs`, `Centertized.Core/WindowManagement`):
a `SetWinEventHook` watcher, toggled by a General-section `ToggleSwitch`
(`AppSettings.AutoCenterNewWindows`), not a catalog action — it has no hotkey, so it
doesn't fit the `IWindowAction`/`WindowActionCatalog` pattern at all.

Design notes worth keeping for next time:
- **Hooked event is `EVENT_OBJECT_SHOW`, not `EVENT_SYSTEM_FOREGROUND`.** First attempt
  used FOREGROUND (fires when a window becomes active) plus an artificial ~200ms "settle"
  delay before centering (worry: some apps resize themselves right after creation). User
  feedback: this made the jump *more* visible, not less — the window fully rendered at its
  original spot, sat there for 200ms, then visibly jumped to center. Fixed by switching to
  `EVENT_OBJECT_SHOW` (fires at `ShowWindow(SW_SHOW)`, earlier than FOREGROUND) and
  dropping the artificial delay — center immediately, with one ~40ms retry only if the
  first geometry read fails (DWM occasionally hasn't computed
  `DWMWA_EXTENDED_FRAME_BOUNDS` yet at the exact SHOW instant). This is a **best-effort**
  improvement, not a guarantee — Centertized reacts to another process's window
  asynchronously (via a hidden message-loop callback), and Windows has no API to
  reposition a foreign window before its first paint. Said this plainly to the user rather
  than overselling it.
- **`WindowCenterer` extracted** (`Centertized.Core/Actions/WindowCenterer.cs`) — the
  actual geometry-read + `SetWindowPos` logic used to live only in
  `CenterActiveWindowAction`, which always centers `GetForegroundWindowHandle()`. The
  watcher needs to center the *specific* hwnd from the WinEvent, which is frequently not
  yet the foreground window when `EVENT_OBJECT_SHOW` fires — so it can't reuse
  `CenterActiveWindowAction.ExecuteAsync()` as-is. `WindowCenterer.TryCenter(service,
  logger, hwnd, failureLogLevel)` is the shared core both now call.
- **`failureLogLevel` parameter exists for a real reason, not speculative flexibility**:
  `EVENT_OBJECT_SHOW` fires for far more than top-level app windows — WPF's own internal
  popups (`ComboBox` dropdowns, tooltips, `ToggleSwitch` template parts) showed up during
  Settings-window construction alone, several *per* window shown, mostly failing the
  geometry read (not real top-level windows). First version logged this at Warning like
  the hotkey path does — and since `TrayNotificationSink` turns Warning+ into a tray
  balloon, this produced a burst of tray balloons just from the Settings window opening,
  and would keep firing constantly during ordinary use (any menu, tooltip, dropdown
  anywhere in the system). The hotkey-triggered `CenterActiveWindowAction` still logs
  Warning (a user explicitly pressed a key; silence would be confusing). The watcher's own
  calls pass `LogLevel.Debug` (filtered out entirely by `MinimumLevel.Information()`) —
  encountering an ineligible transient window is the expected common case for a
  system-wide passive watcher, not something worth surfacing.
- `IsEligibleForActions` filtering (owner check, tool-window check, visibility) already
  catches most noise, but evidently not all of it at `EVENT_OBJECT_SHOW` granularity —
  worth remembering if more false-positive window types turn up later.
- Skips windows already seen (`HashSet<IntPtr>` inside the watcher, cleared on
  `Stop()`/toggle-off) so switching back to an existing window (Alt+Tab) doesn't
  re-trigger centering — matches "newly *opened*", not "newly focused".
- Verified for real, no GUI: pre-seeded `settings.json` with `AutoCenterNewWindows: true`,
  launched the app, opened Notepad via PowerShell (no `MoveWindow` needed — the window's
  own default position was off-center already), waited briefly, and read back its DWM
  extended-frame-bounds center vs. the monitor's work-area center — matched to within 1px,
  and the activity log showed the expected "Okno … přesunuto" entry timed to match. Also
  confirmed via the log that switching the log level to Debug for the watcher's own
  failures eliminated the Warning-level spam from Settings-window-internal popups without
  losing the hotkey path's Warning behavior.

### Follow-up same day: UWP/modern apps (Settings, Store) weren't actually centering

User noticed `ms-settings:`/Microsoft Store didn't end up centered even though
`AutoCenterNewWindows` was on. Root-caused with a real diagnostic script (`EnumWindows` +
`GetClassName`/`DwmGetWindowAttribute` dump of all visible titled windows) rather than
guessing: these are `ApplicationFrameWindow`-classed windows hosted by
`ApplicationFrameHost.exe`, which passed `IsEligibleForActions` and `DwmGetWindowAttribute`
fine (not an eligibility bug) — the real problem was **timing**. UWP/modern apps run a
short open animation; at the exact instant `EVENT_OBJECT_SHOW` fires, the window's
geometry is still transitional (small/off), not its final size. The log confirmed this
exactly: the watcher's first attempt moved the window to a "centered" position computed
from that transitional geometry (e.g. `(1837, 1030)`), then the app's own animation grew
it to its real size without re-centering, leaving it visibly off-center — independently
confirmed by reading the window's bounds back a few seconds later and finding them
nowhere near centered.

Fix: `NewWindowWatcher.FollowUpAsync` now does **three** centering attempts total, not
one — immediate (existing, avoids the flash for well-behaved apps), a ~40ms retry
(existing, for the DWM-bounds-not-ready case), and a new ~450ms `AnimationSettleDelay`
pass specifically for apps whose size/position is still settling after `SHOW`. Confirmed
via the same diagnostic script: for both `ms-settings:` and the Store, the 40ms retry
already landed on the correct final position in this test (the animation had settled by
then), and the 450ms pass was a no-op confirmation — but it's the safety net for slower
apps/animations. Re-verified plain Notepad still centers on the very first (immediate)
attempt afterward, so the extra passes don't reintroduce the flash for normal apps —
`WindowCenterer.TryCenter` is idempotent (re-centering an already-centered window is a
harmless no-op `SetWindowPos` to the same spot).

### Follow-up: reopened UWP windows weren't re-centered (`_seenWindows` staleness)

User confirmed: after closing Settings/Store and opening them again, no centering.
Cause: closing a UWP frame window only *hides* it, the next launch re-shows the same
`hwnd`, and `NewWindowWatcher._seenWindows` (dedup set) still contained it — so the
`SHOW` event was skipped as "already seen". Same flaw would hit any recycled `hwnd`
value. Fix: the hook now covers `EVENT_OBJECT_DESTROY..EVENT_OBJECT_HIDE`
(0x8001-0x8003, includes SHOW) and `OnWinEvent` removes the hwnd from the set on
DESTROY/HIDE, so "seen" means "currently shown and already handled", not "ever seen".
Verified: open Settings, move it, `WM_CLOSE`, reopen -> centered again; three cold
starts (ApplicationFrameHost killed first) all settled on the centered position by ~530ms.

PowerShell test-script gotchas found while verifying this (worth knowing next time):
Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so a literal like "Nastavení"
silently never matches — build such strings from `[char]` codes; and variables inside a
scriptblock passed as an `EnumWindows` delegate are unreliable — collect raw results into
a `$script:` ArrayList and filter outside the callback.

### Follow-up: Microsoft Store, notifications and the "what counts as a normal window" rule

Three separate problems found the same evening, all fixed in `NewWindowWatcher`:

1. **Notifications (toasts) and other system UI were being centered** — reported by the
   user as a serious regression. `EVENT_OBJECT_SHOW` sees every shown window, including
   `Windows.UI.Core.CoreWindow` (toasts, UWP content), OSD, overlays, popups. The watcher
   now only acts on windows that pass `IsEligibleForActions` **and** `HasTitleBar()`
   (`WS_CAPTION`). Checked against real windows: Chrome/Electron/Zen/Qt/WPF and UWP
   `ApplicationFrameWindow` all have `WS_CAPTION`; `CoreWindow`, NVIDIA overlay, shell
   windows don't. The hotkey actions deliberately do NOT use this rule. Frameless apps
   (custom-chrome windows without `WS_CAPTION`) therefore aren't auto-centered — accepted
   trade-off, a misplaced notification is far worse than a missed frameless window.
   Also skipped: windows covering the whole work area (`CoversWholeWorkArea`) — the
   Snipping Tool overlay (`SnipOverlayRootWindow`, has `WS_CAPTION`, full screen) was
   being shoved to (0,-30).
2. **Store's real frame settles later than any fixed delay.** At `SHOW` an
   `ApplicationFrameWindow` is a 166x47 stub at (0,0); the app applies its true
   size/saved position some hundreds of ms later. UWP frames now use `TrackAsync`: poll
   `GetWindowRect` every 50ms for 3s and re-center whenever the size differs from what we
   last applied (position-only changes count only during the first 600ms, so a user
   dragging a fresh window isn't fought), and only once the rect held still for one poll.
   **Non-UWP windows keep the original path unchanged** (immediate + 40ms + 450ms passes,
   `FollowUpAsync`) — user explicitly asked not to disturb what works there.
3. **Reopening a closed UWP window sends only `UNCLOAKED`, not `SHOW`** (closing = DWM
   cloak with flags=2 "shell", same as virtual-desktop cloaking, so the cloak flags can't
   tell them apart). Handled by a second hook on `EVENT_OBJECT_UNCLOAKED` for
   `ApplicationFrameWindow` only. To avoid re-centering everything on a virtual-desktop
   switch (which uncloaks many windows at once), an UNCLOAKED of a *regular titled
   non-UWP window* within 150ms marks it as a desktop switch and the reopen is dropped.
   Gotcha that cost a debugging round: the UWP app's own `CoreWindow` uncloaks together
   with its frame on every open, so untitled windows must NOT count toward the desktop-
   switch heuristic.

Diagnosing this needed the geometry in the log: `WindowCenterer` now logs
`[visual, rect, work]` plus `DescribeWindow()` (class, process, style, exStyle) for every
successful move — keep that, it's how the toast/overlay problems were identified.
Verified (Store close+reopen, Store cold start, Settings close+reopen, Notepad): all end
centered to ~1px; Snipping Tool overlay no longer moved. Real toast notifications could
not be triggered from a script here (PowerShell-app toast never showed) — the fix rests on
the class/style analysis above, so a human glance next time a notification pops up is
worthwhile.

### Follow-up: shortening the visible "jump" for UWP windows

User could still see Store/Settings appear off-center and then jump (their own late
resize can't be blocked — no Win32 API holds back a foreign window before first paint).
Two changes made the jump much shorter: (1) while a UWP window is tracked (3s) an
`EVENT_OBJECT_LOCATIONCHANGE` hook reacts within a frame to the app's own resize/move
instead of waiting for the 50ms poll (the poll stays as a fallback). The hook is installed
*only while something is tracked* — LOCATIONCHANGE fires system-wide for every cursor/
window movement — and (un)hooking is marshalled to the UI thread via the
`SynchronizationContext` captured in `Start()`, because `UnhookWinEvent` must run on the
installing thread. Our own `SetWindowPos` re-triggers LOCATIONCHANGE; that's ignored since
the rect then equals `TrackState.Applied`. (2) `UncloakBurstWindow` (desktop-switch
detection wait before re-centering a reopened UWP window) cut from 150ms to 60ms — that
wait was the largest single contributor to the visible jump on reopen. Measurement
caveat: PowerShell polling has ~50ms resolution, so sub-100ms differences can't be
measured from scripts; judge by eye.

## Session 2026-09-23 (evening): per-app rules, localization, native tray menu, installer

Big batch driven by the user's feature list. Read this before touching any of it.

**Per-app rules (`AppRulesService`, `AppRule`)** - one settings dictionary keyed by the app's
exe name lowercase (`AppIdentity.Key`). For UWP windows the identity comes from the
`Windows.UI.Core.CoreWindow` child's process, NOT from `ApplicationFrameHost.exe` (which hosts
every UWP window) - `Win32WindowService.GetAppIdentity` does this and returns null for a
frame with no CoreWindow yet (the watcher then centers as usual and re-checks exclusion at
its next tracking step). A rule holds `ExcludedFromAutoCenter` (+ `AccentColor`) and the
remembered size. Sizes are stored in **96-DPI units** and scaled by the window's DPI when
applied. The service caches in memory (it is read from watcher threads) and writes through
Load -> change -> Save so it never clobbers other settings.

**Remembered sizes are learned automatically, not by shortcut** (`WindowSizeLearner`,
`EVENT_SYSTEM_MOVESIZEEND`). An earlier iteration had "remember size" / "restore size" hotkeys
and a per-app toggle hotkey - the user rejected that design: *a global toggle, learned by
itself*, and exceptions chosen from an app list. Those three `IWindowAction`s are deleted. The
learner skips non-resizable, minimized and maximized windows, windows smaller than 240x160, and
Aero-Snapped or full-work-area sizes (`LooksSnappedOrFullscreen`, unit-tested) so a snap never
becomes an app's "normal" size. Sizes are applied only by the auto-center watcher (through
`IWindowSizePolicy` / `RememberedSizePolicy`, using `WindowCenteringCalculator.CalculateResized`
so size and position go in a single `SetWindowPos`); the centering hotkey stays pure centering.
Verified end to end with a real mouse drag of a Notepad corner (`mouse_event`): it learned
1200x880 (96-dpi units), and the next Notepad opened at 1500x1100 px, centered.

**Exceptions UI** - "Choose apps..." opens `InstalledAppsWindow`: Win32 apps from Start-menu
shortcuts (`WScript.Shell` COM, uninstallers filtered), installed UWP apps
(`Windows.Management.Deployment.PackageManager.FindPackagesForUser("")` - works without
elevation; the exe is read from `AppxManifest.xml`, only packages with an `AppListEntry`), plus
apps that currently have a window. UWP support required the UI project TFM
`net10.0-windows10.0.19041.0` (Core stays `net10.0-windows`). Checked = excluded; on Done the
whole list is applied at once (`SetExcludedApps`); excluded apps that are no longer
installed are re-added as checked so confirming can't silently drop them. Each exclusion row
has a dot colored by `AccentColorExtractor` (hue buckets weighted by saturation x brightness,
not a plain average - averaging gives muddy grey; grey icons get mid-grey; too-dark colors are
lightened). UWP icons come from `AppListEntry.DisplayInfo.GetLogo`. Verified by driving the
picker with UI Automation (checked 4 apps, pressed Done): the stored colors matched the icons.

**Localization** - see the language convention at the top. `Loc.Apply` keeps the English
dictionary always merged and overlays Czech, so a missing key falls back to English; the
language switches live (`SettingsWindow` rebuilds its data-driven lists on
`Loc.LanguageChanged`). `AppSettings.Language` defaults to `System` (Czech Windows -> Czech UI,
otherwise English). Known leftover: Warning-level *log* messages still reach the user as tray
balloons through `TrayNotificationSink` in Czech (would need message keys).

**Tray menu - the long way round, do not repeat it.** Attempt 1: WPF `ContextMenu` with
`ui:MenuItem` (blurry text because it is a transparent layered window, so no ClearType; a white
focus rectangle on the first item; and light-themed at startup because the theme was only
applied when the Settings window was created - `ThemeService.Apply` now runs at startup and
follows `SystemEvents.UserPreferenceChanged`). Attempt 2: a custom `FluentWindow` popup - the
user rejected it too ("why custom, can't you use the default one?"). **Final, correct
answer: the real Win32 popup menu** (`NativeTrayMenu`: `CreatePopupMenu` + `InsertMenuItem` +
`TrackPopupMenuEx` with `TPM_RETURNCMD`), which on Windows 11 is natively rounded, crisp and
has native hover. Dark mode via the undocumented uxtheme ordinals `#135 SetPreferredAppMode`
(2 = ForceDark, 3 = ForceLight) and `#136 FlushMenuThemes` (wrapped in try/catch; worst case a
light menu). Item icons are Segoe Fluent Icons glyphs rendered to premultiplied 32-bit DIB
bitmaps (`hbmpItem`) in the text color. The menu must be shown after `SetForegroundWindow`
on an owner window and followed by `WM_NULL`, or it will not dismiss on an outside click.
Items: Open Centertized / About / Close. Test switches: `--show-tray-menu` opens the menu at
startup, `--show-picker` opens the app picker, `--settings` forces the Settings window.

**About window** (`AboutWindow`, opened from the tray) is modeled on the user's EtherWave app:
icon, name, version, description, license, GitHub link.

**Installer + updates (Velopack)** - `Program.cs` is the entry point (`StartupObject`) and runs
`VelopackApp.Build().Run()` before WPF starts. `UpdateService` uses `GithubSource` on
`AppInfo.RepositoryUrl` (this works because the repo is public - a private repo would need a
token baked into the app, which is a no-go). The update UI is in Settings -> About; the startup
check (`AppSettings.CheckForUpdatesAutomatically`) only notifies, and download + restart is always
the user's click. `IsInstalled` is false in dev/portable runs, so the UI then says updates need
the installed version. `vpk pack` was verified locally (Setup.exe 85 MB, portable zip, full
nupkg from a self-contained `win-x64` publish), but the installer and the real update flow
were NOT run on the dev machine (it would install into the user's profile) - the first real
test is the first tagged release. The git tag is the single source of truth for the version
(`-p:Version=` in `release.yml`); the csproj `<Version>` is only the local default.
`.github/workflows/ci.yml` builds and tests on push/PR; `release.yml` (tag `v*`) publishes,
packs with vpk and creates the GitHub Release. **GPLv3** (same as EtherWave) was chosen for
`LICENSE` and the About text - the user should confirm.

Test scripts from this session live only in the session scratchpad (not in the repo): real
mouse-drag resize, UI Automation driving of the picker, screenshots of the native menu via
`CopyFromScreen` after finding its window by class `#32768`. PowerShell 5.1 gotchas: `$pid`
is read-only, BOM-less scripts with diacritics are read as ANSI, `$using:` does not work in
`EnumWindows` callbacks. Shell gotcha: a Bash call whose heredoc text contains certain quote
patterns can fail to parse as a whole (nothing runs) - write files with the Write tool instead.

## Session 2026-10-03: "Run as administrator" mode

Trigger: the user's RHI app (WinUI 3, runs elevated) never centered. Diagnosed from the log
(`SetWindowPos ... selhal (pravděpodobně běží se zvýšenými právy)` for RHI's hwnd) and
`OpenProcess` -> access denied on RHI.exe: UIPI, exactly the documented limitation.

Implementation: `AppSettings.RunAsAdministrator` + a toggle in General. `ElevationService`:
`IsElevated`, `RelaunchElevated()` (`runas` verb; returns false when the user declines UAC,
Win32 error 1223), `RelaunchNotElevated()` (a plain Process.Start from an elevated process
inherits elevation, so it goes through a delayed `cmd /c ping ... & explorer.exe "<exe>"`,
because explorer can't pass arguments and the old instance must release the mutex/hotkeys
first). Startup: if the setting is on and the process isn't elevated it relaunches elevated;
declined UAC just keeps running normally. The single-instance mutex is `Global\`: an elevated
instance's mutex can't be opened by a non-elevated process (UnauthorizedAccessException ->
treated as "already running"), and a `--relaunch` instance waits up to 10 s for the old one
to release it. `App.RestartAs(elevated)` returns false if UAC was declined (the toggle and
setting are then reverted) — a first version returned void and the handler reverted the
setting even on success; don't reintroduce that.
Autostart (`AutostartService`): in admin mode a Run key would start non-elevated and trigger a
UAC prompt every logon, so it uses a scheduled task (`schtasks /Create ... /SC ONLOGON /RL HIGHEST`)
that only an elevated instance can create/delete; elevated startup migrates Run key -> task,
turning admin mode off removes the task before relaunching.

Testing gotchas (they cost time): a **non-elevated** script can neither kill the elevated
Centertized nor build over it (the exe stays locked) — the user has to close it from the tray;
and synthetic input (`keybd_event`) from a non-elevated script is dropped when the foreground
window is elevated (UIPI), so hotkeys on elevated windows must be pressed by hand. Verified
by the user: Ctrl+Shift+C now centers RHI. Not yet verified: disabling the mode (relaunch
non-elevated) and the scheduled-task autostart at a real logon.

