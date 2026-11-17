using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace GpuMonitor;

internal sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("release_notes")] string? ReleaseNotes);

internal sealed record UpdateCheckResult(bool Configured, bool UpdateAvailable, Version CurrentVersion, Version? LatestVersion, UpdateManifest? Manifest, string? Error);

internal static class UpdateClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion;
        var url = AppPreferences.Load().UpdateManifestUrl;
        if (string.IsNullOrWhiteSpace(url)) return new(false, false, current, null, null, null);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new(true, false, current, null, null, "The update manifest URL must use HTTPS.");
        try
        {
            var manifest = await Client.GetFromJsonAsync<UpdateManifest>(uri, cancellationToken);
            if (manifest is null || !Version.TryParse(manifest.Version, out var latest))
                return new(true, false, current, null, manifest, "The update manifest is invalid.");
            return new(true, latest > current, current, latest, manifest, null);
        }
        catch (Exception error)
        {
            return new(true, false, current, null, null, error.Message);
        }
    }

    public static bool OpenDownload(UpdateManifest manifest)
    {
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return true;
    }
}
