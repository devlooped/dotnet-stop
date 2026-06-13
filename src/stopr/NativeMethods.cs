using System;
using System.Runtime.InteropServices;

namespace Devlooped;

static class NativeMethods
{
    public const uint CtrlCEvent = 0;

    public delegate bool ConsoleCtrlHandler(uint ctrlType);

    [DllImport("kernel32.dll")]
    public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler, bool add);
}
