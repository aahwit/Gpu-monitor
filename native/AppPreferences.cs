using Microsoft.Win32;

namespace GpuMonitor;

internal sealed record Preferences(
    bool AutoUpdate, bool AnonymousTelemetry, string InstallId, int ConsentVersion,
    string TelemetryEndpoint, string UpdateManifestUrl, int SelectedGpuIndex);

internal static class AppPreferences
{
    private const string RegistryPath = @"Software\KENRI\GPU Monitor";

    public static Preferences Load()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        var autoUpdate = ReadBoolean(key, "AutoUpdate", true);
        var telemetry = ReadBoolean(key, "AnonymousTelemetry", true);
        var consentVersion = Convert.ToInt32(key.GetValue("ConsentVersion", 1));
        var installId = Convert.ToString(key.GetValue("InstallId"));
        if (string.IsNullOrWhiteSpace(installId))
        {
            installId = Guid.NewGuid().ToString("D");
            key.SetValue("InstallId", installId, RegistryValueKind.String);
        }
        var endpoint = Environment.GetEnvironmentVariable("GPU_MONITOR_TELEMETRY_ENDPOINT")
            ?? Convert.ToString(key.GetValue("TelemetryEndpoint"))
            ?? string.Empty;
        var updateManifestUrl = Environment.GetEnvironmentVariable("GPU_MONITOR_UPDATE_MANIFEST_URL")
            ?? Convert.ToString(key.GetValue("UpdateManifestUrl"))
            ?? string.Empty;
        var selectedGpuIndex = Math.Max(0, Convert.ToInt32(key.GetValue("SelectedGpuIndex", 0)));
        return new Preferences(autoUpdate, telemetry, installId, consentVersion, endpoint, updateManifestUrl, selectedGpuIndex);
    }

    public static void Save(bool autoUpdate, bool telemetry)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("AutoUpdate", autoUpdate ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("AnonymousTelemetry", telemetry ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("ConsentVersion", 1, RegistryValueKind.DWord);
    }

    public static void SaveSelectedGpu(int index)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("SelectedGpuIndex", Math.Max(0, index), RegistryValueKind.DWord);
    }

    private static bool ReadBoolean(RegistryKey key, string name, bool fallback)
    {
        var value = key.GetValue(name);
        return value is null ? fallback : Convert.ToInt32(value) != 0;
    }
}
