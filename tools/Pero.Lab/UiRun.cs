using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Pero.Core.Packaging;

namespace Pero.Lab;

/// <summary>Прогон готового Pero.exe: реальные клики и клавиши, скриншоты экрана, проверки файлов.</summary>
public sealed class UiRun(string exe, string samples, string outDir, Action<string> log)
{
    private readonly UIA3Automation _auto = new();
    private readonly List<(string Name, bool Ok, string Note)> _results = [];
    private string Work => Path.Combine(Path.GetTempPath(), "pero-ui");

    public List<(string Name, bool Ok, string Note)> RunAll()
    {
        Directory.CreateDirectory(outDir);
        if (Directory.Exists(Work)) Directory.Delete(Work, true);
        Directory.CreateDirectory(Work);
        string recovery = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Recovery");
        if (Directory.Exists(recovery)) Directory.Delete(recovery, true);

        Scenario("01-empty", () =>
        {
            using var s = Start();
            Shot("ui-01-empty");
            Check(s.Window.Title.Contains("Перо"), $"заголовок «{s.Window.Title}»");
        });

        foreach (var sample in new[] { "01-styles-lists-multipage", "02-tables", "03-images-links" })
            Scenario("open-" + sample, () =>
            {
                using var s = Start(Copy(sample));
                WaitTitle(s.Window, sample);
                Thread.Sleep(2500);
                Shot("ui-open-" + sample);
            });

        Scenario("readonly-04", () =>
        {
            using var s = Start(Copy("04-unsupported"), waitMain: false);
            var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(30));
            Check(dlg is not null, "нет вопроса о режиме только чтения");
            Shot("ui-04-prompt");
            Press(dlg!, "6"); // Да
            var w = s.App.GetMainWindow(_auto, TimeSpan.FromSeconds(30));
            WaitTitle(w, "04-unsupported");
            Thread.Sleep(1500);
            Shot("ui-04-readonly");
            Check(w.Title.Contains("Только чтение"), $"заголовок «{w.Title}»");
        });

        foreach (var bad in new[] { "90-password-or-doc", "91-truncated", "92-zip-slip" })
            Scenario("reject-" + bad, () =>
            {
                using var s = Start(Copy(bad), waitMain: false);
                var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(30));
                Check(dlg is not null, "нет сообщения об ошибке");
                Shot("ui-reject-" + bad);
                Press(dlg!, "2", "1");
            });

        Scenario("edit-save", () =>
        {
            string path = Copy("01-styles-lists-multipage");
            var before = File.GetLastWriteTimeUtc(path);
            using var s = Start(path);
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(2000);
            ClickPage(s.Window);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            Keyboard.Type("Проверка ввода Перо. ");
            Thread.Sleep(800);
            Shot("ui-edit-typed");
            Check(s.Window.Title.Contains("Не сохранено"), $"заголовок после ввода «{s.Window.Title}»");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            WaitUntil(() => File.GetLastWriteTimeUtc(path) > before, TimeSpan.FromSeconds(30));
            Thread.Sleep(1000);
            Shot("ui-edit-saved");
            var fp = DocxFingerprint.FromFile(path);
            Check(fp.Text.Contains("Проверка ввода Перо"), "введённый текст не попал в файл");
            var diffs = DocxFingerprint.FromFile(Path.Combine(samples, "01-styles-lists-multipage.docx")).CompareTo(fp)
                .Where(d => d.IsLoss && d.Property != "Текст").ToList();
            Check(diffs.Count == 0, "потери после правки: " + string.Join("; ", diffs.Select(d => d.Property + ": " + d.Before + " → " + d.After)));
            Check(!Directory.EnumerateFiles(Work, "~$*").Any(), "остался временный файл");
            File.Copy(path, Path.Combine(outDir, "ui-edited-01.docx"), true);
        });

        Scenario("crash-recovery", () =>
        {
            string path = Copy("02-tables");
            var s = Start(path);
            WaitTitle(s.Window, "02-tables");
            Thread.Sleep(2000);
            ClickPage(s.Window);
            Keyboard.Type("Несохранённая правка для восстановления. ");
            Thread.Sleep(40_000); // интервал автовосстановления 30 с
            s.App.Kill();
            s.Dispose();
            Thread.Sleep(1500);
            using var s2 = Start(waitMain: false);
            var dlg = WaitDialog(s2.App.ProcessId, TimeSpan.FromSeconds(30));
            Check(dlg is not null, "нет предложения восстановления");
            Shot("ui-recovery-prompt");
            Press(dlg!, "6");
            Thread.Sleep(4000);
            Shot("ui-recovery-restored");
            var wins = _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(s2.App.ProcessId));
            Check(wins.Any(w => (w.Name ?? "").Contains("02-tables")), "восстановленный документ не открылся: " + string.Join(", ", wins.Select(w => w.Name)));
        });

        Scenario("external-change", () =>
        {
            string path = Copy("03-images-links");
            using var s = Start(path);
            WaitTitle(s.Window, "03-images");
            Thread.Sleep(1500);
            ClickPage(s.Window);
            Keyboard.Type("x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1)); // «другая программа» изменила файл
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(15), exclude: s.Window);
            Check(dlg is not null, "нет предупреждения о внешнем изменении");
            Shot("ui-external-change");
            Press(dlg!, "2"); // Отмена
        });

        log($"UI: {_results.Count(r => r.Ok)} из {_results.Count} сценариев пройдено");
        File.WriteAllLines(Path.Combine(outDir, "ui-results.md"),
            new[] { "| Сценарий | Итог | Примечание |", "|---|---|---|" }
                .Concat(_results.Select(r => $"| {r.Name} | {(r.Ok ? "✓" : "✗")} | {r.Note.Replace('|', '/').Replace('\n', ' ')} |")));
        return _results;
    }

    // ── инфраструктура ───────────────────────────────────────────────────────

    private sealed class Session(Application app, Window window, DialogKiller killer) : IDisposable
    {
        public Application App { get; } = app;
        public Window Window { get; } = window;
        public void Dispose()
        {
            killer.Dispose();
            try { if (!App.HasExited) App.Kill(); } catch { }
            Thread.Sleep(500);
        }
    }

    private Session Start(string? file = null, bool waitMain = true)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        if (file is not null) psi.ArgumentList.Add(file);
        var app = Application.Launch(psi);
        var killer = new DialogKiller(app.ProcessId, log);
        Window window = waitMain ? app.GetMainWindow(_auto, TimeSpan.FromSeconds(60)) : null!;
        window?.Move(0, 0);
        return new Session(app, window!, killer);
    }

    private string Copy(string sample)
    {
        string dst = Path.Combine(Work, sample + ".docx");
        File.Copy(Path.Combine(samples, sample + ".docx"), dst, true);
        return dst;
    }

    private void Scenario(string name, Action body)
    {
        log($"UI scenario {name}");
        try { body(); _results.Add((name, true, "")); }
        catch (Exception ex)
        {
            _results.Add((name, false, ex.Message));
            log($"UI FAIL {name}: {ex}");
            try { Shot("ui-FAIL-" + name); } catch { }
        }
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            try { p.Kill(); } catch { }
        Thread.Sleep(800);
    }

    private static void Check(bool cond, string message) { if (!cond) throw new Exception(message); }

    private void Shot(string name) => Capture.Screen().ToFile(Path.Combine(outDir, name + ".png"));

    private static void ClickPage(Window w)
    {
        var r = w.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + (int)(r.Height * 0.6)));
        Thread.Sleep(300);
    }

    private static void WaitTitle(Window w, string part) =>
        WaitUntil(() => (w.Title ?? "").Contains(part), TimeSpan.FromSeconds(60), () => $"заголовок «{w.Title}» не содержит «{part}»");

    private static void WaitUntil(Func<bool> cond, TimeSpan timeout, Func<string>? message = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout) { if (cond()) return; Thread.Sleep(250); }
        throw new TimeoutException(message?.Invoke() ?? "время ожидания истекло");
    }

    /// <summary>Модальное окно процесса (MessageBox) — верхнего уровня или дочернее главного окна.</summary>
    private AutomationElement? WaitDialog(int pid, TimeSpan timeout, AutomationElement? exclude = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            foreach (var top in _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(pid)))
            {
                if (HasButton(top)) return top;
                foreach (var inner in top.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window)))
                    if (HasButton(inner)) return inner;
            }
            Thread.Sleep(300);
        }
        return null;

        static bool HasButton(AutomationElement e) =>
            e.FindFirstChild(cf => cf.ByAutomationId("6").Or(cf.ByAutomationId("2")).Or(cf.ByAutomationId("1"))) is not null;
    }

    private static void Press(AutomationElement dialog, params string[] ids)
    {
        foreach (var id in ids)
            if (dialog.FindFirstChild(cf => cf.ByAutomationId(id)) is { } b) { b.AsButton().Invoke(); Thread.Sleep(500); return; }
        throw new Exception("в диалоге нет кнопки " + string.Join("/", ids));
    }
}
