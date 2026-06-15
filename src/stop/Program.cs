using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using ConsoleAppFramework;
using Devlooped;
using Spectre.Console;

ConsoleApp.Run(args, Stop);

/// <summary>Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.</summary>
/// <param name="id">ID of the process to stop. When omitted, reads process IDs from standard input.</param>
/// <param name="timeout">-t, Optional timeout in milliseconds to wait for the process to exit.</param>
/// <param name="quiet">-q, Do not display any output.</param>
/// <param name="debug">-d, Print diagnostic details about the stop operation.</param>
/// <param name="attach">Attach a .NET debugger before stopping.</param>
static int Stop([Argument] int? id = null, [HideDefaultValue] int? timeout = default, bool quiet = false, bool debug = false, bool attach = false)
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
            if (StopProcess(pid, timeout, quiet, debug) != 0)
                anyFailure = true;

        return anyFailure ? -1 : 0;
    }

    return StopProcess(id.Value, timeout, quiet, debug);
}

static int StopProcess(int id, int? timeout, bool quiet, bool debug) =>
    Environment.OSVersion.Platform == PlatformID.Win32NT
        ? StopWindowsProcess(id, timeout, quiet, debug)
        : StopUnixProcess(id, timeout, quiet, debug);

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

static int StopUnixProcess(int id, int? timeout, bool quiet, bool debug)
{
    Process process;
    try
    {
        process = Process.GetProcessById(id);
    }
    catch (ArgumentException)
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Process with id '{id}' not found[/]");

        return -1;
    }

    if (!quiet)
        AnsiConsole.MarkupLine($"[yellow]Shutting down {process.ProcessName}:{process.Id}...[/]");

    if (!SendUnixSigInt(process.Id, debug))
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Failed to send SIGINT to process {process.ProcessName}:{process.Id}[/]");

        return -1;
    }

    var deadline = timeout != null ? Environment.TickCount64 + timeout.Value : long.MaxValue;
    while (Environment.TickCount64 < deadline)
    {
        process.Refresh();
        if (process.HasExited)
            return 0;

        if (IsUnixStopComplete(process.Id))
        {
            if (debug)
                AnsiConsole.MarkupLine("[grey]Target process group is stopped or has exited[/]");

            return 0;
        }

        Thread.Sleep(50);
    }

    if (!quiet)
        AnsiConsole.MarkupLine($"[red]Timed out waiting for process {process.ProcessName}:{process.Id} to exit[/]");

    return -1;
}

static bool SendUnixSigInt(int pid, bool debug)
{
    var pgid = GetUnixProcessGroupId(pid);
    var args = pgid > 0 ? $"-INT -{pgid}" : $"-s SIGINT {pid}";

    if (debug)
        AnsiConsole.MarkupLine($"[grey]kill {args}[/]");

    using var kill = Process.Start(new ProcessStartInfo("kill", args)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    if (kill == null)
        return false;

    kill.WaitForExit();
    return kill.ExitCode == 0;
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

static int StopWindowsProcess(int id, int? timeout, bool quiet, bool debug)
{
    Process process;
    try
    {
        process = Process.GetProcessById(id);
    }
    catch (ArgumentException)
    {
        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Process with id '{id}' not found[/]");

        return -1;
    }

    if (!quiet)
        AnsiConsole.MarkupLine($"[yellow]Shutting down {process.ProcessName}:{process.Id}...[/]");

    var stoprExit = RunStopr(id, debug);

    if (debug)
        AnsiConsole.MarkupLine($"[grey]stopr exit code = {stoprExit}[/]");

    if (!IsStoprSuccess(stoprExit))
    {
        if (debug)
            AnsiConsole.MarkupLine("[grey]Using GUI (WM_CLOSE) shutdown path[/]");

        if (!SendCloseToGuiProcess(process, debug))
        {
            process.Refresh();
            if (process.HasExited)
            {
                if (debug)
                    AnsiConsole.MarkupLine("[grey]Target process already exited after stopr[/]");

                return 0;
            }

            if (!quiet)
                AnsiConsole.MarkupLine($"[red]No closeable windows found for process {process.ProcessName}:{process.Id}[/]");

            return -1;
        }
    }
    else if (debug)
    {
        if (stoprExit == 0)
            AnsiConsole.MarkupLine("[grey]Using console (Ctrl+C) shutdown path via stopr[/]");
        else
            AnsiConsole.MarkupLine("[grey]stopr posted Ctrl+C (exit code indicates signal was delivered)[/]");
    }

    if (timeout != null)
    {
        if (process.WaitForExit((int)timeout))
            return 0;

        if (!quiet)
            AnsiConsole.MarkupLine($"[red]Timed out waiting for process {process.ProcessName}:{process.Id} to exit[/]");

        return -1;
    }
    else
    {
        process.WaitForExit();
        return 0;
    }
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
    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
    {
        if (debug)
            AnsiConsole.MarkupLine($"[grey]CloseMainWindow succeeded for 0x{process.MainWindowHandle:X}[/]");

        process.Refresh();
        if (process.HasExited)
            return true;
    }

    var state = new GuiCloseState((uint)process.Id);
    var handle = GCHandle.Alloc(state);

    try
    {
        if (process.MainWindowHandle != IntPtr.Zero)
            state.Windows.Add(process.MainWindowHandle);

        var statePtr = GCHandle.ToIntPtr(handle);
        NativeMethods.EnumWindows(EnumWindowsByPidCallback, statePtr);
        NativeMethods.EnumWindows(EnumHostedParentCallback, statePtr);

        if (state.Windows.Count == 0)
            return false;

        var posted = 0;
        foreach (var hWnd in state.Windows)
        {
            if (NativeMethods.PostMessage(hWnd, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero))
                posted++;
            else if (debug)
                AnsiConsole.MarkupLine($"[grey]PostMessage WM_CLOSE failed for 0x{hWnd:X} (error {Marshal.GetLastWin32Error()})[/]");
        }

        if (debug)
            AnsiConsole.MarkupLine($"[grey]Posted WM_CLOSE to {posted} of {state.Windows.Count} window(s)[/]");

        return posted > 0;
    }
    finally
    {
        handle.Free();
    }
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
