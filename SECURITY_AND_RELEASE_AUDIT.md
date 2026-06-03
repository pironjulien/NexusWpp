# NexusWpp Security And Release Audit

## Current Status

- Local production build: `C:\nexuswpp\nexuswpp.exe`.
- Public-source ready: source tree excludes generated binaries, logs, and benchmark JSON files through `.gitignore`.
- Secrets scan: no obvious `password`, `secret`, `token`, or `api key` strings were found in project source.
- Network surface: no Node server, no HTTP listener. WebView2 loads local files through a virtual host mapping.
- Startup: user Startup shortcut plus non-elevated scheduled task fallback.
- Privilege: deployment may request elevation only to register/update scheduled-task and registry startup preferences.

## Performance Gates

- GPU WMI 2s cache: `KEEP`, CPU improved by `38.45%`.
- Network link-speed scaling: `KEEP`, CPU improved by `16.63%` on clean comparison.
- Final fullscreen probe: CPU reduction `66.7%`, probe matched the test fullscreen window, resume detected.
- Benchmark scripts filter NexusWpp's own WebView2 processes, so other desktop WebView2 apps do not pollute CPU/RAM measurements.
- Power-plan selector uses `PowerSetActiveScheme`, validates the active GUID after the request, and keeps the UI state pending until confirmation.
- The mouse hook forwards power-panel clicks only when the desktop/wallpaper is actually under the pointer. Clicks on another foreground application at the same screen coordinates are ignored.

## Known Limits

- Executable signing and installer packaging are not done.
- External build dependency: `compile.ps1` downloads the pinned WebView2 NuGet package over HTTPS if DLLs are absent.
