using Pero.Core.Packaging;
using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using static Pero.Doc.Tests.T;

namespace Pero.Doc.Tests;

internal static class EditTests
{
    private static Model.Document SaveReload(Model.Document doc, out byte[] bytes)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        bytes = ms.ToArray();
        return DocxReader.Read(new MemoryStream(bytes));
    }

    private static Model.Document SaveReload(Model.Document doc) => SaveReload(doc, out _);

    // 1×1 PNG
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static void Register(string? samples)
    {
        Test("Новый документ: ввод, Enter, полужирный, отмена и повтор", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("Привет");
            ed.TypeText(", мир");
            Eq("Привет, мир", ed.P(0).Text, "текст");
            Eq(1, CountUndo(ed), "ввод подряд — один шаг отмены");
            ed.NewParagraph();
            ed.TypeText("Вторая строка");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 6)));
            ed.ToggleBold();
            Eq(true, ed.GetFormatState().Bold, "жирный на выделении");
            ed.Undo();
            Eq(false, ed.GetFormatState().Bold, "после отмены не жирный");
            ed.Redo();
            var re = SaveReload(ed.Doc);
            var p0 = re.AllParagraphs().First();
            Eq("Привет, мир", p0.Text, "текст после сохранения");
            Eq(true, ((Run)p0.Inlines[0]).Props.Bold, "жирный после сохранения");
            Eq("Привет", ((Run)p0.Inlines[0]).Text, "граница жирного");
            Eq("Вторая строка", re.AllParagraphs().ElementAt(1).Text, "второй абзац");
        });

        Test("Backspace и Delete сливают абзацы; удаление через несколько абзацев", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("один\nдва\nтри");
            Eq(3, ed.Index.Count, "три абзаца");
            ed.Select(Selection.At(new DocPos(1, 0)));
            ed.Backspace();
            Eq("одиндва", ed.P(0).Text, "слияние Backspace");
            ed.Select(Selection.At(new DocPos(0, 7)));
            ed.DeleteForward();
            Eq("одиндватри", ed.P(0).Text, "слияние Delete");
            ed.Undo(); ed.Undo();
            ed.Select(new Selection(new DocPos(0, 2), new DocPos(2, 1)));
            ed.DeleteSelection();
            Eq("одри", ed.P(0).Text, "удаление через абзацы");
            Eq(1, ed.Index.Count, "один абзац");
        });

        Test("Shift+Enter и Ctrl+Enter сохраняются как разрывы", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("а"); ed.LineBreak(); ed.TypeText("б"); ed.PageBreak(); ed.TypeText("в");
            var re = SaveReload(ed.Doc);
            var kinds = re.AllParagraphs().First().Inlines.OfType<Break>().Select(b => b.Kind).ToList();
            Eq("Line,Page", string.Join(",", kinds), "разрывы");
        });

        Test("Шрифт, размер, цвет, маркер, выравнивание, интервал, стиль — сохраняются", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("Заголовок"); ed.NewParagraph(); ed.TypeText("Текст абзаца");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 9)));
            ed.ApplyStyle("Heading1");
            ed.Select(new Selection(new DocPos(1, 0), new DocPos(1, 5)));
            ed.SetFont("Times New Roman"); ed.SetSize(14); ed.SetColor("C00000"); ed.SetHighlight("yellow");
            ed.SetAlign(Align.Center); ed.SetLineSpacing(360);
            var st = ed.GetFormatState();
            Eq("Times New Roman", st.Font, "шрифт в состоянии");
            Eq(14.0, st.SizePt, "размер в состоянии");
            var re = SaveReload(ed.Doc);
            var ps = re.AllParagraphs().ToList();
            Eq("Heading1", ps[0].Props.StyleId, "стиль");
            var r = (Run)ps[1].Inlines[0];
            Eq("Times New Roman", r.Props.Font, "шрифт");
            Eq(28, r.Props.SizeHalfPt, "размер");
            Eq("C00000", r.Props.Color, "цвет");
            Eq("yellow", r.Props.Highlight, "маркер");
            Eq(Align.Center, ps[1].Props.Align, "выравнивание");
            Eq(360, ps[1].Props.Line, "интервал");
            Eq("Текст", r.Text, "граница оформления");
        });

        Test("Шрифт темы заменяется явным: атрибуты темы снимаются", () =>
        {
            var doc = Model.Document.CreateBlank();
            var p = doc.AllParagraphs().First();
            var fonts = new System.Xml.Linq.XElement(Ns.W + "rFonts", new System.Xml.Linq.XAttribute(Ns.W + "asciiTheme", "minorHAnsi"), new System.Xml.Linq.XAttribute(Ns.W + "hAnsiTheme", "minorHAnsi"));
            p.Inlines.Add(new Run { Text = "abc", Props = new RunProps { FontTheme = "minorHAnsi", RawFonts = fonts } });
            var ed = new DocEditor(doc);
            ed.SelectAll(); ed.SetFont("Arial");
            using var ms = new MemoryStream(); DocxWriter.Write(ed.Doc, ms);
            string xml = DocXml(ms.ToArray());
            Assert(xml.Contains("w:ascii=\"Arial\"") && !xml.Contains("asciiTheme"), "в rFonts осталась тема: " + xml);
        });

        Test("Списки: маркированный и нумерованный, Enter продолжает, пустой пункт выходит из списка", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("пункт 1");
            ed.ToggleList(bullet: true);
            Assert(ed.GetFormatState().IsBulletList, "маркированный список не включён");
            ed.NewParagraph(); ed.TypeText("пункт 2");
            Assert(ed.P(1).Props.NumId == ed.P(0).Props.NumId, "Enter не продолжил список");
            ed.NewParagraph(); ed.NewParagraph();
            Assert(ed.P(2).Props.NumId is null, "пустой пункт не вышел из списка");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(1, 1)));
            ed.ToggleList(bullet: false);
            var re = SaveReload(ed.Doc);
            var n = re.AllParagraphs().First().Props.NumId!.Value;
            Eq("decimal", re.Numbering.Level(n, 0)!.Format, "нумерованный после сохранения");
        });

        Test("Список в документе без numbering.xml: часть добавляется в пакет", () =>
        {
            var blank = Model.Document.CreateBlank();
            using var ms = new MemoryStream(); DocxWriter.Write(blank, ms);
            var bytes = StripPart(ms.ToArray(), "word/numbering.xml");
            var ed = new DocEditor(DocxReader.Read(new MemoryStream(bytes)));
            ed.TypeText("x"); ed.ToggleList(true);
            var re = SaveReload(ed.Doc, out var saved);
            Assert(re.Numbering.IsBullet(re.AllParagraphs().First().Props.NumId!.Value), "список потерян");
            Assert(new PackageInspector(Pero.Core.Compatibility.CompatibilityRegistry.CreateDefault()).Inspect(new MemoryStream(saved), "x").Status != InspectionStatus.Rejected, "пакет отклонён");
        });

        Test("Ссылка и изображение: вставка, сохранение, повторное открытие", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("Сайт: ");
            ed.SetLink("https://example.com", "пример");
            ed.TypeText(" и картинка ");
            ed.InsertImage(Png, "png", 120, 80);
            var re = SaveReload(ed.Doc, out var bytes);
            var p = re.AllParagraphs().First();
            var linkRun = p.Inlines.OfType<Run>().First(r => r.Link is not null);
            Eq("https://example.com", linkRun.Link!.Url, "адрес ссылки");
            Eq("пример", linkRun.Text, "текст ссылки");
            Assert(linkRun.Props.StyleId == "Hyperlink", "нет стиля ссылки");
            var img = p.Inlines.OfType<InlineImage>().Single();
            Eq(120L * 9525, img.Cx, "ширина изображения");
            var fp = DocxFingerprint.FromStream(new MemoryStream(bytes));
            Eq(1, fp.InlineImages, "изображений в файле");
            Eq(1, fp.Hyperlinks, "ссылок в файле");
            Assert(new DocEditor(re).ImageBytes(img.RelId)?.Length == Png.Length, "байты изображения не найдены");
        });

        Test("Изменение размера изображения пропорционально", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertImage(Png, "png", 200, 100);
            ed.ResizeImage(new DocPos(0, 0), 100 * 9525);
            var img = ed.P(0).Inlines.OfType<InlineImage>().Single();
            Eq(50L * 9525, img.Cy, "высота после уменьшения");
        });

        Test("Таблица: вставка, текст в ячейке, строки и столбцы, ширина, сохранение", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("до");
            ed.InsertTable(2, 3);
            ed.TypeText("A1");
            ed.InsertRow(below: true);
            ed.InsertColumn(right: true);
            var (t, _, _) = ed.CurrentCell()!.Value;
            Eq(3, t.Rows.Count, "строк");
            Eq(4, t.Grid.Count, "колонок сетки");
            Assert(t.Rows.All(r => r.Cells.Sum(c => c.GridSpan) == t.Grid.Count), "строка не совпадает с сеткой");
            ed.SetColumnWidth(t, 0, 3000);
            ed.DeleteColumn();
            ed.DeleteRow();
            var re = SaveReload(ed.Doc, out var bytes);
            var tbl = re.Body.OfType<Table>().Single();
            Assert(tbl.Rows.All(r => r.Cells.Sum(c => c.GridSpan) == tbl.Grid.Count), "после сохранения строка не совпадает с сеткой");
            Eq("A1", ((Paragraph)tbl.Rows[0].Cells[0].Blocks[0]).Text, "текст ячейки");
            Assert(re.Body[^1] is Paragraph, "после таблицы нет абзаца");
        });

        Test("Строка внутри вертикального объединения продолжает его", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertTable(3, 2);
            var (t, _, _) = ed.CurrentCell()!.Value;
            t.Rows[0].Cells[1].VMerge = VMerge.Restart;
            t.Rows[1].Cells[1].VMerge = VMerge.Continue;
            t.Rows[2].Cells[1].VMerge = VMerge.Continue;
            ed.Select(Selection.At(new DocPos(ed.Index.IndexOf((Paragraph)t.Rows[0].Cells[0].Blocks[0]), 0)));
            ed.InsertRow(below: true);
            Eq(VMerge.Continue, t.Rows[1].Cells[1].VMerge, "новая строка в объединении");
            ed.Select(Selection.At(new DocPos(ed.Index.IndexOf((Paragraph)t.Rows[0].Cells[0].Blocks[0]), 0)));
            ed.DeleteRow();
            Eq(VMerge.Restart, t.Rows[0].Cells[1].VMerge, "после удаления начала объединение начинается со следующей строки");
        });

        Test("Параметры страницы: альбомная, поля, Letter — сохраняются", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.SetOrientation(Orientation.Landscape);
            ed.SetMargins(720, 720, 720, 720);
            ed.SetPageSize(12240, 15840);
            var re = SaveReload(ed.Doc);
            Eq(Orientation.Landscape, re.Section.Orientation, "ориентация");
            Eq(15840, re.Section.PageWidth, "ширина Letter альбомная");
            Eq(720, re.Section.MarginLeft, "поле");
        });

        Test("Очистка форматирования и копирование формата", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("образец цель");
            ed.Select(new Selection(new DocPos(0, 0), new DocPos(0, 7)));
            ed.ToggleBold(); ed.SetColor("FF0000");
            ed.Select(Selection.At(new DocPos(0, 2)));
            ed.CopyFormat();
            ed.Select(new Selection(new DocPos(0, 8), new DocPos(0, 12)));
            ed.PasteFormat();
            Eq(true, ed.GetFormatState().Bold, "формат скопирован");
            ed.SelectAll(); ed.ClearFormatting();
            Eq(false, ed.GetFormatState().Bold, "формат очищен");
        });

        if (samples is null) return;

        Test("Правка эталона 01: текст, шрифт, выравнивание — без потерь остального", () =>
        {
            string src = Path.Combine(samples, "01-styles-lists-multipage.docx");
            var ed = new DocEditor(DocxReader.Read(src));
            ed.TypeText("ПРАВКА ");
            ed.Select(new Selection(new DocPos(3, 0), new DocPos(3, 10)));
            ed.SetFont("Georgia");
            ed.SetAlign(Align.Right);
            using var ms = new MemoryStream(); DocxWriter.Write(ed.Doc, ms);
            var after = DocxFingerprint.FromStream(new MemoryStream(ms.ToArray()));
            var diffs = DocxFingerprint.FromFile(src).CompareTo(after).Where(d => d.IsLoss && d.Property != "Текст").ToList();
            Assert(diffs.Count == 0, string.Join("; ", diffs.Select(d => $"{d.Property}: {d.Before} → {d.After}")));
            Assert(after.Text.StartsWith("ПРАВКА Годовой отчёт"), "правка не попала в текст: " + after.Text[..40]);
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "pero-edit-01.docx"), ms.ToArray());
        });

        Test("Правка таблицы с вертикальным объединением (эталон 02) — объединения сохраняются (AC-04)", () =>
        {
            string src = Path.Combine(samples, "02-tables.docx");
            var ed = new DocEditor(DocxReader.Read(src));
            var t = ed.Doc.Body.OfType<Table>().First();
            var cell = (Paragraph)t.Rows[2].Cells[0].Blocks[0];
            ed.Select(Selection.At(new DocPos(ed.Index.IndexOf(cell), cell.Length)));
            ed.TypeText(" изменено");
            using var ms = new MemoryStream(); DocxWriter.Write(ed.Doc, ms);
            var before = DocxFingerprint.FromFile(src);
            var after = DocxFingerprint.FromStream(new MemoryStream(ms.ToArray()));
            Eq(before.MergedCells, after.MergedCells, "объединения");
            Eq(before.TableCells, after.TableCells, "ячейки");
            Assert(after.Text.Contains("изменено"), "правка не сохранилась");
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "pero-edit-02.docx"), ms.ToArray());
        });
    }

    private static int CountUndo(DocEditor ed)
    {
        int n = 0;
        while (ed.CanUndo) { ed.Undo(); n++; }
        for (int i = 0; i < n; i++) ed.Redo();
        return n;
    }

    private static string DocXml(byte[] docx)
    {
        using var z = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        using var r = new StreamReader(z.GetEntry("word/document.xml")!.Open());
        return r.ReadToEnd();
    }

    private static byte[] StripPart(byte[] docx, string part)
    {
        using var src = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        using var ms = new MemoryStream();
        using (var dst = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            foreach (var e in src.Entries)
            {
                if (e.FullName == part) continue;
                using var r = new StreamReader(e.Open());
                string text = r.ReadToEnd();
                if (e.FullName.EndsWith(".rels")) text = System.Text.RegularExpressions.Regex.Replace(text, "<Relationship [^>]*numbering[^>]*/>", "");
                if (e.FullName == "[Content_Types].xml") text = System.Text.RegularExpressions.Regex.Replace(text, "<Override [^>]*numbering[^>]*/>", "");
                using var w = new StreamWriter(dst.CreateEntry(e.FullName).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }
}
