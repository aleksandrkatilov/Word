using System.Xml.Linq;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Docx;

/// <summary>
/// Чтение DOCX в модель (CORE-03). Проверку безопасности пакета и решение о режиме только чтения
/// выполняет <see cref="Pero.Core.Packaging.PackageInspector"/> до вызова.
/// </summary>
public static class DocxReader
{
    public static Model.Document Read(string path)
    {
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    public static Model.Document Read(Stream stream)
    {
        var store = PackageStore.Read(stream);
        var xml = store.LoadXml(store.MainPart) ?? throw new InvalidDataException("пустая основная часть");
        var body = xml.Root?.Element(W + "body") ?? throw new InvalidDataException("в документе нет w:body");

        var doc = new Model.Document { Package = store };
        doc.Styles = StylesReader.Read(store);
        doc.Numbering = StylesReader.ReadNumbering(store);

        // «оболочка» документа: корень со всеми пространствами имён и фоном, без содержимого тела
        var shell = new XDocument(xml);
        shell.Root!.Element(W + "body")!.RemoveNodes();
        store.DocumentShell = shell;

        foreach (var el in body.Elements())
        {
            if (el.Name == W + "sectPr") doc.Section = ReadSection(el);
            else doc.Body.Add(ReadBlock(el, store));
        }
        if (doc.Body.Count == 0) doc.Body.Add(new Paragraph()); // при чтении ничего больше не добавляем
        return doc;
    }

    internal static Block ReadBlock(XElement el, PackageStore store)
    {
        if (el.Name == W + "p") return ReadParagraph(el, store);
        if (el.Name == W + "tbl") return ReadTable(el, store);
        return new RawBlock(new XElement(el));
    }

    // ── абзац ────────────────────────────────────────────────────────────────

    internal static Paragraph ReadParagraph(XElement p, PackageStore store)
    {
        var para = new Paragraph { Props = ReadParaProps(p.Element(W + "pPr")) };
        foreach (var child in p.Elements())
        {
            var name = child.Name;
            if (name == W + "pPr") continue;
            if (name == W + "r") ReadRun(child, para.Inlines, null);
            else if (name == W + "hyperlink")
            {
                var link = ReadLink(child, store);
                foreach (var r in child.Elements())
                {
                    if (r.Name == W + "r") ReadRun(r, para.Inlines, link);
                    else para.Inlines.Add(new RawInline(new XElement(r), visible: false) { Link = link });
                }
            }
            else if (name == W + "proofErr") continue; // отметки орфографии Word — служебные, не сохраняем
            else if (name == W + "bookmarkStart" || name == W + "bookmarkEnd" || name == W + "commentRangeStart" ||
                     name == W + "commentRangeEnd" || name == W + "permStart" || name == W + "permEnd")
                para.Inlines.Add(new RawInline(new XElement(child), visible: false));
            else
            {
                // поля fldSimple, формулы, вложенные sdt, исправления и т.п. — на месте, видимым объектом
                string text = string.Concat(child.Descendants(W + "t").Select(t => t.Value));
                para.Inlines.Add(new RawInline(new XElement(child), visible: true) { DisplayText = text.Length > 0 ? text : null });
            }
        }
        return para;
    }

    private static LinkRef ReadLink(XElement h, PackageStore store)
    {
        string? relId = (string?)h.Attribute(R + "id");
        string? anchor = (string?)h.Attribute(W + "anchor");
        string? url = relId is null ? null : store.ResolveTarget(relId);
        var raw = new XElement(W + "hyperlinkProps", h.Attributes().Where(a => a.Name != R + "id" && a.Name != W + "anchor").Select(a => new XAttribute(a)));
        return new LinkRef(url, anchor, raw.HasAttributes ? raw : null);
    }

    private static void ReadRun(XElement r, List<Inline> into, LinkRef? link)
    {
        var props = ReadRunProps(r.Element(W + "rPr"));
        var text = new System.Text.StringBuilder();
        void FlushText()
        {
            if (text.Length == 0) return;
            into.Add(new Run { Text = text.ToString(), Props = props.Clone(), Link = link });
            text.Clear();
        }
        foreach (var c in r.Elements())
        {
            var n = c.Name;
            if (n == W + "rPr") continue;
            if (n == W + "t" || n == W + "delText") text.Append(c.Value);
            else if (n == W + "tab") { FlushText(); into.Add(new TabChar { Props = props.Clone(), Link = link }); }
            else if (n == W + "br")
            {
                FlushText();
                string? type = (string?)c.Attribute(W + "type");
                if (type is null or "textWrapping" && c.Attribute(W + "clear") is null)
                    into.Add(new Break { Kind = BreakKind.Line, Props = props.Clone(), Link = link });
                else if (type == "page") into.Add(new Break { Kind = BreakKind.Page, Props = props.Clone(), Link = link });
                else if (type == "column") into.Add(new Break { Kind = BreakKind.Column, Props = props.Clone(), Link = link });
                else into.Add(RawRun(r, c, link, visible: true));
            }
            else if (n == W + "cr") { FlushText(); into.Add(new Break { Kind = BreakKind.Line, Props = props.Clone(), Link = link }); }
            else if (n == W + "noBreakHyphen") text.Append('‑');
            else if (n == W + "softHyphen") text.Append('­');
            else if (n == W + "lastRenderedPageBreak") continue; // подсказка Word о прежней разметке
            else if (n == W + "drawing" && ReadInlineImage(c, props, link) is { } img) { FlushText(); into.Add(img); }
            else if (n == W + "fldChar" || n == W + "instrText")
            {
                FlushText();
                into.Add(RawRun(r, c, link, visible: false));
            }
            else
            {
                FlushText();
                into.Add(RawRun(r, c, link, visible: true));
            }
        }
        FlushText();
    }

    /// <summary>Нераспознанный элемент прогона — отдельный w:r с теми же свойствами.</summary>
    private static RawInline RawRun(XElement run, XElement child, LinkRef? link, bool visible)
    {
        var copy = new XElement(W + "r", run.Attributes().Select(a => new XAttribute(a)));
        if (run.Element(W + "rPr") is { } rPr) copy.Add(new XElement(rPr));
        copy.Add(new XElement(child));
        return new RawInline(copy, visible) { Link = link };
    }

    private static InlineImage? ReadInlineImage(XElement drawing, RunProps props, LinkRef? link)
    {
        var inline = drawing.Element(WP + "inline");
        if (inline is null) return null; // плавающий объект — RawInline
        var blip = inline.Descendants(A + "blip").FirstOrDefault();
        string? embed = (string?)blip?.Attribute(R + "embed");
        var ext = inline.Element(WP + "extent");
        if (embed is null || ext is null) return null;
        return new InlineImage
        {
            RelId = embed,
            Cx = long.TryParse((string?)ext.Attribute("cx"), out long cx) ? cx : 0,
            Cy = long.TryParse((string?)ext.Attribute("cy"), out long cy) ? cy : 0,
            DrawingXml = new XElement(drawing),
            Props = props.Clone(),
            Link = link,
        };
    }

    // ── свойства ─────────────────────────────────────────────────────────────

    internal static ParaProps ReadParaProps(XElement? pPr)
    {
        var pp = new ParaProps();
        if (pPr is null) return pp;
        foreach (var e in pPr.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "pStyle": pp.StyleId = (string?)e.Attribute(W + "val"); break;
                case "jc":
                    pp.Align = (string?)e.Attribute(W + "val") switch
                    {
                        "center" => Align.Center,
                        "right" or "end" => Align.Right,
                        "both" or "distribute" => Align.Justify,
                        "left" or "start" => Align.Left,
                        _ => null,
                    };
                    if (pp.Align is null) pp.RawExtra.Add(new XElement(e));
                    break;
                case "spacing":
                    pp.SpaceBefore = Int(e, "before");
                    pp.SpaceAfter = Int(e, "after");
                    pp.Line = Int(e, "line");
                    pp.LineRule = (string?)e.Attribute(W + "lineRule") switch { "exact" => LineRule.Exact, "atLeast" => LineRule.AtLeast, null => null, _ => LineRule.Auto };
                    pp.RawSpacing = new XElement(e);
                    break;
                case "ind":
                    pp.IndLeft = Int(e, "left") ?? Int(e, "start");
                    pp.IndRight = Int(e, "right") ?? Int(e, "end");
                    if (Int(e, "hanging") is int h) pp.IndFirst = -h;
                    else if (Int(e, "firstLine") is int f) pp.IndFirst = f;
                    pp.RawInd = new XElement(e);
                    break;
                case "numPr":
                    pp.NumId = Int(e.Element(W + "numId"), "val");
                    pp.Ilvl = Int(e.Element(W + "ilvl"), "val") ?? (pp.NumId is null ? null : 0);
                    if (e.Elements().Any(x => x.Name.LocalName is not ("numId" or "ilvl"))) pp.RawExtra.Add(new XElement(e)); // ins и т.п.
                    break;
                case "pageBreakBefore": pp.PageBreakBefore = OnOff(e) == true; break;
                case "keepNext": pp.KeepNext = OnOff(e) == true; break;
                case "rPr": pp.MarkRunProps = new XElement(e); break;
                default: pp.RawExtra.Add(new XElement(e)); break;
            }
        }
        return pp;
    }

    internal static RunProps ReadRunProps(XElement? rPr)
    {
        var rp = new RunProps();
        if (rPr is null) return rp;
        foreach (var e in rPr.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "rStyle": rp.StyleId = (string?)e.Attribute(W + "val"); break;
                case "b": rp.Bold = OnOff(e); break;
                case "i": rp.Italic = OnOff(e); break;
                case "strike": rp.Strike = OnOff(e); break;
                case "u": rp.Underline = (string?)e.Attribute(W + "val") ?? "single"; rp.RawUnderline = new XElement(e); break;
                case "color": rp.Color = (string?)e.Attribute(W + "val"); rp.RawColor = new XElement(e); break;
                case "highlight": rp.Highlight = (string?)e.Attribute(W + "val"); break;
                case "rFonts":
                    rp.Font = (string?)e.Attribute(W + "ascii") ?? (string?)e.Attribute(W + "hAnsi");
                    rp.FontTheme = (string?)e.Attribute(W + "asciiTheme") ?? (string?)e.Attribute(W + "hAnsiTheme");
                    rp.RawFonts = new XElement(e);
                    break;
                case "sz": rp.SizeHalfPt = Int(e, "val"); break;
                case "vertAlign": rp.VertAlign = (string?)e.Attribute(W + "val"); break;
                default: rp.RawExtra.Add(new XElement(e)); break;
            }
        }
        return rp;
    }

    // ── таблица ──────────────────────────────────────────────────────────────

    internal static Table ReadTable(XElement tbl, PackageStore store)
    {
        var t = new Table { RawTblPr = tbl.Element(W + "tblPr") is { } tp ? new XElement(tp) : null };
        foreach (var g in tbl.Element(W + "tblGrid")?.Elements(W + "gridCol") ?? [])
            t.Grid.Add(Int(g, "w") ?? 0);
        foreach (var child in tbl.Elements())
        {
            if (child.Name != W + "tr")
            {
                if (child.Name != W + "tblPr" && child.Name != W + "tblGrid") t.Extra.Add(new XElement(child));
                continue;
            }
            var row = new TableRow { RawTrPr = child.Element(W + "trPr") is { } trPr ? new XElement(trPr) : null };
            row.IsHeader = row.RawTrPr?.Element(W + "tblHeader") is { } th && OnOff(th) == true;
            foreach (var c in child.Elements())
            {
                if (c.Name == W + "tc")
                {
                    var tcPr = c.Element(W + "tcPr");
                    var cell = new TableCell
                    {
                        RawTcPr = tcPr is null ? null : new XElement(tcPr),
                        GridSpan = Math.Max(1, Int(tcPr?.Element(W + "gridSpan"), "val") ?? 1),
                        VMerge = tcPr?.Element(W + "vMerge") is { } vm ? ((string?)vm.Attribute(W + "val") == "restart" ? VMerge.Restart : VMerge.Continue) : VMerge.None,
                    };
                    foreach (var b in c.Elements().Where(b => b.Name != W + "tcPr")) cell.Blocks.Add(ReadBlock(b, store));
                    if (cell.Blocks.Count == 0 || cell.Blocks[^1] is not Paragraph) cell.Blocks.Add(new Paragraph());
                    row.Cells.Add(cell);
                }
                else if (c.Name != W + "trPr") row.Extra.Add(new XElement(c)); // tblPrEx, sdt-ячейки и т.п.
            }
            t.Rows.Add(row);
        }
        return t;
    }

    // ── раздел ───────────────────────────────────────────────────────────────

    internal static SectionProps ReadSection(XElement sectPr)
    {
        var s = new SectionProps { RawSectPr = new XElement(sectPr) };
        var pgSz = sectPr.Element(W + "pgSz");
        s.PageWidth = Int(pgSz, "w") ?? s.PageWidth;
        s.PageHeight = Int(pgSz, "h") ?? s.PageHeight;
        s.Orientation = (string?)pgSz?.Attribute(W + "orient") == "landscape" || s.PageWidth > s.PageHeight ? Orientation.Landscape : Orientation.Portrait;
        var m = sectPr.Element(W + "pgMar");
        s.MarginTop = Math.Abs(Int(m, "top") ?? s.MarginTop);
        s.MarginBottom = Math.Abs(Int(m, "bottom") ?? s.MarginBottom);
        s.MarginLeft = Int(m, "left") ?? s.MarginLeft;
        s.MarginRight = Int(m, "right") ?? s.MarginRight;
        s.HeaderDistance = Int(m, "header") ?? s.HeaderDistance;
        s.FooterDistance = Int(m, "footer") ?? s.FooterDistance;
        return s;
    }
}
