![Icon](https://raw.githubusercontent.com/devlooped/dotnet-stop/main/assets/img/icon.png) dotnet-stop
============

[![Version](https://img.shields.io/nuget/v/stop.svg?color=royalblue)](https://www.nuget.org/packages/stop) [![Downloads](https://img.shields.io/nuget/dt/stop.svg?color=green)](https://www.nuget.org/packages/stop) [![License](https://img.shields.io/github/license/devlooped/dotnet-stop.svg?color=blue)](https://github.com/devlooped/dotnet-stop/blob/main/license.txt)

<!-- #content -->
A dotnet global tool that gracefully stops processes by sending them SIGINT (Ctrl+C) in a cross platform way.

## Why

PowerShell's `Stop-Process` (and the underlying `TerminateProcess` on Windows / `SIGKILL` on Unix) kills a process immediately — the target has no opportunity to clean up. This matters for apps that rely on graceful shutdown: flushing writes to disk, completing in-flight work, disposing resources, or honouring a `CancellationToken` in a .NET `IHost`/`IHostedService`.

`dnx stop` sends **SIGINT** instead — the same signal as pressing Ctrl+C — which lets the process shut down on its own terms. On Windows this is non-trivial to do programmatically; it requires detaching from the current console, attaching to the target process's console group, and calling `GenerateConsoleCtrlEvent`. This tool handles all of that transparently, cross-platform.

``` bash
dnx stop [<processId>] [--timeout <milliseconds>] [--quiet]
```

<!-- include src/help.md -->
```shell
Usage: [arguments...] [options...] [-h|--help] [--version]

Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.

Arguments:
  [0] <int?>    ID of the process to stop. When omitted, reads process IDs from standard input.

Options:
  -t, --timeout <int?>    Optional timeout in milliseconds to wait for the process to exit.
  -q, --quiet             Do not display any output.
```

<!-- src/help.md -->

If no timeout is provided, the tool will wait indefinitely for the target process to exit.
Otherwise, the process will exit with a non-zero exit code if the target process didn't 
exit within the specified timeout time.

## Piping from PowerShell

When no process ID is given, `dnx stop` reads process IDs from standard input. This allows 
piping directly from PowerShell's `Get-Process`:

```powershell
# Stop all processes named 'foo'
Get-Process -Name foo | dnx stop

# Stop multiple processes by name
Get-Process -Name foo, bar | dnx stop
```

You can also pipe just the IDs:

```powershell
Get-Process -Name foo | Select-Object -ExpandProperty Id | dnx stop
```

<!-- #content -->
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![Ryan McCaffery](https://avatars.githubusercontent.com/u/16667079?u=c0daa64bb5c1b572130e05ae2b6f609ecc912d4d&v=4&s=39 "Ryan McCaffery")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![eska-gmbh](https://avatars.githubusercontent.com/devlooped-team?s=39 "eska-gmbh")](https://github.com/eska-gmbh)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
