# NexusWpp True HDR Plan

## Current evidence

- Windows Advanced Color reports HDR enabled on the active display:
  - `supported=True`
  - `enabled=True`
  - `bpc=10`
  - `encoding=RGB`
- DXGI probe confirms the active output is already in HDR10/PQ space:
  - `DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`
  - `bitsPerColor=10`
  - `maxLuminance=301.8`
  - `maxFullFrameLuminance=301.8`
- Current app renderer is still `WebView2 SDR`; the badge intentionally says `OS HDR ON` rather than claiming the wallpaper itself renders HDR.

## Resource measurements

Latest WebView2 wallpaper measurement with HDR detection:

- `AvgCpuCores=0.2962`
- `AvgWorkingSetMB=715.8`
- `AvgAttachSeconds=0.157`
- `TotalErrorsRecent=0`

Minimal native HDR swapchain prototype:

- Source: `scripts/hdr_native_swapchain_probe.cs`
- Benchmark: `scripts/benchmark_hdr_native_swapchain.ps1`
- Results:
  - `scripts/last-benchmark-hdr-native-active.json`
  - `scripts/last-benchmark-hdr-native-fullscreen.json`
  - `scripts/last-benchmark-hdr-native-swapchain.json`
- D3D adapter: `Intel(R) UHD Graphics 770`
- Feature level: `0xb100`
- Swapchain format: `R10G10B10A2_UNORM`
- Color space: `DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`
- `CheckColorSpaceSupport=1`
- `SetColorSpace1=true`
- Render pass: `ClearRenderTargetView animated HDR10/PQ values`
- Render target view: `true`
- Desktop attached: `true`
- Rejected CPU-generated Nexus HDR scene at 1280x720 internal render size:
  - Active: `AvgCpuCores=0.3232`, `PeakWorkingSetMB=76.6`, `presentCount=73`, `renderCount=73`
  - Fullscreen suspended: `AvgCpuCores=0.1114`, `PeakWorkingSetMB=76.3`, `presentCount=27`, `renderCount=27`, `skippedFrameCount=46`, `suspendCount=1`, `resumeCount=1`
- The CPU-generated scene proves structured HDR rendering, but it is not acceptable as the final renderer because active CPU is worse than WebView2 (`0.3232` vs `0.2962` cores).
- Accepted GPU shader Nexus HDR scene at 1280x720 internal render size:
  - Initial shader active: `AvgCpuCores=0.0661`, `PeakWorkingSetMB=63.0`, `presentCount=73`, `renderCount=73`
  - Enriched Nexus-like shader active: `AvgCpuCores=0.0746`, `PeakWorkingSetMB=63.2`, `presentCount=73`, `renderCount=73`
  - Enriched Nexus-like shader fullscreen suspended: `AvgCpuCores=0.0490`, `PeakWorkingSetMB=62.7`, `presentCount=27`, `renderCount=27`, `skippedFrameCount=46`, `suspendCount=1`, `resumeCount=1`
  - This beats the current WebView2 baseline on CPU and memory while keeping true HDR swapchain output.
- Added lightweight Win32 telemetry gauges to the GPU shader scene:
  - Values currently wired: CPU use, RAM use, and C: storage occupancy.
  - Active: `AvgCpuCores=0.0669`, `PeakWorkingSetMB=75.1`, `presentCount=54`, `renderCount=54`, telemetry updates `12`.
  - Fullscreen suspended: `AvgCpuCores=0.0426`, `PeakWorkingSetMB=65.7`, `presentCount=27`, `renderCount=27`, `skippedFrameCount=46`, `suspendCount=1`, `resumeCount=1`, telemetry updates `6`.
  - Last active telemetry sample: CPU `0.0759`, RAM `0.4300`, SSD occupancy `0.6235`.
  - The gauges remain below the WebView2 baseline; native text/glyph rendering still needs a separate measured step.
- Rejected numeric shader glyph experiment:
  - Visual result was not acceptable compared with the current premium WebView2 UI.
  - Performance also regressed:
    - Before glyphs active: `AvgCpuCores=0.0447`, `PeakWorkingSetMB=63.6`, `presentCount=73`.
    - After glyphs active: `AvgCpuCores=0.0663`, `PeakWorkingSetMB=66.0`, `presentCount=71`.
    - Before glyphs fullscreen: `AvgCpuCores=0.0563`, `PeakWorkingSetMB=65.3`, `presentCount=26`, `skippedFrameCount=47`.
    - After glyphs fullscreen: `AvgCpuCores=0.0934`, `PeakWorkingSetMB=74.6`, `presentCount=26`, `skippedFrameCount=45`.
  - Decision: reverted. Do not migrate to a rough shader demo. The current WebView2 design is the visual reference.
- Fullscreen detection before/after test:
  - Baseline before WinEvent/foreground split:
    - Active: `AvgCpuCores=0.0649`, `PeakWorkingSetMB=63.5`, `presentCount=73`.
    - Fullscreen: `AvgCpuCores=0.0427`, `PeakWorkingSetMB=62.9`, `presentCount=27`, `skippedFrameCount=46`, `suspendCount=1`, `resumeCount=1`.
  - Rejected intermediate state: WinEvent plus slow full fallback only. It reduced active CPU, but fullscreen suspension was later (`presentCount=37`, `skippedFrameCount=36`), so it did not meet the immediate-cut requirement.
  - Accepted state: WinEvent hook plus cheap foreground check every `100 ms`, full `EnumWindows` fallback every `2000 ms`.
    - Active: `AvgCpuCores=0.0266`, `PeakWorkingSetMB=64.5`, `presentCount=72`, `fullscreenForegroundChecks=71`, `fullscreenFallbackScans=4`.
    - Fullscreen: `AvgCpuCores=0.0470`, `PeakWorkingSetMB=65.4`, `presentCount=27`, `skippedFrameCount=46`, `suspendCount=1`, `resumeCount=1`, `fullscreenEvents=1`, `fullscreenForegroundChecks=73`, `fullscreenFallbackScans=4`.
  - Decision: keep the event plus foreground-check split. It preserves immediate suspend/resume behavior in the benchmark while reducing full-window scans from roughly `16` to `4` over an 8-second run.

Fullscreen suspension measurement:

- Active wallpaper: `0.2998` CPU cores
- Suspended under fullscreen app: `0.1108` CPU cores
- CPU reduction: `63%`
- Resume and telemetry request happened in the same second.

Production WebView2 fullscreen-detection experiment:

- Tested porting the native prototype's WinEvent plus `100 ms` foreground check strategy into `DesktopHtmlHost.cs`.
- Baseline before experiment: `AvgCpuCores=0.2914`, `AvgWorkingSetMB=752.4`, `AvgAttachSeconds=0.155`.
- Experiment result after clean rerun: `AvgCpuCores=0.6239`, `AvgWorkingSetMB=784.0`, `AvgAttachSeconds=0.170`.
- Decision: reverted. The strategy is acceptable for the lean native prototype, but too expensive in the current WebView2 production host.
- Rollback confirmation: `AvgCpuCores=0.3330`, `AvgWorkingSetMB=765.2`, `AvgAttachSeconds=0.150`, `TotalErrorsRecent=0`.

Production WebView2 canvas idle optimization:

- Removed a forced central-canvas wake on every telemetry tick when no packet or visible map value change is needed.
- Baseline after removing the visible HDR badge: `AvgCpuCores=0.4994`, `AvgWorkingSetMB=748.8`, `AvgAttachSeconds=0.180`.
- After optimization: `AvgCpuCores=0.3745`, `AvgWorkingSetMB=749.6`, `AvgAttachSeconds=0.180`, `TotalErrorsRecent=0`.
- Measured active CPU reduction: `25.0%`.
- Fullscreen suspend still works:
  - Active: `AvgCpuCores=0.4982`, `WorkingSetMB=784.0`.
  - Fullscreen suspended: `AvgCpuCores=0.1427`, `WorkingSetMB=669.2`.
  - CPU reduction: `71.4%`.
  - Resume and `REQUEST_TELEMETRY` happened in the same second.
- Decision: keep.

Production WebView2 DOM write cache:

- Added small guards around high-frequency SVG/ARIA/bar writes:
  - circular gauge `strokeDashoffset`
  - gauge `aria-valuenow`
  - VRAM bar widths
  - network value HTML
- Removed the dead JS updater for the hidden HDR badge.
- Baseline after canvas idle optimization: `AvgCpuCores=0.3329`, `AvgWorkingSetMB=746.0`, `AvgAttachSeconds=0.170`.
- After DOM cache: `AvgCpuCores=0.2914`, `AvgWorkingSetMB=741.7`, `AvgAttachSeconds=0.170`, `TotalErrorsRecent=0`.
- Measured active CPU reduction: `12.5%`.
- Fullscreen suspend still triggers and resumes immediately:
  - Test 1: active `0.3746`, fullscreen `0.2851`, reduction `23.9%`.
  - Test 2: active `0.2493`, fullscreen `0.1425`, reduction `42.8%`.
  - The second suspended absolute CPU matches the previous good fullscreen measurement; the percent is lower because active CPU is now lower.
- Decision: keep.

Production WebView2 GPU WMI 2s cache:

- Changed GPU WMI performance counter refresh from `1 s` to `2 s`; NVIDIA dGPU telemetry still uses NVML every telemetry tick.
- Before: `scripts/last-benchmark-before-gpu-wmi-2s-cache.json`, `AvgCpuCores=0.5409`, `AvgWorkingSetMB=765.7`, `AvgAttachSeconds=0.175`, `TotalErrorsRecent=0`.
- After: `scripts/last-benchmark-after-gpu-wmi-2s-cache.json`, `AvgCpuCores=0.3329`, `AvgWorkingSetMB=739.3`, `AvgAttachSeconds=0.170`, `TotalErrorsRecent=0`.
- Comparator verdict: `KEEP`, CPU improved by `38.45%`.
- Decision: keep.

Production WebView2 network link-speed scaling:

- Fixed network card scaling to use the real Windows link speed from `NetworkInterface.Speed`.
- Confirmed active adapter: `Intel(R) Ethernet Controller I226-V`, `2.5 Gbps`.
- The card now scales against the link capacity, so `1.6 Mo/s` is treated as about `12.8 Mb/s`, roughly `0.5%` of a `2.5 Gb/s` link.
- Before clean comparison: `scripts/last-benchmark-before-network-link-speed-scale-clean.json`, `AvgCpuCores=0.4992`, `AvgWorkingSetMB=784.7`, `AvgAttachSeconds=0.165`, `TotalErrorsRecent=0`.
- After clean comparison: `scripts/last-benchmark-after-network-link-speed-scale-clean.json`, `AvgCpuCores=0.4162`, `AvgWorkingSetMB=765.2`, `AvgAttachSeconds=0.190`, `TotalErrorsRecent=0`.
- Comparator verdict: `KEEP`, CPU improved by `16.63%`; attach delta `+0.025s` stayed below the `0.05s` rejection threshold.
- Decision: keep.

Rejected production WebView2 broad text-cache experiment:

- Tested extending the DOM cache to most sub-metric `textContent` writes.
- Baseline before broad text-cache: `AvgCpuCores=0.3330`, `AvgWorkingSetMB=735.1`, `TotalErrorsRecent=0`.
- After broad text-cache: `AvgCpuCores=0.4164`, `AvgWorkingSetMB=741.9`, `TotalErrorsRecent=0`.
- Decision: reverted. For these text nodes, the extra JS comparisons cost more than the avoided text writes.
- Current source keeps only the narrower DOM write cache that was previously measured as beneficial.

Rejected production native disk-perf cache experiment:

- Tested caching `Win32_PerfFormattedData_PerfDisk_LogicalDisk` for `2 s` instead of querying disk activity every telemetry tick.
- Baseline before disk cache: `AvgCpuCores=0.4159`, `AvgWorkingSetMB=768.0`, `AvgAttachSeconds=0.153`, `TotalErrorsRecent=0`.
- After disk cache: `AvgCpuCores=0.4715`, `AvgWorkingSetMB=750.8`, `AvgAttachSeconds=0.167`, `TotalErrorsRecent=0`.
- Decision: reverted. CPU and attach time got worse, and the live SSD activity feed should stay responsive.

Rejected production WebView2 adaptive fullscreen-resume polling:

- Tested switching fullscreen detection from `500 ms` to `100 ms` while runtime was suspended, then restoring `500 ms` after resume.
- Normal active baseline: `scripts/last-benchmark-before-adaptive-resume.json`, `AvgCpuCores=0.6656`, `AvgWorkingSetMB=786.3`, `AvgAttachSeconds=0.175`, `TotalErrorsRecent=0`.
- Adaptive polling: `scripts/last-benchmark-after-adaptive-resume.json`, `AvgCpuCores=0.7488`, `AvgWorkingSetMB=777.7`, `AvgAttachSeconds=0.155`, `TotalErrorsRecent=0`.
- Comparator verdict: `REJECT`, CPU regressed by `12.50%`.
- Fullscreen probe data was useful but not enough to justify the active regression: `scripts/last-benchmark-fullscreen-after-adaptive-resume-v2.json` measured `ResumeLatencyMs=261`, while the clean rollback probe measured `ResumeLatencyMs=689`.
- Decision: reverted. Current production keeps the stable `500 ms` fullscreen scan.

## What true HDR requires

Real HDR is not a CSS/glow setting. For this wallpaper, it means replacing or bypassing WebView2 rendering with a native Windows graphics path that owns a HDR-capable swap chain:

- Direct3D 11 or Direct3D 12 device.
- DXGI flip-model swap chain.
- 10-bit or scRGB render target.
- `IDXGISwapChain3::SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020)` for HDR10, or scRGB where appropriate.
- Tone mapping calibrated from `DXGI_OUTPUT_DESC1` luminance values.
- Desktop/WorkerW attachment behavior equivalent to the current host.
- Same fullscreen suspend/resume guard before any heavy rendering work.

## Risk / cost

True HDR is programming work, but it is not free:

- It should reduce RAM compared with WebView2 if the renderer is lean.
- It may increase GPU cost if we recreate the current glass/canvas UI naively.
- It needs a prototype measured against the current WebView2 baseline before migration.
- It cannot reuse the HTML/CSS UI directly; the visuals must be redrawn in Direct2D/Direct3D, or the app becomes a hybrid with WebView2 SDR plus native HDR background layer.
- Visual parity is a hard requirement. A technically HDR renderer that looks worse than the current WebView2 wallpaper is not a valid replacement.

## Recommended next prototype

The first native HDR swapchain probe with a real render pass is complete. Next step is to turn it into a renderer prototype before replacing the app:

1. Continue expanding the GPU shader scene only while it remains below the WebView2 baseline.
2. Keep the lower-cost fullscreen signal strategy: WinEvent hook plus cheap foreground polling, with slow full-window fallback scan.
3. Next native HDR work must start from a faithful recreation of the current premium layout, not from debug-style shader glyphs.
4. Benchmark CPU, working set, GPU process absence, attach time, and resume latency.
5. Only migrate the full UI if the prototype beats or matches the current WebView2 baseline.

Benchmark reliability note:

- `scripts/benchmark_hdr_native_swapchain.ps1` now deletes the old probe executable before compiling and fails on `csc` errors. This avoids accidentally measuring a stale binary.
- `scripts/compare_benchmark_result.ps1` compares before/after JSON outputs from `scripts/benchmark_nexuswpp.ps1` and returns `KEEP`, `REJECT`, or `NEUTRAL` from CPU, RAM, attach time, and recent errors.

Success threshold:

- Startup attach stays under `0.25s`.
- Idle CPU is lower than `0.2962` cores.
- Working set is materially lower than `715.8MB`.
- Fullscreen suspend still cuts CPU by at least `50%`.
- DXGI output reports HDR color space during rendering.
- Visual quality matches or exceeds the current premium WebView2 UI.
