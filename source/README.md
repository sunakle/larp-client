# LARP Client launcher

Small Windows x64 WinForms launcher for the separate client folder. Requires Windows .NET Framework 4.x; Java is loaded only from the bundled runtime. Build with Windows PowerShell or PowerShell:

```powershell
.\build-launcher.ps1
```

The executable reads `launch-config.json` beside itself. All configured Java and classpath paths are relative to that folder. The game uses its own `game` working directory; `game/assets` and `game/natives` are required. `${root}`, `${game_directory}`, `${natives_directory}`, `${assets_root}`, `${classpath}`, `${auth_player_name}`, `${auth_uuid}`, and `${memory_mb}` are expanded at launch. Heap flags are added by the launcher.

The launcher creates a **local profile** from a nickname and its standard Java offline UUID. It does not import accounts or tokens and does not provide Microsoft sign-in. Servers requiring an authenticated Microsoft/Minecraft session need a separate sign-in implementation. The packaged mods are not changed by the launcher.

Normal usage is to run `LARP Client.exe`, enter a nickname of 3–16 ASCII letters/digits/underscores, select 2–12 GiB of RAM, and click Play. Only nickname and memory are saved in `client-settings.json`. Closing the launcher window while Minecraft runs hides the launcher until Minecraft exits so redirected logs continue working; Minecraft is never killed by closing the window. A named mutex prevents two copies of this same client folder from launching simultaneously.

Automated checks and launch:

```powershell
& '.\LARP Client.exe' --check
& '.\LARP Client.exe' --launch Player 4096
```

`--check` validates configured files and exits 0/1, recording the result in `logs/check-result.json`. It does not verify game rendering or mod behaviour. `--launch` starts the game immediately with the given local nickname and memory in MiB, keeps the launcher alive until the game exits, and returns 0/1. `logs/last-launch.json` records `javaPID`, `launcherPID`, and the game log path; `logs/last-exit.json` records the actual game exit code. Standard output and error are captured together in `logs/game-*.log`. The JVM argument file is UTF-8 and remains beside the log. There are no imported credentials in these argument files.

The child process removes inherited `_JAVA_OPTIONS`, `JAVA_TOOL_OPTIONS`, `JDK_JAVA_OPTIONS`, and `CLASSPATH`. No other launcher process, installation path, or system Java is consulted.

## Updated appearance
Graphite layout, vector logo, embedded multi-resolution icon and system DPI manifest. The build script exports the icon automatically if needed. `--export-assets DIR` exports PNGs and ICO; `--preview FILE.png` renders the launcher without Java. Install.ps1 backs up changed files and replaces three branding PNGs in the mod; bytecode is not edited.
