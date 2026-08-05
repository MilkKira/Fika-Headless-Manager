# Fika Headless Manager

Fika Headless Manager is a Windows desktop dashboard for controlling multiple Fika headless clients from one window.

## Features

- Single-window WPF analytics dashboard with a fixed navigation sidebar, search, KPI cards, live charts, and activity history.
- Add, edit, remove, start, and stop multiple SPT/Fika headless profiles.
- Open a per-manager **View** panel that streams manager events, stdout, stderr, `BepInEx/LogOutput.log`, and optional `Headless.log` output inside the application.
- Start or stop every configured profile in one action.
- Validate `EscapeFromTarkov.exe`, `Fika.Headless.dll`, and the Fika backend before launch.
- Automatically restart headless clients after an unexpected exit.
- Run the manager as a Windows GUI executable without a console window.
- Suppress the child console with `CreateNoWindow`, `--enable-console false`, and a Win32 monitor that hides console windows created later by BepInEx.
- Import the legacy `HeadlessConfig.json` automatically when the app is first run inside an SPT directory.

Profiles are stored per user in `%LOCALAPPDATA%\FikaHeadlessManager\instances.json`.

## Build

Requires the .NET 9 SDK on Windows.

```powershell
dotnet build FikaHeadlessManager.csproj --configuration Release
```

## Publish an EXE

Framework-dependent single-file executable:

```powershell
dotnet publish FikaHeadlessManager.csproj --configuration Release --runtime win-x64 --output publish
```

The target computer must have the .NET 9 Windows Desktop Runtime. To include the runtime, append `--self-contained true`.

## Usage

1. Run `FikaHeadlessManager.exe`.
2. Select **Add manager**.
3. Choose the SPT installation directory and enter its Fika profile ID and backend URL.
4. Save the profile, then use **Start**, **Stop**, **Start all**, or **Stop all**.
5. Select **View** beside an instance to inspect its captured output without opening a console window.

Closing the dashboard stops all headless processes launched by the current dashboard session.
Captured output is kept in memory for the current application session and is capped at 3,000 entries per manager.
Running managers must use separate SPT installation directories so BepInEx and Unity file logs remain isolated per process.
