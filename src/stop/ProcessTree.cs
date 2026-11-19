using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Devlooped;

/// <summary>
/// Child processes of the process being stopped. After the target exits, the OS
/// reparents them, so their ids have to be recorded while the target is alive.
/// </summary>
static class ProcessTree
{
    public static void AddDescendants(int root, HashSet<int> into)
    {
        var children = ChildrenByParent();
        var queue = new Queue<int>();
        queue.Enqueue(root);
        foreach (var pid in into)
            queue.Enqueue(pid);

        var seen = new HashSet<int>();
        while (queue.Count > 0)
        {
            var pid = queue.Dequeue();
            if (!seen.Add(pid))
                continue;

            if (pid != root)
                into.Add(pid);

            if (!children.TryGetValue(pid, out var kids))
                continue;

            foreach (var kid in kids)
                queue.Enqueue(kid);
        }
    }

    static Dictionary<int, List<int>> ChildrenByParent()
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, ppid) in ReadParentMap())
        {
            if (!children.TryGetValue(ppid, out var list))
            {
                list = [];
                children[ppid] = list;
            }

            list.Add(pid);
        }

        return children;
    }

    static Dictionary<int, int> ReadParentMap()
    {
        if (OperatingSystem.IsWindows())
            return ReadWindowsParents();

        if (OperatingSystem.IsLinux())
        {
            var proc = ReadProcParents();
            if (proc.Count > 0)
                return proc;
        }

        return ReadPsParents();
    }

    static Dictionary<int, int> ReadProcParents()
    {
        var map = new Dictionary<int, int>();
        if (!Directory.Exists("/proc"))
            return map;

        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid))
                continue;

            try
            {
                var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                var end = stat.LastIndexOf(')');
                if (end < 0 || end + 2 >= stat.Length)
                    continue;

                var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 && int.TryParse(fields[1], out var ppid))
                    map[pid] = ppid;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return map;
    }

    static Dictionary<int, int> ReadPsParents()
    {
        var map = new Dictionary<int, int>();
        try
        {
            using var ps = Process.Start(new ProcessStartInfo("ps", "-ax -o pid=,ppid=")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            if (ps is null)
                return map;

            var text = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit();
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], out var pid) &&
                    int.TryParse(parts[1], out var ppid))
                    map[pid] = ppid;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or IOException)
        {
        }

        return map;
    }

    static Dictionary<int, int> ReadWindowsParents()
    {
        var map = new Dictionary<int, int>();
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32CsSnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
            return map;

        try
        {
            var entry = new NativeMethods.ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>() };
            if (!NativeMethods.Process32First(snapshot, ref entry))
                return map;

            do
            {
                map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));

            return map;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }
}
