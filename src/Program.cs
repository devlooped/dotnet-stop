using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConsoleAppFramework;
using Spectre.Console;

ConsoleApp.Run(args, Stop);

/// <summary>Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.</summary>
/// <param name="id">ID of the process to stop.</param>
/// <param name="timeout">-t, Optional timeout in milliseconds to wait for the process to exit.</param>
/// <param name="quiet">-q, Do not display any output.</param>
static int Stop([Argument] int id, [HideDefaultValue] int? timeout = default, bool quiet = false)
{
    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        return StopWindowsProcess(id, timeout, quiet);
    else
        return StopUnixProcess(id, timeout, quiet);
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