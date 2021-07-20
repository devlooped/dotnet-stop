![Icon](https://raw.githubusercontent.com/devlooped/dotnet-stop/main/src/icon.png) dotnet-stop
============

A dotnet global tool that gracefully stops processes by sending them SIGNINT (Ctrl+C) in a cross platform way.

```
dotnet stop
  Sends the SIGINT (Ctrl+C) signal to a process to gracefully stop it.

Usage:
  dotnet stop [options] <id>

Arguments:
  <id>  ID of the process to stop.

Options:
  -t, --timeout <timeout>  Optional timeout in milliseconds to wait for the process to exit.
  --version                Show version information
  -?, -h, --help           Show help and usage information
```