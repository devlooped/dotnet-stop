using Devlooped;

if (!OperatingSystem.IsWindows())
    return 1;

if (args.Length != 1 || !int.TryParse(args[0], out var pid))
    return 2;

NativeMethods.FreeConsole();
if (!NativeMethods.AttachConsole((uint)pid))
    return 1;

NativeMethods.SetConsoleCtrlHandler(null, true);
if (!NativeMethods.GenerateConsoleCtrlEvent(NativeMethods.CtrlCEvent, 0))
{
    NativeMethods.FreeConsole();
    NativeMethods.SetConsoleCtrlHandler(null, false);
    return 3;
}

NativeMethods.FreeConsole();
NativeMethods.SetConsoleCtrlHandler(null, false);
return 0;
