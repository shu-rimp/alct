using System.IO;

namespace AlctClient.Utils;

public static class Logger
{
    private static readonly string _logPath =
        Path.Combine(AppContext.BaseDirectory, "error.log");
    private static readonly object _lock = new();

    public static void Error(string context, Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex.GetType().Name}: {ex.Message}");
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
