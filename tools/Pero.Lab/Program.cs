using System.Diagnostics;
using System.Text;

namespace Pero.Lab;

public static class Program
{
    private static StreamWriter? _log;

    public static void Log(string s)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {s}";
        Console.WriteLine(line);
        lock (typeof(Program)) _log?.WriteLine(line);
    }

    /// <summary>
    /// pero-lab all &lt;samples&gt; &lt;Pero.exe&gt; &lt;out&gt; — ядро на эталонах и корпусе, зависимости, UI-прогон.
    /// Отдельные команды: core &lt;samples&gt; &lt;out&gt;, deps &lt;publish-dir&gt; &lt;out&gt;, ui &lt;samples&gt; &lt;exe&gt; &lt;out&gt;.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Проверки рассчитаны на русский интерфейс; английский по умолчанию проверяет сценарий 00 в UiRun.
        Environment.SetEnvironmentVariable("RWRITE_LANG", "ru");
        Pero.Core.Text.Loc.Use("ru");
        if (args.Length < 3) { Console.WriteLine("pero-lab all <samples> <exe> <out> | core <samples> <out> | deps <dir> <out> | ui <samples> <exe> <out>"); return 64; }
        string cmd = args[0];
        string outDir = args[^1];
        Directory.CreateDirectory(outDir);
        _log = new StreamWriter(Path.Combine(outDir, "lab.log"), append: true) { AutoFlush = true };
        Log($"lab {cmd} {string.Join(' ', args.Skip(1))}");

        int failures = 0;
        if (cmd is "all" or "core")
            Safe("core", () => failures += CoreCheck.Run(args[1], Path.Combine(outDir, "core"), Log), ref failures);
        if (cmd is "deps")
            Safe("deps", () => failures += DepsCheck.Run(args[1], outDir, Log), ref failures);
        if (cmd is "all")
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(args[2]))!;
            Safe("deps", () => failures += DepsCheck.Run(dir, outDir, Log), ref failures);
        }
        if (cmd is "all" or "dpi")
            Safe("dpi", () => failures += DpiShots.Run(args[1], Path.Combine(outDir, "ui"), Log), ref failures);
        if (cmd is "all" or "ui")
        {
            string exe = args[2];
            if (File.Exists(exe))
                Safe("ui", () => { var r = new UiRun(exe, args[1], Path.Combine(outDir, "ui"), Log).RunAll(); failures += r.Count(x => !x.Ok); }, ref failures);
            else Log("ui skipped: no exe " + exe);
        }
        Log($"DONE failures={failures}");
        _log.Dispose();
        return failures == 0 ? 0 : 1;
    }

    private static void Safe(string name, Action a, ref int failures)
    {
        var sw = Stopwatch.StartNew();
        try { a(); Log($"{name} finished in {sw.ElapsedMilliseconds} ms"); }
        catch (Exception ex) { failures++; Log($"{name} FAILED: {ex}"); }
    }
}
