using System;
using System.Runtime.InteropServices;

namespace Devlooped;

static class NativeMethods
{
    public const uint WmClose = 0x0010;
    public const uint AttachParentProcess = 0xFFFFFFFF;
    public const uint CtrlCEvent = 0;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool ConsoleCtrlHandler(uint ctrlType);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    public const uint GaRoot = 2;

    [DllImport("kernel32.dll")]
    public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandler? handler, bool add);
}
