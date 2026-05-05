using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ConsoleAppFramework;
using Spectre.Console;

ConsoleApp.Run(args, Stop);

/// <summary>Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.</summary>
/// <param name="id">ID of the process to stop. When omitted, reads process IDs from standard input.</param>
/// <param name="timeout">-t, Optional timeout in milliseconds to wait for the process to exit.</param>
/// <param name="quiet">-q, Do not display any output.</param>
static int Stop([Argument] int? id = null, [HideDefaultValue] int? timeout = default, bool quiet = false)
{
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
            if (StopProcess(pid, timeout, quiet) != 0)
                anyFailure = true;

        return anyFailure ? -1 : 0;
    }

    return StopProcess(id.Value, timeout, quiet);
}

static int StopProcess(int id, int? timeout, bool quiet) =>
    Environment.OSVersion.Platform == PlatformID.Win32NT
        ? StopWindowsProcess(id, timeout, quiet)
        : StopUnixProcess(id, timeout, quiet);

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

static int StopUnixProcess(int id, int? timeout, bool quiet)
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

    Process.Start(new ProcessStartInfo("kill", "-s SIGINT " + process.Id) { UseShellExecute = true })?.WaitForExit();

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

static int StopWindowsProcess(int id, int? timeout, bool quiet)
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

    FreeConsole();
    AttachConsole((uint)id);
    GenerateConsoleCtrlEvent(0, 0);

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

[DllImport("kernel32.dll")]
static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AttachConsole(uint dwProcessId);
[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
static extern bool FreeConsole();