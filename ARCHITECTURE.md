# NexusWpp Architecture

## Runtime

```mermaid
graph TD
    EXE[nexuswpp.exe] --> WV[WebView2]
    EXE --> DESK[WorkerW / Progman desktop parent]
    EXE --> HOOK[WH_MOUSE_LL selective click hook]
    EXE --> TEL[Native telemetry collector]
    TEL --> WMI[WMI / Win32 APIs]
    TEL --> NVIDIA[Isolated nvidia-smi process when available]
    TEL --> POWER[powercfg / PowerGetActiveScheme]
    WV --> UI[index.html + style.css + cockpit-state.js / radar.js / dashboard.js / app.js]
    TEL --> MSG[PostWebMessageAsJson]
    MSG --> UI
    UI --> CMD[SET_POWER / REQUEST_RUNTIME_STATE / REQUEST_TELEMETRY messages]
    CMD --> EXE
```

## Startup Path

1. Windows starts `nexuswpp.exe` through `HKLM\...\Run` for the EXE installer, or through the packaged `windows.startupTask` for MSIX.
2. A named mutex keeps only one instance alive.
3. WebView2 starts immediately off screen and loads `http://nexuswpp.local/index.html` through a virtual host folder mapping.
4. A 100 ms timer asks Explorer to create the wallpaper layer and searches for `WorkerW`.
5. If `WorkerW` is not ready after 800 ms, the host temporarily attaches to `Progman` so the wallpaper appears sooner.
6. Once attached, the host covers the virtual screen and keeps listening for display changes.

## Telemetry

Telemetry is collected in `TelemetryCollector.cs`. `NvidiaTelemetry.cs` isolates driver reads in a bounded helper process and invalidates failed readings. `WindowsGpuTelemetry.cs` reads engine and memory counters for all adapters. Live counters (memory, system, CPU clock, GPU/NPU engines and memory, per-process CPU and private memory) come from persistent native PDH queries in `PdhCounters.cs` with English counter paths, sampled once per collection (about 50 ms instead of about 1.4 s through WMI). WMI is kept for one-time hardware inventory and a few light reads. DOM-independent freshness and alert rules live in `telemetry-state.js`.

- CPU load: `GetSystemTimes`
- Thermal reading: Windows thermal zone counters or ACPI `MSAcpi_ThermalZoneTemperature`, explicitly labelled as a thermal zone rather than a CPU core sensor
- CPU frequency: `% Processor Performance` PDH counter scaled by its `Processor Frequency` base
- RAM: `GlobalMemoryStatusEx` plus PDH memory counters; type and module count from SMBIOS
- Disk: raw WMI logical disk counters of the system drive (`%SystemDrive%`), differenced between samples for throughput, busy time and latency
- Network: `NetworkInterface` byte counters plus async ping to `1.1.1.1`, then `8.8.8.8`; the local gateway is a last resort reported as `pingVia: "gateway"` so LAN latency is never shown as internet latency
- GPU/iGPU: DirectX LUID registry mapping plus GPU performance counters
- NVIDIA details: isolated `nvidia-smi` process (temperature, clocks, power, VRAM), cached for five seconds; otherwise driver-reported VRAM size and Windows counters
- System uptime: `GetTickCount64`
- Power plans: `powercfg /list`, `PowerGetActiveScheme`, and `PowerSetActiveScheme`

Visible text lives in `i18n.js` (French and English, chosen from the Windows display language through `navigator.language`; `?lang=` overrides it in tests).

The frontend receives telemetry only through WebView2 messages. There is no HTTP server in the current native architecture.

## Settings

A tray icon (`NotifyIcon`) opens the settings window (`SettingsForm.cs`) and quits the app; launching NexusWpp again signals the running instance (named event) to show the same window. The custom center logo is imported by `UserSettings.cs` (center-cropped, 512 px PNG in the user data folder) and sent to the page as `{"control":"SETTINGS","logo":"data:image/png;base64,..."}`; `null` restores the brand logo. Quitting repaints the user's own Windows wallpaper.

## Interaction

The injected WebView2 window lives below desktop icons, so normal mouse delivery is unreliable. The frontend reports the bounds of the power-plan panel with `BOUNDS:left,top,right,bottom`. The host installs a low-level mouse hook and forwards clicks inside that rectangle to Chromium, then swallows those clicks so Windows does not select desktop icons behind the UI.

## Performance Choices

- WebView2 is warmed before `WorkerW` is found.
- One 12 Hz clock drives all continuous motion: the canvas loop, the two logo spin rings (same 180 / 144 steps as the former CSS `steps()` animation) and the 0.18 s ease of the gauge rings share the canvas tick, so no CSS animation or transition runs at the display refresh rate. The canvas skips drawing when particles/interactions stop and the clock sleeps when nothing moves. When Windows energy saver is on (`SystemStatusFlag`) or Windows "Animation effects" is off (`prefers-reduced-motion`), the logo rings hold still; values, clock and data packets keep updating, and with reduced motion gauges and bars take new values at once.
- The radar canvas backing store follows `devicePixelRatio` (capped at 2), so it stays sharp at 125-200 % display scaling; at 100 % it is unchanged.
- A missing or stale reading keeps its last valid value on screen (no jump to a dash) with the existing stale cues: card badge, greyed sub-metric or radar value, and an accessible value marked stale. Only a reading never received shows "—". The 60 s history leaves a gap for the missing sample.
- Top-process ranking uses its own PDH query sampled every 4 s (CPU averaged over that window) and is protected against overlapping calls.
- A discarded warm-up collection at startup makes the first visible frame show fresh values even after a covered start.
- GPU routing is left to Windows; NexusWpp does not write DirectX GPU preferences.
- A 250 ms coverage check pauses work only when all monitor work areas stay covered for 1.5 s (or the session is inactive). While paused, a WinEvent hook (foreground, move/size end, minimize, show/hide/destroy of top-level windows) re-checks within about 60 ms; the hook exists only while paused. WebView2 stays visible and is never suspended for occlusion; the real composed scene remains available before animation resumes.
- Canvas pixels, particles and critical-state styling survive pause. CSS animations and in-flight transitions pause, clock callbacks stop, and stale asynchronous telemetry is discarded. The page requests runtime state after its listener is ready, including recovery/navigation.
- The mouse hook runs on its own message-pump thread, consuming immutable routing snapshots from the UI thread.
