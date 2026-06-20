using AlctClient.Utils;
using System.IO;
using System.Text.Json;

namespace AlctClient.Core;

public static class UserSettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ALCT", "usersettings.json");
    private static readonly object _saveLock = new();

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception ex)
        {
            Logger.Error("UserSettings", ex);  // corrupt/unreadable file — settings silently reset to defaults
            return new();
        }
    }

    public static void Save(UserSettings settings)
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(settings));
            }
            catch (Exception ex) { Logger.Error("UserSettings", ex); }  // save failed — settings won't persist
        }
    }
}
