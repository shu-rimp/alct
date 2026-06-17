using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AlctClient.Core;

internal sealed record UpdateInfo(string LatestVersion, string DownloadUrl, string ReleaseNotes);

internal static class UpdateChecker
{
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    }) { Timeout = TimeSpan.FromSeconds(10) };

    internal static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var url = BuildConstants.SERVER_URL.TrimEnd('/') + "/update/version.json";
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("latest_version", out var v) ||
                !root.TryGetProperty("download_url",   out var u) ||
                !root.TryGetProperty("release_notes",  out var n)) return null;

            var latestVersion = v.GetString();
            var downloadUrl   = u.GetString();
            if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(downloadUrl)) return null;

            if (!IsNewer(latestVersion)) return null;
            return new UpdateInfo(latestVersion, downloadUrl, n.GetString() ?? string.Empty);
        }
        catch { return null; }
    }

    private static bool IsNewer(string serverVersion)
    {
        if (!Version.TryParse(serverVersion, out var server)) return false;
        var asm = Assembly.GetExecutingAssembly().GetName().Version;
        if (asm is null) return false;
        var current = new Version(asm.Major, asm.Minor, Math.Max(asm.Build, 0));
        var latest  = new Version(server.Major, server.Minor, Math.Max(server.Build, 0));
        return latest > current;
    }
}
