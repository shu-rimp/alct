using System.IO;

namespace AlctClient.Utils;

public static class Logger
{
    private static readonly string _logPath = InitLogPath();

    private static string InitLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ALCT");
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, "alct.log");
    }
    private static readonly object _lock = new();

    public static void Info(string context, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] [{context}] {message}{Environment.NewLine}";
        try { lock (_lock) File.AppendAllText(_logPath, line); }
        catch { }
    }

    public static void Warn(string context, Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [WARN] [{context}] {ex.GetType().Name}: {ex.Message}");

        var inner = ex.InnerException;
        int depth = 1;
        while (inner is not null && depth <= 5)
        {
            sb.Append($" | Inner({depth}) {inner.GetType().Name}: {inner.Message}");
            inner = inner.InnerException;
            depth++;
        }
        sb.AppendLine();

        try { lock (_lock) File.AppendAllText(_logPath, sb.ToString()); }
        catch { }
    }

    public static void Error(string context, Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] [{context}] {ex.GetType().Name}: {ex.Message}");
        sb.AppendLine(ex.StackTrace);

        var inner = ex.InnerException;
        int depth = 1;
        while (inner is not null && depth <= 5)
        {
            sb.AppendLine($"  --- Inner({depth}) {inner.GetType().Name}: {inner.Message}");
            sb.AppendLine(inner.StackTrace);
            inner = inner.InnerException;
            depth++;
        }
        sb.AppendLine();

        try { lock (_lock) File.AppendAllText(_logPath, sb.ToString()); }
        catch { }
    }
}
