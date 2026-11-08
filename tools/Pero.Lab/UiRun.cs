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
    private readonly List<Session> _sessions = [];
    private string RecoveryDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pero", "Recovery");
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
            var s = Start();
            Shot("ui-01-empty");
            Check(s.Window.Title.Contains("Перо"), $"заголовок «{s.Window.Title}»");
        });

        foreach (var sample in new[] { "01-styles-lists-multipage", "02-tables", "03-images-links" })
            Scenario("open-" + sample, () =>
            {
                var s = Start(Copy(sample));
                WaitTitle(s.Window, sample);
                Thread.Sleep(2500);
                Shot("ui-open-" + sample);
            });

        Scenario("readonly-04", () =>
        {
            var s = Start(Copy("04-unsupported"), waitMain: false);
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
                var s = Start(Copy(bad), waitMain: false);
                var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(30));
                Check(dlg is not null, "нет сообщения об ошибке");
                Shot("ui-reject-" + bad);
                Press(dlg!, "2", "1");
            });

        Scenario("edit-save", () =>
        {
            string path = Copy("01-styles-lists-multipage");
            var before = File.GetLastWriteTimeUtc(path);
            var s = Start(path);
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
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            Keyboard.Type("Несохранённая правка для восстановления. ");
            Thread.Sleep(1000);
            Shot("ui-recovery-typed");
            Check(s.Window.Title.Contains("Не сохранено"), $"правка не отмечена: «{s.Window.Title}»");
            Thread.Sleep(40_000); // интервал автовосстановления 30 с
            s.App.Kill();
            s.Dispose();
            Thread.Sleep(1500);
            var s2 = Start(waitMain: false);
            var dlg = WaitDialog(s2.App.ProcessId, TimeSpan.FromSeconds(30));
            Check(dlg is not null, "нет предложения восстановления");
            Shot("ui-recovery-prompt");
            Press(dlg!, "6");
            Thread.Sleep(4000);
            Shot("ui-recovery-restored");
            var wins = _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(s2.App.ProcessId));
            Check(wins.Any(w => (w.Name ?? "").Contains("02-tables")), "восстановленный документ не открылся: " + string.Join(", ", wins.Select(w => w.Name)));
        });

        Scenario("backstage-file", () =>
        {
            var s = Start(Copy("01-styles-lists-multipage"));
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(2500);
            ClickByName(s.Window, "Файл");
            Thread.Sleep(1500);
            Shot("ui-file-info");
            ClickByName(s.Window, "Открыть");
            Thread.Sleep(1000);
            Shot("ui-file-open");
            ClickByName(s.Window, "Параметры");
            Thread.Sleep(1000);
            Shot("ui-file-options");
            var all = string.Join("|", s.Window.FindAllDescendants().Select(e => e.Name).Where(n => !string.IsNullOrEmpty(n)));
            Check(!all.Contains("Зашифровать") && !all.Contains("Ограничить редактирование"), "в меню «Файл» остались команды вне P0");
        });

        Scenario("layout-tab", () =>
        {
            string path = Copy("01-styles-lists-multipage");
            var s = Start(path);
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(2500);
            bool byName = SelectRibbonTab(s.Window, steps: 2, expectButton: "Альбомная");
            Thread.Sleep(800);
            Shot("ui-layout-tab");
            // Кнопки добавленной вкладки движок не отдаёт в UI Automation — тогда щелчок по месту
            // кнопки (тема Windows11Light, окно в 0,0). Результат проверяется по сохранённому файлу.
            var wr = s.Window.BoundingRectangle;
            if (byName) ClickByName(s.Window, "Альбомная"); else Mouse.Click(new System.Drawing.Point(wr.Left + 330, wr.Top + 100));
            Thread.Sleep(2500);
            Shot("ui-layout-landscape");
            wr = s.Window.BoundingRectangle;
            if (byName) ClickByName(s.Window, "Узкие"); else Mouse.Click(new System.Drawing.Point(wr.Left + 55, wr.Top + 101));
            Thread.Sleep(1500);
            Shot("ui-layout-narrow");
            var before = File.GetLastWriteTimeUtc(path);
            ClickPage(s.Window); // фокус обратно в документ
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            WaitUntil(() => File.GetLastWriteTimeUtc(path) > before && FileReady(path), TimeSpan.FromSeconds(30));
            Thread.Sleep(1500);
            var fp = DocxFingerprint.FromFile(path);
            var dims = fp.PageSize.Split(' ')[0].Split('x').Select(v => int.TryParse(v, out int n) ? n : 0).ToArray();
            Check(dims.Length == 2 && dims[0] > dims[1], "после «Альбомная» размер страницы " + fp.PageSize);
            Check(fp.PageMargins.Contains("L720") || fp.PageMargins.Contains("L719") || fp.PageMargins.Contains("L721"), "поля «Узкие» не сохранились: " + fp.PageMargins);
        });

        Scenario("nav-search", () =>
        {
            var s = Start(Copy("01-styles-lists-multipage"));
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(2500);
            ClickPage(s.Window);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Thread.Sleep(800);
            Keyboard.Type("Подраздел");
            Thread.Sleep(2000);
            Shot("ui-nav-search");
            var names = string.Join("|", s.Window.FindAllDescendants().Select(e => e.Name ?? ""));
            Check(names.Contains("Найдено: 5"), "нет «Найдено: 5» в области навигации");
        });

        Scenario("external-change", () =>
        {
            string path = Copy("03-images-links");
            var s = Start(path);
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

        log($"PERF cold start to main window (first launch): {_coldStart} ms");
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
        Window window = waitMain ? MainWindowOf(app) : null!;
        if (window is not null) try { window.Move(0, 0); } catch { }
        var session = new Session(app, window!, killer);
        _sessions.Add(session);
        return session;
    }

    /// <summary>Главное окно «Пера» (заголовок с «Перо»), а не всплывающее окно лицензии движка.</summary>
    private Window MainWindowOf(Application app)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            var w = _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(app.ProcessId))
                .FirstOrDefault(e => (e.Name ?? "").EndsWith("Перо", StringComparison.Ordinal) && e.ControlType == FlaUI.Core.Definitions.ControlType.Window);
            if (w is not null) { _coldStart ??= sw.ElapsedMilliseconds; return w.AsWindow(); }
            Thread.Sleep(200);
        }
        throw new TimeoutException("главное окно «Перо» не появилось");
    }

    private long? _coldStart;

    private string Copy(string sample)
    {
        string dst = Path.Combine(Work, sample + ".docx");
        File.Copy(Path.Combine(samples, sample + ".docx"), dst, true);
        return dst;
    }

    private void Scenario(string name, Action body, bool keepRecovery = false)
    {
        log($"UI scenario {name}");
        if (!keepRecovery && Directory.Exists(RecoveryDir)) try { Directory.Delete(RecoveryDir, true); } catch { }
        try { body(); _results.Add((name, true, "")); }
        catch (Exception ex)
        {
            _results.Add((name, false, ex.Message));
            log($"UI FAIL {name}: {ex.Message}");
            try { Shot("ui-FAIL-" + name); } catch { }   // снимок до закрытия приложения
        }
        foreach (var s in _sessions) s.Dispose();
        _sessions.Clear();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            try { p.Kill(); } catch { }
        Thread.Sleep(800);
    }

    /// <summary>
    /// Движок отдаёт в UI Automation только выбранную вкладку ленты. Переход: щелчок по «Главная»
    /// и стрелки вправо (проверка клавиатурной навигации), запасной путь — щелчок по строке вкладок.
    /// </summary>
    private bool SelectRibbonTab(Window w, int steps, string expectButton)
    {
        var home = w.FindFirstDescendant(cf => cf.ByAutomationId("PART_HomeTab")) ?? throw new Exception("нет вкладки «Главная»");
        var r = home.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + 12));
        Thread.Sleep(300);
        for (int i = 0; i < steps; i++) { Keyboard.Press(VirtualKeyShort.RIGHT); Thread.Sleep(250); }
        if (WaitFor(w, expectButton, 2000)) { log("ribbon tab selected by keyboard"); return true; }
        File.WriteAllLines(Path.Combine(outDir, "uia-tree-after-keys.txt"),
            w.FindAllDescendants().Select(e => $"{e.ControlType} | {e.Name} | {e.AutomationId} | {e.ClassName} | {e.BoundingRectangle}"));
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        // ширина заголовка вкладки ≈ 72 px в теме Windows11Light
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2 + 72 * steps, r.Top + 12));
        bool found = WaitFor(w, expectButton, 3000);
        log($"ribbon tab selected by mouse; buttons visible to UIA: {found}");
        return found;
    }

    private static bool WaitFor(Window w, string name, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            if (w.FindFirstDescendant(cf => cf.ByName(name)) is not null) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private static bool FileReady(string path)
    {
        try { using var f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return f.Length > 0; }
        catch { return false; }
    }

    private void ClickByName(Window w, string name)
    {
        AutomationElement? el = null;
        var sw = Stopwatch.StartNew();
        while (el is null && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            el = w.FindFirstDescendant(cf => cf.ByName(name));
            if (el is null) Thread.Sleep(300);
        }
        if (el is null)
        {
            // для разбора: что видно через UI Automation
            File.WriteAllLines(Path.Combine(outDir, $"uia-tree-{name}.txt"),
                w.FindAllDescendants().Select(e => $"{e.ControlType} | {e.Name} | {e.AutomationId} | {e.ClassName}"));
            throw new Exception($"не найден элемент «{name}»");
        }
        var r = el.BoundingRectangle;
        if (r.Width > 0) Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        else el.Click();
        Thread.Sleep(300);
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
