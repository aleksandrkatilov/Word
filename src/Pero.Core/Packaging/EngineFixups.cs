using System.IO.Compression;
using System.Xml.Linq;

namespace Pero.Core.Packaging;

/// <summary>
/// Исправления DOCX, который записал движок, до записи на диск. Каждое исправление — ответ на
/// дефект, найденный замером кругового обмена в CI (tools/Pero.Lab).
///
/// 1. Вертикальное объединение ячеек: SfRichTextBoxAdv 35.1 пишет ячейку с vMerge="restart",
///    но выбрасывает «продолжающие» ячейки в следующих строках. Строки становятся короче сетки
///    таблицы, и в Word таблица распадается. Исправление вставляет ячейки vMerge="continue"
///    на место недостающих колонок.
/// </summary>
public static class EngineFixups
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public sealed record Report(int MergeCellsRestored);

    /// <summary>Скопировать пакет, исправив разметку частей с текстом.</summary>
    public static Report Apply(Stream input, Stream output)
    {
        int restored = 0;
        using var src = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        using var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var budget = new DecompressionBudget(PackageLimits.Default.MaxTotalUncompressedBytes);
        foreach (var e in src.Entries)
        {
            var ne = dst.CreateEntry(e.FullName, CompressionLevel.Optimal);
            ne.LastWriteTime = e.LastWriteTime;
            bool body = e.FullName is "word/document.xml" ||
                        (e.FullName.StartsWith("word/header", StringComparison.Ordinal) || e.FullName.StartsWith("word/footer", StringComparison.Ordinal))
                        && e.FullName.EndsWith(".xml", StringComparison.Ordinal);
            using var from = new BoundedReadStream(e.Open(), budget);
            using var to = ne.Open();
            if (!body) { from.CopyTo(to); continue; }

            XDocument doc;
            using (var r = SafeXml.Create(from)) doc = XDocument.Load(r, LoadOptions.PreserveWhitespace);
            int n = RestoreVerticalMerges(doc);
            restored += n;
            doc.Save(to, SaveOptions.DisableFormatting);
        }
        return new Report(restored);
    }

    /// <summary>Вставить недостающие ячейки vMerge="continue". Возвращает число вставленных ячеек.</summary>
    public static int RestoreVerticalMerges(XDocument doc)
    {
        int inserted = 0;
        foreach (var tbl in doc.Descendants(W + "tbl").ToList())
        {
            int gridCols = tbl.Element(W + "tblGrid")?.Elements(W + "gridCol").Count() ?? 0;
            if (gridCols == 0) continue;
            // колонка начала объединения → ширина в колонках сетки
            var active = new Dictionary<int, int>();
            foreach (var tr in tbl.Elements(W + "tr"))
            {
                int before = GridInt(tr.Element(W + "trPr")?.Element(W + "gridBefore"));
                int after = GridInt(tr.Element(W + "trPr")?.Element(W + "gridAfter"));
                var cells = tr.Elements(W + "tc").ToList();
                int used = before + after + cells.Sum(Span);
                int deficit = gridCols - used;

                int col = before, i = 0;
                var nextActive = new Dictionary<int, int>();
                while (col < gridCols - after)
                {
                    if (deficit > 0 && active.TryGetValue(col, out int w) && w <= deficit)
                    {
                        var cont = ContinueCell(w, CellWidth(tbl, col, w));
                        if (i < cells.Count) cells[i].AddBeforeSelf(cont); else tr.Add(cont);
                        cells.Insert(i, cont);
                        inserted++;
                        deficit -= w;
                    }
                    if (i >= cells.Count) break;
                    var tc = cells[i];
                    int span = Span(tc);
                    string? vm = VMerge(tc);
                    if (vm == "restart" || (vm == "continue" && active.ContainsKey(col))) nextActive[col] = span;
                    col += span;
                    i++;
                }
                active = nextActive;
            }
        }
        return inserted;
    }

    private static int Span(XElement tc) => Math.Max(1, GridInt(tc.Element(W + "tcPr")?.Element(W + "gridSpan")));

    private static int GridInt(XElement? e) => e is not null && int.TryParse((string?)e.Attribute(W + "val"), out int v) ? v : 0;

    private static string? VMerge(XElement tc)
    {
        var vm = tc.Element(W + "tcPr")?.Element(W + "vMerge");
        if (vm is null) return null;
        return (string?)vm.Attribute(W + "val") == "restart" ? "restart" : "continue";
    }

    private static int? CellWidth(XElement tbl, int col, int span)
    {
        var cols = tbl.Element(W + "tblGrid")!.Elements(W + "gridCol").Skip(col).Take(span)
            .Select(g => int.TryParse((string?)g.Attribute(W + "w"), out int v) ? v : 0).ToList();
        return cols.Count == span && cols.All(v => v > 0) ? cols.Sum() : null;
    }

    private static XElement ContinueCell(int span, int? widthTwips)
    {
        var tcPr = new XElement(W + "tcPr");
        if (widthTwips is { } wd) tcPr.Add(new XElement(W + "tcW", new XAttribute(W + "w", wd), new XAttribute(W + "type", "dxa")));
        if (span > 1) tcPr.Add(new XElement(W + "gridSpan", new XAttribute(W + "val", span)));
        tcPr.Add(new XElement(W + "vMerge"));
        return new XElement(W + "tc", tcPr, new XElement(W + "p"));
    }
}

/// <summary>
/// Общий конвейер записи для приложения и лабораторных прогонов:
/// файл движка → исправления → возврат отделимых частей → выходной поток.
/// </summary>
public static class SavePipeline
{
    public sealed record Result(int MergeCellsRestored, IReadOnlyList<string> PartsRestored, IReadOnlyList<string> PartsSkipped);

    public static Result Process(Stream engineOutput, Stream output, DetachedParts detached)
    {
        using var fixedUp = new MemoryStream();
        var fix = EngineFixups.Apply(engineOutput, fixedUp);
        fixedUp.Position = 0;
        if (detached.IsEmpty)
        {
            fixedUp.CopyTo(output);
            return new Result(fix.MergeCellsRestored, [], []);
        }
        var re = detached.ReinjectInto(fixedUp, output);
        return new Result(fix.MergeCellsRestored, re.Restored, re.Skipped);
    }
}
