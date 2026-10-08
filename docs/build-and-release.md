# Build / run

Moved out of CLAUDE.md (verbatim, detail on demand). CLAUDE.md has the summary.

## Build / run
- Godot 4.7.2 mono: `C:\Users\pvoos_nnvpp5d\Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\`
  (use the `_console.exe` for log output). `dotnet build Kloarte.sln`, then run a scene of the app with
  `--path src/Kloarte.Host` (e.g. `res://shows/risquons_tout.tscn`); the editor: open `src/Kloarte.Host/project.godot`.
- After adding assets run `<godot>_console.exe --headless --path src/Kloarte.Host --import` once (and for the test
  project, see "Tests").
- Patch scripts: write Python helpers with the Write tool (heredocs in Git Bash and sed mangle `\n`, `\0`, `\\`).
- MagicQ PC (`C:\Program Files\ChamSys Ltd\MagicQ PC`) may be used for testing; it ships `ffmpeg.exe` (movie MP4).
- Distribution: `powershell -ExecutionPolicy Bypass -File tools\make_dist.ps1` (`-InstallTemplates` downloads the
  export templates from the Godot release on GitHub; installed 9 Oct 2026) exports `src\Kloarte.Host` ->
  `build\Kloarte` + `dist\Kloarte-<date>.zip` in the repository root (~73 MB; both git-ignored). Preset "Windows
  Desktop" (`src/Kloarte.Host/export_presets.cfg`, custom feature `dist`: `run/main_scene.dist` = the show scene; F5 in
  the editor still runs `main.tscn`; `Projects/` excluded). The export ships the libraries in
  `data_Kloarte.Host_windows_x86_64`. The script copies `data\`, `venue\*.yaml`, `gobos\*.png`,
  `shows\*.fixtures.json` next to the exe (`AppPaths.File`).
  No console wrapper in a release export: log with `Kloarte.exe --log-file <file>` (`--quit-after N` frames).
  Without it the log is `%APPDATA%\Godot\app_userdata\Kloarte up de plankn\logs\godot.log` (previous runs dated next
  to it); `application/run/flush_stdout_on_print = true` so it is complete even when the app hangs.
- **A hung build**: `Get-Process Kloarte` (Responding False; CPU still rising = busy loop, flat = blocked). Managed
  stacks: `dotnet-stack report -p <pid>` (dotnet global tool). Full dump: `dotnet-dump collect -p <pid> --type Full`,
  native stacks with WinDbg's cdb (`winget install Microsoft.WinDbg`; `cdb -z <dmp> -y srv*<cache>*https://msdl.microsoft.com/download/symbols -c "~*kc 25; q"`).
  Godot's release exe has no symbols (frames show as `Kloarte!NoHotPatch+...`); compare with the Godot source of the
  same tag (e.g. `platform/windows/display_server_windows.cpp`). All four tools are installed (10 Oct 2026).

