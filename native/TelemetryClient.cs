using Microsoft.Win32;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GpuMonitor;

internal sealed record TelemetrySnapshot(
    int GpuIndex, string PciBus, string GpuName, string Driver, double VramUsed, double VramTotal,
    double GpuLoad, double MemoryLoad, double Temperature, double Power, double PowerLimit,
    double Fan, string[] Throttle, int ProcessCount,
    double CpuLoad, double RamUsed, double RamTotal);

internal sealed record HealthAlert(string Key, string Severity, string Title, string Message);

internal static class TelemetryClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static async Task<string?> ReadUuidAsync(string bus)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(bus, @"^(?:[0-9a-fA-F]{4}|[0-9a-fA-F]{8}):[0-9a-fA-F]{2}:[0-9a-fA-F]{2}\.[0-7]$")) return null;
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-i", bus, "--query-gpu=uuid", "--format=csv,noheader" }) info.ArgumentList.Add(arg);
        try {
            using var process = System.Diagnostics.Process.Start(info)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var output = process.StandardOutput.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); return null; }
            var uuid = (await output).Trim();
            return process.ExitCode == 0 && uuid.StartsWith("GPU-", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(uuid[4..], "D", out var id) && id != Guid.Empty ? "GPU-" + id : null;
        } catch (System.ComponentModel.Win32Exception) { return null; }
    }

    public static async Task<IReadOnlyList<HealthAlert>> SendAsync(TelemetrySnapshot sample, CancellationToken cancellationToken = default)
    {
        try { return await SendCoreAsync(sample, cancellationToken); }
        catch (Exception error)
        {
            TryWriteDiagnostic(error.Message);
            return Array.Empty<HealthAlert>();
        }
    }

    private static async Task<IReadOnlyList<HealthAlert>> SendCoreAsync(TelemetrySnapshot sample, CancellationToken cancellationToken)
    {
        var preferences = AppPreferences.Load();
        if (!preferences.AnonymousTelemetry || string.IsNullOrWhiteSpace(preferences.TelemetryEndpoint)) return Array.Empty<HealthAlert>();
        if (!Uri.TryCreate(preferences.TelemetryEndpoint, UriKind.Absolute, out var endpoint)) return Array.Empty<HealthAlert>();
        if (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)) return Array.Empty<HealthAlert>();

        var payload = new
        {
            install_id = preferences.InstallId,
            hardware_id_hash = HardwareIdHash(preferences.InstallId),
            app_version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            consent_version = preferences.ConsentVersion,
            auto_update = preferences.AutoUpdate,
            anonymous_telemetry = true,
            os_version = Environment.OSVersion.VersionString,
            gpu = new
            {
                index = sample.GpuIndex,
                uuid = await ReadUuidAsync(sample.PciBus),
                name = sample.GpuName,
                driver = sample.Driver,
                vram_used_bytes = (long)sample.VramUsed,
                vram_total_bytes = (long)sample.VramTotal,
                load_pct = sample.GpuLoad,
                memory_load_pct = sample.MemoryLoad,
                temperature_c = double.IsFinite(sample.Temperature) ? sample.Temperature : 0,
                power_w = double.IsFinite(sample.Power) ? sample.Power : 0,
                power_limit_w = double.IsFinite(sample.PowerLimit) ? sample.PowerLimit : 0,
                fan_pct = double.IsFinite(sample.Fan) ? sample.Fan : 0,
                throttle = sample.Throttle,
                process_count = sample.ProcessCount
            },
            system = new
            {
                cpu_load_pct = sample.CpuLoad,
                ram_used_bytes = (long)sample.RamUsed,
                ram_total_bytes = (long)sample.RamTotal
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        var key = Environment.GetEnvironmentVariable("GPU_MONITOR_INGEST_KEY");
        if (!string.IsNullOrWhiteSpace(key) && endpoint.Scheme == Uri.UriSchemeHttps &&
            Uri.TryCreate(Environment.GetEnvironmentVariable("GPU_MONITOR_TELEMETRY_ENDPOINT"), UriKind.Absolute, out var pinned) && pinned == endpoint)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        using var response = await Client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Telemetry server returned {(int)response.StatusCode}: {responseBody}");
        using var json = JsonDocument.Parse(responseBody);
        if (!json.RootElement.TryGetProperty("alerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array) return Array.Empty<HealthAlert>();
        return alerts.EnumerateArray().Select(item => new HealthAlert(
            GetText(item, "alert_key"), GetText(item, "severity"), GetText(item, "title"), GetText(item, "message"))).ToArray();
    }

    private static void TryWriteDiagnostic(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GPU Monitor");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "telemetry.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static string GetText(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string HardwareIdHash(string installId)
    {
        var machineGuid = Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", string.Empty)) ?? string.Empty;
        var systemSerial = Convert.ToString(Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "SystemSerialNumber", string.Empty)) ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{machineGuid}|{systemSerial}|{installId}"))).ToLowerInvariant();
    }
}
