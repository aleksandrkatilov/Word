using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FlaUI.Core.Capturing;

namespace Pero.Lab;

public static class Lab
{
    public static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

    public static void Shot(Window w, string path)
    {
        // Снимок экрана целиком: видно и окно, и всплывающие сообщения.
        try { Capture.Screen().ToFile(path); } catch (Exception ex) { File.WriteAllText(path + ".err.txt", ex.ToString()); }
    }

    public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}

public static class Program
{
    private static StreamWriter? _log;

    private static void Log(string s)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {s}";
        Console.WriteLine(line);
        lock (typeof(Program)) _log?.WriteLine(line);
    }

    /// <summary>
    /// pero-lab all &lt;samples&gt; &lt;Pero.exe&gt; &lt;out&gt;  — разведка API, лента, круговой обмен, UI-прогон.
    /// Отдельные команды: api, ribbon, engine, ui.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2) { Console.WriteLine("pero-lab all|api|ribbon|engine|ui <samples> <exe> <out>"); return 64; }
        string cmd = args[0];
        string outDir = args[^1];
        Directory.CreateDirectory(outDir);
        _log = new StreamWriter(Path.Combine(outDir, "lab.log"), append: true) { AutoFlush = true };

        Syncfusion.Telemetry.Telemetry.Disable();
        string? key = Environment.GetEnvironmentVariable("PERO_SYNCFUSION_KEY");
        Log($"license key present: {!string.IsNullOrWhiteSpace(key)}");
        if (!string.IsNullOrWhiteSpace(key)) Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(key);
        Log($"screen {SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight}");

        int failures = 0;
        if (cmd is "all" or "api")
            Safe("api", () => ApiDump.Run(Path.Combine(outDir, "api")), ref failures);

        if (cmd is "all" or "ribbon" or "engine")
        {
            using var killer = new DialogKiller(Environment.ProcessId, Log);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            int wpfFailures = 0;
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (cmd is "all" or "ribbon")
                        try { await RibbonProbe.Run(Path.Combine(outDir, "ribbon"), Log); }
                        catch (Exception ex) { wpfFailures++; Log("ribbon FAILED: " + ex); }
                    if (cmd is "all" or "engine")
                        try
                        {
                            var res = await EngineRoundTrip.Run(args[1], Path.Combine(outDir, "engine"), Log, perfRepeats: 5);
                            Log($"engine: {res.Count} files, losses in {res.Count(r => r.Diffs.Any(d => d.IsLoss))}, errors {res.Count(r => r.Error is not null && r.Status != "Rejected")}");
                        }
                        catch (Exception ex) { wpfFailures++; Log("engine FAILED: " + ex); }
                }
                finally { app.Shutdown(); }
            });
            app.Run();
            failures += wpfFailures;
        }

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
        try { a(); Log($"{name} OK in {sw.ElapsedMilliseconds} ms"); }
        catch (Exception ex) { failures++; Log($"{name} FAILED: {ex}"); }
    }
}
