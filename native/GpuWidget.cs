using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GpuMonitor;

internal sealed class GpuWidget : Form
{
    private const int CompactWidth = 200;
    private const int CompactHeight = 116;
    private const int DetailWidth = 480;
    private const int DetailHeight = 620;
    private const int VisibleProcessRows = 8;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer flashTimer = new() { Interval = 450 };
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip gpuPicker = new();
    private readonly ToolStripMenuItem gpuTrayMenu = new("GPU");
    private GpuData? data;
    private string? error;
    private bool refreshing;
    private bool detailsVisible;
    private bool exiting;
    private int processScroll;
    private DateTime lastTelemetry = DateTime.MinValue;
    private readonly HashSet<string> notifiedAlerts = new();
    private string? dangerMessage;
    private bool flashOn;
    private DateTime lastAlertSound = DateTime.MinValue;
    private List<GpuAdapter> gpuAdapters = new();
    private int selectedGpuIndex;
    private UpdateManifest? availableUpdate;

    private Rectangle CloseButton => new(Width - 34, 6, 28, 28);
    private Rectangle CollapseButton => new(Width - 68, 6, 28, 28);
    private Rectangle GpuSelectorButton => detailsVisible ? new Rectangle(26, 5, Width - 105, 32) : Rectangle.Empty;
    private Rectangle GpuSwitchButton => !detailsVisible && gpuAdapters.Count > 1 ? new Rectangle(145, 4, 20, 22) : Rectangle.Empty;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, int lParam);

    public GpuWidget()
    {
        selectedGpuIndex = AppPreferences.Load().SelectedGpuIndex;
        Text = "GPU Monitor";
        ClientSize = new Size(CompactWidth, CompactHeight);
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(8, 15, 21);
        DoubleBuffered = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open GPU Monitor", null, (_, _) => ShowWidget());
        menu.Items.Add(gpuTrayMenu);
        menu.Items.Add("Check for Updates...", null, async (_, _) => await CheckForUpdatesAsync(true));
        menu.Items.Add("Privacy & Updates...", null, (_, _) => { using var dialog = new PreferencesDialog(); dialog.ShowDialog(this); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit GPU Monitor", null, (_, _) => ExitWidget());
        tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "GPU Monitor", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowWidget();
        timer.Tick += async (_, _) => await RefreshGpuAsync();
        flashTimer.Tick += (_, _) => { flashOn = !flashOn; Invalidate(); };
        Shown += async (_, _) =>
        {
            PlaceBottomRight();
            await LoadGpuAdaptersAsync();
            timer.Start();
            await RefreshGpuAsync();
            if (AppPreferences.Load().AutoUpdate) await CheckForUpdatesAsync(false);
        };
        FormClosing += (_, args) => { if (!exiting) { args.Cancel = true; Hide(); } };
    }

    private void ShowWidget() { if (!Visible) Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront(); }
    private void ExitWidget() { exiting = true; timer.Stop(); tray.Visible = false; Close(); }
    private void PlaceBottomRight()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
    }

    private void ToggleDetails()
    {
        var oldRight = Right;
        var oldBottom = Bottom;
        detailsVisible = !detailsVisible;
        processScroll = 0;
        ClientSize = detailsVisible ? new Size(DetailWidth, DetailHeight) : new Size(CompactWidth, CompactHeight);
        var area = Screen.FromControl(this).WorkingArea;
        Left = Math.Clamp(oldRight - Width, area.Left + 8, area.Right - Width - 8);
        Top = Math.Clamp(oldBottom - Height, area.Top + 8, area.Bottom - Height - 8);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        using var path = RoundRect(new Rectangle(0, 0, Width, Height), detailsVisible ? 20 : 14);
        Region = new Region(path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (CloseButton.Contains(e.Location) || (detailsVisible && CollapseButton.Contains(e.Location)) || GpuSelectorButton.Contains(e.Location) || GpuSwitchButton.Contains(e.Location)) Cursor = Cursors.Hand;
        else if (e.Y < 42) Cursor = Cursors.SizeAll;
        else Cursor = detailsVisible ? Cursors.Default : Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y < 42 && !CloseButton.Contains(e.Location) && !(detailsVisible && CollapseButton.Contains(e.Location)) && !GpuSelectorButton.Contains(e.Location) && !GpuSwitchButton.Contains(e.Location))
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 0x2, 0);
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (CloseButton.Contains(e.Location)) Hide();
        else if (detailsVisible && CollapseButton.Contains(e.Location)) ToggleDetails();
        else if (GpuSelectorButton.Contains(e.Location)) ShowGpuPicker();
        else if (GpuSwitchButton.Contains(e.Location)) SelectNextGpu();
        else if (!detailsVisible && e.Y >= 28) ToggleDetails();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!detailsVisible || data is null) return;
        var rows = data.Throttle.Length > 0 ? 7 : VisibleProcessRows;
        var maximum = Math.Max(0, data.Processes.Count - rows);
        processScroll = Math.Clamp(processScroll + (e.Delta < 0 ? 1 : -1), 0, maximum);
        Invalidate(new Rectangle(12, 278, Width - 24, Height - 278));
    }

    private async Task RefreshGpuAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            data = await Task.Run(() => ReadGpu(selectedGpuIndex));
            error = null;
            UpdateLocalDanger(data);
            if (DateTime.UtcNow - lastTelemetry >= TimeSpan.FromMinutes(1))
            {
                lastTelemetry = DateTime.UtcNow;
                await SendTelemetryAsync(data);
            }
        }
        catch (Exception ex) { error = ex.Message; }
        finally { refreshing = false; Invalidate(); }
    }

    private static GpuData ReadGpu(int gpuIndex)
    {
        var executable = FindGpuTaskman();
        var start = new ProcessStartInfo(executable, $"--json --gpu {Math.Max(0, gpuIndex)}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start gputaskman.");
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(8000)) { process.Kill(true); throw new TimeoutException("GPU data collection timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(errors) ? "Unable to read GPU data." : errors.Trim());

        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var device = root.GetProperty("device");
        var adapter = root.TryGetProperty("adapter", out var adapterData) ? adapterData : default;
        var system = root.GetProperty("system");
        var cpu = system.GetProperty("cpu");
        var ram = system.GetProperty("ram");
        var processes = new List<GpuProcess>();
        if (root.TryGetProperty("processes", out var processList) && processList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in processList.EnumerateArray())
            {
                processes.Add(new GpuProcess(
                    (int)Number(item, "pid"), JsonText(item, "name", "process"), JsonText(item, "kind", "app"),
                    Number(item, "gpu_pct"), Number(item, "vram_bytes"), Number(item, "shared_bytes"),
                    Strings(item, "runtimes"), Strings(item, "model_files").Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray()));
            }
        }
        processes = processes.OrderByDescending(item => item.VramBytes).ThenByDescending(item => item.GpuPercent).ToList();
        return new GpuData(
            JsonText(root, "gpu", "GPU"), JsonText(root, "driver"), JsonText(root, "cuda"), JsonText(root, "backend"), JsonText(root, "memory_note"),
            Number(root, "vram_used"), Number(root, "vram_total"), Number(root, "shared_used"), Number(adapter, "shared_system"), Number(cpu, "pct"), Number(ram, "used"), Number(ram, "total"),
            Number(device, "util_gpu"), Number(device, "util_mem"), OptionalNumber(device, "temp"), OptionalNumber(device, "power_w"), OptionalNumber(device, "power_limit_w"), OptionalNumber(device, "fan"),
            Number(device, "enc"), Number(device, "dec"), Number(device, "sm_clock"), Number(device, "sm_clock_max"), Number(device, "mem_clock"), Number(device, "video_clock"),
            (int)Number(device, "pstate"), (int)Number(device, "pcie_gen"), (int)Number(device, "pcie_width"), Number(device, "pcie_rx_kbs"), Number(device, "pcie_tx_kbs"),
            JsonText(device, "compute_cap"), JsonText(device, "source"), JsonText(device, "driver_model"), Strings(device, "throttle"), processes);
    }

    private async Task SendTelemetryAsync(GpuData value)
    {
        var alerts = await TelemetryClient.SendAsync(new TelemetrySnapshot(
            selectedGpuIndex, value.Name, value.Driver, value.VramUsed, value.VramTotal, value.Utilization, value.MemoryUtilization,
            value.Temperature, value.Power, value.PowerLimit, value.Fan, value.Throttle, value.Processes.Count,
            value.CpuLoad, value.RamUsed, value.RamTotal));
        var activeKeys = alerts.Select(alert => alert.Key).ToHashSet();
        notifiedAlerts.IntersectWith(activeKeys);
        var alert = alerts.FirstOrDefault(item => !notifiedAlerts.Contains(item.Key));
        if (alert is null) return;
        notifiedAlerts.Add(alert.Key);
        tray.ShowBalloonTip(10000, alert.Title, alert.Message,
            alert.Severity == "critical" ? ToolTipIcon.Error : ToolTipIcon.Warning);
    }

    private void UpdateLocalDanger(GpuData value)
    {
        var vramPercent = value.VramTotal > 0 ? value.VramUsed / value.VramTotal * 100 : 0;
        var ramPercent = value.RamTotal > 0 ? value.RamUsed / value.RamTotal * 100 : 0;
        dangerMessage = value.Temperature >= 87 ? $"CRITICAL: GPU {value.Temperature:0}°C"
            : vramPercent >= 97 ? $"CRITICAL: VRAM {vramPercent:0}%"
            : ramPercent >= 97 ? $"CRITICAL: SYSTEM RAM {ramPercent:0}%"
            : null;
        if (dangerMessage is null)
        {
            flashTimer.Stop();
            flashOn = false;
            return;
        }
        if (!flashTimer.Enabled) flashTimer.Start();
        if (!Visible) ShowWidget();
        if (DateTime.UtcNow - lastAlertSound >= TimeSpan.FromSeconds(30))
        {
            lastAlertSound = DateTime.UtcNow;
            SystemSounds.Exclamation.Play();
            tray.ShowBalloonTip(10000, "GPU Monitor Critical Alert", dangerMessage, ToolTipIcon.Error);
        }
    }

    private static double Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.TryGetDouble(out var number) ? number : 0;
    private static double OptionalNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null && value.TryGetDouble(out var number) ? number : double.NaN;
    private static string JsonText(JsonElement element, string name, string fallback = "—") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : fallback;
    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : Array.Empty<string>();

    private static string FindGpuTaskman()
    {
        var candidates = new[] { Environment.GetEnvironmentVariable("GPUTASKMAN_EXE"), Path.Combine(AppContext.BaseDirectory, "gputaskman", "gputaskman.exe"), @"D:\Tools\gputaskman-0.4.2\gputaskman-0.4.2-windows-x64\gputaskman.exe" };
        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)) ?? throw new FileNotFoundException("gputaskman.exe was not found.");
    }

    private async Task LoadGpuAdaptersAsync()
    {
        try
        {
            gpuAdapters = await Task.Run(ReadGpuAdapters);
            if (gpuAdapters.Count == 0) gpuAdapters.Add(new GpuAdapter(0, "GPU 0"));
            if (gpuAdapters.All(item => item.Index != selectedGpuIndex)) selectedGpuIndex = gpuAdapters[0].Index;
            AppPreferences.SaveSelectedGpu(selectedGpuIndex);
            RebuildGpuMenus();
        }
        catch
        {
            gpuAdapters = new List<GpuAdapter> { new(selectedGpuIndex, $"GPU {selectedGpuIndex}") };
            RebuildGpuMenus();
        }
    }

    private static List<GpuAdapter> ReadGpuAdapters()
    {
        var executable = FindGpuTaskman();
        var start = new ProcessStartInfo(executable, "--list-gpus")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to list GPUs.");
        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(8000)) { process.Kill(true); throw new TimeoutException("GPU discovery timed out."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Unable to list GPUs.");
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"^(\d+):\s+(.+)$"))
            .Where(match => match.Success)
            .Select(match => new GpuAdapter(int.Parse(match.Groups[1].Value), Regex.Replace(match.Groups[2].Value.Trim(), @"\s+\([^)]*LUID[^)]*\)$", string.Empty)))
            .OrderBy(item => item.Index).ToList();
    }

    private void RebuildGpuMenus()
    {
        gpuTrayMenu.DropDownItems.Clear();
        gpuPicker.Items.Clear();
        foreach (var adapter in gpuAdapters)
        {
            var trayItem = new ToolStripMenuItem($"GPU {adapter.Index}: {adapter.Name}") { Checked = adapter.Index == selectedGpuIndex };
            trayItem.Click += (_, _) => SelectGpu(adapter.Index);
            gpuTrayMenu.DropDownItems.Add(trayItem);
            var pickerItem = new ToolStripMenuItem($"GPU {adapter.Index}: {adapter.Name}") { Checked = adapter.Index == selectedGpuIndex };
            pickerItem.Click += (_, _) => SelectGpu(adapter.Index);
            gpuPicker.Items.Add(pickerItem);
        }
    }

    private void ShowGpuPicker()
    {
        if (gpuPicker.Items.Count == 0) return;
        gpuPicker.Show(this, new Point(GpuSelectorButton.Left, GpuSelectorButton.Bottom));
    }

    private async void SelectGpu(int index)
    {
        if (selectedGpuIndex == index) return;
        selectedGpuIndex = index;
        AppPreferences.SaveSelectedGpu(index);
        data = null;
        error = null;
        processScroll = 0;
        lastTelemetry = DateTime.MinValue;
        RebuildGpuMenus();
        Invalidate();
        await RefreshGpuAsync();
    }

    private void SelectNextGpu()
    {
        if (gpuAdapters.Count < 2) return;
        var position = gpuAdapters.FindIndex(item => item.Index == selectedGpuIndex);
        var next = gpuAdapters[(position + 1 + gpuAdapters.Count) % gpuAdapters.Count];
        SelectGpu(next.Index);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        var result = await UpdateClient.CheckAsync();
        if (!result.Configured)
        {
            if (interactive) MessageBox.Show(this, $"GPU Monitor v{result.CurrentVersion.ToString(3)}\n\nNo update service has been configured yet.", "GPU Monitor Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (result.Error is not null)
        {
            if (interactive) MessageBox.Show(this, $"Update check failed.\n\n{result.Error}", "GPU Monitor Updates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        availableUpdate = result.UpdateAvailable ? result.Manifest : null;
        Invalidate();
        if (!result.UpdateAvailable)
        {
            if (interactive) MessageBox.Show(this, $"GPU Monitor v{result.CurrentVersion.ToString(3)} is up to date.", "GPU Monitor Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var notes = string.IsNullOrWhiteSpace(result.Manifest?.ReleaseNotes) ? string.Empty : $"\n\n{result.Manifest.ReleaseNotes}";
        var choice = MessageBox.Show(this, $"GPU Monitor v{result.LatestVersion?.ToString(3)} is available.{notes}\n\nOpen the download page?", "GPU Monitor Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (choice == DialogResult.Yes && result.Manifest is not null) UpdateClient.OpenDownload(result.Manifest);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var accent = dangerMessage is not null ? Color.FromArgb(255, 55, 70) : TemperatureColor(data?.Temperature ?? double.NaN);
        var heatAmount = dangerMessage is not null && flashOn ? .92 : .68;
        using var background = new LinearGradientBrush(ClientRectangle, Mix(Color.FromArgb(8, 16, 23), accent, dangerMessage is not null && flashOn ? .65 : .28), Mix(Color.FromArgb(5, 10, 14), accent, heatAmount), 90f);
        g.FillRectangle(background, ClientRectangle);
        using var border = new Pen(Color.FromArgb(225, accent), detailsVisible ? 2f : 1.5f);
        using var outline = RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), detailsVisible ? 19 : 13);
        g.DrawPath(border, outline);
        DrawHeader(g, accent);
        if (error is not null) { DrawCentered(g, error); return; }
        if (data is null) { DrawCentered(g, "Reading GPU data..."); return; }
        if (detailsVisible) DrawDetails(g, data, accent); else DrawCompact(g, data, accent);
        DrawVersion(g);
        if (dangerMessage is not null) DrawDangerBanner(g, dangerMessage);
    }

    private void DrawHeader(Graphics g, Color accent)
    {
        using var font = new Font("Segoe UI", detailsVisible ? 10f : 6.2f, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var dot = new SolidBrush(accent);
        using var line = new Pen(Color.FromArgb(55, Color.White));
        using var icon = new Pen(Color.White, detailsVisible ? 2f : 1.5f);
        var name = data is null ? "GPU MONITOR" : detailsVisible
            ? $"GPU {selectedGpuIndex}: {data.Name} · {data.VramTotal / GiB:0} GB  ▼"
            : $"{data.Name}\n{data.VramTotal / GiB:0} GB";
        g.FillEllipse(dot, detailsVisible ? 16 : 10, detailsVisible ? 16 : 10, detailsVisible ? 8 : 6, detailsVisible ? 8 : 6);
        var titleArea = detailsVisible ? new RectangleF(30, 8, Width - 115, 27) : new RectangleF(22, 1, Width - 58, 27);
        g.DrawString(name, font, white, titleArea);
        g.DrawLine(line, detailsVisible ? 14 : 8, detailsVisible ? 41 : 28, Width - (detailsVisible ? 14 : 8), detailsVisible ? 41 : 28);
        if (detailsVisible)
        {
            g.DrawLine(icon, Width - 60, 17, Width - 53, 24); g.DrawLine(icon, Width - 46, 17, Width - 53, 24);
        }
        else if (gpuAdapters.Count > 1)
        {
            using var switchFont = new Font("Segoe UI Symbol", 7f, FontStyle.Bold);
            using var switchFill = new SolidBrush(Color.FromArgb(80, Color.White));
            using var switchFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.FillEllipse(switchFill, GpuSwitchButton);
            g.DrawString("⇄", switchFont, white, GpuSwitchButton, switchFormat);
        }
        g.DrawLine(icon, Width - 26, detailsVisible ? 14 : 10, Width - 16, detailsVisible ? 24 : 20);
        g.DrawLine(icon, Width - 16, detailsVisible ? 14 : 10, Width - 26, detailsVisible ? 24 : 20);
    }

    private static void DrawCompact(Graphics g, GpuData value, Color accent)
    {
        DrawMetricCircle(g, new Rectangle(20, 36, 64, 64), $"{value.Utilization:0}%", "GPU", accent);
        var memoryUsed = DisplayMemoryUsed(value);
        var memoryTotal = DisplayMemoryTotal(value);
        var vramPercent = memoryTotal > 0 ? memoryUsed / memoryTotal * 100 : 0;
        var memoryText = vramPercent > 0 && vramPercent < 1 ? "<1%" : $"{vramPercent:0}%";
        DrawMetricCircle(g, new Rectangle(116, 36, 64, 64), memoryText, IsIntegrated(value) ? "MEM" : "VRAM", accent);
    }

    private static void DrawMetricCircle(Graphics g, Rectangle bounds, string value, string label, Color accent)
    {
        using var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
        using var fill = new SolidBrush(Color.FromArgb(190, 6, 25, 38));
        using var ring = new Pen(Color.FromArgb(235, accent), 2f);
        using var valueFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 6.2f, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(210, 230, 236));
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var shadowBounds = bounds; shadowBounds.Offset(0, 3);
        g.FillEllipse(shadow, shadowBounds); g.FillEllipse(fill, bounds); g.DrawEllipse(ring, bounds);
        g.DrawString(value, valueFont, white, new Rectangle(bounds.X, bounds.Y + 2, bounds.Width, 40), centered);
        g.DrawString(label, labelFont, muted, new Rectangle(bounds.X, bounds.Y + 39, bounds.Width, 17), centered);
    }

    private void DrawDetails(Graphics g, GpuData value, Color accent)
    {
        using var white = new SolidBrush(Color.White);
        using var muted = new SolidBrush(Color.FromArgb(205, 228, 235));
        using var accentBrush = new SolidBrush(accent);
        using var sectionFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var small = new Font("Segoe UI", 7.2f);
        using var smallBold = new Font("Segoe UI", 7.4f, FontStyle.Bold);
        DrawSummaryCard(g, new Rectangle(18, 56, 104, 70), "TEMPERATURE", double.IsNaN(value.Temperature) ? "N/A" : $"{value.Temperature:0}°C", accent);
        DrawSummaryCard(g, new Rectangle(130, 56, 104, 70), "GPU LOAD", $"{value.Utilization:0}%", accent);
        DrawSummaryCard(g, new Rectangle(242, 56, 104, 70), IsIntegrated(value) ? "GPU MEMORY" : "VRAM", $"{DisplayMemoryUsed(value) / GiB:0.0}/{DisplayMemoryTotal(value) / GiB:0.#} GB", accent);
        DrawSummaryCard(g, new Rectangle(354, 56, 108, 70), "POWER", double.IsNaN(value.Power) ? "N/A" : $"{value.Power:0}/{value.PowerLimit:0} W", accent);

        g.DrawString("DEVICE", sectionFont, muted, 18, 139);
        using var deviceCard = RoundRect(new Rectangle(18, 157, 444, 92), 12);
        using var deviceFill = new SolidBrush(Color.FromArgb(100, 4, 18, 26));
        g.FillPath(deviceFill, deviceCard);
        var ramPercent = value.RamTotal > 0 ? value.RamUsed / value.RamTotal * 100 : 0;
        DrawDeviceLine(g, 168, "DRIVER", $"{value.Driver} · CUDA {value.Cuda} · CC {value.ComputeCapability}", "SYSTEM", $"CPU {value.CpuLoad:0}% · RAM {ramPercent:0}%", muted, white, small, smallBold);
        DrawDeviceLine(g, 193, "CLOCKS", $"SM {value.SmClock:0}/{value.SmClockMax:0} · MEM {value.MemoryClock:0} · VID {value.VideoClock:0} MHz", "ENGINES", $"MEM {value.MemoryUtilization:0}% · ENC {value.Encoder:0}% · DEC {value.Decoder:0}%", muted, white, small, smallBold);
        DrawDeviceLine(g, 218, "STATE", $"FAN {value.Fan:0}% · P{value.PState}", "PCIE", $"Gen{value.PcieGen} x{value.PcieWidth} · RX {value.PcieRx:0} · TX {value.PcieTx:0} KB/s", muted, white, small, smallBold);
        if (value.Throttle.Length > 0) { using var warning = new SolidBrush(Color.FromArgb(255, 218, 120)); g.DrawString($"Throttle: {string.Join(", ", value.Throttle)}", smallBold, warning, 18, 254); }

        var processTitleY = value.Throttle.Length > 0 ? 274 : 258;
        g.DrawString($"GPU MEMORY BY PROCESS  ·  {value.Processes.Count} processes  ·  shared {value.SharedUsed / MiB:0} MB", sectionFont, muted, 18, processTitleY);
        var headerY = processTitleY + 20;
        using var tableHeader = new SolidBrush(Color.FromArgb(95, 4, 18, 26));
        g.FillRectangle(tableHeader, 18, headerY, 444, 22);
        g.DrawString("PROCESS / RUNTIME", smallBold, muted, 28, headerY + 5);
        g.DrawString("GPU", smallBold, muted, 292, headerY + 5);
        g.DrawString("VRAM", smallBold, muted, 345, headerY + 5);
        g.DrawString("SHARED", smallBold, muted, 405, headerY + 5);
        var rowY = headerY + 23;
        var availableRows = value.Throttle.Length > 0 ? 7 : VisibleProcessRows;
        var visible = value.Processes.Skip(processScroll).Take(availableRows).ToArray();
        for (var index = 0; index < visible.Length; index++) DrawProcessRow(g, visible[index], rowY + index * 36, index, accent, white, muted);
        var remaining = Math.Max(0, value.Processes.Count - processScroll - visible.Length);
        var footer = remaining > 0 ? $"Mouse wheel: {remaining} more · showing {processScroll + 1}-{processScroll + visible.Length}" : $"Showing {processScroll + 1}-{processScroll + visible.Length} of {value.Processes.Count}";
        g.DrawString(footer, small, muted, 96, Height - 25);
        g.DrawString("⌃  collapse", smallBold, accentBrush, Width - 90, Height - 25);
    }

    private static readonly double GiB = Math.Pow(1024, 3);
    private static readonly double MiB = Math.Pow(1024, 2);

    private static void DrawSummaryCard(Graphics g, Rectangle bounds, string label, string value, Color accent)
    {
        using var path = RoundRect(bounds, 12);
        using var fill = new SolidBrush(Color.FromArgb(110, 4, 18, 26));
        using var labelFont = new Font("Segoe UI", 6.8f, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var muted = new SolidBrush(Color.FromArgb(200, 225, 232));
        using var white = new SolidBrush(Color.White);
        using var line = new Pen(Color.FromArgb(200, accent), 1.5f);
        g.FillPath(fill, path); g.DrawLine(line, bounds.X + 11, bounds.Bottom - 8, bounds.Right - 11, bounds.Bottom - 8);
        g.DrawString(label, labelFont, muted, bounds.X + 10, bounds.Y + 9); g.DrawString(value, valueFont, white, bounds.X + 10, bounds.Y + 31);
    }

    private static void DrawDeviceLine(Graphics g, int y, string leftLabel, string leftValue, string rightLabel, string rightValue, Brush muted, Brush white, Font font, Font bold)
    {
        g.DrawString(leftLabel, bold, muted, 30, y); g.DrawString(leftValue, font, white, 82, y);
        g.DrawString(rightLabel, bold, muted, 258, y); g.DrawString(rightValue, font, white, 310, y);
    }

    private static void DrawProcessRow(Graphics g, GpuProcess process, int y, int index, Color accent, Brush white, Brush muted)
    {
        using var row = new SolidBrush(Color.FromArgb(index % 2 == 0 ? 82 : 55, 4, 18, 26));
        using var nameFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 6.5f);
        using var valueFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        using var kindBrush = new SolidBrush(Color.FromArgb(210, accent));
        using var trim = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.FillRectangle(row, 18, y, 444, 34);
        g.FillEllipse(kindBrush, 27, y + 12, 9, 9);
        g.DrawString($"{process.Name}  ·  PID {process.Pid}", nameFont, white, new RectangleF(43, y + 3, 238, 15), trim);
        var details = string.Join(" · ", new[] { process.Kind.ToUpperInvariant(), string.Join("/", process.Runtimes), string.Join(", ", process.Models) }.Where(text => !string.IsNullOrWhiteSpace(text)));
        g.DrawString(details, metaFont, muted, new RectangleF(43, y + 19, 238, 12), trim);
        g.DrawString($"{process.GpuPercent:0.0}%", valueFont, white, 290, y + 11);
        g.DrawString($"{process.VramBytes / MiB:0} MB", valueFont, white, 339, y + 11);
        g.DrawString(process.SharedBytes > 0 ? $"{process.SharedBytes / MiB:0} MB" : "—", valueFont, white, 405, y + 11);
    }

    private static Color TemperatureColor(double temperature)
    {
        if (double.IsNaN(temperature)) return Color.FromArgb(65, 150, 205);
        if (temperature <= 40) return Color.FromArgb(44, 177, 235);
        if (temperature <= 55) return Lerp(Color.FromArgb(44, 177, 235), Color.FromArgb(45, 209, 127), (temperature - 40) / 15);
        if (temperature <= 70) return Lerp(Color.FromArgb(45, 209, 127), Color.FromArgb(255, 177, 55), (temperature - 55) / 15);
        if (temperature <= 85) return Lerp(Color.FromArgb(255, 177, 55), Color.FromArgb(255, 73, 73), (temperature - 70) / 15);
        return Color.FromArgb(255, 55, 70);
    }

    private static Color Lerp(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    }
    private static Color Mix(Color from, Color to, double amount) => Lerp(from, to, amount);

    private void DrawCentered(Graphics g, string text)
    {
        using var font = new Font("Segoe UI", detailsVisible ? 9f : 7f);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new Rectangle(12, detailsVisible ? 55 : 34, Width - 24, detailsVisible ? 180 : 68), format);
    }

    private void DrawDangerBanner(Graphics g, string message)
    {
        var height = detailsVisible ? 24 : 14;
        var top = Height - height - 2;
        using var fill = new SolidBrush(Color.FromArgb(flashOn ? 235 : 190, 140, 0, 12));
        using var font = new Font("Segoe UI", detailsVisible ? 8f : 5.5f, FontStyle.Bold);
        using var white = new SolidBrush(Color.White);
        using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.FillRectangle(fill, 2, top, Width - 4, height);
        g.DrawString(message, font, white, new Rectangle(3, top, Width - 6, height), centered);
    }

    private void DrawVersion(Graphics g)
    {
        using var font = new Font("Segoe UI", detailsVisible ? 6.5f : 5.2f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(210, 235, 240));
        var suffix = availableUpdate is null ? string.Empty : " ↑";
        g.DrawString($"v{UpdateClient.CurrentVersion.ToString(3)}{suffix}", font, brush, detailsVisible ? 18 : 7, Height - (detailsVisible ? 25 : 13));
    }

    private static bool IsIntegrated(GpuData value) => value.SharedTotal > value.VramTotal * 4 && value.VramTotal < GiB;
    private static double DisplayMemoryUsed(GpuData value) => IsIntegrated(value) ? value.VramUsed + value.SharedUsed : value.VramUsed;
    private static double DisplayMemoryTotal(GpuData value) => IsIntegrated(value) ? value.VramTotal + value.SharedTotal : value.VramTotal;

    private static GraphicsPath RoundRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90); path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); flashTimer.Dispose(); tray.Dispose(); gpuPicker.Dispose(); } base.Dispose(disposing); }

    private sealed record GpuAdapter(int Index, string Name);
    private sealed record GpuProcess(int Pid, string Name, string Kind, double GpuPercent, double VramBytes, double SharedBytes, string[] Runtimes, string[] Models);
    private sealed record GpuData(
        string Name, string Driver, string Cuda, string Backend, string MemoryNote,
        double VramUsed, double VramTotal, double SharedUsed, double SharedTotal, double CpuLoad, double RamUsed, double RamTotal,
        double Utilization, double MemoryUtilization, double Temperature, double Power, double PowerLimit, double Fan,
        double Encoder, double Decoder, double SmClock, double SmClockMax, double MemoryClock, double VideoClock,
        int PState, int PcieGen, int PcieWidth, double PcieRx, double PcieTx,
        string ComputeCapability, string Source, string DriverModel, string[] Throttle, List<GpuProcess> Processes);
}
