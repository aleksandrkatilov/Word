using System.Diagnostics;
using Pero.Core.Compatibility;
using Pero.Core.Packaging;
using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Fonts;
using Pero.Doc.Layout;
using Pero.Doc.Render;

namespace Pero.Lab;

/// <summary>
/// Проверки собственного ядра на реальных шрифтах Windows (AC-02, AC-06, REL-04, REL-05):
/// открыть → разметить → изменить → сохранить (с проверкой) → открыть снова → сравнить. Плюс замеры времени.
/// </summary>
public static class CoreCheck
{
    public static int Run(string samples, string outDir, Action<string> log)
    {
        Directory.CreateDirectory(outDir);
        var inspector = new PackageInspector(CompatibilityRegistry.CreateDefault());
        var sw = Stopwatch.StartNew();
        var cat = FontCatalog.Shared;
        log($"fonts: {cat.FaceCount} faces in {sw.ElapsedMilliseconds} ms");
        var measurer = new FontMeasurer(cat);
        var rows = new List<string> { "| Файл | Статус | Стр. | Открытие, мс | Разметка, мс | Сохранение, мс | Итог | Примечание |", "|---|---|---|---|---|---|---|---|" };
        int failures = 0;

        var files = Directory.GetFiles(samples, "*.docx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (var f in files)
        {
            string name = Path.GetRelativePath(samples, f);
            string status = "", note = "";
            long openMs = 0, layoutMs = 0, saveMs = 0;
            int pages = 0;
            bool ok = true;
            try
            {
                var report = inspector.Inspect(f);
                status = report.Status.ToString();
                if (report.Status == InspectionStatus.Rejected)
                {
                    note = "отклонён с понятным сообщением: " + report.RejectReason;
                    rows.Add($"| {name} | {status} | – | – | – | – | ✓ | {Md(note)} |");
                    continue;
                }
                sw.Restart();
                var doc = DocxReader.Read(f);
                openMs = sw.ElapsedMilliseconds;
                sw.Restart();
                var lr = DocumentLayout.Build(doc, measurer);
                layoutMs = sw.ElapsedMilliseconds;
                pages = lr.Pages.Count;
                CheckCoverage(doc, lr);

                // круговой обмен без правок
                string plain = Path.Combine(outDir, "rt-" + Path.GetFileName(f));
                SafeSave.Save(doc.SnapshotForSave(), plain, inspector);
                var lossesPlain = DocxFingerprint.FromFile(f).CompareTo(DocxFingerprint.FromFile(plain)).Where(d => d.IsLoss).ToList();
                if (lossesPlain.Count > 0) { ok = false; note += "без правок: " + string.Join("; ", lossesPlain.Select(d => $"{d.Property} {d.Before}→{d.After}")); }

                // правка (только для документов, которые открываются для правки)
                if (report.Status != InspectionStatus.ReadOnlyRecommended)
                {
                    var ed = new DocEditor(doc);
                    ed.TypeText("Проверка правки. ");
                    ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 8)));
                    ed.ToggleBold();
                    string edited = Path.Combine(outDir, "ed-" + Path.GetFileName(f));
                    sw.Restart();
                    SafeSave.Save(ed.Doc.SnapshotForSave(), edited, inspector);
                    saveMs = sw.ElapsedMilliseconds;
                    var after = DocxFingerprint.FromFile(edited);
                    var losses = DocxFingerprint.FromFile(f).CompareTo(after).Where(d => d.IsLoss && d.Property != "Текст").ToList();
                    if (losses.Count > 0) { ok = false; note += " после правки: " + string.Join("; ", losses.Select(d => $"{d.Property} {d.Before}→{d.After}")); }
                    if (!after.Text.Contains("Проверка правки")) { ok = false; note += " правка не сохранилась"; }
                    var reread = DocxReader.Read(edited);
                    if (reread.AllParagraphs().Count() != ed.Doc.AllParagraphs().Count()) { ok = false; note += " число абзацев изменилось"; }
                }
                else note += "только чтение (неподдерживаемое содержимое); ";

                if (Path.GetFileName(f).StartsWith("0") || name.Contains("corpus"))
                {
                    var ed2 = new DocEditor(doc);
                    var drawer = new PageDrawer(cat, (img, story) => doc.ImageBytes(img.RelId, story?.PartPath));
                    PdfExport.Write(lr, drawer, cat, Path.Combine(outDir, Path.ChangeExtension(Path.GetFileName(f), ".pdf")), name);
                    _ = ed2;
                }
                // AC-06: документ до 20 страниц и 5 МБ — открытие и сохранение не дольше 3 с
                if (new FileInfo(f).Length <= 5 * 1024 * 1024 && pages <= 20 && (openMs + layoutMs > 3000 || saveMs > 3000))
                {
                    ok = false;
                    note += $" медленно: открытие {openMs + layoutMs} мс, сохранение {saveMs} мс";
                }
            }
            catch (Exception ex)
            {
                ok = false;
                note += " ошибка: " + ex.Message;
                log($"core {name}: {ex}");
            }
            if (!ok) failures++;
            log($"core {name}: {(ok ? "OK" : "FAIL")} pages={pages} open={openMs} layout={layoutMs} save={saveMs} {note}");
            rows.Add($"| {name} | {status} | {pages} | {openMs} | {layoutMs} | {saveMs} | {(ok ? "✓" : "✗")} | {Md(note.Trim())} |");
        }
        File.WriteAllLines(Path.Combine(outDir, "core-results.md"), rows);
        log($"core: {files.Count} files, failures {failures}");
        return failures;
    }

    private static string Md(string s) => s.Replace('|', '/').Replace('\n', ' ');

    /// <summary>REL-05: каждый символ каждого абзаца размечен ровно один раз, строки не выходят за страницу.</summary>
    private static void CheckCoverage(Pero.Doc.Model.Document doc, LayoutResult lr)
    {
        var index = new ParaIndex(doc);
        var lines = lr.StoryLines(null).GroupBy(l => l.Para).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Start).ToList());
        for (int i = 0; i < index.Count; i++)
        {
            var p = index[i].Paragraph;
            if (!lines.TryGetValue(i, out var ls)) throw new Exception($"абзац {i + 1} не размечен");
            int at = 0;
            foreach (var l in ls) { if (l.Start != at) throw new Exception($"абзац {i + 1}: пропуск текста с позиции {at}"); at = l.End; }
            if (at != p.Length) throw new Exception($"абзац {i + 1}: размечено {at} из {p.Length} символов");
        }
        foreach (var page in lr.Pages)
            foreach (var l in page.Lines)
                if (l.Y < -0.5 || l.Y + l.Height > page.Height + 0.5) throw new Exception($"строка за пределами страницы {page.Index + 1}");
    }
}

/// <summary>AC-07: в сборке нет Syncfusion, DevExpress и других коммерческих редакторских SDK, ключей и активации.</summary>
public static class DepsCheck
{
    private static readonly string[] Forbidden =
        ["Syncfusion", "DevExpress", "Telerik", "Aspose", "GrapeCity", "TXTextControl", "Spire.", "Xceed.Words", "GemBox", "ComponentOne", "Infragistics"];

    public static int Run(string dir, string outDir, Action<string> log)
    {
        var lines = new List<string> { "# Проверка зависимостей (AC-07)", "", $"Папка: `{dir}`", "" };
        int bad = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string n = Path.GetFileName(f);
            if (Forbidden.Any(x => n.Contains(x, StringComparison.OrdinalIgnoreCase))) { bad++; lines.Add($"- ✗ запрещённый файл: {n}"); }
            if (n.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            {
                string text = File.ReadAllText(f);
                foreach (var x in Forbidden.Where(x => text.Contains(x, StringComparison.OrdinalIgnoreCase))) { bad++; lines.Add($"- ✗ {n} ссылается на {x}"); }
                var libs = System.Text.RegularExpressions.Regex.Matches(text, "\"([A-Za-z0-9_.\\-]+)/([0-9][^\"]*)\": \\{\\s*\"type\": \"(package|project)\"")
                    .Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value} ({m.Groups[3].Value})").Distinct().ToList();
                lines.Add($"- {n}: {libs.Count} зависимостей: " + string.Join(", ", libs));
            }
            if (n.Contains("license", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".txt") && File.ReadAllText(f).Contains("Syncfusion", StringComparison.OrdinalIgnoreCase))
            { bad++; lines.Add($"- ✗ лицензионный ключ: {n}"); }
        }
        lines.Add("");
        lines.Add(bad == 0 ? "Итог: ✓ коммерческих редакторских SDK, ключей и активации нет." : $"Итог: ✗ найдено нарушений: {bad}");
        File.WriteAllLines(Path.Combine(outDir, "deps-check.md"), lines);
        log($"deps: {bad} problems");
        return bad == 0 ? 0 : 1;
    }
}
