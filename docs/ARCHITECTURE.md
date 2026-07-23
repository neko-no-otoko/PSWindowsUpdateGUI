# Architecture

`PSWindowsUpdateGUI.exe` has one entry point. With no arguments it starts WinUI 3; with a
subcommand it attaches to the parent console and runs headlessly.

The typed domain boundary is `IWindowsUpdateEngine`. `WuaWindowsUpdateEngine` owns a
dedicated background STA dispatcher and serializes operations. All WUA COM objects are
created, used, cleaned up, and released on that apartment. Searches, downloads,
installs, and uninstalls use WUA `Begin*` callbacks and `End*` completion calls.
Cancellation calls the corresponding job's `RequestAbort`; installation cancellation
remains a request and the final Windows state must be verified.

Build-time interop is generated from `%SystemRoot%\System32\wuapi.dll` by
`build/Generate-WuaInterop.ps1`. The generated assembly exists only under `obj`; C#
embeds referenced interop types in the EXE.

The WinUI view model, CLI, scheduled job runner, and remote worker all use the same
request models and validation. Update mutations re-scan and match `UpdateID` plus
revision before constructing a WUA update collection.

The WinUI 3 shell uses Windows App SDK controls, NavigationView, Mica, and semantic
theme resources rather than fixed page colors. `AppThemeService` resolves the
persisted System, Light, or Dark choice and applies the matching element and app title
bar themes; high contrast continues to resolve through WinUI system resources. Theme
selection is a presentation concern and cannot alter update requests or privileged
execution.

Normal builds use the elevated application manifest. The production-assembly UI smoke
build defines `UI_SMOKE`, substitutes a test-only `asInvoker` manifest and fake WUA
adapter, loads the same compiled XAML resources, captures a page, and exits. That test
path is excluded from release builds.

Remote mode uses built-in Windows PowerShell only as WinRM transport. A fixed embedded
script receives arguments separately, verifies the staged EXE hash, and invokes its
CLI on the target. PowerShell is never the update engine.

Policy, component maintenance, explicit DISM/WUSA package fallback, scheduling,
reporting, and payload export are intentionally separate services. This prevents WUA
operations from becoming a generic privileged script runner.
