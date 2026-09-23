- přepínač v GUI, který bude zapínat to, že každé nově otevřené okno se otevře rovnou vycentrované přesně uprostřed obrazovky
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

## Otevřené položky

- Tray context menu pořád nevypadá úplně nativně (chybí třeba zaoblený hover jako u
  Flow Launcheru) - ikonky přidané, zbytek je defaultní ControlsDictionary styl.
- První spuštění appky by mohlo ukázat tray "balloon", ať uživatel objeví ikonu.
- Elevated (admin) okna - chování zdokumentované, ale živě neověřené (tenhle stroj má
  vypnuté UAC).
- Nápad "auto-centrovat každé nově otevřené okno" (řádek 1 výš) - potřebuje jiný
  mechanismus než hotkey/IWindowAction (SetWinEventHook na vznik okna), zatím nenavržené.
