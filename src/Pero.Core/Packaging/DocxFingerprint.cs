using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Pero.Core.Packaging;

/// <summary>
/// Структурный отпечаток DOCX для проверки кругового обмена (QA-01): сравнение по содержимому,
/// а не по байтам ZIP. Используется утилитой Pero.Probe и автотестами импорта/экспорта.
/// </summary>
public sealed class DocxFingerprint
{
    public int Paragraphs { get; private set; }
    public int Tables { get; private set; }
    public int TableRows { get; private set; }
    public int TableCells { get; private set; }
    public int MergedCells { get; private set; }
    public int InlineImages { get; private set; }
    public int FloatingObjects { get; private set; }
    public int Hyperlinks { get; private set; }
    public int NumberedParagraphs { get; private set; }
    public int PageBreaks { get; private set; }
    public int Sections { get; private set; }
    public int HeaderParts { get; private set; }
    public int FooterParts { get; private set; }
    public int PageFields { get; private set; }
    public int Comments { get; private set; }
    public int TrackedChanges { get; private set; }
    public int Footnotes { get; private set; }
    public int MediaParts { get; private set; }
    public SortedSet<string> UsedStyles { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> Fonts { get; } = new(StringComparer.Ordinal);
    public string PageSize { get; private set; } = "";
    public string PageMargins { get; private set; } = "";
    public string Text { get; private set; } = "";
    public SortedSet<string> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    public static DocxFingerprint FromFile(string path, PackageLimits? limits = null)
    {
        using var fs = File.OpenRead(path);
        return FromStream(fs, limits);
    }

    public static DocxFingerprint FromStream(Stream stream, PackageLimits? limits = null)
    {
        var lim = limits ?? PackageLimits.Default;
        var fp = new DocxFingerprint();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > lim.MaxEntries) throw new PackageRejectedException("слишком много частей пакета");
        var budget = new DecompressionBudget(lim.MaxTotalUncompressedBytes);

        foreach (var e in zip.Entries)
        {
            string n = e.FullName;
            if (n.EndsWith('/')) continue;
            fp.Parts.Add(n);
            if (n.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)) fp.MediaParts++;
            if (n.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml")) { fp.HeaderParts++; fp.ScanFields(e, budget); }
            if (n.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml")) { fp.FooterParts++; fp.ScanFields(e, budget); }
        }

        var main = zip.GetEntry("word/document.xml") ?? throw new PackageRejectedException("нет word/document.xml");
        fp.ScanMain(main, budget);
        return fp;
    }

    private void ScanFields(ZipArchiveEntry e, DecompressionBudget budget)
    {
        using var s = new BoundedReadStream(e.Open(), budget);
        using var r = SafeXml.Create(s);
        bool inInstr = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W)
            {
                if (r.LocalName == "fldSimple") CountField(r.GetAttribute("instr", W));
                else if (r.LocalName == "instrText") inInstr = !r.IsEmptyElement;
            }
            else if (r.NodeType == XmlNodeType.Text && inInstr) { CountField(r.Value); inInstr = false; }
        }
    }

    private void CountField(string? instr)
    {
        var w = instr?.Trim().Split(' ', 2)[0].ToUpperInvariant();
        if (w is "PAGE" or "NUMPAGES") PageFields++;
        else if (w == "HYPERLINK") Hyperlinks++; // движок пишет ссылки полем HYPERLINK — это та же гиперссылка
    }

    private void ScanMain(ZipArchiveEntry e, DecompressionBudget budget)
    {
        using var s = new BoundedReadStream(e.Open(), budget);
        using var r = SafeXml.Create(s);
        var text = new StringBuilder();
        bool inT = false, inInstr = false;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement && r.NamespaceURI == W)
            {
                if (r.LocalName == "p") text.Append('\n');
                if (r.LocalName == "t") inT = false;
                if (r.LocalName == "instrText") inInstr = false;
                continue;
            }
            if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                if (inT) text.Append(r.Value);
                else if (inInstr) CountField(r.Value);
                continue;
            }
            if (r.NodeType != XmlNodeType.Element) continue;

            if (r.NamespaceURI == WP)
            {
                if (r.LocalName == "inline") InlineImages++;
                else if (r.LocalName == "anchor") FloatingObjects++;
                continue;
            }
            if (r.NamespaceURI != W) continue;
            switch (r.LocalName)
            {
                case "p": Paragraphs++; break;
                case "t": inT = !r.IsEmptyElement; break;
                case "tab": text.Append('\t'); break;
                case "tbl": Tables++; break;
                case "tr": TableRows++; break;
                case "tc": TableCells++; break;
                case "gridSpan": MergedCells++; break;
                case "vMerge": MergedCells++; break;
                case "hyperlink": Hyperlinks++; break;
                case "numPr": NumberedParagraphs++; break;
                case "br": if (r.GetAttribute("type", W) == "page") PageBreaks++; break;
                case "sectPr": Sections++; break;
                case "pStyle": case "rStyle": case "tblStyle":
                    if (r.GetAttribute("val", W) is { } st) UsedStyles.Add(st); break;
                case "rFonts":
                    if (r.GetAttribute("ascii", W) is { } f) Fonts.Add(f); break;
                case "pgSz":
                    PageSize = $"{r.GetAttribute("w", W)}x{r.GetAttribute("h", W)}{(r.GetAttribute("orient", W) is { } o ? " " + o : "")}"; break;
                case "pgMar":
                    PageMargins = $"T{r.GetAttribute("top", W)} R{r.GetAttribute("right", W)} B{r.GetAttribute("bottom", W)} L{r.GetAttribute("left", W)}"; break;
                case "fldSimple": CountField(r.GetAttribute("instr", W)); break;
                case "instrText": inInstr = !r.IsEmptyElement; break;
                case "commentReference": Comments++; break;
                case "ins": case "del": TrackedChanges++; break;
                case "footnoteReference": Footnotes++; break;
            }
        }
        Text = Normalize(text.ToString());
    }

    /// <summary>Служебные части, которые редактор вправе пересоздать или не писать (не влияют на содержимое).</summary>
    private static bool IsRegenerablePart(string p)
    {
        string n = p.ToLowerInvariant();
        return n.StartsWith("docprops/thumbnail") || n.StartsWith("word/theme/") ||
               n is "docprops/app.xml" or "word/websettings.xml" or "word/styleswitheffects.xml" or "word/fonttable.xml"
                 or "word/people.xml" or "word/commentsextended.xml" or "word/commentsids.xml" or "word/commentsextensible.xml";
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false, newline = false;
        foreach (char ch in s.Normalize(NormalizationForm.FormC))
        {
            char c = ch is ' ' or '\t' or '\r' ? ' ' : ch;
            if (c == '\n')
            {
                // пустые абзацы (например, в покрытых ячейках объединения) не считаются изменением текста
                if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
                if (!newline && sb.Length > 0) sb.Append('\n');
                newline = true; space = true;
            }
            else if (c == ' ') { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(c); space = false; newline = false; }
        }
        return sb.ToString().Trim();
    }

    public sealed record Difference(string Property, string Before, string After, bool IsLoss);

    /// <summary>Сравнить исходник (this) с результатом кругового обмена.</summary>
    public List<Difference> CompareTo(DocxFingerprint after)
    {
        var d = new List<Difference>();
        void Num(string name, int a, int b) { if (a != b) d.Add(new(name, a.ToString(), b.ToString(), b < a)); }
        void Str(string name, string a, string b) { if (a != b) d.Add(new(name, a, b, true)); }

        if (Text != after.Text)
        {
            int i = 0;
            while (i < Text.Length && i < after.Text.Length && Text[i] == after.Text[i]) i++;
            string Around(string s) => s.Substring(Math.Max(0, i - 20), Math.Min(60, Math.Max(0, s.Length - Math.Max(0, i - 20)))).Replace('\n', '¶');
            d.Add(new("Текст", $"…{Around(Text)}… ({Text.Length} симв.)", $"…{Around(after.Text)}… ({after.Text.Length} симв.)", true));
        }
        Num("Абзацы", Paragraphs, after.Paragraphs);
        Num("Таблицы", Tables, after.Tables);
        Num("Строки таблиц", TableRows, after.TableRows);
        Num("Ячейки", TableCells, after.TableCells);
        Num("Объединения ячеек", MergedCells, after.MergedCells);
        Num("Изображения в строке", InlineImages, after.InlineImages);
        Num("Плавающие объекты", FloatingObjects, after.FloatingObjects);
        Num("Гиперссылки", Hyperlinks, after.Hyperlinks);
        Num("Абзацы списков", NumberedParagraphs, after.NumberedParagraphs);
        Num("Разрывы страниц", PageBreaks, after.PageBreaks);
        Num("Разделы", Sections, after.Sections);
        Num("Части верхних колонтитулов", HeaderParts, after.HeaderParts);
        Num("Части нижних колонтитулов", FooterParts, after.FooterParts);
        Num("Поля PAGE/NUMPAGES", PageFields, after.PageFields);
        Num("Примечания", Comments, after.Comments);
        Num("Исправления", TrackedChanges, after.TrackedChanges);
        Num("Сноски", Footnotes, after.Footnotes);
        Num("Медиафайлы", MediaParts, after.MediaParts);
        Str("Размер страницы", PageSize, after.PageSize);
        Str("Поля страницы", PageMargins, after.PageMargins);

        var lostStyles = UsedStyles.Except(after.UsedStyles).ToList();
        if (lostStyles.Count > 0) d.Add(new("Стили", string.Join(", ", lostStyles), "отсутствуют", true));
        var lostFonts = Fonts.Except(after.Fonts).ToList();
        if (lostFonts.Count > 0) d.Add(new("Шрифты", string.Join(", ", lostFonts), "отсутствуют", true));
        var lostBenign = Parts.Where(IsRegenerablePart).Except(after.Parts, StringComparer.OrdinalIgnoreCase).ToList();
        if (lostBenign.Count > 0) d.Add(new("Служебные части", string.Join(", ", lostBenign), "пересозданы или не нужны", false));
        var lostParts = Parts.Where(p => !p.EndsWith(".rels") && !IsRegenerablePart(p))
                             .Except(after.Parts, StringComparer.OrdinalIgnoreCase)
                             // переименование медиа и колонтитулов допустимо — их количество сравнивается выше
                             .Where(p => !p.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase) &&
                                         !p.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) &&
                                         !p.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase))
                             .ToList();
        if (lostParts.Count > 0) d.Add(new("Части пакета", string.Join(", ", lostParts.Take(10)), "отсутствуют", true));
        return d;
    }
}
