using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pero.Core.Compatibility;
using Pero.Core.Packaging;
using Syncfusion.Windows.Controls.RichTextBoxAdv;

namespace Pero.Lab;

/// <summary>
/// QA-01 и раздел 6 ТЗ на настоящем движке: загрузить каждый эталон в SfRichTextBoxAdv, дождаться
/// разметки, сохранить, вернуть отделимые части (как делает приложение) и сравнить отпечатки.
/// </summary>
public static class EngineRoundTrip
{
    public sealed record Result(string File, string Status, int Pages, long LoadMs, long SaveMs, List<DocxFingerprint.Difference> Diffs, string? Error);

    public static async Task<List<Result>> Run(string samplesDir, string outDir, Action<string> log, int perfRepeats)
    {
        Directory.CreateDirectory(outDir);
        string work = Path.Combine(Path.GetTempPath(), "pero-lab");
        Directory.CreateDirectory(work);
        var inspector = new PackageInspector(CompatibilityRegistry.CreateDefault());

        var editor = new SfRichTextBoxAdv { LayoutType = LayoutType.Pages, Background = Brushes.Gainsboro };
        var win = new Window { Width = 1280, Height = 900, Content = editor, Title = "engine", Left = 0, Top = 0 };
        win.Show();
        await Lab.Idle();

        var results = new List<Result>();
        foreach (var file in Directory.EnumerateFiles(samplesDir, "*.docx").OrderBy(f => f))
        {
            string name = Path.GetFileName(file);
            var report = inspector.Inspect(file);
            if (report.Status == InspectionStatus.Rejected)
            {
                results.Add(new(name, "Rejected", 0, 0, 0, [], report.RejectReason));
                continue;
            }
            bool perf = name.StartsWith("perf-", StringComparison.Ordinal);
            int repeats = !perf ? 1 : name.Contains("500") ? 1 : perfRepeats;
            var loads = new List<long>();
            var saves = new List<long>();
            int pages = 0;
            List<DocxFingerprint.Difference> diffs = [];
            string? error = null;
            try
            {
                for (int i = 0; i < repeats; i++)
                {
                    var sw = Stopwatch.StartNew();
                    editor.Load(file);
                    pages = await WaitLayout(editor, TimeSpan.FromSeconds(perf ? 180 : 60));
                    loads.Add(sw.ElapsedMilliseconds);

                    string engineOut = Path.Combine(work, "engine-" + name);
                    sw.Restart();
                    editor.Save(engineOut);
                    saves.Add(sw.ElapsedMilliseconds);

                    if (i == 0)
                    {
                        Lab.Shot(win, Path.Combine(outDir, Path.ChangeExtension(name, ".png")));
                        string merged = Path.Combine(work, "merged-" + name);
                        var detached = DetachedParts.Capture(file);
                        using (var input = File.OpenRead(engineOut))
                        using (var output = File.Create(merged))
                        {
                            var pr = SavePipeline.Process(input, output, detached);
                            if (pr.MergeCellsRestored > 0) log($"{name}: EngineFixups восстановил ячеек объединения: {pr.MergeCellsRestored}");
                        }
                        File.Copy(merged, Path.Combine(outDir, "saved-" + name), true);
                        diffs = DocxFingerprint.FromFile(file).CompareTo(DocxFingerprint.FromFile(Watermark.StripIfTrial(merged)));
                        if (i == 0 && name.StartsWith("01")) log($"page size px={editor.Document.Sections.Cast<SectionAdv>().First().SectionFormat.PageSize} margin={editor.Document.Sections.Cast<SectionAdv>().First().SectionFormat.PageMargin}");
                        var re = inspector.Inspect(merged);
                        if (re.Status == InspectionStatus.Rejected) error = "сохранённый файл отклонён: " + re.RejectReason;
                    }
                }
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; log($"{name}: {ex}"); }

            long Median(List<long> v) => v.Count == 0 ? 0 : v.OrderBy(x => x).ElementAt(v.Count / 2);
            results.Add(new(name, report.Status.ToString(), pages, Median(loads), Median(saves), diffs, error));
            if (perf)
                log($"PERF {name}: pages={pages} load median={Median(loads)} p95≈{(loads.Count > 0 ? loads.Max() : 0)} ms; save median={Median(saves)} ms; peakWS={Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024)} MB");
        }
        results.Add(await EditRoundTrip(editor, win, samplesDir, work, outDir, log));
        win.Close();
        WriteReport(results, Path.Combine(outDir, "engine-roundtrip.md"));
        return results;
    }

    /// <summary>
    /// Правка через API движка → сохранение → проверка, что правка в файле и остальное не потеряно:
    /// вставка текста в начало, альбомная ориентация, узкие поля.
    /// </summary>
    private static async Task<Result> EditRoundTrip(SfRichTextBoxAdv editor, Window win, string samplesDir, string work, string outDir, Action<string> log)
    {
        string src = Path.Combine(samplesDir, "01-styles-lists-multipage.docx");
        try
        {
            editor.Load(src);
            await WaitLayout(editor, TimeSpan.FromSeconds(60));
            var start = editor.Document.DocumentStart;
            editor.Selection.Select(start, start);
            editor.Selection.InsertText("ПРАВКА ЧЕРЕЗ API. ");
            var sf0 = editor.Document.Sections.Cast<SectionAdv>().First().SectionFormat;
            var size = sf0.PageSize;
            editor.Selection.Select(editor.Document.DocumentStart, editor.Document.DocumentEnd);
            editor.Selection.SectionFormat.PageSize = new Size(size.Height, size.Width);
            double narrow = 12.7 * 96 / 25.4;
            editor.Selection.SectionFormat.PageMargin = new Thickness(narrow);
            int pages = await WaitLayout(editor, TimeSpan.FromSeconds(60));
            Lab.Shot(win, Path.Combine(outDir, "01-edited-landscape.png"));

            string engineOut = Path.Combine(work, "engine-edit.docx"), merged = Path.Combine(work, "merged-edit.docx");
            editor.Save(engineOut);
            using (var input = File.OpenRead(engineOut))
            using (var output = File.Create(merged))
                SavePipeline.Process(input, output, DetachedParts.Capture(src));
            File.Copy(merged, Path.Combine(outDir, "saved-01-edited.docx"), true);
            var fp = DocxFingerprint.FromFile(Watermark.StripIfTrial(merged));
            var problems = new List<string>();
            if (!fp.Text.Contains("ПРАВКА ЧЕРЕЗ API")) problems.Add("нет вставленного текста");
            var dims = fp.PageSize.Split(' ')[0].Split('x').Select(v => int.TryParse(v, out int n) ? n : 0).ToArray();
            if (dims.Length != 2 || dims[0] <= dims[1]) problems.Add("страница не альбомная: " + fp.PageSize);
            if (!fp.PageMargins.Contains("L72")) problems.Add("поля не узкие: " + fp.PageMargins);
            var losses = DocxFingerprint.FromFile(src).CompareTo(fp).Where(d => d.IsLoss && d.Property is not ("Текст" or "Размер страницы" or "Поля страницы")).ToList();
            problems.AddRange(losses.Select(d => $"{d.Property}: {d.Before} → {d.After}"));
            log($"edit roundtrip: pages={pages} problems={problems.Count} size={fp.PageSize} margins={fp.PageMargins}");
            return new Result("01-edit-via-api", "Ok", pages, 0, 0,
                problems.Select(pr => new DocxFingerprint.Difference("Правка", "", pr, true)).ToList(), null);
        }
        catch (Exception ex)
        {
            log("edit roundtrip FAILED: " + ex);
            return new Result("01-edit-via-api", "Ok", 0, 0, 0, [], ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Ждать, пока число страниц перестанет меняться (разметка завершена).</summary>
    private static async Task<int> WaitLayout(SfRichTextBoxAdv editor, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        int last = -1, stable = 0;
        while (sw.Elapsed < timeout)
        {
            await Lab.Idle();
            int now = editor.PageCount;
            if (now > 0 && now == last) { if (++stable >= 3) return now; }
            else stable = 0;
            last = now;
            await Task.Delay(150);
        }
        return last;
    }

    private static void WriteReport(List<Result> results, string path)
    {
        var sb = new StringBuilder("# Круговой обмен на SfRichTextBoxAdv\n\n| Файл | Проверка | Страниц | Открытие, мс | Сохранение, мс | Итог |\n|---|---|---|---|---|---|\n");
        foreach (var r in results)
        {
            string verdict = r.Error is not null ? "ошибка: " + r.Error
                : r.Status == "Rejected" ? "отклонён до движка"
                : r.Diffs.Any(d => d.IsLoss) ? "ПОТЕРИ" : r.Diffs.Count > 0 ? "без потерь (есть изменения)" : "без различий";
            sb.AppendLine($"| {r.File} | {r.Status} | {r.Pages} | {r.LoadMs} | {r.SaveMs} | {verdict} |");
        }
        sb.AppendLine();
        foreach (var r in results.Where(r => r.Diffs.Count > 0))
        {
            sb.AppendLine($"## {r.File}");
            foreach (var d in r.Diffs) sb.AppendLine($"- {(d.IsLoss ? "✗" : "~")} {d.Property}: {d.Before} → {d.After}");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }
}
