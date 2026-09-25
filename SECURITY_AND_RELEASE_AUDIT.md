# NexusWpp Security And Release Audit

## Current Status

- Current packaged installation: `julienpiron.fr.NexusWpp_yq2nr3sn86fpg`, with the runtime path provided by `Get-AppxPackage`. `C:\nexuswpp` is reserved for the separate EXE installation mode.
- Public-source ready: source tree excludes generated binaries, logs, and benchmark JSON files through `.gitignore`.
- Secrets scan: no real secret found; only a documented placeholder password and signing environment variable references were detected.
- 2026-06-19 audit: `compile.ps1`, `scripts\build_installer.ps1`, and `scripts\build_msix.ps1` complete successfully; `bin/`, `dist/`, logs and benchmark outputs remain ignored and untracked.
- Network surface: no Node server, no HTTP listener. WebView2 loads local files through a virtual host mapping.
- Startup: packaged `NexusWppStartup` task for Store/MSIX; a single `HKLM\...\Run` entry only for the separate EXE installation mode.
- Privilege: deployment and installation request elevation only to install in `C:\nexuswpp` and register Windows startup/uninstall entries.

## Historical Performance Gates

- GPU WMI 2s cache: `KEEP`, CPU improved by `38.45%`.
- Network link-speed scaling: `KEEP`, CPU improved by `16.63%` on clean comparison.
- Final fullscreen probe: CPU reduction `66.7%`, probe matched the test fullscreen window, resume detected.
- Benchmark scripts filter NexusWpp's own WebView2 processes, so other desktop WebView2 apps do not pollute CPU/RAM measurements.
- Power-plan selector uses `PowerSetActiveScheme`, validates the active GUID after the request, and keeps the UI state pending until confirmation.
- The mouse hook forwards power-panel clicks only when the desktop/wallpaper is actually under the pointer. Clicks on another foreground application at the same screen coordinates are ignored.
- Build gates verified on 2026-06-19: `compile.ps1`, `scripts\build_installer.ps1`, and `scripts\build_msix.ps1` complete successfully.

## Known Limits

- Installer packaging is available through `scripts\build_installer.ps1`.
- Publicly trusted EXE signing is not configured by default. Without a certificate provided through environment variables, the installer is signed with a local self-signed certificate, which Windows reports as an untrusted root outside this machine.
- External build dependency: `compile.ps1` downloads the pinned WebView2 NuGet package over HTTPS if DLLs are absent.
- Desktop composition capture cannot resolve physical panel scanout or sub-frame latency. The visual regression harness records its actual sampling intervals; it does not convert logs or GPU reductions into evidence of scene retention.
- Real multi-monitor transition tests require multiple connected displays. Geometric coverage tests cover multiple monitors and negative coordinates independently of available hardware.
