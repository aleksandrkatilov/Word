using System.IO.Compression;
using System.Xml;
using Pero.Core.Compatibility;

namespace Pero.Core.Packaging;

public enum InspectionStatus
{
    /// <summary>Можно открыть и редактировать.</summary>
    Ok,
    /// <summary>Можно открыть и редактировать; часть конструкций передаётся частично.</summary>
    OkWithWarnings,
    /// <summary>Безопасный круговой обмен невозможен: открыть только для чтения с отчётом (DOCX-01).</summary>
    ReadOnlyRecommended,
    /// <summary>Открыть нельзя: повреждён, зашифрован, превышает лимиты и т.п.</summary>
    Rejected,
}

public sealed record Finding(Construct Construct, int Count, CompatibilityEntry Entry, string? Detail = null);

public sealed class InspectionReport
{
    public required string Path { get; init; }
    public InspectionStatus Status { get; internal set; } = InspectionStatus.Ok;
    public string? RejectReason { get; internal set; }
    public bool IsPasswordProtectedOrLegacy { get; internal set; }
    public int PartCount { get; internal set; }
    public long UncompressedBytes { get; internal set; }
    public List<Finding> Findings { get; } = [];

    public IEnumerable<Finding> Risks => Findings.Where(f => !f.Entry.RoundTripSafe);
    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Entry.RoundTripSafe && f.Entry.Save == Support.Partial);

    /// <summary>Текст отчёта для пользователя (русский).</summary>
    public string ToUserText()
    {
        var sb = new System.Text.StringBuilder();
        switch (Status)
        {
            case InspectionStatus.Rejected:
                sb.AppendLine($"Файл не может быть открыт: {RejectReason}");
                return sb.ToString();
            case InspectionStatus.ReadOnlyRecommended:
                sb.AppendLine("Документ содержит конструкции, которые редактор пока не сохраняет без потерь.");
                sb.AppendLine("Он открыт только для чтения, исходный файл не изменяется.");
                sb.AppendLine("Можно сохранить преобразованную копию под другим именем.");
                break;
            case InspectionStatus.OkWithWarnings:
                sb.AppendLine("Часть оформления поддерживается частично и может измениться при сохранении.");
                break;
            default:
                sb.AppendLine("Все найденные конструкции поддерживаются.");
                break;
        }
        foreach (var f in Risks)
            sb.AppendLine($"  ✗ {f.Entry.Title} — {f.Count} шт.{(f.Detail is null ? "" : $" ({f.Detail})")} — будет потеряно");
        foreach (var f in Warnings)
            sb.AppendLine($"  ⚠ {f.Entry.Title} — {f.Count} шт. — частично");
        return sb.ToString();
    }
}

/// <summary>
/// Проверка DOCX до передачи движку: структура и лимиты ZIP (SEC-01), шифрование и старый DOC,
/// перечень конструкций и решение о режиме открытия по реестру совместимости (DOCX-01, DOCX-02).
/// </summary>
public sealed class PackageInspector(CompatibilityRegistry registry, PackageLimits? limits = null)
{
    private readonly PackageLimits _limits = limits ?? PackageLimits.Default;

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string WPS = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string M = "http://schemas.openxmlformats.org/officeDocument/2006/math";
    private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DGM = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    private const string V = "urn:schemas-microsoft-com:vml";
    private const string PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string CT = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string OfficeDocRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public InspectionReport Inspect(string path)
    {
        var report = new InspectionReport { Path = path };
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            InspectStream(fs, report);
        }
        catch (PackageRejectedException ex) { Reject(report, ex.Message); }
        catch (InvalidDataException) { Reject(report, "файл повреждён или не является документом DOCX."); }
        catch (XmlException ex) { Reject(report, $"повреждённый XML в пакете ({ex.Message})."); }
        catch (UnauthorizedAccessException) { Reject(report, "нет доступа к файлу."); }
        catch (FileNotFoundException) { Reject(report, "файл не найден."); }
        catch (IOException ex) { Reject(report, $"ошибка чтения ({ex.Message})."); }
        return report;
    }

    public InspectionReport Inspect(Stream stream, string displayName = "<stream>")
    {
        var report = new InspectionReport { Path = displayName };
        try { InspectStream(stream, report); }
        catch (PackageRejectedException ex) { Reject(report, ex.Message); }
        catch (InvalidDataException) { Reject(report, "файл повреждён или не является документом DOCX."); }
        catch (XmlException ex) { Reject(report, $"повреждённый XML в пакете ({ex.Message})."); }
        return report;
    }

    private static void Reject(InspectionReport r, string reason)
    {
        r.Status = InspectionStatus.Rejected;
        r.RejectReason = reason;
    }

    private void InspectStream(Stream stream, InspectionReport report)
    {
        Span<byte> head = stackalloc byte[8];
        int read = stream.ReadAtLeast(head, 8, throwOnEndOfStream: false);
        if (read == 8 && head.SequenceEqual(OleSignature))
        {
            report.IsPasswordProtectedOrLegacy = true;
            throw new PackageRejectedException(
                "документ защищён паролем или сохранён в старом формате DOC. Редактирование таких файлов появится в расширенной версии.");
        }
        if (read < 4 || head[0] != 'P' || head[1] != 'K')
            throw new PackageRejectedException("файл повреждён или не является документом DOCX.");
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateEntries(zip, report);

        var budget = new DecompressionBudget(_limits.MaxTotalUncompressedBytes);
        var contentTypes = ReadContentTypes(zip, budget);
        string mainPart = FindMainPart(zip, budget);

        var counts = new Dictionary<Construct, int>();
        void Add(Construct c, int n = 1) => counts[c] = counts.GetValueOrDefault(c) + n;
        var details = new Dictionary<Construct, HashSet<string>>();
        void Detail(Construct c, string d) { if (!details.TryGetValue(c, out var s)) details[c] = s = []; s.Add(d); }

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue; // каталог
            string name = "/" + entry.FullName;
            string ct = contentTypes.TypeOf(name);
            var kind = ClassifyPart(name, ct, mainPart);
            if (kind is { } k)
            {
                Add(k);
                if (k == Construct.UnknownPart) Detail(k, name);
            }
            if (ct.EndsWith("wordprocessingml.numbering+xml", StringComparison.Ordinal))
                CountNumberingDefinitions(OpenBounded(entry, budget), Add);
            if (IsScannablePart(ct))
                ScanBody(OpenBounded(entry, budget), Add, Detail, isMain: name.Equals(mainPart, StringComparison.OrdinalIgnoreCase));
        }

        report.UncompressedBytes = budget.Consumed;
        foreach (var (c, n) in counts.OrderBy(kv => kv.Key))
        {
            string? d = details.TryGetValue(c, out var set) ? string.Join(", ", set.Take(5)) : null;
            report.Findings.Add(new Finding(c, n, registry[c], d));
        }

        report.Status = report.Risks.Any() ? InspectionStatus.ReadOnlyRecommended
                      : report.Warnings.Any() ? InspectionStatus.OkWithWarnings
                      : InspectionStatus.Ok;
    }

    private void ValidateEntries(ZipArchive zip, InspectionReport report)
    {
        var entries = zip.Entries;
        if (entries.Count > _limits.MaxEntries)
            throw new PackageRejectedException($"пакет содержит {entries.Count} частей, допустимо не более {_limits.MaxEntries}.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declared = 0;
        foreach (var e in entries)
        {
            string n = e.FullName;
            if (n.Length == 0 || n.StartsWith('/') || n.StartsWith('\\') || n.Contains('\\') || n.Contains(':') ||
                n.Split('/').Any(seg => seg == ".." || seg == "."))
                throw new PackageRejectedException($"недопустимый путь части пакета: «{n}».");
            if (!names.Add(n))
                throw new PackageRejectedException($"дублирующееся имя части пакета: «{n}».");

            declared += e.Length;
            if (declared > _limits.MaxTotalUncompressedBytes)
                throw new PackageRejectedException($"распакованный объём превышает {_limits.MaxTotalUncompressedBytes / (1024 * 1024)} МБ.");
            if (e.Length > _limits.RatioCheckThresholdBytes && e.CompressedLength > 0 &&
                (double)e.Length / e.CompressedLength > _limits.MaxCompressionRatio)
                throw new PackageRejectedException($"подозрительно высокий коэффициент сжатия части «{n}».");
        }
        report.PartCount = entries.Count;
        report.UncompressedBytes = declared;
    }

    private static Stream OpenBounded(ZipArchiveEntry e, DecompressionBudget budget) => new BoundedReadStream(e.Open(), budget);

    private sealed class ContentTypeMap
    {
        public Dictionary<string, string> Defaults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string TypeOf(string partName)
        {
            if (Overrides.TryGetValue(partName, out var t)) return t;
            string ext = System.IO.Path.GetExtension(partName).TrimStart('.');
            return Defaults.TryGetValue(ext, out t) ? t : "";
        }
    }

    private static ContentTypeMap ReadContentTypes(ZipArchive zip, DecompressionBudget budget)
    {
        var entry = zip.GetEntry("[Content_Types].xml")
            ?? throw new PackageRejectedException("в пакете нет [Content_Types].xml — это не документ DOCX.");
        var map = new ContentTypeMap();
        using var s = OpenBounded(entry, budget);
        using var r = SafeXml.Create(s, 16 * 1024 * 1024);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != CT) continue;
            if (r.LocalName == "Default")
                map.Defaults[r.GetAttribute("Extension") ?? ""] = r.GetAttribute("ContentType") ?? "";
            else if (r.LocalName == "Override")
                map.Overrides[r.GetAttribute("PartName") ?? ""] = r.GetAttribute("ContentType") ?? "";
        }
        return map;
    }

    private static string FindMainPart(ZipArchive zip, DecompressionBudget budget)
    {
        var rels = zip.GetEntry("_rels/.rels") ?? throw new PackageRejectedException("в пакете нет _rels/.rels.");
        using var s = OpenBounded(rels, budget);
        using var r = SafeXml.Create(s, 16 * 1024 * 1024);
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.NamespaceURI == PkgRel &&
                r.GetAttribute("Type") == OfficeDocRel)
            {
                if (string.Equals(r.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                    throw new PackageRejectedException("основная часть документа указывает на внешний ресурс.");
                string target = (r.GetAttribute("Target") ?? "").TrimStart('/');
                if (zip.GetEntry(target) is null)
                    throw new PackageRejectedException("в пакете отсутствует основная часть документа.");
                return "/" + target;
            }
        }
        throw new PackageRejectedException("в пакете не найдена основная часть документа.");
    }

    private static bool IsScannablePart(string ct) =>
        ct.StartsWith("application/vnd.openxmlformats-officedocument.wordprocessingml.", StringComparison.Ordinal) &&
        (ct.Contains(".main+xml") || ct.EndsWith("header+xml") || ct.EndsWith("footer+xml") ||
         ct.EndsWith("footnotes+xml") || ct.EndsWith("endnotes+xml") || ct.EndsWith("comments+xml"))
        || ct.StartsWith("application/vnd.ms-word.document.macroEnabled.main+xml", StringComparison.Ordinal);

    /// <summary>Классификация части пакета. null — служебная часть, не влияющая на совместимость.</summary>
    internal static Construct? ClassifyPart(string name, string ct, string mainPart)
    {
        string n = name.ToLowerInvariant();
        if (n.EndsWith(".rels") || n == "/[content_types].xml") return null;
        if (n.Equals(mainPart, StringComparison.OrdinalIgnoreCase))
            return ct.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) ? Construct.Macros : null;

        if (n.Contains("vbaproject") || n.Contains("vbadata") || ct.Contains("vbaProject") || ct.Contains("vbaData")) return Construct.Macros;
        if (n.StartsWith("/word/activex/") || ct.Contains("activeX")) return Construct.ActiveX;
        if (n.StartsWith("/word/embeddings/") || ct.Contains("oleObject")) return Construct.OleObjects;
        if (n.StartsWith("/word/charts/") || ct.Contains("drawingml.chart") || ct.Contains("chartstyle") || ct.Contains("chartcolorstyle")) return Construct.Charts;
        if (n.StartsWith("/word/diagrams/") || ct.Contains("drawingml.diagram")) return Construct.SmartArt;
        if (n.StartsWith("/word/glossary/") || ct.Contains("glossary")) return Construct.Glossary;
        if (n.StartsWith("/word/fonts/") || ct.Contains("obfuscatedFont") || ct.Contains("fontdata")) return Construct.EmbeddedFonts;
        if (n.StartsWith("/word/ink/") || ct.Contains("inkml")) return Construct.Ink;
        if (n.StartsWith("/customxml/") || ct.Contains("customXmlProperties")) return Construct.CustomXml;
        if (n == "/docprops/custom.xml" || ct.Contains("custom-properties")) return Construct.CustomXml;
        if (n.StartsWith("/word/media/") || ct.StartsWith("image/")) return null; // учитываются через разметку
        if (n.StartsWith("/docprops/")) return null; // core/app/thumbnail

        if (ct.StartsWith("application/vnd.openxmlformats-officedocument.wordprocessingml.", StringComparison.Ordinal))
        {
            // header/footer/styles/numbering/settings/fontTable/webSettings/footnotes/endnotes/comments
            return null;
        }
        if (ct.Contains("theme+xml") || ct.Contains("stylesWithEffects") || ct.Contains("vnd.ms-word.people") ||
            ct.Contains("commentsExtended") || ct.Contains("commentsIds") || ct.Contains("commentsExtensible") ||
            ct.Contains("vnd.ms-word.intelligence"))
            return null;
        return Construct.UnknownPart;
    }

    /// <summary>Списки через стили (List Bullet и т.п.) не дают w:numPr в тексте — считаем экземпляры w:num.</summary>
    private static void CountNumberingDefinitions(Stream s, Action<Construct, int> add)
    {
        using var _ = s;
        using var r = SafeXml.Create(s);
        int n = 0;
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == W && r.LocalName == "num") n++;
        if (n > 0) add(Construct.Numbering, n);
    }

    private static void ScanBody(Stream s, Action<Construct, int> add, Action<Construct, string> detail, bool isMain)
    {
        using var _ = s;
        using var r = SafeXml.Create(s);
        int tableDepth = 0, sectPr = 0, paragraphs = 0;
        bool inInstr = false;
        var instr = new System.Text.StringBuilder();
        var textSeen = false;

        void Field(string code)
        {
            string word = code.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? "";
            if (word.Length == 0) return;
            if (word is "PAGE" or "NUMPAGES" or "SECTIONPAGES") add(Construct.PageFields, 1);
            else if (word == "HYPERLINK") add(Construct.Hyperlinks, 1);
            else { add(Construct.OtherFields, 1); detail(Construct.OtherFields, word); }
        }

        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.NamespaceURI == W && r.LocalName == "tbl") tableDepth--;
                if (r.NamespaceURI == W && r.LocalName == "instrText") inInstr = false;
                continue;
            }
            if (r.NodeType == XmlNodeType.Text && inInstr) { instr.Append(r.Value); continue; }
            if (r.NodeType != XmlNodeType.Element) continue;

            string ns = r.NamespaceURI, ln = r.LocalName;
            if (ns == W)
            {
                switch (ln)
                {
                    case "p": paragraphs++; break;
                    case "t": textSeen = true; break;
                    case "tbl":
                        tableDepth++;
                        add(tableDepth > 1 ? Construct.NestedTables : Construct.Tables, 1);
                        break;
                    case "gridSpan": case "vMerge": case "hMerge": add(Construct.MergedCells, 1); break;
                    case "hyperlink": add(Construct.Hyperlinks, 1); break;
                    case "numPr": add(Construct.Numbering, 1); break;
                    case "pStyle": case "rStyle": case "tblStyle": add(Construct.Styles, 1); break;
                    case "headerReference": case "footerReference": add(Construct.HeadersFooters, 1); break;
                    case "sectPr": if (isMain) sectPr++; break;
                    case "pgSz": case "pgMar": if (isMain) add(Construct.PageSetup, 1); break;
                    case "cols":
                        if (int.TryParse(r.GetAttribute("num", W), out int num) && num > 1) add(Construct.Columns, 1);
                        break;
                    case "br":
                        if (r.GetAttribute("type", W) == "page") add(Construct.PageBreaks, 1);
                        break;
                    case "pageBreakBefore": add(Construct.PageBreaks, 1); break;
                    case "ins": case "del": case "moveFrom": case "moveTo":
                    case "rPrChange": case "pPrChange": case "tblPrChange": case "trPrChange": case "tcPrChange": case "sectPrChange":
                        add(Construct.TrackChanges, 1); break;
                    case "commentReference": add(Construct.Comments, 1); break;
                    case "footnoteReference": add(Construct.Footnotes, 1); break;
                    case "endnoteReference": add(Construct.Endnotes, 1); break;
                    case "bookmarkStart":
                        if (r.GetAttribute("name", W) is { } bn && !bn.StartsWith('_')) add(Construct.Bookmarks, 1);
                        break;
                    case "sdt": add(Construct.ContentControls, 1); break;
                    case "object": add(Construct.OleObjects, 1); break;
                    case "altChunk": add(Construct.AltChunk, 1); break;
                    case "txbxContent": add(Construct.TextBoxes, 1); break;
                    case "b": case "i": case "u": case "sz": case "color": case "rFonts": case "highlight": case "strike": case "vertAlign":
                        add(Construct.CharacterFormatting, 1); break;
                    case "jc": case "ind": case "spacing": case "tabs": case "keepNext": case "keepLines": case "widowControl":
                        add(Construct.ParagraphFormatting, 1); break;
                    case "fldSimple":
                        Field(r.GetAttribute("instr", W) ?? ""); break;
                    case "instrText":
                        inInstr = true; break;
                    case "fldChar":
                        if (r.GetAttribute("fldCharType", W) == "separate" || r.GetAttribute("fldCharType", W) == "end")
                        {
                            if (instr.Length > 0) { Field(instr.ToString()); instr.Clear(); }
                        }
                        break;
                }
            }
            else if (ns == WP)
            {
                if (ln == "inline") add(Construct.InlineImages, 1);
                else if (ln == "anchor") add(Construct.FloatingObjects, 1);
            }
            else if (ns == WPS && ln == "wsp") add(Construct.Shapes, 1);
            else if (ns == V && (ln is "rect" or "oval" or "line" or "roundrect" or "polyline")) add(Construct.Shapes, 1);
            else if (ns == M && ln == "oMath") add(Construct.Equations, 1);
            else if (ns == C && ln == "chart") add(Construct.Charts, 1);
            else if (ns == DGM && ln == "relIds") add(Construct.SmartArt, 1);
        }

        if (isMain)
        {
            if (textSeen || paragraphs > 0) add(Construct.Text, paragraphs);
            if (sectPr > 1) add(Construct.MultipleSections, sectPr);
        }
    }
}
