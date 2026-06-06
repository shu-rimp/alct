using Microsoft.Win32;
using System.Diagnostics;

namespace AlctClient.Core;

public static class LanguagePackService
{
    private static readonly Dictionary<string, string> Capabilities = new()
    {
        ["ja-JP"] = "Language.Speech~~~ja-JP~0.0.1.0",
        ["zh-CN"] = "Language.Speech~~~zh-CN~0.0.1.0",
    };

    // DEBUG: set to true to force all packs as uninstalled (remove after testing)
    public static bool ForceUninstalled { get; set; } = false;

    public static Task<bool> IsInstalledAsync(string bcp47) =>
        ForceUninstalled ? Task.FromResult(false) : Task.Run(() => CheckAll(bcp47));

    private static bool CheckAll(string bcp47)
    {
        // Live Captions registers speech packs as AppX packages under AppModel repository.
        // This is the authoritative check: if the AppX package is absent, Live Captions
        // cannot use the language regardless of other speech-related registry entries
        // (Speech_OneCore tokens and CBS packages persist independently and cause false positives).
        const string appxRepo = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        var speechPrefix = $"MicrosoftWindows.Speech.{bcp47}";

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var pkgs = hive.OpenSubKey(appxRepo);
                if (pkgs?.GetSubKeyNames().Any(n => n.StartsWith(speechPrefix, StringComparison.OrdinalIgnoreCase)) == true)
                    return true;
            }
            catch { }
        }

        return false;
    }

}
