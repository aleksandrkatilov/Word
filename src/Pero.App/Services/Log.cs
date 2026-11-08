using System.IO;

namespace Pero.Services;

/// <summary>
/// Локальный журнал диагностики. По SEC-01 в журнал не пишется текст документа:
/// только события, имена операций, длительности и типы ошибок.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Logs",
            $"pero-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string operation, Exception ex) =>
        Write("ERROR", $"{operation}: {ex.GetType().FullName} HResult=0x{ex.HResult:X8}\n{ex.StackTrace}");

    /// <summary>Замер для отчёта производительности этапа 0.</summary>
    public static IDisposable Measure(string operation) => new Timer(operation);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private sealed class Timer(string operation) : IDisposable
    {
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        public void Dispose() => Info($"PERF {operation} {_sw.ElapsedMilliseconds} ms, WS={Environment.WorkingSet / (1024 * 1024)} MB");
    }
}
