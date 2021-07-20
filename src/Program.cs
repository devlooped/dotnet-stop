using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Spectre.Console;

var command = new RootCommand("Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.")
{
    new Argument<int>("id", "ID of the process to stop."),
    new Option<int?>(new[] { "-t", "/t", "--timeout", "/timeout" }, "Optional timeout in milliseconds to wait for the process to exit."),
};

if (Environment.OSVersion.Platform == PlatformID.Win32NT)
    command.Handler = CommandHandler.Create<int, int?>(StopWindowsProcess);
else
    command.Handler = CommandHandler.Create<int, int?>(StopUnixProcess);

return command.Invoke(args);

static int StopUnixProcess(int id, int? timeout)
{
    Process process;
    try
    {
        process = Process.GetProcessById(id);
    }
    catch (ArgumentException)
    {
        AnsiConsole.MarkupLine($"[red]Process with id '{id}' not found[/]");
        return -1;
    }

    AnsiConsole.MarkupLine($"[yellow]Shutting down {process.Id}:{process.ProcessName}...[/]");
    Process.Start(new ProcessStartInfo("kill", "-s SIGINT " + process.Id) { UseShellExecute = true })?.WaitForExit();

    if (timeout != null)
    {
        if (process.WaitForExit((int)timeout))
            return 0;

        AnsiConsole.MarkupLine($"[red]Timed out waiting for process {process.Id}:{process.ProcessName} to exit[/]");
        return -1;
    }
    else
    {
        process.WaitForExit();
        return 0;
    }
}

static int StopWindowsProcess(int id, int? timeout)
{
    Process process;
    try
    {
        process = Process.GetProcessById(id);
    }
    catch (ArgumentException)
    {
        AnsiConsole.MarkupLine($"[red]Process with id '{id}' not found[/]");
        return -1;
    }

    AnsiConsole.MarkupLine($"[yellow]Shutting down {process.Id}:{process.ProcessName}...[/]");
    FreeConsole();
    AttachConsole((uint)id);
    GenerateConsoleCtrlEvent(0, 0);

    if (timeout != null)
    {
        if (process.WaitForExit((int)timeout))
            return 0;

        AnsiConsole.MarkupLine($"[red]Timed out waiting for process {process.Id}:{process.ProcessName} to exit[/]");
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