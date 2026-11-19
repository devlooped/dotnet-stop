using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using ConsoleAppFramework;
using Devlooped;
using Spectre.Console;

ConsoleApp.Run(args, Stop);

/// <summary>Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.</summary>
/// <param name="id">ID of the process to stop. When omitted, reads process IDs from standard input.</param>
/// <param name="timeout">-t, Optional timeout in milliseconds to wait for the process to exit.</param>
/// <param name="kill">-k, Kill the process and the processes it started if they do not exit within the timeout.</param>
/// <param name="quiet">-q, Do not display any output.</param>
/// <param name="debug">-d, Print diagnostic details about the stop operation.</param>
/// <param name="attach">Attach a .NET debugger before stopping.</param>
static int Stop([Argument] int? id = null, [HideDefaultValue] int? timeout = default, bool kill = false, bool quiet = false, bool debug = false, bool attach = false)
{
    if (attach)
        Debugger.Launch();

    if (id == null)
    {
        if (!Console.IsInputRedirected)
        {
            if (!quiet)
                AnsiConsole.MarkupLine("[red]No process ID specified[/]");
            return -1;
        }

        var lines = Console.In.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<int>();
        var pids = new List<int>();
        foreach (var pid in ParsePids(lines))
            if (seen.Add(pid))
                pids.Add(pid);

        if (pids.Count == 0)
        {
            if (!quiet)
                AnsiConsole.MarkupLine("[red]No process IDs found in standard input[/]");
            return -1;
        }

        var anyFailure = false;
        foreach (var pid in pids)
            if (StopProcess(pid, timeout, kill, quiet, debug) != 0)
                anyFailure = true;

        return anyFailure ? -1 : 0;
    }

    return StopProcess(id.Value, timeout, kill, quiet, debug);
}

// Stops the target the way ndx stops the tools it runs: record the processes it started,
// signal it, wait for it and then for those processes, and kill what is left only with --kill.
static int StopProcess(int id, int? timeout, bool kill, bool quiet, bool debug)
{
    Process process;
    string name;
    try
    {
        process = Process.GetProcessById(id);
        name = $"{process.ProcessName}:{process.Id}";
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Process with id '{id}' not found[/]");

        return -1;
    }

    using (process)
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[yellow]Shutting down {name}...[/]");

        var deadline = timeout != null ? Environment.TickCount64 + timeout.Value : long.MaxValue;

        // Once the target exits, the processes it started are reparented and can no longer
        // be found from its id, so record them before the signal and while waiting.
        var tracked = new HashSet<int>();
        ProcessTree.AddDescendants(id, tracked);

        var signaled = OperatingSystem.IsWindows()
            ? SignalWindowsProcess(process, debug)
            : SendUnixSigInt(id, debug);

        if (!signaled && !IsTargetStopped(process, debug))
        {
            if (!quiet)
            {
                AnsiConsole.MarkupLine(OperatingSystem.IsWindows()
                    ? $"[red]No closeable windows or console found for process {name}[/]"
                    : $"[red]Failed to send SIGINT to process {name}[/]");
            }

            if (!kill)
                return -1;

            deadline = 0;
        }

        if (!WaitForTarget(process, tracked, deadline, debug))
        {
            if (!quiet && signaled)
                AnsiConsole.MarkupLine($"[red]Timed out waiting for process {name} to exit[/]");

            if (!kill || !Kill(process, name, quiet))
                return -1;
        }

        ProcessTree.AddDescendants(id, tracked);
        return WaitForDescendants(id, name, tracked, deadline, kill, quiet, debug);
    }
}

static bool WaitForTarget(Process process, HashSet<int> tracked, long deadline, bool debug)
{
    while (!IsTargetStopped(process, debug))
    {
        ProcessTree.AddDescendants(process.Id, tracked);
        if (Environment.TickCount64 >= deadline)
            return false;

        Thread.Sleep(50);
    }

    return true;
}

static bool IsTargetStopped(Process process, bool debug)
{
    if (IsStopped(process))
        return true;

    if (OperatingSystem.IsWindows() || !IsUnixStopComplete(process.Id))
        return false;

    if (debug)
        AnsiConsole.MarkupLine("[grey]Target process group is stopped or has exited[/]");

    return true;
}

// A process we cannot query any more is treated as stopped rather than waited on forever.
static bool IsStopped(Process process)
{
    try
    {
        process.Refresh();
        if (process.HasExited)
            return true;
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
    {
        return true;
    }

    return !OperatingSystem.IsWindows() && GetUnixProcessState(process.Id) is 'T' or 't' or 'Z';
}

// Processes the target started keep stopping after a graceful exit (they got the same
// Ctrl+C or SIGINT), so wait for them within the same deadline before killing anything.
static int WaitForDescendants(int root, string rootName, HashSet<int> tracked, long deadline, bool kill, bool quiet, bool debug)
{
    var pending = new List<(Process Process, string Name)>();
    foreach (var pid in tracked)
    {
        if (pid <= 4 || pid == root || pid == Environment.ProcessId)
            continue;

        try
        {
            var child = Process.GetProcessById(pid);
            if (IsStopped(child))
                child.Dispose();
            else
                pending.Add((child, $"{child.ProcessName}:{pid}"));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }

    if (pending.Count == 0)
        return 0;

    try
    {
        if (debug)
            AnsiConsole.MarkupLine($"[grey]Waiting for processes started by {rootName}: {string.Join(", ", pending.Select(x => x.Name))}[/]");

        var running = pending.ToList();
        while (Environment.TickCount64 < deadline)
        {
            running.RemoveAll(x => IsStopped(x.Process));
            if (running.Count == 0)
                return 0;

            Thread.Sleep(50);
        }

        running.RemoveAll(x => IsStopped(x.Process));
        if (running.Count == 0)
            return 0;

        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Timed out waiting for processes started by {rootName} to exit: {string.Join(", ", running.Select(x => x.Name))}[/]");

        if (!kill)
            return -1;

        var result = 0;
        foreach (var (child, name) in running)
        {
            if (!Kill(child, name, quiet))
                result = -1;
        }

        return result;
    }
    finally
    {
        foreach (var (child, _) in pending)
            child.Dispose();
    }
}

static bool Kill(Process process, string name, bool quiet)
{
    try
    {
        process.Kill(entireProcessTree: true);
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
    {
    }

    if (process.WaitForExit(5000))
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[yellow]Killed {name}[/]");

        return true;
    }

    if (!quiet)
        AnsiConsole.MarkupLine($"[red]Failed to kill {name}[/]");

    return false;
}

// Parses process IDs from stdin, supporting:
//   - PowerShell Get-Process table format (detects "Id" column header + separator line)
//   - Plain integers, one per line (e.g. piped via Select-Object -ExpandProperty Id)
static IEnumerable<int> ParsePids(string[] lines)
{
    // Detect table format: a line containing whole-word "Id" followed by a separator line (dashes/spaces)
    var idRightEdge = -1;
    var dataStartLine = 0;

    for (var i = 0; i < lines.Length; i++)
    {
        var idMatch = Regex.Match(lines[i], @"\bId\b");
        if (idMatch.Success && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], @"^[\s\-]+$"))
        {
            // Right edge of "Id" aligns with the right edge of PID values (right-aligned column)
            idRightEdge = idMatch.Index + 1;
            dataStartLine = i + 2;
            break;
        }
    }

    if (idRightEdge >= 0)
    {
        for (var i = dataStartLine; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length <= idRightEdge) continue;

            // Scan backward from the right edge of the Id column to extract the integer
            var end = idRightEdge;
            while (end >= 0 && char.IsWhiteSpace(line[end])) end--;
            var start = end;
            while (start > 0 && char.IsDigit(line[start - 1])) start--;

            if (end >= start && end >= 0 && int.TryParse(line.AsSpan(start, end - start + 1), out var pid))
                yield return pid;
        }
        yield break;
    }

    // Plain integer fallback: one PID per line
    foreach (var line in lines)
        if (int.TryParse(line.Trim(), out var pid))
            yield return pid;
}

// Signals the target's process group, like Ctrl+C in its terminal, unless that is our own group
// (a script that started the target in the background), which would interrupt us and the caller.
// libc kill() rather than the kill tool: procps-ng kill takes "-INT -<pgid>" without signaling
// the group, and still exits with 0.
static bool SendUnixSigInt(int pid, bool debug)
{
    var pgid = GetUnixProcessGroupId(pid);
    var target = pgid > 0 && pgid != GetUnixProcessGroupId(Environment.ProcessId) ? -pgid : pid;

    if (debug)
        AnsiConsole.MarkupLine($"[grey]kill({target}, SIGINT)[/]");

    if (NativeMethods.Kill(target, NativeMethods.SigInt) == 0)
        return true;

    if (debug)
        AnsiConsole.MarkupLine($"[grey]kill failed (errno {Marshal.GetLastPInvokeError()})[/]");

    return false;
}

static bool IsUnixStopComplete(int pid)
{
    if (!UnixProcessExists(pid))
        return true;

    var pgid = GetUnixProcessGroupId(pid);
    if (pgid > 0 && IsUnixProcessGroupQuiesced(pgid))
        return true;

    return GetUnixProcessState(pid) is 'T' or 't' or 'Z';
}

static bool IsUnixProcessGroupQuiesced(int pgid)
{
    foreach (var procPid in EnumerateUnixProcessIds())
    {
        if (GetUnixProcessGroupId(procPid) != pgid)
            continue;

        if (GetUnixProcessState(procPid) is 'R' or 'S' or 'D' or 'I')
            return false;
    }

    return true;
}

static IEnumerable<int> EnumerateUnixProcessIds()
{
    foreach (var dir in Directory.EnumerateDirectories("/proc"))
    {
        if (int.TryParse(Path.GetFileName(dir), out var pid))
            yield return pid;
    }
}

static bool UnixProcessExists(int pid) => Directory.Exists($"/proc/{pid}");

static char? GetUnixProcessState(int pid)
{
    try
    {
        foreach (var line in File.ReadLines($"/proc/{pid}/status"))
        {
            if (!line.StartsWith("State:", StringComparison.Ordinal))
                continue;

            var state = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            return state.Length > 1 && state[1].Length > 0 ? state[1][0] : null;
        }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }

    return null;
}

static int GetUnixProcessGroupId(int pid)
{
    try
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var index = stat.LastIndexOf(')');
        if (index < 0)
            return -1;

        var fields = stat[(index + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 2 && int.TryParse(fields[2], out var pgid) ? pgid : -1;
    }
    catch (IOException)
    {
        return -1;
    }
    catch (UnauthorizedAccessException)
    {
        return -1;
    }
}

// WM_CLOSE first, then Ctrl+C, as ndx does: a GUI app has no console of its own, and stopr
// would otherwise attach to the console of an ancestor and report a Ctrl+C the app never gets.
static bool SignalWindowsProcess(Process process, bool debug)
{
    if (SendCloseToGuiProcess(process, debug))
    {
        if (debug)
            AnsiConsole.MarkupLine("[grey]Using GUI (WM_CLOSE) shutdown path[/]");

        return true;
    }

    var stoprExit = RunStopr(process.Id, debug);

    if (debug)
        AnsiConsole.MarkupLine($"[grey]stopr exit code = {stoprExit}[/]");

    if (IsStoprSuccess(stoprExit))
    {
        if (debug)
        {
            if (stoprExit == 0)
                AnsiConsole.MarkupLine("[grey]Using console (Ctrl+C) shutdown path via stopr[/]");
            else
                AnsiConsole.MarkupLine("[grey]stopr posted Ctrl+C (exit code indicates signal was delivered)[/]");
        }

        return true;
    }

    process.Refresh();
    if (process.HasExited && debug)
        AnsiConsole.MarkupLine("[grey]Target process already exited[/]");

    return process.HasExited;
}

static int RunStopr(int pid, bool debug)
{
    try
    {
        using var stopr = Process.Start(CreateStoprStartInfo(pid));
        if (stopr == null)
            return -1;

        stopr.WaitForExit();
        return stopr.ExitCode;
    }
    catch (Exception ex)
    {
        if (debug)
            AnsiConsole.MarkupLine($"[grey]stopr failed: {ex.Message}[/]");

        return -1;
    }
}

// stopr may exit with STATUS_CONTROL_C_EXIT after successfully posting Ctrl+C.
static bool IsStoprSuccess(int exitCode) =>
    exitCode == 0 || unchecked((int)0xC000013A) == exitCode;

static ProcessStartInfo CreateStoprStartInfo(int pid)
{
    var dir = AppContext.BaseDirectory;
    var stoprExe = Path.Combine(dir, OperatingSystem.IsWindows() ? "stopr.exe" : "stopr");
    if (File.Exists(stoprExe))
    {
        return new ProcessStartInfo(stoprExe, pid.ToString())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    var stoprDll = Path.Combine(dir, "stopr.dll");
    if (!File.Exists(stoprDll))
        throw new FileNotFoundException($"stopr helper was not found next to {dir}.", stoprDll);

    var host = Environment.ProcessPath ?? "dotnet";
    return new ProcessStartInfo(host, $"exec \"{stoprDll}\" {pid}")
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };
}

static bool SendCloseToGuiProcess(Process process, bool debug)
{
    var windows = CollectWindows((uint)process.Id);
    if (TrySelectNeoAstraFrames(windows, out var frames))
    {
        if (debug)
            AnsiConsole.MarkupLine($"[grey]NeoAstra app: closing {frames.Count} unowned top-level frame(s) only[/]");

        return PostClose(frames, debug) > 0;
    }

    var mainWindow = process.MainWindowHandle;
    if (IsConsoleWindow(mainWindow))
        mainWindow = IntPtr.Zero;

    var closed = false;
    if (mainWindow != IntPtr.Zero && process.CloseMainWindow())
    {
        closed = true;
        if (debug)
            AnsiConsole.MarkupLine($"[grey]CloseMainWindow succeeded for 0x{mainWindow:X}[/]");

        process.Refresh();
        if (process.HasExited)
            return true;
    }

    if (mainWindow != IntPtr.Zero)
        windows.Add(mainWindow);

    return windows.Count > 0 && PostClose(windows, debug) > 0 || closed;
}

static int PostClose(ICollection<IntPtr> windows, bool debug)
{
    var posted = 0;
    foreach (var hWnd in windows)
    {
        if (NativeMethods.PostMessage(hWnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero))
            posted++;
        else if (debug)
            AnsiConsole.MarkupLine($"[grey]PostMessage WM_CLOSE failed for 0x{hWnd:X} (error {Marshal.GetLastWin32Error()})[/]");
    }

    if (debug)
        AnsiConsole.MarkupLine($"[grey]Posted WM_CLOSE to {posted} of {windows.Count} window(s)[/]");

    return posted;
}

static HashSet<IntPtr> CollectWindows(uint pid)
{
    var state = new GuiCloseState(pid);
    var handle = GCHandle.Alloc(state);
    try
    {
        var statePtr = GCHandle.ToIntPtr(handle);
        NativeMethods.EnumWindows(EnumWindowsByPidCallback, statePtr);
        NativeMethods.EnumWindows(EnumHostedParentCallback, statePtr);
        state.Windows.RemoveWhere(IsConsoleWindow);
        return state.Windows;
    }
    finally
    {
        handle.Free();
    }
}

// NeoAstra turns WM_CLOSE on an unowned top-level NeoAstra.Window into a cancelable user close.
// An owned window of that class is a dialog. The same message on its dispatcher, caption, or
// WebView child destroys that window instead, leaving an app that cannot exit. Filtering applies
// only once such a frame is present; every other GUI is closed as before.
static bool TrySelectNeoAstraFrames(HashSet<IntPtr> windows, out List<IntPtr> frames)
{
    frames = [];
    var sawNeoAstraWindow = false;
    foreach (var hWnd in windows)
    {
        if (!HasClass(hWnd, "NeoAstra.Window"))
            continue;

        sawNeoAstraWindow = true;
        if (NativeMethods.GetAncestor(hWnd, NativeMethods.GaRoot) == hWnd &&
            NativeMethods.GetWindow(hWnd, NativeMethods.GwOwner) == IntPtr.Zero)
            frames.Add(hWnd);
    }

    return sawNeoAstraWindow;
}

static bool HasClass(IntPtr hWnd, string className) =>
    string.Equals(GetClassName(hWnd), className, StringComparison.Ordinal);

// The console window of a console app reports that app as its owner, but WM_CLOSE on it closes
// the console (CTRL_CLOSE_EVENT) instead of the graceful Ctrl+C that stopr sends.
static bool IsConsoleWindow(IntPtr hWnd) =>
    hWnd != IntPtr.Zero && GetClassName(hWnd) is "ConsoleWindowClass" or "PseudoConsoleWindow";

static string? GetClassName(IntPtr hWnd)
{
    var name = new StringBuilder(256);
    return NativeMethods.GetClassName(hWnd, name, name.Capacity) > 0 ? name.ToString() : null;
}

static bool EnumWindowsByPidCallback(IntPtr hWnd, IntPtr lParam)
{
    var state = (GuiCloseState)GCHandle.FromIntPtr(lParam).Target!;
    NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
    if (pid == state.TargetPid)
        state.Windows.Add(hWnd);
    return true;
}

static bool EnumHostedParentCallback(IntPtr hWnd, IntPtr lParam)
{
    var state = (GuiCloseState)GCHandle.FromIntPtr(lParam).Target!;
    NativeMethods.EnumChildWindows(hWnd, EnumChildForHostedCallback, lParam);
    return true;
}

static bool EnumChildForHostedCallback(IntPtr hWnd, IntPtr lParam)
{
    var state = (GuiCloseState)GCHandle.FromIntPtr(lParam).Target!;
    NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
    if (pid != state.TargetPid)
        return true;

    state.Windows.Add(hWnd);

    var parent = NativeMethods.GetAncestor(hWnd, NativeMethods.GaRoot);
    if (parent != IntPtr.Zero)
        state.Windows.Add(parent);

    return true;
}