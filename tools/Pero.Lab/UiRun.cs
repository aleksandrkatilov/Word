using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Pero.Core.Packaging;
using Pero.Doc.Docx;

namespace Pero.Lab;

/// <summary>Прогон готового Pero.exe: реальные клики и клавиши, скриншоты экрана, проверка сохранённых файлов.</summary>
public sealed class UiRun(string exe, string samples, string outDir, Action<string> log)
{
    private readonly UIA3Automation _auto = new();
    private readonly List<(string Name, bool Ok, string Note)> _results = [];
    private readonly List<Session> _sessions = [];
    private static string LocalApp => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private string RecoveryDir => Path.Combine(LocalApp, "Pero", "Recovery");
    private string Work => Path.Combine(Path.GetTempPath(), "pero-ui");
    private long? _coldStart;
    /// <summary>Язык, с которым запускается RWrite (RWRITE_LANG). Сценарии проверяют русский интерфейс; null — без переменной (настройка, по умолчанию английский).</summary>
    private string? _lang = "ru";

    public List<(string Name, bool Ok, string Note)> RunAll()
    {
        Directory.CreateDirectory(outDir);
        if (Directory.Exists(Work)) Directory.Delete(Work, true);
        Directory.CreateDirectory(Work);
        foreach (var d in new[] { RecoveryDir, Path.Combine(LocalApp, "Pero", "Locks") })
            if (Directory.Exists(d)) try { Directory.Delete(d, true); } catch { }

        Scenario("00-english-by-default", () =>
        {
            _lang = null;
            try
            {
                var s = Start(size: (1366, 768));
                Thread.Sleep(1500);
                Shot("ui-00-english-default");
                Check(s.Window.Title.Contains("New document"), $"заголовок «{s.Window.Title}»");
                foreach (var name in new[] { "File", "Edit", "View", "Insert", "Format", "Tools", "Help" })
                    Check(Find(s.Window, name) is not null, $"нет меню «{name}»");
                Check(Find(s.Window, "Файл") is null, "меню на русском");
                foreach (var name in new[] { "Menu", "Undo", "Zoom", "Style", "Font", "Font size", "Bold", "Italic", "Underline" })
                    Check(Find(s.Window, name) is not null, $"нет элемента «{name}»");
            }
            finally { _lang = "ru"; }
        });

        Scenario("01-empty-1366x768", () =>
        {
            var s = Start(size: (1366, 768));
            Thread.Sleep(1500);
            Shot("ui-01-empty-1366x768");
            Check(s.Window.Title.Contains("Новый документ"), $"заголовок «{s.Window.Title}»");
            foreach (var name in new[] { "Файл", "Правка", "Вид", "Вставка", "Формат", "Инструменты", "Справка" })
                Check(Find(s.Window, name) is not null, $"нет меню «{name}»");
            Check(Find(s.Window, "Расширения") is null, "лишнее меню «Расширения»");
            foreach (var name in new[] { "Меню", "Отменить", "Масштаб", "Стиль", "Шрифт", "Размер шрифта", "Полужирный", "Курсив", "Подчёркнутый", "Цвет текста", "Горизонтальная линейка", "Вертикальная линейка" })
                Check(Find(s.Window, name) is not null, $"нет элемента «{name}»");
            Check(Find(s.Window, "Сохранено") is null, "у нового документа статус «Сохранено»");
        });

        Scenario("02-narrow-1024x640", () =>
        {
            var s = Start(size: (1024, 640));
            Thread.Sleep(1500);
            Shot("ui-02-narrow-1024x640");
            foreach (var name in new[] { "Отменить", "Стиль", "Шрифт", "Полужирный" })
                Check(IsOnScreen(s.Window, name), $"основная команда «{name}» не видна в узком окне");
        });

        foreach (var sample in new[] { "01-styles-lists-multipage", "02-tables", "03-images-links", "06-sections" })
            Scenario("open-" + sample, () =>
            {
                var s = Start(Copy(sample));
                WaitTitle(s.Window, sample);
                Thread.Sleep(1500);
                Shot("ui-open-" + sample);
                Check(s.Window.Title.Contains("RWrite"), "заголовок");
            });

        Scenario("readonly-unsupported", () =>
        {
            var s = Start(Copy("04-unsupported"));
            WaitTitle(s.Window, "04-unsupported");
            Thread.Sleep(1200);
            Shot("ui-readonly-04");
            Check(s.Window.Title.Contains("Только чтение"), $"заголовок «{s.Window.Title}»");
            ClickPage(s.Window);
            Keyboard.Type("x");
            Thread.Sleep(500);
            Check(!s.Window.Title.Contains("Есть изменения"), "документ только для чтения изменился");
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

        Scenario("type-format-save", () =>
        {
            string path = Copy("01-styles-lists-multipage");
            var before = File.GetLastWriteTimeUtc(path);
            var s = Start(path);
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(1200);
            ClickPage(s.Window);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            Keyboard.Type("Проверка ввода Перо. ");
            // выделить «Проверка» и сделать полужирным сочетанием Ctrl+B
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            for (int i = 0; i < 8; i++) Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RIGHT);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            Thread.Sleep(500);
            Check(Toggle(s.Window, "Полужирный") == true, "кнопка «Полужирный» не отражает выделение");
            Shot("ui-typed-bold");
            Check(s.Window.Title.Contains("Есть изменения"), $"заголовок после ввода «{s.Window.Title}»");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            WaitUntil(() => File.GetLastWriteTimeUtc(path) > before && FileReady(path), TimeSpan.FromSeconds(30));
            Thread.Sleep(800);
            WaitTitle(s.Window, "Сохранено");
            Shot("ui-saved");
            var fp = DocxFingerprint.FromFile(path);
            Check(fp.Text.Contains("Проверка ввода Перо"), "введённый текст не попал в файл");
            var first = DocxReader.Read(path).AllParagraphs().First(p => p.Length > 0);
            Check(first.Inlines.OfType<Pero.Doc.Model.Run>().First().Props.Bold == true, "полужирный не сохранён");
            var diffs = DocxFingerprint.FromFile(Path.Combine(samples, "01-styles-lists-multipage.docx")).CompareTo(fp)
                .Where(d => d.IsLoss && d.Property != Pero.Core.Text.Loc.L("Текст")).ToList();
            Check(diffs.Count == 0, "потери после правки: " + string.Join("; ", diffs.Select(d => d.Property + ": " + d.Before + " → " + d.After)));
            Check(!Directory.EnumerateFiles(Work, "~$*").Any(), "остался временный файл");
            File.Copy(path, Path.Combine(outDir, "ui-edited-01.docx"), true);
        });

        Scenario("toolbar-style-align-undo", () =>
        {
            string path = Path.Combine(Work, "toolbar.docx");
            var s = Start();
            ClickPage(s.Window);
            Keyboard.Type("Заголовок документа");
            SelectCombo(s.Window, "Стиль", "Заголовок 1");
            Thread.Sleep(400);
            ClickDrop(s.Window, "Выравнивание", "По центру");
            Thread.Sleep(400);
            Shot("ui-toolbar-heading-center");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Thread.Sleep(300);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
            Thread.Sleep(300);
            SaveAs(s, path);
            var p = DocxReader.Read(path).AllParagraphs().First();
            Check(p.Props.StyleId == "Heading1", "стиль не применён: " + p.Props.StyleId);
            Check(p.Props.Align == Pero.Doc.Model.Align.Center, "выравнивание после отмены и повтора: " + p.Props.Align);
        });

        Scenario("rulers-margins-indent", () =>
        {
            string path = Path.Combine(Work, "rulers.docx");
            var s = Start(size: (1366, 768));
            ClickPage(s.Window);
            Keyboard.Type("Абзац для проверки линеек и отступов.");
            Thread.Sleep(500);
            var ruler = Find(s.Window, "Горизонтальная линейка") ?? throw new Exception("нет горизонтальной линейки");
            var r = ruler.BoundingRectangle;
            var view = s.Window.FindFirstDescendant(cf => cf.ByAutomationId("DocumentView"))?.BoundingRectangle ?? throw new Exception("нет области документа");
            // страница A4 при 100 %: ширина 793,7 DIP по центру области документа; левое поле 3 см = 113,4 DIP
            double pageLeft = view.Left + Math.Max(24, (view.Width - 793.7) / 2);
            int y = r.Top + r.Height / 2;
            var from = new System.Drawing.Point((int)Math.Round(pageLeft + 113.4), y);
            var to = new System.Drawing.Point((int)Math.Round(pageLeft + 113.4 + 37.8), y);
            Mouse.Drag(from, to);
            Thread.Sleep(700);
            Shot("ui-ruler-left-margin");
            var vr = Find(s.Window, "Вертикальная линейка") ?? throw new Exception("нет вертикальной линейки");
            var v = vr.BoundingRectangle;
            int vx = v.Left + v.Width / 2;
            // верхнее поле 2 см = 75,6 DIP от верха первой страницы (промежуток 16 DIP)
            var vfrom = new System.Drawing.Point(vx, (int)Math.Round(view.Top + 16 + 75.6));
            Mouse.Drag(vfrom, new System.Drawing.Point(vx, vfrom.Y + 38));
            Thread.Sleep(700);
            Shot("ui-ruler-top-margin");
            SaveAs(s, path);
            var sec = DocxReader.Read(path).Section;
            Check(Math.Abs(sec.MarginLeft - 2268) <= 30, $"левое поле после линейки: {sec.MarginLeft} twips (ожидалось ≈2268)");
            Check(Math.Abs(sec.MarginTop - 1701) <= 30, $"верхнее поле после линейки: {sec.MarginTop} twips (ожидалось ≈1701)");
            // отмена возвращает поле
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
            Thread.Sleep(400);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            Thread.Sleep(2000);
            var sec2 = DocxReader.Read(path).Section;
            Check(sec2.MarginLeft == 1701 && sec2.MarginTop == 1134, $"после отмены поля {sec2.MarginLeft}/{sec2.MarginTop}");
            // отступ первой строки: верхний маркер на +1,25 см
            ClickPage(s.Window);
            int topY = r.Top + 3;
            Mouse.Drag(new System.Drawing.Point((int)Math.Round(pageLeft + 113.4), topY), new System.Drawing.Point((int)Math.Round(pageLeft + 113.4 + 47.2), topY));
            Thread.Sleep(600);
            Shot("ui-ruler-first-line");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            Thread.Sleep(2000);
            var para = DocxReader.Read(path).AllParagraphs().First();
            Check(para.Props.IndFirst is int fi && Math.Abs(fi - 709) <= 40, $"отступ первой строки после линейки: {para.Props.IndFirst}");
            // скрыть линейки через «Вид»
            ClickMenu(s.Window, "Вид", "Горизонтальная линейка");
            ClickMenu(s.Window, "Вид", "Вертикальная линейка");
            Thread.Sleep(500);
            Shot("ui-rulers-hidden");
            Check(!IsOnScreen(s.Window, "Горизонтальная линейка"), "горизонтальная линейка не скрылась");
            ClickMenu(s.Window, "Вид", "Горизонтальная линейка");
            ClickMenu(s.Window, "Вид", "Вертикальная линейка");
        });

        Scenario("table-link-pagenumber", () =>
        {
            string path = Path.Combine(Work, "objects.docx");
            var s = Start();
            ClickPage(s.Window);
            Keyboard.Type("Таблица:");
            ClickMenu(s.Window, "Вставка", "Таблица…");
            var dlg = WaitWindow(s, "Вставить таблицу");
            Keyboard.Type("2");
            Keyboard.Press(VirtualKeyShort.TAB);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type("3");
            Keyboard.Press(VirtualKeyShort.ENTER);
            Thread.Sleep(600);
            Keyboard.Type("A1");
            Keyboard.Press(VirtualKeyShort.TAB);
            Keyboard.Type("B1");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.END);
            Keyboard.Type("Сайт ");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_K);
            WaitWindow(s, "Ссылка");
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type("https://example.com");
            Keyboard.Press(VirtualKeyShort.ENTER);
            Thread.Sleep(500);
            ClickMenu(s.Window, "Вставка", "Номер страницы");
            Thread.Sleep(500);
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Thread.Sleep(500);
            Shot("ui-table-link-footer");
            SaveAs(s, path);
            var fp = DocxFingerprint.FromFile(path);
            Check(fp.Tables == 1 && fp.TableCells == 6, $"таблица: {fp.Tables} / ячеек {fp.TableCells}");
            Check(fp.Hyperlinks == 1, "ссылок " + fp.Hyperlinks);
            var doc = DocxReader.Read(path);
            Check(doc.HeadersFooters.Any(h => h.Kind == Pero.Doc.Model.HfKind.Footer), "нет нижнего колонтитула");
            Check(fp.Text.Contains("A1") && fp.Text.Contains("B1"), "текст ячеек");
            _ = dlg;
        });

        Scenario("image-insert-pdf", () =>
        {
            string png = Path.Combine(Work, "pic.png");
            File.WriteAllBytes(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAFklEQVR42mNk+M/wn4EIwDiqkL4KAcT9GO0U4BxoAAAAAElFTkSuQmCC"));
            string path = Path.Combine(Work, "image.docx");
            string pdf = Path.Combine(Work, "image.pdf");
            var s = Start();
            ClickPage(s.Window);
            Keyboard.Type("Картинка: ");
            ClickMenu(s.Window, "Вставка", "Изображение…");
            Thread.Sleep(1200);
            Keyboard.Type(png);
            Keyboard.Press(VirtualKeyShort.ENTER);
            Thread.Sleep(1000);
            Shot("ui-image");
            SaveAs(s, path);
            Check(DocxFingerprint.FromFile(path).InlineImages == 1, "изображение не сохранено");
            ClickMenu(s.Window, "Файл", "Экспорт PDF…");
            Thread.Sleep(1200);
            Keyboard.Type(pdf);
            Keyboard.Press(VirtualKeyShort.ENTER);
            WaitUntil(() => File.Exists(pdf) && FileReady(pdf), TimeSpan.FromSeconds(30));
            var head = File.ReadAllBytes(pdf).Take(5).ToArray();
            Check(System.Text.Encoding.ASCII.GetString(head) == "%PDF-", "PDF не создан");
            File.Copy(pdf, Path.Combine(outDir, "ui-image.pdf"), true);
        });

        Scenario("find-replace", () =>
        {
            var s = Start(Copy("01-styles-lists-multipage"));
            WaitTitle(s.Window, "01-styles");
            Thread.Sleep(1200);
            ClickPage(s.Window);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
            Thread.Sleep(500);
            Keyboard.Type("Подраздел");
            Thread.Sleep(800);
            Shot("ui-find");
            var count = s.Window.FindFirstDescendant(cf => cf.ByAutomationId("FindCount"))?.Name ?? "";
            Check(count.Contains("Найдено: 5"), $"счётчик поиска «{count}»");
        });

        Scenario("command-search", () =>
        {
            var s = Start();
            ClickPage(s.Window);
            Keyboard.Type("текст");
            var box = Find(s.Window, "Меню") ?? throw new Exception("нет поля «Меню»");
            box.Click();
            Keyboard.Type("полужир");
            Thread.Sleep(600);
            Shot("ui-command-search");
            Keyboard.Press(VirtualKeyShort.ENTER);
            Thread.Sleep(400);
            Check(Toggle(s.Window, "Полужирный") == true, "команда из поиска «Меню» не выполнилась");
        });

        Scenario("close-with-changes", () =>
        {
            var s = Start();
            ClickPage(s.Window);
            Keyboard.Type("несохранённый текст");
            s.Window.Close();
            var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(10));
            Check(dlg is not null, "нет вопроса о сохранении при закрытии");
            Shot("ui-close-prompt");
            Press(dlg!, "2"); // Отмена — окно остаётся
            Thread.Sleep(500);
            Check(!s.App.HasExited, "окно закрылось после «Отмена»");
        });

        Scenario("readonly-file", () =>
        {
            string path = Copy("03-images-links");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                var s = Start(path);
                WaitTitle(s.Window, "03-images");
                Thread.Sleep(800);
                Shot("ui-readonly-file");
                Check(s.Window.Title.Contains("Только чтение"), $"заголовок «{s.Window.Title}»");
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });

        Scenario("two-instances", () =>
        {
            string path = Copy("02-tables");
            var a = Start(path);
            WaitTitle(a.Window, "02-tables");
            var b = Start(path);
            WaitTitle(b.Window, "02-tables");
            Thread.Sleep(800);
            Shot("ui-two-instances");
            Check(b.Window.Title.Contains("Только чтение"), $"второй экземпляр: «{b.Window.Title}»");
        });

        Scenario("external-change", () =>
        {
            string path = Copy("03-images-links");
            var s = Start(path);
            WaitTitle(s.Window, "03-images");
            Thread.Sleep(1000);
            ClickPage(s.Window);
            Keyboard.Type("x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
            var dlg = WaitDialog(s.App.ProcessId, TimeSpan.FromSeconds(15));
            Check(dlg is not null, "нет предупреждения о внешнем изменении");
            Shot("ui-external-change");
            Press(dlg!, "2");
        });

        Scenario("crash-recovery", () =>
        {
            if (Directory.Exists(RecoveryDir)) Directory.Delete(RecoveryDir, true);
            string path = Copy("02-tables");
            var s = Start(path);
            WaitTitle(s.Window, "02-tables");
            Thread.Sleep(1200);
            ClickPage(s.Window);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.HOME);
            Keyboard.Type("Несохранённая правка для восстановления. ");
            Thread.Sleep(800);
            Check(s.Window.Title.Contains("Есть изменения"), $"правка не отмечена: «{s.Window.Title}»");
            Thread.Sleep(36_000); // интервал автовосстановления 30 с
            s.App.Kill();
            s.Dispose();
            Thread.Sleep(1500);
            var s2 = Start(waitMain: false);
            var dlg = WaitDialog(s2.App.ProcessId, TimeSpan.FromSeconds(30));
            Check(dlg is not null, "нет предложения восстановления");
            Shot("ui-recovery-prompt");
            Press(dlg!, "6");
            Thread.Sleep(3000);
            Shot("ui-recovery-restored");
            var wins = _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(s2.App.ProcessId));
            Check(wins.Any(w => (w.Name ?? "").Contains("02-tables")), "восстановленный документ не открылся: " + string.Join(", ", wins.Select(w => w.Name)));
        }, keepRecovery: true);

        log($"PERF cold start to main window: {_coldStart} ms");
        log($"UI: {_results.Count(r => r.Ok)} из {_results.Count} сценариев пройдено");
        File.WriteAllLines(Path.Combine(outDir, "ui-results.md"),
            new[] { "| Сценарий | Итог | Примечание |", "|---|---|---|" }
                .Concat(_results.Select(r => $"| {r.Name} | {(r.Ok ? "✓" : "✗")} | {r.Note.Replace('|', '/').Replace('\n', ' ')} |")));
        return _results;
    }

    // ── инфраструктура ───────────────────────────────────────────────────────

    private sealed class Session(Application app, Window window) : IDisposable
    {
        public Application App { get; } = app;
        public Window Window { get; } = window;
        public void Dispose()
        {
            try { if (!App.HasExited) App.Kill(); } catch { }
            Thread.Sleep(400);
        }
    }

    private Session Start(string? file = null, bool waitMain = true, (int W, int H)? size = null)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        if (file is not null) psi.ArgumentList.Add(file);
        if (_lang is null) psi.Environment.Remove("RWRITE_LANG"); else psi.Environment["RWRITE_LANG"] = _lang;
        var app = Application.Launch(psi);
        Window window = waitMain ? MainWindowOf(app) : null!;
        if (window is not null)
        {
            try
            {
                window.Patterns.Window.PatternOrDefault?.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);
                window.Move(0, 0);
                var (w, h) = size ?? (1366, 768);
                window.Patterns.Transform.PatternOrDefault?.Resize(w, h);
            }
            catch { }
            Thread.Sleep(300);
        }
        var session = new Session(app, window!);
        _sessions.Add(session);
        return session;
    }

    private Window MainWindowOf(Application app)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60))
        {
            var w = _auto.GetDesktop().FindAllChildren(cf => cf.ByProcessId(app.ProcessId))
                .FirstOrDefault(e => (e.Name ?? "").EndsWith(" — RWrite", StringComparison.Ordinal) && e.ControlType == FlaUI.Core.Definitions.ControlType.Window);
            if (w is not null) { _coldStart ??= sw.ElapsedMilliseconds; return w.AsWindow(); }
            Thread.Sleep(200);
        }
        throw new TimeoutException("главное окно RWrite не появилось");
    }

    private string Copy(string sample)
    {
        string dst = Path.Combine(Work, sample + ".docx");
        if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
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
            try { Shot("ui-FAIL-" + name); } catch { }
        }
        foreach (var s in _sessions) s.Dispose();
        _sessions.Clear();
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            try { p.Kill(); } catch { }
        Thread.Sleep(700);
    }

    private static AutomationElement? Find(Window w, string name) => w.FindFirstDescendant(cf => cf.ByName(name));

    private static bool IsOnScreen(Window w, string name)
    {
        var e = Find(w, name);
        if (e is null) return false;
        try { return !e.IsOffscreen && e.BoundingRectangle.Width > 0; } catch { return false; }
    }

    private static bool? Toggle(Window w, string name)
    {
        var e = Find(w, name);
        var t = e?.Patterns.Toggle.PatternOrDefault;
        return t is null ? null : t.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On;
    }

    private void Click(Window w, string name)
    {
        AutomationElement? el = null;
        var sw = Stopwatch.StartNew();
        while (el is null && sw.Elapsed < TimeSpan.FromSeconds(8))
        {
            el = Find(w, name);
            if (el is null) Thread.Sleep(250);
        }
        if (el is null)
        {
            File.WriteAllLines(Path.Combine(outDir, $"uia-tree-{name.Replace('…', '_')}.txt"),
                w.FindAllDescendants().Select(e => $"{e.ControlType} | {e.Name} | {e.AutomationId} | {e.ClassName}"));
            throw new Exception($"не найден элемент «{name}»");
        }
        var r = el.BoundingRectangle;
        if (r.Width > 0) Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        else el.Click();
        Thread.Sleep(300);
    }

    /// <summary>Пункт меню: верхнее меню, затем пункт во всплывающем меню (ищется по всему рабочему столу).</summary>
    private void ClickMenu(Window w, string top, string item)
    {
        Click(w, top);
        Thread.Sleep(400);
        AutomationElement? el = null;
        var sw = Stopwatch.StartNew();
        while (el is null && sw.Elapsed < TimeSpan.FromSeconds(6))
        {
            el = w.FindFirstDescendant(cf => cf.ByName(item).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)))
                 ?? _auto.GetDesktop().FindFirstDescendant(cf => cf.ByName(item).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)));
            if (el is null) Thread.Sleep(250);
        }
        if (el is null) throw new Exception($"нет пункта «{top} → {item}»");
        var r = el.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        Thread.Sleep(400);
    }

    private void ClickDrop(Window w, string button, string item)
    {
        Click(w, button);
        Thread.Sleep(400);
        var el = _auto.GetDesktop().FindFirstDescendant(cf => cf.ByName(item).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.MenuItem)))
                 ?? throw new Exception($"нет пункта «{item}» в меню «{button}»");
        var r = el.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
        Thread.Sleep(300);
    }

    private void SelectCombo(Window w, string combo, string item)
    {
        var c = Find(w, combo)?.AsComboBox() ?? throw new Exception($"нет списка «{combo}»");
        c.Expand();
        Thread.Sleep(400);
        var it = c.Items.FirstOrDefault(i => i.Name == item) ?? throw new Exception($"в списке «{combo}» нет «{item}»");
        it.Select();
        Thread.Sleep(200);
        c.Collapse();
        Thread.Sleep(300);
    }

    private Window WaitWindow(Session s, string title)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            var w = s.App.GetAllTopLevelWindows(_auto).FirstOrDefault(x => x.Title == title)
                    ?? s.Window.FindFirstDescendant(cf => cf.ByName(title).And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window)))?.AsWindow();
            if (w is not null) { Thread.Sleep(300); return w; }
            Thread.Sleep(200);
        }
        throw new Exception($"не открылось окно «{title}»");
    }

    private void SaveAs(Session s, string path)
    {
        if (File.Exists(path)) File.Delete(path);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S);
        Thread.Sleep(1200);
        Keyboard.Type(path);
        Keyboard.Press(VirtualKeyShort.ENTER);
        WaitUntil(() => File.Exists(path) && FileReady(path), TimeSpan.FromSeconds(30), () => "файл не сохранён: " + path);
        Thread.Sleep(800);
    }

    private static void Check(bool cond, string message) { if (!cond) throw new Exception(message); }

    private void Shot(string name) => Capture.Screen().ToFile(Path.Combine(outDir, name + ".png"));

    private static bool FileReady(string path)
    {
        try { using var f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return f.Length > 0; }
        catch { return false; }
    }

    private static void ClickPage(Window w)
    {
        var view = w.FindFirstDescendant(cf => cf.ByAutomationId("DocumentView"));
        var r = view?.BoundingRectangle ?? w.BoundingRectangle;
        Mouse.Click(new System.Drawing.Point(r.Left + r.Width / 2, r.Top + 60));
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
    private AutomationElement? WaitDialog(int pid, TimeSpan timeout)
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
