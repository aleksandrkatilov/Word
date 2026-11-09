using System.Text;
using System.Xml.Linq;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Docx;

/// <summary>
/// Запись модели в DOCX (CORE-03). Переписывается только основная разметка; неизменённые свойства
/// записываются исходными элементами; порядок элементов — по схеме OOXML.
/// </summary>
public static class DocxWriter
{
    public static void Write(Model.Document doc, Stream output)
    {
        var store = doc.Package.CloneShallow();
        var shell = store.DocumentShell is null
            ? XDocument.Parse(BlankTemplates.Document)
            : new XDocument(store.DocumentShell);
        var root = shell.Root!;
        EnsureNamespaces(root);
        var body = root.Element(W + "body") ?? new XElement(W + "body");
        if (body.Parent is null) root.Add(body);
        body.RemoveNodes();

        var ctx = new Ctx(store, null);
        foreach (var b in doc.Body) body.Add(WriteBlock(b, ctx));
        var newRefs = WriteHeadersFooters(doc, store);
        var sectPr = WriteSection(doc.Section);
        if (newRefs.Count > 0)
        {
            sectPr.Add(newRefs);
            var ordered = sectPr.Elements().OrderBy(x => OrderIndex(SectPrOrder, x)).ToList();
            sectPr.RemoveNodes(); sectPr.Add(ordered);
        }
        body.Add(sectPr);
        store.SaveXml(store.MainPart, shell);

        if (doc.Styles.Added.Count > 0) AppendStyles(store, doc.Styles.Added);
        store.WriteTo(output);
    }

    public static void Write(Model.Document doc, string path)
    {
        using var fs = File.Create(path);
        Write(doc, fs);
    }

    private sealed class Ctx(PackageStore store, string? owner)
    {
        public PackageStore Store { get; } = store;
        /// <summary>Часть, в которую пишется разметка (null — основная): от неё считаются связи.</summary>
        public string? Owner { get; } = owner;
    }

    /// <summary>Переписывает изменённые колонтитулы; для новых создаёт части и возвращает ссылки для sectPr.</summary>
    private static List<XElement> WriteHeadersFooters(Model.Document doc, PackageStore store)
    {
        var refs = new List<XElement>();
        foreach (var hf in doc.HeadersFooters.Where(h => h.Modified || !store.Parts.ContainsKey(h.PartPath)))
        {
            bool header = hf.Kind == HfKind.Header;
            string part = hf.PartPath;
            bool isNew = !store.Parts.ContainsKey(part);
            var xml = store.LoadXml(part) ?? new XDocument(new XElement(W + (header ? "hdr" : "ftr")));
            var root = xml.Root!;
            EnsureNamespaces(root);
            root.RemoveNodes();
            var ctx = new Ctx(store, part);
            foreach (var b in hf.Blocks) root.Add(WriteBlock(b, ctx));
            if (!root.Elements(W + "p").Any()) root.Add(new XElement(W + "p"));
            store.SaveXml(part, xml);
            if (isNew)
            {
                string mainDir = PackageStore.DirOf(store.MainPart);
                string target = mainDir.Length > 0 && part.StartsWith(mainDir + "/") ? part[(mainDir.Length + 1)..] : "/" + part;
                string id = store.EnsureRelationship(header ? RelHeader : RelFooter, target, external: false);
                store.EnsureContentTypeOverride(part, "application/vnd.openxmlformats-officedocument.wordprocessingml." + (header ? "header" : "footer") + "+xml");
                refs.Add(new XElement(W + (header ? "headerReference" : "footerReference"), new XAttribute(W + "type", hf.Type), new XAttribute(R + "id", id)));
            }
        }
        return refs;
    }

    private static void EnsureNamespaces(XElement root)
    {
        void Ns(string prefix, XNamespace ns)
        {
            if (root.GetNamespaceOfPrefix(prefix) is null && root.Attributes().All(a => a.Value != ns.NamespaceName))
                root.Add(new XAttribute(XNamespace.Xmlns + prefix, ns.NamespaceName));
        }
        Ns("w", W); Ns("r", R); Ns("wp", WP); Ns("a", A); Ns("pic", Pic);
    }

    private static XElement WriteBlock(Block b, Ctx ctx) => b switch
    {
        Paragraph p => WriteParagraph(p, ctx),
        Table t => WriteTable(t, ctx),
        RawBlock r => new XElement(r.Xml),
        _ => throw new NotSupportedException(b.GetType().Name),
    };

    // ── абзац ────────────────────────────────────────────────────────────────

    private static XElement WriteParagraph(Paragraph p, Ctx ctx)
    {
        var el = new XElement(W + "p");
        if (BuildPPr(p.Props) is { } pPr) el.Add(pPr);
        int i = 0;
        while (i < p.Inlines.Count)
        {
            var inl = p.Inlines[i];
            if (inl.Link is null) { el.Add(WriteInline(inl)); i++; continue; }
            var link = inl.Link;
            var h = new XElement(W + "hyperlink");
            if (link.Url is not null) h.Add(new XAttribute(R + "id", ctx.Store.EnsureRelationship(RelHyperlink, link.Url, external: true, ctx.Owner)));
            if (link.Anchor is not null) h.Add(new XAttribute(W + "anchor", link.Anchor));
            if (link.RawHyperlinkProps is not null) h.Add(link.RawHyperlinkProps.Attributes().Select(a => new XAttribute(a)));
            while (i < p.Inlines.Count && link.SameAs(p.Inlines[i].Link)) { h.Add(WriteInline(p.Inlines[i])); i++; }
            el.Add(h);
        }
        return el;
    }

    private static IEnumerable<XElement> WriteInline(Inline inl)
    {
        switch (inl)
        {
            case Run r:
                foreach (var e in WriteRun(r)) yield return e;
                break;
            case TabChar t:
                yield return RunWith(t.Props, new XElement(W + "tab"));
                break;
            case Break b:
                yield return RunWith(b.Props, b.Kind switch
                {
                    BreakKind.Page => new XElement(W + "br", new XAttribute(W + "type", "page")),
                    BreakKind.Column => new XElement(W + "br", new XAttribute(W + "type", "column")),
                    _ => new XElement(W + "br"),
                });
                break;
            case InlineImage img:
                yield return RunWith(img.Props, WriteDrawing(img));
                break;
            case RawInline raw:
                yield return new XElement(raw.Xml);
                break;
        }
    }

    private static XElement RunWith(RunProps props, params XElement[] content)
    {
        var r = new XElement(W + "r");
        if (BuildRPr(props) is { } rPr) r.Add(rPr);
        r.Add(content);
        return r;
    }

    private static IEnumerable<XElement> WriteRun(Run run)
    {
        if (run.Text.Length == 0) yield break;
        var r = new XElement(W + "r");
        if (BuildRPr(run.Props) is { } rPr) r.Add(rPr);
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            string s = sb.ToString();
            var t = new XElement(W + "t", s);
            if (s[0] == ' ' || s[^1] == ' ' || s.Contains("  ")) t.Add(new XAttribute(Xml + "space", "preserve"));
            r.Add(t);
            sb.Clear();
        }
        foreach (char c in run.Text)
        {
            if (c == '‑') { Flush(); r.Add(new XElement(W + "noBreakHyphen")); }
            else if (c == '­') { Flush(); r.Add(new XElement(W + "softHyphen")); }
            else if (c == '\t') { Flush(); r.Add(new XElement(W + "tab")); }
            else if (IsXmlChar(c)) sb.Append(c);
        }
        Flush();
        yield return r;
    }

    private static bool IsXmlChar(char c) => c is '\t' or '\n' or '\r' || c >= 0x20 && c is not ('￾' or '￿');

    private static XElement WriteDrawing(InlineImage img)
    {
        var d = new XElement(img.DrawingXml);
        var inline = d.Element(WP + "inline");
        if (inline?.Element(WP + "extent") is { } ext)
        {
            ext.SetAttributeValue("cx", img.Cx);
            ext.SetAttributeValue("cy", img.Cy);
        }
        foreach (var x in d.Descendants(A + "ext").Where(e => e.Parent?.Name == A + "xfrm"))
        {
            x.SetAttributeValue("cx", img.Cx);
            x.SetAttributeValue("cy", img.Cy);
        }
        foreach (var blip in d.Descendants(A + "blip")) blip.SetAttributeValue(R + "embed", img.RelId);
        return d;
    }

    // ── свойства абзаца и прогона ────────────────────────────────────────────

    internal static XElement? BuildPPr(ParaProps pp)
    {
        var items = new List<XElement>();
        if (pp.StyleId is not null) items.Add(Val("pStyle", pp.StyleId));
        if (pp.KeepNext) items.Add(new XElement(W + "keepNext"));
        if (pp.PageBreakBefore) items.Add(new XElement(W + "pageBreakBefore"));
        if (pp.NumId is int num)
            items.Add(new XElement(W + "numPr", Val("ilvl", (pp.Ilvl ?? 0).ToString()), Val("numId", num.ToString())));
        if (Spacing(pp) is { } sp) items.Add(sp);
        if (Ind(pp) is { } ind) items.Add(ind);
        if (pp.Align is Align a)
            items.Add(Val("jc", a switch { Align.Center => "center", Align.Right => "right", Align.Justify => "both", _ => "left" }));
        if (pp.MarkRunProps is not null) items.Add(new XElement(pp.MarkRunProps));
        items.AddRange(pp.RawExtra.Select(x => new XElement(x)));
        if (items.Count == 0) return null;
        return new XElement(W + "pPr", items.OrderBy(e => OrderIndex(PPrOrder, e)));
    }

    private static XElement? Spacing(ParaProps pp)
    {
        var raw = pp.RawSpacing;
        if (raw is not null && Int(raw, "before") == pp.SpaceBefore && Int(raw, "after") == pp.SpaceAfter &&
            Int(raw, "line") == pp.Line && RuleOf(raw) == pp.LineRule)
            return new XElement(raw); // не менялось — дословно
        var e = raw is null ? new XElement(W + "spacing") : new XElement(raw);
        Set(e, "before", pp.SpaceBefore);
        Set(e, "after", pp.SpaceAfter);
        Set(e, "line", pp.Line);
        e.SetAttributeValue(W + "lineRule", pp.Line is null ? null : pp.LineRule switch { LineRule.Exact => "exact", LineRule.AtLeast => "atLeast", _ => "auto" });
        // явное значение отменяет «в строках» и «авто» (иначе Word проигнорирует новое число)
        if (pp.SpaceBefore != Int(raw, "before")) { e.SetAttributeValue(W + "beforeLines", null); e.SetAttributeValue(W + "beforeAutospacing", null); }
        if (pp.SpaceAfter != Int(raw, "after")) { e.SetAttributeValue(W + "afterLines", null); e.SetAttributeValue(W + "afterAutospacing", null); }
        return e.HasAttributes ? e : null;
    }

    private static LineRule? RuleOf(XElement raw) => (string?)raw.Attribute(W + "lineRule") switch
    {
        "exact" => LineRule.Exact, "atLeast" => LineRule.AtLeast, null => null, _ => LineRule.Auto,
    };

    private static XElement? Ind(ParaProps pp)
    {
        var raw = pp.RawInd;
        int? rawFirst = Int(raw, "hanging") is int h ? -h : Int(raw, "firstLine");
        if (raw is not null && (Int(raw, "left") ?? Int(raw, "start")) == pp.IndLeft &&
            (Int(raw, "right") ?? Int(raw, "end")) == pp.IndRight && rawFirst == pp.IndFirst)
            return new XElement(raw);
        var e = raw is null ? new XElement(W + "ind") : new XElement(raw);
        foreach (var n in new[] { "left", "start", "right", "end", "firstLine", "hanging", "leftChars", "startChars", "rightChars", "endChars", "firstLineChars", "hangingChars" })
            e.SetAttributeValue(W + n, null);
        Set(e, "left", pp.IndLeft);
        Set(e, "right", pp.IndRight);
        if (pp.IndFirst is int f) Set(e, f < 0 ? "hanging" : "firstLine", Math.Abs(f));
        return e.HasAttributes ? e : null;
    }

    internal static XElement? BuildRPr(RunProps rp)
    {
        var items = new List<XElement>();
        if (rp.StyleId is not null) items.Add(Val("rStyle", rp.StyleId));
        if (Fonts(rp) is { } f) items.Add(f);
        if (rp.Bold is bool b) items.Add(OnOffEl("b", b));
        if (rp.Italic is bool i) items.Add(OnOffEl("i", i));
        if (rp.Strike is bool s) items.Add(OnOffEl("strike", s));
        if (rp.Color is not null)
            items.Add(rp.RawColor is not null && (string?)rp.RawColor.Attribute(W + "val") == rp.Color ? new XElement(rp.RawColor) : Val("color", rp.Color));
        var extra = rp.RawExtra.Select(x => new XElement(x)).ToList();
        if (rp.SizeHalfPt is int sz)
        {
            items.Add(Val("sz", sz.ToString()));
            // размер для сложных письменностей держим в согласии с основным
            foreach (var cs in extra.Where(x => x.Name == W + "szCs")) cs.SetAttributeValue(W + "val", sz);
        }
        if (rp.Highlight is not null) items.Add(Val("highlight", rp.Highlight));
        if (rp.Underline is not null)
            items.Add(rp.RawUnderline is not null && ((string?)rp.RawUnderline.Attribute(W + "val") ?? "single") == rp.Underline
                ? new XElement(rp.RawUnderline) : Val("u", rp.Underline));
        if (rp.VertAlign is not null) items.Add(Val("vertAlign", rp.VertAlign));
        items.AddRange(extra);
        if (items.Count == 0) return null;
        return new XElement(W + "rPr", items.OrderBy(e => OrderIndex(RPrOrder, e)));
    }

    private static XElement? Fonts(RunProps rp)
    {
        var raw = rp.RawFonts;
        if (raw is not null)
        {
            string? rawFont = (string?)raw.Attribute(W + "ascii") ?? (string?)raw.Attribute(W + "hAnsi");
            string? rawTheme = (string?)raw.Attribute(W + "asciiTheme") ?? (string?)raw.Attribute(W + "hAnsiTheme");
            if (rawFont == rp.Font && rawTheme == rp.FontTheme) return new XElement(raw);
        }
        if (rp.Font is null && rp.FontTheme is null) return null;
        var e = raw is null ? new XElement(W + "rFonts") : new XElement(raw);
        if (rp.Font is not null)
        {
            // явный шрифт: атрибуты темы иначе перекроют его
            foreach (var t in new[] { "asciiTheme", "hAnsiTheme", "cstheme", "eastAsiaTheme" }) e.SetAttributeValue(W + t, null);
            foreach (var a in new[] { "ascii", "hAnsi", "cs", "eastAsia" }) e.SetAttributeValue(W + a, rp.Font);
        }
        return e;
    }

    private static XElement Val(string name, string value) => new(W + name, new XAttribute(W + "val", value));
    private static XElement OnOffEl(string name, bool on) => on ? new XElement(W + name) : Val(name, "0");
    private static void Set(XElement e, string attr, int? v) => e.SetAttributeValue(W + attr, v);

    // ── таблица ──────────────────────────────────────────────────────────────

    private static readonly string[] TcPrOrder =
    [
        "cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection",
        "tcFitText", "vAlign", "hideMark", "headers", "cellIns", "cellDel", "cellMerge", "tcPrChange",
    ];

    /// <summary>Свойства новой таблицы: стиль «Сетка таблицы», автоматическая ширина.</summary>
    internal static XElement DefaultTblPr() =>
        new(W + "tblPr", new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")), new XElement(W + "tblW", new XAttribute(W + "w", 0), new XAttribute(W + "type", "auto")),
            new XElement(W + "tblLook", new XAttribute(W + "val", "04A0")));

    private static XElement WriteTable(Table t, Ctx ctx)
    {
        var el = new XElement(W + "tbl");
        el.Add(t.RawTblPr is not null ? new XElement(t.RawTblPr) : DefaultTblPr());
        el.Add(new XElement(W + "tblGrid", t.Grid.Select(w => new XElement(W + "gridCol", new XAttribute(W + "w", w)))));
        int col;
        foreach (var row in t.Rows)
        {
            var tr = new XElement(W + "tr");
            tr.Add(row.Extra.Where(x => x.Name == W + "tblPrEx").Select(x => new XElement(x)));
            var trPr = row.RawTrPr is null ? null : new XElement(row.RawTrPr);
            if (row.IsHeader != (trPr?.Element(W + "tblHeader") is not null))
            {
                trPr ??= new XElement(W + "trPr");
                trPr.Element(W + "tblHeader")?.Remove();
                if (row.IsHeader) trPr.Add(new XElement(W + "tblHeader"));
            }
            if (trPr is not null && trPr.HasElements) tr.Add(trPr);
            col = 0;
            foreach (var cell in row.Cells)
            {
                var tc = new XElement(W + "tc");
                tc.Add(BuildTcPr(cell, t, col));
                foreach (var b in cell.Blocks) tc.Add(WriteBlock(b, ctx));
                if (cell.Blocks.Count == 0 || cell.Blocks[^1] is not Paragraph) tc.Add(new XElement(W + "p"));
                tr.Add(tc);
                col += cell.GridSpan;
            }
            tr.Add(row.Extra.Where(x => x.Name != W + "tblPrEx").Select(x => new XElement(x)));
            el.Add(tr);
        }
        el.Add(t.Extra.Select(x => new XElement(x)));
        return el;
    }

    private static XElement BuildTcPr(TableCell cell, Table t, int col)
    {
        var tcPr = cell.RawTcPr is null ? new XElement(W + "tcPr") : new XElement(cell.RawTcPr);
        tcPr.Element(W + "gridSpan")?.Remove();
        tcPr.Element(W + "vMerge")?.Remove();
        if (cell.GridSpan > 1) tcPr.Add(Val("gridSpan", cell.GridSpan.ToString()));
        if (cell.VMerge == VMerge.Restart) tcPr.Add(Val("vMerge", "restart"));
        else if (cell.VMerge == VMerge.Continue) tcPr.Add(new XElement(W + "vMerge"));
        if (tcPr.Element(W + "tcW") is null && col + cell.GridSpan <= t.Grid.Count)
            tcPr.Add(new XElement(W + "tcW", new XAttribute(W + "w", t.Grid.Skip(col).Take(cell.GridSpan).Sum()), new XAttribute(W + "type", "dxa")));
        var ordered = tcPr.Elements().OrderBy(e => OrderIndex(TcPrOrder, e)).ToList();
        tcPr.RemoveNodes();
        tcPr.Add(ordered);
        return tcPr;
    }

    // ── раздел ───────────────────────────────────────────────────────────────

    internal static XElement WriteSection(SectionProps s)
    {
        var e = s.RawSectPr is null ? new XElement(W + "sectPr") : new XElement(s.RawSectPr);
        var pgSz = e.Element(W + "pgSz") ?? new XElement(W + "pgSz");
        if (pgSz.Parent is null) e.Add(pgSz);
        pgSz.SetAttributeValue(W + "w", s.PageWidth);
        pgSz.SetAttributeValue(W + "h", s.PageHeight);
        pgSz.SetAttributeValue(W + "orient", s.Orientation == Orientation.Landscape ? "landscape" : null);
        var m = e.Element(W + "pgMar") ?? new XElement(W + "pgMar", new XAttribute(W + "gutter", 0));
        if (m.Parent is null) e.Add(m);
        m.SetAttributeValue(W + "top", s.MarginTop);
        m.SetAttributeValue(W + "right", s.MarginRight);
        m.SetAttributeValue(W + "bottom", s.MarginBottom);
        m.SetAttributeValue(W + "left", s.MarginLeft);
        m.SetAttributeValue(W + "header", s.HeaderDistance);
        m.SetAttributeValue(W + "footer", s.FooterDistance);
        var tp = e.Element(W + "titlePg");
        if (s.TitlePage && tp is null) e.Add(new XElement(W + "titlePg"));
        else if (!s.TitlePage) tp?.Remove();
        var ordered = e.Elements().OrderBy(x => OrderIndex(SectPrOrder, x)).ToList();
        e.RemoveNodes();
        e.Add(ordered);
        return e;
    }

    private static void AppendStyles(PackageStore store, List<XElement> added)
    {
        string part = store.FindRelTarget(RelStyles) ?? "word/styles.xml";
        var xml = store.LoadXml(part) ?? XDocument.Parse(BlankTemplates.Styles);
        var existing = xml.Root!.Elements(W + "style").Select(s => (string?)s.Attribute(W + "styleId")).ToHashSet();
        foreach (var s in added.Where(s => !existing.Contains((string?)s.Attribute(W + "styleId"))))
            xml.Root.Add(new XElement(s));
        store.SaveXml(part, xml);
    }
}
