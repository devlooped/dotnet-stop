![Icon](https://raw.githubusercontent.com/devlooped/dotnet-stop/main/assets/img/icon.png) dotnet-stop
============

[![Version](https://img.shields.io/nuget/v/dotnet-stop.svg?color=royalblue)](https://www.nuget.org/packages/dotnet-stop) [![Downloads](https://img.shields.io/nuget/dt/dotnet-stop.svg?color=green)](https://www.nuget.org/packages/dotnet-stop) [![License](https://img.shields.io/github/license/devlooped/dotnet-stop.svg?color=blue)](https://github.com/devlooped/dotnet-stop/blob/main/license.txt) [![Build](https://github.com/devlooped/dotnet-stop/workflows/build/badge.svg?branch=main)](https://github.com/devlooped/dotnet-stop/actions)

A dotnet global tool that gracefully stops processes by sending them SIGINT (Ctrl+C) in a cross platform way.

```
dotnet-stop
  Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.

Usage:
  dotnet stop [options] <id>

Arguments:
  <id>  ID of the process to stop.

Options:
  -t, --timeout <timeout>  Optional timeout in milliseconds to wait for the process to exit.
  -q, --quiet              Do not display any output. [default: False]
  --version                Show version information
  -?, -h, --help           Show help and usage information
```

If no timeout is provided, the tool will wait indefinitely for the target process to exit.
Otherwise, the process will exit with a non-zero exit code if the target process didn't 
exit within the specified timeout time.

## Sponsors

[![sponsored](https://raw.githubusercontent.com/devlooped/oss/main/assets/images/sponsors.svg)](https://github.com/sponsors/devlooped) [![clarius](https://raw.githubusercontent.com/clarius/branding/main/logo/byclarius.svg)](https://github.com/clarius)[![clarius](https://raw.githubusercontent.com/clarius/branding/main/logo/logo.svg)](https://github.com/clarius)

*[get mentioned here too](https://github.com/sponsors/devlooped)!*