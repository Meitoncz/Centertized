- [HOTOVO] přepínač v GUI, který bude zapínat to, že každé nově otevřené okno se otevře rovnou vycentrované přesně uprostřed obrazovky — NewWindowWatcher (SetWinEventHook na EVENT_OBJECT_SHOW, ne FOREGROUND - kvůli menšímu "poskoku" viz CLAUDE.md), přepínač v Nastavení → General → "Auto-center new windows" (nad Theme)
- [HOTOVO] druhá zkratka pro to, aby aktivní okno provedlo maximized s tím, že po opětovném použití zkratky se okno vrátí do předchozího stavu, velikosti a polohy — ToggleMaximizeAction, přidej si zkratku v Nastavení → Shortcuts

## Vizuální pass 2026-09-22/23 — [HOTOVO]

- [HOTOVO] Přepínač motivu v Nastavení: System (default) / Light / Dark.
- [HOTOVO] Acrylic zrušen, zůstává jen Mica (porovnáno naživo, Mica lepší/bližší Windows).
- [HOTOVO] Opraven skutečný bug - Mica se vůbec nevykreslovala (chybělo nastavit
  WindowBackdropType přímo na okně, ne jen přes ApplicationThemeManager.Apply()).
- [HOTOVO] Opraven černý text v dark režimu (ui:TextBlock / ui:ThemeResource místo
  obyčejného TextBlock).
- [HOTOVO] Přepracované Nastavení - žádný sidebar/NavigationView, jedna scrollovatelná
  stránka s kartami (ui:CardControl), inspirováno Windows Nastavení a WPF-UI Gallery.
- [HOTOVO] Živé přepnutí motivu bez zavírání okna (odstranění NavigationView vyřešilo
  i starý known issue s rozjetým vzhledem).
- [HOTOVO] Appka po startu defaultně ukáže Settings okno; přidán přepínač "Start
  minimized" pro staré chování (jen tray).
- [HOTOVO] Dvojklik na tray ikonu otevře Settings.
- [HOTOVO] Oprava: vlastní okna appky (Settings) šla vyloučit z akcí bez dobrého důvodu,
  takže zkratka na Settings okně nefungovala - odstraněno.

## Vzhled/UX pass 2026-09-23 (kolo 2) — [HOTOVO]

- [HOTOVO] Tlačítko pro zachycení zkratky nešlo myší kliknout (Border s ruční focus
  logikou nefungoval spolehlivě uvnitř CardControl) - přepsáno na normální Button.
- [HOTOVO] Pevná šířka + zarovnání doprava u tlačítek pro zkratky, ať karta neposkakuje.
- [HOTOVO] Tray menu dostalo ikonky (Settings/Exit).
- [ZAMÍTNUTO] Win+písmeno/číslo (např. Ctrl+Win+C) jako zkratka - zkusili jsme to přes
  globální low-level keyboard hook, ale při neúspěšném zachycení hook zůstal viset a
  zablokoval klávesnici v celém systému (i Alt+Tab), dokud appka neskončila. Uživatel
  řekl ať to necháme být. Zůstává jen Win+funkční klávesa (Win+F1-F24), to funguje
  spolehlivě bez hooku. Podrobnosti a poučení pro případné budoucí pokusy v CLAUDE.md.

## Vzhled/UX pass 2026-09-23 (kolo 3) — [HOTOVO]

- [HOTOVO] Tray menu: ui:MenuItem/ui:SymbolIcon místo obyčejného MenuItem (lepší
  vystředění ikon, Fluent hover styl), odstraněný zbytečný separator mezi 2 položkami.
- [ZAMÍTNUTO/VYŘEŠENO JINAK] Plynulý scroll v Nastavení - zkusili jsme (1) řetězené
  DoubleAnimation na cíl kolečka, (2) totéž s navazováním na cíl rozjeté animace
  (PendingTarget) - trochu choppy, ale použitelné, (3) fyziku (rychlost+tření přes
  CompositionTarget.Rendering) - po doladění pořád necítilo přirozeně jako WinUI
  momentum scroll. Skončili jsme u varianty (2) - nejlepší kompromis, co šel v
  rozumném čase doladit. Skutečné WinUI-style momentum scrollování by zřejmě
  vyžadovalo výrazně větší investici (sledování skutečné rychlosti gesta v čase,
  ne jen počet/velikost wheel událostí).

## Otevřené položky

- Tray context menu pořád nevypadá úplně nativně (chybí třeba zaoblený hover jako u
  Flow Launcheru) - ikonky a ui:MenuItem přidané, zbytek je defaultní ControlsDictionary
  styl.
- Elevated (admin) okna - chování zdokumentované, ale živě neověřené (tenhle stroj má
  vypnuté UAC).
