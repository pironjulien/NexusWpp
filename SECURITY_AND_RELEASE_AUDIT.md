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

## Known Limits

- True HDR is not shipped as production. The current renderer is premium WebView2 SDR.
- Native HDR proof-of-concept exists, but visual parity with the current UI is not solved.
- Executable signing and installer packaging are not done.
- External build dependency: `compile.ps1` downloads the pinned WebView2 NuGet package over HTTPS if DLLs are absent.
