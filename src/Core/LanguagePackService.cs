using Microsoft.Win32;

namespace AlctClient.Core;

public static class LanguagePackService
{
    public static Task<bool> IsInstalledAsync(string bcp47) =>
        Task.Run(() => CheckAll(bcp47));

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
