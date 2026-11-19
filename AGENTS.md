# Agent Notes

## Packaging

- `src/stop/stop.csproj` targets `net10.0` only.
- Hybrid Native AOT/CoreCLR tool packaging is opt-in via `HybridToolPackage=true`.
- Keep `.github/workflows/build.yml` on the default non-AOT package path for fast CI and dogfooding. It builds with `RuntimeIdentifiers=any`, for which `stop.csproj` turns AOT off: the .NET 11 SDK drops `any` from an AOT tool's RID packages and, with none left, fails with a circular dependency on `_CreateRIDSpecificToolPackages`. It builds `-r any` before the solution, since packing the solution packs `stop.any` from that build's output.
- In `.github/workflows/publish.yml`, publish RID-specific packages and the `any` fallback before publishing the top-level pointer package.

## Stopping

The stop logic in `src/stop/Program.cs` follows ndx's `ProcessStop`/`ChildProcess` (github.com/devlooped/ndx), adapted to stopping a process we did not start:

- Windows: `WM_CLOSE` first, then Ctrl+C through the `stopr` helper, which attaches to the target's console. A GUI app started from a shell has no console, and `stopr` would otherwise attach to an ancestor's console and report a Ctrl+C the app never gets. Console windows (`ConsoleWindowClass`, `PseudoConsoleWindow`) report the console app as owner and are never sent `WM_CLOSE`, which would close the console instead of sending Ctrl+C.
- NeoAstra apps: when a `NeoAstra.Window` is present, only unowned top-level frames of that class get `WM_CLOSE`; the message on its other windows destroys them and leaves an app that cannot exit.
- Unix: libc `kill()` with SIGINT to the target's process group (libc `getpgid()`), or to the target alone when that group is our own. The `kill` tool is not used: procps-ng takes `-INT -<pgid>` without signaling and exits with 0.
- The wait ends early on stopped/zombie state or a quiesced process group only on Linux, where `/proc` gives them; on macOS it waits for `Process.HasExited`.
- The descendants of the target (`ProcessTree`) are recorded before the signal and while waiting, since they are reparented once it exits, then waited for within the same timeout.
- Killing is opt-in (`--kill`), after the timeout; ndx always kills because it must restart the tool.
