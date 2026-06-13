using System;
using System.Collections.Generic;

namespace Devlooped;

sealed class GuiCloseState(uint targetPid)
{
    public uint TargetPid { get; } = targetPid;
    public HashSet<IntPtr> Windows { get; } = [];
}
