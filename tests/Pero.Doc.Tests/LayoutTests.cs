using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Layout;
using Pero.Doc.Model;
using static Pero.Doc.Tests.T;

namespace Pero.Doc.Tests;

internal static class LayoutTests
{
    private static readonly ITextMeasurer M = new ApproxMeasurer();

    /// <summary>Каждый символ каждого абзаца попадает ровно в одну строку, строки не выходят за страницу.</summary>
    internal static void CheckCoverage(Model.Document doc, LayoutResult lr, string what)
    {
        var index = new ParaIndex(doc);
        var lines = lr.StoryLines(null).GroupBy(l => l.Para).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Start).ToList());
        for (int i = 0; i < index.Count; i++)
        {
            var p = index[i].Paragraph;
            Assert(lines.TryGetValue(i, out var ls), $"{what}: абзац {i} «{Short(p.Text)}» не размечен");
            int at = 0;
            foreach (var l in ls!)
            {
                Assert(l.Start == at, $"{what}: абзац {i}: разрыв покрытия на {at} (строка с {l.Start})");
                at = l.End;
            }
            Assert(at == p.Length, $"{what}: абзац {i}: покрыто {at} из {p.Length}");
        }
        foreach (var page in lr.Pages)
            foreach (var l in page.Lines.Concat(page.HeaderLines).Concat(page.FooterLines))
            {
                Assert(l.Y >= -0.01 && l.Y + l.Height <= page.Height + 0.01, $"{what}: строка за пределами страницы {page.Index + 1} по вертикали (y={l.Y:0.#})");
                foreach (var s in l.Segs.Where(s => s.Kind == SegKind.Text))
                    Assert(s.X >= -0.01 && s.X + s.Width <= page.Width + 0.01, $"{what}: текст «{Short(s.Text)}» выходит за край страницы {page.Index + 1}");
            }
    }

    private static string Short(string s) => s.Length > 30 ? s[..30] + "…" : s;

    public static void Register(string? samples)
    {
        Test("Разметка: длинный абзац переносится по словам в ширину текста", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText(string.Join(" ", Enumerable.Repeat("слово", 200)));
            var lr = DocumentLayout.Build(ed.Doc, M);
            var lines = lr.StoryLines(null).ToList();
            Assert(lines.Count > 5, "мало строк: " + lines.Count);
            var page = lr.Pages[0];
            foreach (var l in lines)
            {
                var txt = l.Segs.Where(s => s.Kind == SegKind.Text).ToList();
                double right = txt.Count == 0 ? l.X : txt.Max(s => s.X + s.Advances.Select((a, k) => s.Text[k] == ' ' ? 0 : a).Sum());
                Assert(l.CaretX.Length == l.End - l.Start + 1, "размер массива позиций");
            }
            Assert(lines.Take(lines.Count - 1).All(l => ed.Doc.AllParagraphs().First().Text[l.End - 1] == ' '), "перенос не по пробелу");
            CheckCoverage(ed.Doc, lr, "длинный абзац");
        });

        Test("Разметка: позиция → курсор → щелчок возвращает ту же позицию", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("Первая строка текста для проверки.\nВторая, " + string.Join(" ", Enumerable.Repeat("длинная", 40)) + "\n\nПосле пустого");
            var lr = DocumentLayout.Build(ed.Doc, M);
            for (int pi = 0; pi < ed.Index.Count; pi++)
                for (int off = 0; off <= ed.Index[pi].Paragraph.Length; off++)
                {
                    var c = lr.Caret(null, new DocPos(pi, off));
                    Assert(c is not null, $"нет курсора для {pi}:{off}");
                    var hit = lr.Hit(null, c!.Value.Page, c.Value.X + 0.01, c.Value.Y + c.Value.Height / 2);
                    var line = lr.LineAt(null, new DocPos(pi, off))!;
                    bool wrapEnd = off == line.End && !line.LastInParagraph;
                    if (!wrapEnd) Eq(new DocPos(pi, off), hit, $"щелчок в позицию {pi}:{off}");
                }
        });

        Test("Разметка: строки ниже/выше и Home/End", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("аааа бббб\nвввв гггг");
            var lr = DocumentLayout.Build(ed.Doc, M);
            var c = lr.Caret(null, new DocPos(0, 2))!.Value;
            Eq(new DocPos(1, 2), lr.Vertical(null, new DocPos(0, 2), +1, c.X), "вниз");
            Eq(new DocPos(0, 2), lr.Vertical(null, new DocPos(1, 2), -1, c.X), "вверх");
            Eq(new DocPos(0, 9), lr.LineEndPos(null, new DocPos(0, 3)), "End");
            Eq(new DocPos(1, 0), lr.LineHome(null, new DocPos(1, 3)), "Home");
            Eq(null, lr.Vertical(null, new DocPos(0, 2), -1, c.X), "выше первой строки");
        });

        Test("Разметка: много абзацев — несколько страниц, строки в пределах области текста", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText(string.Join("\n", Enumerable.Range(1, 120).Select(i => $"Абзац номер {i} с текстом.")));
            var lr = DocumentLayout.Build(ed.Doc, M);
            Assert(lr.Pages.Count >= 2, "страниц: " + lr.Pages.Count);
            foreach (var p in lr.Pages)
                foreach (var l in p.Lines)
                    Assert(l.Y >= p.Body.Y - 0.01 && l.Y + l.Height <= p.Body.Bottom + 0.01, $"строка вне области текста на стр. {p.Index + 1}");
            CheckCoverage(ed.Doc, lr, "много абзацев");
        });

        Test("Разметка: разрыв страницы (Ctrl+Enter) и «с новой страницы»", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("один"); ed.PageBreak(); ed.TypeText("два");
            var lr = DocumentLayout.Build(ed.Doc, M);
            Eq(2, lr.Pages.Count, "страниц после разрыва");
            Eq(1, lr.Caret(null, new DocPos(0, 5))!.Value.Page, "текст после разрыва на 2-й странице");
        });

        Test("Разметка: выравнивание по центру, справа и по ширине", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("центр\nсправа\n" + string.Join(" ", Enumerable.Repeat("ширина", 40)));
            ed.Select(Selection.At(new DocPos(0, 0))); ed.SetAlign(Align.Center);
            ed.Select(Selection.At(new DocPos(1, 0))); ed.SetAlign(Align.Right);
            ed.Select(Selection.At(new DocPos(2, 0))); ed.SetAlign(Align.Justify);
            var lr = DocumentLayout.Build(ed.Doc, M);
            var body = lr.Pages[0].Body;
            var l0 = lr.LineAt(null, new DocPos(0, 0))!; var l1 = lr.LineAt(null, new DocPos(1, 0))!;
            Assert(Math.Abs((l0.CaretX[0] - body.X) - (body.Right - l0.CaretX[^1])) < 1, "центр не по центру");
            Assert(Math.Abs(l1.CaretX[^1] - body.Right) < 0.5, "справа не у правого поля");
            var j = lr.StoryLines(null).First(l => l.Para == 2);
            double lastChar = j.CaretX[^1] - (ed.P(2).Text[j.End - 1] == ' ' ? j.Segs.Last().Advances[^1] : 0);
            Assert(Math.Abs(lastChar - body.Right) < 0.5, $"по ширине: правый край {lastChar:0.##} ≠ {body.Right:0.##}");
        });

        Test("Разметка: списки получают метки 1., 2., маркер", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText("а\nб\nв");
            ed.SelectAll(); ed.ToggleList(bullet: false);
            var lr = DocumentLayout.Build(ed.Doc, M);
            var labels = lr.StoryLines(null).Select(l => l.Label).ToList();
            Eq("1.,2.,3.", string.Join(",", labels), "метки");
            ed.SelectAll(); ed.ToggleList(bullet: true);
            lr = DocumentLayout.Build(ed.Doc, M);
            Assert(lr.StoryLines(null).All(l => l.Label == "●"), "маркер: " + lr.StoryLines(null).First().Label);
            var l = lr.StoryLines(null).First();
            Assert(l.CaretX[0] > l.LabelX + 5, "текст налезает на метку");
        });

        Test("Разметка: таблица — текст внутри ячеек, границы, повтор заголовка на новой странице", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertTable(80, 3);
            var (t, _, _) = ed.CurrentCell()!.Value;
            t.Rows[0].IsHeader = true;
            foreach (var (row, r) in t.Rows.Select((x, i) => (x, i)))
                foreach (var (cell, c) in row.Cells.Select((x, i) => (x, i)))
                    ((Paragraph)cell.Blocks[0]).Inlines.Add(new Run { Text = r == 0 ? $"Заголовок {c}" : $"Ячейка {r}-{c} с текстом" });
            var doc = ed.Doc;
            var lr = DocumentLayout.Build(doc, M);
            Assert(lr.Pages.Count >= 2, "таблица на одной странице: " + lr.Pages.Count);
            Assert(lr.Pages[0].Decor.Any(d => d.Kind == DecorKind.Line), "нет границ (стиль «Сетка таблицы»)");
            Assert(lr.Pages[1].Lines.Any(l => l.Repeat && l.Segs.Any(s => s.Text.StartsWith("Заголовок"))), "заголовок не повторён на 2-й странице");
            CheckCoverage(doc, lr, "таблица");
            // строки ячеек — в пределах своих колонок
            double colW = (doc.Section.PageWidth - doc.Section.MarginLeft - doc.Section.MarginRight) / 20.0 / 3;
            var cellIndex = new ParaIndex(doc);
            foreach (var l in lr.StoryLines(null).Where(l => cellIndex[l.Para].Cell is not null))
                Assert(l.AreaWidth <= colW + 0.01, "строка шире колонки");
        });

        Test("Разметка: колонтитул с номером страницы на каждой странице", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.InsertPlainText(string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Строка {i}")));
            ed.OpenHeaderFooter(HfKind.Footer, 0);
            ed.TypeText("Стр. "); ed.InsertPageField(); ed.TypeText(" из "); ed.InsertPageField(totalPages: true);
            ed.EnterStory(null);
            var lr = DocumentLayout.Build(ed.Doc, M);
            Assert(lr.Pages.Count >= 2, "страниц " + lr.Pages.Count);
            for (int i = 0; i < lr.Pages.Count; i++)
            {
                string text = string.Concat(lr.Pages[i].FooterLines.SelectMany(l => l.Segs).Select(s => s.Text));
                Eq($"Стр. {i + 1} из {lr.Pages.Count}", text, $"нижний колонтитул стр. {i + 1}");
                Assert(lr.Pages[i].FooterLines.All(l => l.Y >= lr.Pages[i].Body.Bottom - 0.01), "колонтитул заходит на текст");
            }
        });

        Test("Разметка: изображение задаёт высоту строки", () =>
        {
            var ed = new DocEditor(Model.Document.CreateBlank());
            ed.TypeText("до ");
            ed.InsertImage(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="), "png", 200, 150);
            var lr = DocumentLayout.Build(ed.Doc, M);
            var l = lr.StoryLines(null).First();
            Assert(l.Height >= 150 * 0.75, "высота строки меньше изображения: " + l.Height);
            Assert(lr.ImageAt(null, 0, l.Segs.Last().X + 10, l.Y + l.Baseline - 10) is not null, "изображение не находится щелчком");
        });

        if (samples is null) return;

        foreach (var f in Directory.GetFiles(samples, "0*.docx").OrderBy(x => x))
        {
            string name = Path.GetFileName(f);
            Test($"Разметка эталона {name}: весь текст размечен, ничего не обрезано (REL-05)", () =>
            {
                var doc = DocxReader.Read(f);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var lr = DocumentLayout.Build(doc, M);
                sw.Stop();
                CheckCoverage(doc, lr, name);
                Assert(lr.Pages.Count >= 1, "нет страниц");
                Console.WriteLine($"      {name}: {lr.Pages.Count} стр., {lr.StoryLines(null).Count()} строк, {sw.ElapsedMilliseconds} мс");
            });
        }
    }
}
