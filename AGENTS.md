# NexusWpp instructions

## Project

- Native Windows dynamic wallpaper application.
- The main host is the partial class `DesktopForm` (`DesktopForm*.cs`, one file per responsibility) started by `Program.cs`.
- The interface is in `index.html`, `style.css` and classic scripts loaded in order (`cockpit-state.js`, `radar.js`, `dashboard.js`, `app.js`) that share the global scope; visible text is translated in `i18n.js` (French and English).
- The standalone EXE mode uses `C:\nexuswpp`. The Store/MSIX mode uses WindowsApps and keeps its data in the packaged user profile; the only EXE install path is the installer (`scripts\build_installer.ps1` then `dist\NexusWppSetup.exe`).

## Useful commands

```powershell
.\compile.ps1
.\scripts\build_installer.ps1
.\scripts\build_msix.ps1
dotnet run --project .\tools\ExportIcons   # icon.ico and MSIX logos from branding\*.svg (Svg.Skia)
.\scripts\tag_release.ps1             # before the push: is the version ready to publish?
.\scripts\tag_release.ps1 -Download   # files of release v<VERSION> into dist\release, attestation verified
```

## Publishing

- Releases are built by GitHub, never on the PC: pushing to `main` a `VERSION` without a `v<VERSION>` release (with its `## [<VERSION>] - <date>` section in `CHANGELOG.md`, Keep a Changelog format, in English) runs `.github/workflows/release.yml`: `test.ps1`, unsigned MSIX (the Store signs it), unsigned installer, `SHA256SUMS.txt`, provenance attestation, then the immutable release with all files and the tag.
- The Store receives the release's MSIX (`tag_release.ps1 -Download`), never a locally built MSIX.
- The installer is not Authenticode-signed (no public certificate; a self-signed certificate proves nothing): its origin is checked with `gh attestation verify <file> --repo pironjulien/NexusWpp`.

## Verification

- Check that `.\compile.ps1` builds `bin\nexuswpp.exe`.
- Run `.\test.ps1` before every commit: build, JavaScript tests, then `dotnet test` on `tests\NexusWpp.Tests` (xUnit v3: measurement rules, desktop coverage, and the host, settings and logo harnesses, mouse hook, pause, quick layout; off-screen windows, never activated).
- Check that `.\scripts\build_installer.ps1` creates `dist\NexusWppSetup.exe`.
- Check that `.\scripts\build_msix.ps1` creates the Store package `dist\msix\julienpiron.fr.NexusWpp_<VERSION>_x64.msix`, with the number from the `VERSION` file.
- Explicit tests, outside `test.ps1` and CI: all resolutions with `dotnet test --project tests\NexusWpp.Tests --filter-method *ResponsiveLayoutFull --explicit only`; Store screenshots with `.\scripts\store_screenshots.ps1` (settings window on screen, never activated).
- Measure the physical return of the desktop with `.\scripts\test_desktop_visual_resume.ps1` (real desktop: minimizes then restores windows, only while the user is away).
- Do not version `bin/`, `dist/`, logs, temporary files or benchmark results.

## Project habits

- Work on `main`.
- Update `CHANGELOG.md` for every functional change.
- For the Store, keep the MSIX identity `julienpiron.fr.NexusWpp` and the Publisher `CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA`.
- Keep commit messages short and in English (public repository), like the CHANGELOG.
- Do not add a fake button, fake data or an unwired feature.

## Pitfalls

- Animation rate: the page resets the rate every 500 ms; for a benchmark, override `setMotionEconomy`, render off screen at the real screen size (e.g. 5120×1440), compare against the real frozen wallpaper and alternate the variants, otherwise the measurements contaminate each other.
- GPU cost: the `GPU Engine` counters only give a LUID; first link the LUID to the adapter (`\GPU Adapter Memory(*)\Dedicated Usage`, and dwm's LUID) before attributing the load to the iGPU or the dedicated GPU. For the real cost, measure watts (`nvidia-smi`) with active rendering against frozen rendering, not the utilization percentage, inflated at the floor frequency.
- RAM node: it shows the memory in use, not the bandwidth. No reliable bandwidth measurement without admin rights exists on Alder/Raptor Lake: LLCMisses counters through ETW (exact but admin + SeSystemProfilePrivilege), latency probe (~21 points of error), RAPL "uncore" power (~8 points of error, false traffic under pure compute), Intel PMT (no DDR descriptor for these processors, the driver exposes no IOCTL). Do not retest these leads.
- `radar.js` (nodeLoad: activity, otherwise value) spins a node according to its value: a node with a stable value (RAM) therefore animates continuously at rest.
- Unexplained intermittent failure (code -1, locked DLL, changed file): first check that another agent is not running `test.ps1` on the same repository at the same time (`scripts\test_lock.ps1` locks each test) before suspecting the code.

## Decisions

- Rate: 12 fps with sliding history curves, chosen by the maintainer after a benchmark (2026-11-06); do not propose 24 fps again, nor any other rate optimization without a request. What matters is the headroom left to other apps, not the watts.
- The visuals do not degrade in normal use: a visible optimization stays conditional (for example only with Windows energy saver) and is stated.
- Everything free: no paid certificate or service.
- No elevated service for the RAM node (Store approval uncertain, 2026-11-04).

## To do

- Calm RAM node at rest (maintainer request of 2026-11-04): packets and rotation only when memory changes, the figure remaining the memory in use.
