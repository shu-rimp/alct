using AlctClient.Core;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace AlctClient.Utils;

internal static class AssetCache
{
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly string _cacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ALCT", "assets");

    private static readonly string _versionFile = Path.Combine(_cacheDir, "version.txt");

    private static readonly string _currentVersion =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}"
            : "unknown";

    private static string BaseUrl =>
        BuildConstants.ASSETS_BASE_URL.Contains("#{")
            ? BuildConstants.ASSETS_BASE_URL.Replace("#{ALCT_VERSION_TAG}#", "main")
            : BuildConstants.ASSETS_BASE_URL;

    internal static void InvalidateIfVersionChanged()
    {
        try
        {
            if (Directory.Exists(_cacheDir) &&
                (!File.Exists(_versionFile) || File.ReadAllText(_versionFile).Trim() != _currentVersion))
            {
                Directory.Delete(_cacheDir, recursive: true);
            }
            Directory.CreateDirectory(_cacheDir);
            File.WriteAllText(_versionFile, _currentVersion);
        }
        catch { }
    }

    internal static async Task<BitmapImage?> GetImageAsync(string filename)
    {
        var localPath = Path.Combine(_cacheDir, filename);
        if (!File.Exists(localPath))
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                var url = BaseUrl.TrimEnd('/') + "/" + filename;
                var bytes = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);
            }
            catch { return null; }
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(localPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
