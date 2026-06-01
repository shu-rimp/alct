using System.IO;

namespace AlctClient.Utils;

public static class Logger
{
    private static readonly string _logPath =
        Path.Combine(AppContext.BaseDirectory, "error.log");
    private static readonly object _lock = new();

    public static void Error(string context, Exception ex)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
        try
        {
            lock (_lock)
                File.AppendAllText(_logPath, entry);
        }
        catch { }
    }
}
