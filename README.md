# GPU Monitor

GPU Monitor is a native Windows widget that displays live GPU temperature, utilization, VRAM, and power usage.

## Native architecture

- The shipping application is .NET WinForms; it does not use Tauri, WebView2, Vite, localhost, or a browser engine.
- Borderless 200 × 116 always-on-top compact card (about 60% of the previous dimensions).
- Compact view shows only GPU workload and VRAM percentages.
- Multi-GPU systems can switch adapters from the compact button, expanded header, or tray menu; the selection is remembered.
- Integrated GPUs use shared GPU memory when dedicated VRAM is not meaningful, and unsupported sensor values are shown as `N/A`.
- The installed version is visible at the lower-left corner. Updates are downloaded in-app, verified with SHA-256, installed silently, and restart the widget.
- Clicking the card expands it to 480 × 620 with temperature, power, clocks, driver/CUDA, PCIe, engine utilization, and a scrollable per-process GPU-memory table.
- The whole card changes color with temperature: blue → green → orange → red.
- Hidden from the taskbar with an Open/Exit tray menu.
- Drag the title area to move it.
- Refreshes GPU data every two seconds.
- `gputaskman` runs with `CreateNoWindow=true`, so no console appears.

## Build

```powershell
## Place the portable gputaskman 0.4.2 distribution at:
## src-tauri\resources\gputaskman\gputaskman.exe
Copy-Item src-tauri\icons\icon.ico native\gpu-monitor.ico -Force
dotnet publish native\GpuMonitor.csproj -c Release -r win-x64 --self-contained true -o native\publish
& "$env:LOCALAPPDATA\tauri\NSIS\Bin\makensis.exe" installer\gpu-monitor.nsi
```

The NSIS installer includes the self-contained .NET Desktop runtime and the portable gputaskman 0.4.2 runtime. End users do not need a browser, .NET, Python, or gputaskman installed separately.

The installer includes a single English consent page with update checks and anonymous telemetry enabled by default. Both options can be disabled before installation or changed later from **Privacy & Updates** in the tray menu.

## Fleet health dashboard

`telemetry-server` contains a private SQLite telemetry receiver and responsive dashboard. The widget sends one anonymous health heartbeat per minute only when telemetry consent is enabled and an HTTPS endpoint is configured. Loopback HTTP is allowed for local development.

The health engine warns on sustained GPU temperature, VRAM pressure, system RAM pressure, fan anomalies, and GPU throttling. Critical local conditions (GPU ≥87°C, VRAM ≥97%, or system RAM ≥97%) make the widget flash red, play a Windows alert sound, and display the cause.

The dashboard is available at the telemetry server root URL and shows live fleet status, open alerts, GPU specifications, current health metrics, and 24-hour per-device history.

The former Tauri source remains only as build history and is not included in the 0.7.0 installer.
