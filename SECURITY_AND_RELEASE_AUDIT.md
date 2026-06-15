# NexusWpp Security And Release Audit

## Current Status

- Local production build: `C:\nexuswpp\nexuswpp.exe`.
- Public-source ready: source tree excludes generated binaries, logs, and benchmark JSON files through `.gitignore`.
- Secrets scan: no obvious `password`, `secret`, `token`, or `api key` strings were found in project source.
- 2026-06-15 audit: `app.js` syntax OK, PowerShell scripts parse OK, HTML IDs referenced from JS have no missing target, generated `bin/` and `dist/` remain ignored.
- Network surface: no Node server, no HTTP listener. WebView2 loads local files through a virtual host mapping.
- Startup: single `HKLM\...\Run` entry configured by the installer or deployment script.
- Privilege: deployment and installation request elevation only to install in `C:\nexuswpp` and register Windows startup/uninstall entries.

## Performance Gates

- GPU WMI 2s cache: `KEEP`, CPU improved by `38.45%`.
- Network link-speed scaling: `KEEP`, CPU improved by `16.63%` on clean comparison.
- Final fullscreen probe: CPU reduction `66.7%`, probe matched the test fullscreen window, resume detected.
- Benchmark scripts filter NexusWpp's own WebView2 processes, so other desktop WebView2 apps do not pollute CPU/RAM measurements.
- Power-plan selector uses `PowerSetActiveScheme`, validates the active GUID after the request, and keeps the UI state pending until confirmation.
- The mouse hook forwards power-panel clicks only when the desktop/wallpaper is actually under the pointer. Clicks on another foreground application at the same screen coordinates are ignored.
- Build gates verified on 2026-06-15: `compile.ps1`, `scripts\build_installer.ps1`, and `scripts\build_msix.ps1` complete successfully.

## Known Limits

- Installer packaging is available through `scripts\build_installer.ps1`.
- Executable signing is not configured unless a signing certificate is provided through environment variables.
- External build dependency: `compile.ps1` downloads the pinned WebView2 NuGet package over HTTPS if DLLs are absent.
- Visual browser smoke test was attempted during the 2026-06-15 audit, but the in-app browser automation did not attach to the local file view; build and static checks were used as the verified gates.
