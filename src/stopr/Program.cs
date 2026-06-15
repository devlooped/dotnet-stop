using System.Runtime.InteropServices;
using Devlooped;

if (!OperatingSystem.IsWindows())
    return 1;

if (args.Length != 1 || !int.TryParse(args[0], out var pid))
    return 2;

NativeMethods.FreeConsole();
if (!TryAttachConsoleChain((uint)pid, out var attachedDirectly))
    return 1;

NativeMethods.SetConsoleCtrlHandler(null, true);
try
{
    var processGroupId = attachedDirectly ? 0u : (uint)pid;
    if (!SendCtrlC(processGroupId))
        return 3;

    Thread.Sleep(300);

    SendCtrlC(processGroupId);
    return 0;
}
finally
{
    NativeMethods.FreeConsole();
    NativeMethods.SetConsoleCtrlHandler(null, false);
}

static bool TryAttachConsoleChain(uint pid, out bool attachedDirectly)
{
    if (NativeMethods.AttachConsole(pid))
    {
        attachedDirectly = true;
        return true;
    }

    for (var current = GetParentProcessId(pid); current != 0; current = GetParentProcessId(current))
    {
        if (NativeMethods.AttachConsole(current))
        {
            attachedDirectly = false;
            return true;
        }
    }

    attachedDirectly = false;
    return false;
}

static uint GetParentProcessId(uint pid)
{
    var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32CsSnapProcess, 0);
    if (snapshot == (nint)(-1))
        return 0;

    try
    {
        var entry = new NativeMethods.ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>() };
        if (!NativeMethods.Process32First(snapshot, ref entry))
            return 0;

        do
        {
            if (entry.th32ProcessID == pid)
                return entry.th32ParentProcessID;
        }
        while (NativeMethods.Process32Next(snapshot, ref entry));
    }
    finally
    {
        CloseHandle(snapshot);
    }

    return 0;
}

static bool SendCtrlC(uint processGroupId) =>
    NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CtrlCEvent, processGroupId);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool CloseHandle(nint hObject);
