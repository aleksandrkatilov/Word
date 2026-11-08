using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Fonts;
using Pero.Doc.Layout;
using Pero.Doc.Model;
using Pero.Doc.Render;
using static Pero.Doc.Tests.T;

namespace Pero.Doc.Tests;

internal static class RenderTests
{
    public static void Register(string? samples)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cat = FontCatalog.Shared;
        Console.WriteLine($"      шрифтов: {cat.FaceCount} граней, сканирование {sw.ElapsedMilliseconds} мс");

        Test("Шрифты: семейство находится, полужирное начертание подбирается, кириллица есть", () =>
        {
            var r = cat.Resolve(new FontKey("Liberation Sans", true, false))!;
            Assert(r is not null, "нет Liberation Sans");
            Assert(r.Face.Bold && !r.SimulateBold, "не найдено полужирное начертание: " + r.Face);
            Assert(r.Face.HasGlyph('Ж'), "нет кириллицы");
            var arial = cat.Resolve(new FontKey("Arial", false, false))!;
            Assert(arial.Face.Family is "Arial" or "Liberation Sans" or "Arimo", "замена Arial: " + arial.Face.Family);
            var m = new FontMeasurer(cat);
            double w = m.Advance(new FontKey("Arial", false, false), 11, 'W');
            Assert(w > 8 && w < 12, "ширина W при 11 пт: " + w);
            var met = m.Metrics(new FontKey("Arial", false, false), 11);
            Assert(Math.Abs(met.LineSpacing - 12.65) < 0.3, "одинарный интервал Arial 11: " + met.LineSpacing);
        });

        Test("PDF: новый документ с кириллицей, таблицей, картинкой и колонтитулом", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("Отчёт о проверке");
            ed.SelectAll(); ed.ApplyStyle("Heading1");
            ed.Select(Selection.At(ed.Index.DocumentEnd));
            ed.NewParagraph();
            ed.TypeText("Обычный текст с ");
            ed.ToggleBold(); ed.TypeText("полужирным"); ed.ToggleBold();
            ed.TypeText(" и ссылкой ");
            ed.SetLink("https://example.com", "пример");
            ed.NewParagraph();
            ed.InsertImage(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="), "png", 80, 40);
            ed.InsertTable(2, 2);
            ed.TypeText("ячейка");
            ed.OpenHeaderFooter(HfKind.Footer, 0);
            ed.TypeText("Страница "); ed.InsertPageField();
            ed.EnterStory(null);
            var lr = DocumentLayout.Build(ed.Doc, new FontMeasurer(cat));
            string path = Path.Combine(Path.GetTempPath(), "pero-new.pdf");
            PdfExport.Write(lr, Drawer(ed), cat, path, "Проверка");
            Assert(new FileInfo(path).Length > 1000, "PDF пустой");
            Assert(File.ReadAllBytes(path).Take(5).SequenceEqual("%PDF-"u8.ToArray()), "не PDF");
            if (!HasPoppler) return;
            string text = PdfText(path);
            Assert(text.Contains("Отчёт о проверке") && text.Contains("полужирным") && text.Contains("ячейка") && text.Contains("Страница 1"), "в PDF нет текста: " + text);
        });

        if (samples is null) return;

        foreach (var name in new[] { "01-styles-lists-multipage", "02-tables", "03-images-links" })
        {
            Test($"PDF эталона {name}: страницы и текст совпадают с разметкой", () =>
            {
                var doc = DocxReader.Read(Path.Combine(samples, name + ".docx"));
                var t0 = System.Diagnostics.Stopwatch.StartNew();
                var lr = DocumentLayout.Build(doc, new FontMeasurer(cat));
                long layoutMs = t0.ElapsedMilliseconds;
                LayoutTests.CheckCoverage(doc, lr, name);
                string path = Path.Combine(Path.GetTempPath(), $"pero-{name}.pdf");
                var ed = new DocEditor(doc);
                PdfExport.Write(lr, Drawer(ed), cat, path, name);
                Console.WriteLine($"      {name}: {lr.Pages.Count} стр., разметка {layoutMs} мс, PDF {new FileInfo(path).Length / 1024} КБ");
                if (!HasPoppler) return;
                string info = Run("pdfinfo", path);
                Assert(System.Text.RegularExpressions.Regex.IsMatch(info, $@"Pages:\s+{lr.Pages.Count}\b"), "pdfinfo: " + info);
                string text = PdfText(path).Replace("\n", " ").Replace("  ", " ");
                foreach (var p in doc.AllParagraphs().Where(p => p.Text.Trim().Length > 8).Take(15))
                {
                    string words = string.Join(" ", p.Text.Replace('\t', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
                    Assert(text.Contains(words.Replace("￼", "").Trim()), $"в PDF нет «{words}»");
                }
            });
        }
    }

    internal static PageDrawer Drawer(DocEditor ed) => new(FontCatalog.Shared, (img, story) => ed.ImageBytes(img.RelId, story?.PartPath));

    /// <summary>Есть ли poppler (pdftotext/pdfinfo): на Linux-стенде есть, на Windows-стенде CI — нет.</summary>
    private static readonly bool HasPoppler = Probe();

    private static bool Probe()
    {
        try { Run("pdfinfo", "-v"); return true; } catch { return false; }
    }

    private static string PdfText(string path) => HasPoppler ? Run("pdftotext", $"-layout \"{path}\" -") : "";

    private static string Run(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return o;
    }
}
