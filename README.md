# New York Chronicles — Launcher

Game launcher for [New York Chronicles](https://newyorkchronicles.online) MTA:SA Roleplay.

Downloads and verifies game files, auto-updates, and launches the game.

## Features

- Auto-update on launch
- Game file verification and download
- Server status display
- CefSharp embedded UI
- No admin privileges required
- Minimal resource usage when minimized

## Build

### Requirements
- [Visual Studio 2022+](https://visualstudio.microsoft.com/vs/) or .NET SDK
- .NET Framework 4.8

### Steps
1. Copy `NYCLauncher/Core/Secrets.example.txt` to `NYCLauncher/Core/Secrets.cs` and fill in your URLs
2. `dotnet build NYCLauncher.sln -c Release`

> `Secrets.cs` is gitignored — the launcher won't build or connect without your own API/CDN URLs.

## Links

- [Website](https://newyorkchronicles.online)
- [Forum](https://forum.newyorkchronicles.online)
- [Discord](https://discord.newyorkchronicles.online)
- [Client Source](https://github.com/NewYorkChronicles/client)

## License

Proprietary. All rights reserved.
