using Pero.Core.Files;
using Pero.Core.Packaging;
using Pero.Doc.Model;

namespace Pero.Doc.Docx;

/// <summary>
/// Надёжное сохранение (SAVE-01, REL-01): резервная версия прежнего файла → запись во временный файл →
/// проверка пакета → повторное чтение и сравнение с моделью → замена оригинала. Любое расхождение
/// (пропал текст, таблица, изображение, связь, колонтитул или часть пакета) блокирует замену.
/// </summary>
public static class SafeSave
{
    public static void Save(Model.Document doc, string target, PackageInspector? inspector = null, BackupStore? backups = null)
    {
        backups?.Backup(target);
        var expected = ModelDigest.Of(doc);
        var parts = doc.Package.Parts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        AtomicFileWriter.Write(target, s => DocxWriter.Write(doc, s), tmp => Verify(tmp, expected, parts, inspector));
    }

    /// <summary>Проверка записанного файла; null — всё в порядке, иначе понятное описание проблемы.</summary>
    public static string? Verify(string path, ModelDigest expected, ISet<string> originalParts, PackageInspector? inspector)
    {
        if (inspector is not null)
        {
            var report = inspector.Inspect(path);
            if (report.Status == InspectionStatus.Rejected) return report.RejectReason ?? "файл не читается как DOCX";
        }
        Model.Document reread;
        try { reread = DocxReader.Read(path); }
        catch (Exception ex) { return "записанный файл не открывается: " + ex.Message; }
        var missing = originalParts.Where(p => !reread.Package.Parts.ContainsKey(p)).ToList();
        if (missing.Count > 0) return "в файле пропали части документа: " + string.Join(", ", missing.Take(5));
        return expected.Compare(ModelDigest.Of(reread));
    }
}

/// <summary>Сводка содержимого для сравнения модели и записанного файла.</summary>
public sealed class ModelDigest
{
    public List<string> Paragraphs { get; } = [];
    public List<string> Tables { get; } = [];
    public List<string> Images { get; } = [];
    public List<string> Links { get; } = [];
    public List<string> HeadersFooters { get; } = [];
    public string Page { get; private set; } = "";

    public static ModelDigest Of(Model.Document doc)
    {
        var d = new ModelDigest();
        foreach (var p in doc.AllParagraphs())
        {
            d.Paragraphs.Add(p.Text);
            foreach (var i in p.Inlines)
            {
                if (i is InlineImage img) d.Images.Add($"{img.Cx}x{img.Cy}:{(doc.Package.ResolvePartPath(img.RelId) is { } part && doc.Package.Parts.ContainsKey(part) ? "ok" : "нет данных")}");
                if (i.Link is { } l && (d.Links.Count == 0 || d.Links[^1] != (l.Url ?? "#" + l.Anchor))) d.Links.Add(l.Url ?? "#" + l.Anchor);
            }
        }
        foreach (var t in AllTables(doc.Body))
            d.Tables.Add($"{t.Rows.Count}:{string.Join(",", t.Rows.Select(r => string.Join("", r.Cells.Select(c => $"{c.GridSpan}{c.VMerge.ToString()[0]}"))))}");
        foreach (var hf in doc.HeadersFooters)
            d.HeadersFooters.Add($"{hf.Kind}/{hf.Type}: {string.Join("¶", hf.Blocks.OfType<Paragraph>().Select(p => p.Text))}");
        var s = doc.Section;
        d.Page = $"{s.PageWidth}x{s.PageHeight} {s.MarginTop},{s.MarginRight},{s.MarginBottom},{s.MarginLeft} {s.Orientation} title={s.TitlePage}";
        return d;
    }

    private static IEnumerable<Table> AllTables(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
            if (b is Table t)
            {
                yield return t;
                foreach (var inner in t.Rows.SelectMany(r => r.Cells).SelectMany(c => AllTables(c.Blocks))) yield return inner;
            }
    }

    /// <summary>null — совпадает; иначе первое расхождение понятными словами.</summary>
    public string? Compare(ModelDigest saved)
    {
        if (saved.Paragraphs.Count != Paragraphs.Count)
            return $"число абзацев {saved.Paragraphs.Count} вместо {Paragraphs.Count}";
        for (int i = 0; i < Paragraphs.Count; i++)
            if (saved.Paragraphs[i] != Paragraphs[i])
                return $"текст абзаца {i + 1} отличается («{Short(Paragraphs[i])}» → «{Short(saved.Paragraphs[i])}»)";
        if (!saved.Tables.SequenceEqual(Tables)) return "таблицы или объединения ячеек отличаются";
        if (!saved.Images.SequenceEqual(Images)) return "изображения отличаются";
        if (!saved.Links.SequenceEqual(Links)) return "гиперссылки отличаются";
        if (!saved.HeadersFooters.SequenceEqual(HeadersFooters)) return "колонтитулы отличаются";
        if (saved.Page != Page) return "параметры страницы отличаются";
        return null;
    }

    private static string Short(string s) => s.Length > 40 ? s[..40] + "…" : s;
}
