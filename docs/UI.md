# WinUI 3 interface and theming

Version 3 moves the desktop application to WinUI 3 on the Windows App SDK. The GUI
and CLI still ship in the same elevated, unpackaged, self-contained executable.

## Design system

- A compact command header keeps the target, identity, elevation, portable-storage
  state, connection check, theme, and About context visible.
- Top NavigationView destinations separate Updates, History & Status, Services &
  Policies, Offline & Maintenance, the operation catalog, and logs.
- Semantic WinUI theme resources provide layered Mica-backed surfaces, status cards,
  restrained borders, table headers, keyboard focus, accent actions, warnings, and
  destructive actions.
- History is a first-class typed list beneath the status controls. Selecting an entry
  shows its exact WUA identity and enables a plan-first removal check. History itself
  remains an immutable audit record.
- Controls expose AutomationProperties names where context is not otherwise clear.
  WinUI's system resources retain Windows high-contrast behavior.

The theme selector offers `System`, `Light`, and `Dark`. Its non-secret preference is
stored in `PSWindowsUpdateGUI.Data/settings.json` beside the EXE when portable storage
is writable. `System` follows Windows app theme; explicit modes also set the WinUI
title-bar theme.

## Deployment behavior

The project uses an unpackaged, self-contained Windows App SDK deployment. The release
contains one `PSWindowsUpdateGUI.exe` and does not register an MSIX package or require
a separately installed Windows App Runtime. .NET single-file extraction places native
framework content in the current user's temporary bundle cache on first launch. This
is runtime extraction, not a permanent module or runtime installation.

The release manifest requires administrator elevation. A separate `asInvoker`
manifest exists only behind the `UI_SMOKE` build property, allowing CI to load the
production XAML with a fake engine and capture it without modifying Windows Update.

Microsoft references:

- [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/)
- [Unpackaged WinUI deployment](https://learn.microsoft.com/windows/apps/package-and-deploy/unpackage-winui-app)
- [WinUI theming](https://learn.microsoft.com/windows/apps/develop/ui/theming)
- [WinUI title bars](https://learn.microsoft.com/windows/apps/develop/title-bar)
