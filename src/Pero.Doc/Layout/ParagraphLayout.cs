using System.Xml.Linq;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Layout;

/// <summary>Абзац, разбитый на строки (относительные Y от 0), с интервалами до и после.</summary>
internal sealed class ParaFlow
{
    public List<LineBox> Lines { get; } = [];
    public double SpaceBefore { get; init; }
    public double SpaceAfter { get; init; }
    public bool PageBreakBefore { get; init; }
    public bool KeepNext { get; init; }
    public bool Contextual { get; init; }
    public string? StyleId { get; init; }
    public double Height => Lines.Count == 0 ? 0 : Lines[^1].Y + Lines[^1].Height;
}

/// <summary>Сведения, которые зависят от страницы: номер для полей PAGE/NUMPAGES.</summary>
internal readonly record struct FieldContext(int PageNumber, int? TotalPages);

/// <summary>Разбиение абзаца на строки (LAYOUT-02): переносы по пробелам, табуляции, выключка, списки.</summary>
internal sealed class ParagraphLayout(Document doc, CachedMeasurer m)
{
    private enum K { Text, Space, Tab, Break, Image, Object, Hidden, ZeroBreak }

    private struct Ch
    {
        public char C;
        public K Kind;
        public int Style;
        public double W;
        public string? Disp;
        public InlineImage? Img;
        public BreakKind BreakKind;
    }

    private sealed record StyleRef(EffectiveRun Eff, FontKey Font, double Size, double Rise, LinkRef? Link);

    private const double Tw = 20.0;              // twips в пункте
    private const double EmuPerPt = 12700.0;

    public ParaFlow Layout(Paragraph p, int paraNo, HeaderFooter? story, double areaX, double areaW, string? label, FieldContext fc, string? tableStyle = null)
    {
        var ep = Resolve.Para(doc, p, tableStyle);
        var styles = new List<StyleRef>();
        var styleIndex = new Dictionary<(RunProps, LinkRef?), int>(ReferenceEqualityComparerPair.Instance);
        int StyleOf(RunProps rp, LinkRef? link)
        {
            if (styleIndex.TryGetValue((rp, link), out int i)) return i;
            var eff = Resolve.Run(doc, p, rp, tableStyle);
            double size = eff.SizePt;
            double rise = 0;
            if (eff.VertAlign is "superscript") { rise = size * 0.33; size *= 0.65; }
            else if (eff.VertAlign is "subscript") { rise = -size * 0.14; size *= 0.65; }
            styles.Add(new StyleRef(eff, new FontKey(eff.Font, eff.Bold, eff.Italic), size, rise, link));
            return styleIndex[(rp, link)] = styles.Count - 1;
        }

        var markProps = new RunProps();
        int markStyle = StyleOf(markProps, null);
        var chars = BuildChars(p, StyleOf, markStyle, styles, fc);

        double indLeft = ep.IndLeft / Tw, indRight = ep.IndRight / Tw, indFirst = ep.IndFirst / Tw;
        double right = areaX + areaW - indRight;
        var tabs = TabStops(p);
        double defTab = 35.4;

        var flow = new ParaFlow
        {
            SpaceBefore = ep.SpaceBefore / Tw,
            SpaceAfter = ep.SpaceAfter / Tw,
            PageBreakBefore = ep.PageBreakBefore,
            KeepNext = ep.KeepNext,
            Contextual = HasFlag(p, "contextualSpacing"),
            StyleId = ep.StyleId,
        };

        // метка списка
        double labelW = 0;
        StyleRef? labelStyle = null;
        string? labelText = null;
        if (label is not null)
        {
            var mark = styles[markStyle];
            string? lvlFont = ep.NumId is int nid ? doc.Numbering.Level(nid, ep.Ilvl)?.Font : null;
            (labelText, bool mapped) = MapBullet(label, lvlFont);
            var font = mapped || lvlFont is null || IsSymbolFont(lvlFont) ? mark.Font with { Family = mapped ? "Arial" : mark.Font.Family } : mark.Font with { Family = lvlFont };
            labelStyle = mark with { Font = font };
            labelW = m.Width(font, mark.Size, labelText);
        }

        var w = new double[chars.Count + 1];
        int pos = 0;
        bool first = true;
        double y = 0;
        while (first || pos < chars.Count)
        {
            double lineLeft = areaX + indLeft + (first ? indFirst : 0);
            double maxW = Math.Max(1, right - lineLeft);
            double x = 0;
            double labelX = lineLeft;
            if (first && labelStyle is not null)
            {
                x = labelW;
                // за меткой — табуляция до отступа (висячий отступ) или до следующей позиции
                double abs = lineLeft - areaX + x;
                double stop = ep.IndFirst < 0 && abs < indLeft ? indLeft : NextDefault(abs, defTab);
                x = Math.Max(x, stop - (lineLeft - areaX));
            }
            double textStart = x;
            int start = pos, end = chars.Count, lastBreak = -1;
            bool forced = false;
            for (int j = pos; j < chars.Count; j++)
            {
                var ch = chars[j];
                if (ch.Kind == K.Break) { w[j] = 0; end = j + 1; forced = true; break; }
                double wj = ch.Kind == K.Tab ? TabWidth(j, x, lineLeft - areaX, tabs, defTab, chars, ep, indLeft) : ch.W;
                w[j] = wj;
                if (ch.Kind == K.Space) { x += wj; lastBreak = j + 1; continue; }
                if (x + wj > maxW + 0.01 && j > start)
                {
                    end = lastBreak > start ? lastBreak : j;
                    break;
                }
                if (ch.Kind == K.Image && j > start && x + wj > maxW + 0.01) { end = j; break; }
                x += wj;
                if (ch.Kind is K.ZeroBreak or K.Tab or K.Image || ch.C == '-') lastBreak = j + 1;
            }
            bool last = end >= chars.Count && !forced;
            var line = BuildLine(p, paraNo, story, chars, styles, w, start, end, lineLeft, maxW, textStart, ep.Align, last, forced,
                markStyle, ep, y, areaX, areaW);
            if (first && labelStyle is not null)
            {
                line = WithLabel(line, labelText!, labelStyle, labelX);
            }
            flow.Lines.Add(line);
            y += line.Height;
            pos = end;
            first = false;
            if (pos >= chars.Count && forced && chars[^1].Kind == K.Break)
            {
                // после разрыва в конце абзаца — пустая строка
                double l2 = areaX + indLeft;
                var empty = BuildLine(p, paraNo, story, chars, styles, w, pos, pos, l2, Math.Max(1, right - l2), 0, ep.Align, true, false, markStyle, ep, y, areaX, areaW);
                flow.Lines.Add(empty);
                y += empty.Height;
            }
        }
        return flow;
    }

    private static LineBox WithLabel(LineBox line, string text, StyleRef s, double x) => new LineBox
    {
        Story = line.Story, Para = line.Para, Paragraph = line.Paragraph, Start = line.Start, End = line.End, X = line.X, Y = line.Y,
        AreaWidth = line.AreaWidth, Height = line.Height, Baseline = line.Baseline, CaretX = line.CaretX, LastInParagraph = line.LastInParagraph,
        EndsWithBreak = line.EndsWithBreak, BreakAfter = line.BreakAfter, Label = text, LabelX = x, LabelFont = s.Font, LabelSize = s.Size, LabelColor = s.Eff.Color,
    }.Also(l => l.Segs.AddRange(line.Segs));

    private LineBox BuildLine(Paragraph p, int paraNo, HeaderFooter? story, List<Ch> chars, List<StyleRef> styles, double[] w,
        int start, int end, double lineLeft, double maxW, double textStart, Align align, bool last, bool forced, int markStyle,
        EffectivePara ep, double y, double areaX, double areaW)
    {
        // ширина без хвостовых пробелов и разрыва
        int trim = end;
        while (trim > start && chars[trim - 1].Kind is K.Space or K.Break or K.Hidden) trim--;
        double content = textStart;
        for (int j = start; j < trim; j++) content += w[j];

        double offset = 0, extraPerSpace = 0;
        double free = maxW - content;
        switch (align)
        {
            case Align.Center: offset = Math.Max(0, free / 2); break;
            case Align.Right: offset = Math.Max(0, free); break;
            case Align.Justify when !last && !forced && free > 0:
            {
                int lastTab = start - 1;
                for (int j = start; j < trim; j++) if (chars[j].Kind == K.Tab) lastTab = j;
                int spaces = 0;
                for (int j = lastTab + 1; j < trim; j++) if (chars[j].Kind == K.Space) spaces++;
                if (spaces > 0)
                {
                    extraPerSpace = free / spaces;
                    for (int j = lastTab + 1; j < trim; j++) if (chars[j].Kind == K.Space) w[j] += extraPerSpace;
                }
                break;
            }
        }

        // высота строки
        double asc = 0, desc = 0, spacing = 0;
        void Take(int style)
        {
            var s = styles[style];
            var fm = m.Metrics(s.Font, styles[style].Eff.SizePt);
            asc = Math.Max(asc, fm.Ascent + Math.Max(0, s.Rise));
            desc = Math.Max(desc, fm.Descent + Math.Max(0, -s.Rise));
            spacing = Math.Max(spacing, fm.LineSpacing);
        }
        bool any = false;
        for (int j = start; j < end; j++)
        {
            var ch = chars[j];
            if (ch.Kind == K.Image) { asc = Math.Max(asc, ch.Img!.Cy / EmuPerPt); any = true; continue; }
            Take(ch.Style); any = true;
        }
        if (!any) Take(markStyle);
        double natural = Math.Max(spacing, asc + desc);
        double h = ep.LineRule switch
        {
            LineRule.Exact => ep.Line / Tw,
            LineRule.AtLeast => Math.Max(natural, ep.Line / Tw),
            _ => natural * Math.Max(0.5, ep.Line / 240.0),
        };
        if (ep.LineRule == LineRule.Auto && ep.Line < 240) h = Math.Max(h, asc + desc); // меньше одинарного текст не режем
        double baseline = h - desc;
        if (ep.LineRule == LineRule.Exact) baseline = Math.Min(h, Math.Max(asc, h - desc));

        var line = new LineBox
        {
            Story = story, Para = paraNo, Paragraph = p, Start = start, End = end, X = areaX, Y = y,
            AreaWidth = areaW, Height = h, Baseline = baseline, LastInParagraph = last, EndsWithBreak = forced,
            BreakAfter = forced ? chars[end - 1].BreakKind : null,
        };

        // отрезки и позиции курсора
        var caret = new double[end - start + 1];
        double x = lineLeft + offset + textStart;
        caret[0] = x;
        int k = start;
        while (k < end)
        {
            var ch = chars[k];
            if (ch.Kind is K.Image or K.Object)
            {
                var s = styles[ch.Style];
                line.Segs.Add(ch.Kind == K.Image
                    ? new Seg { Kind = SegKind.Image, Start = k, Length = 1, X = x, Width = w[k], Image = ch.Img, Height = ch.Img!.Cy / EmuPerPt, Link = s.Link, Style = s.Eff }
                    : new Seg { Kind = SegKind.Object, Start = k, Length = 1, X = x, Width = w[k], Text = ch.Disp ?? "", Advances = Adv(ch.Disp ?? "", s), Font = s.Font, SizePt = s.Size, Rise = s.Rise, Style = s.Eff, Link = s.Link });
                x += w[k]; k++;
                caret[k - start] = x;
                continue;
            }
            int st = ch.Style, from = k;
            var sb = new System.Text.StringBuilder();
            var adv = new List<double>();
            double segX = x;
            while (k < end && chars[k].Kind is not (K.Image or K.Object) && chars[k].Style == st)
            {
                var c = chars[k];
                sb.Append(c.Kind is K.Text ? c.C : ' ');
                adv.Add(w[k]);
                x += w[k]; k++;
                caret[k - start] = x;
            }
            var sr = styles[st];
            line.Segs.Add(new Seg { Kind = SegKind.Text, Start = from, Length = k - from, X = segX, Width = x - segX, Text = sb.ToString(), Advances = [.. adv], Font = sr.Font, SizePt = sr.Size, Rise = sr.Rise, Style = sr.Eff, Link = sr.Link });
        }
        line.CaretX = caret;
        return line;
    }

    private double[] Adv(string s, StyleRef st) => s.Select(c => m.Advance(st.Font, st.Size, c)).ToArray();

    // ── символы абзаца ───────────────────────────────────────────────────────

    private List<Ch> BuildChars(Paragraph p, Func<RunProps, LinkRef?, int> styleOf, int markStyle, List<StyleRef> styles, FieldContext fc)
    {
        var list = new List<Ch>(p.Length);
        string? instr = null;
        int state = 0; // 0 — вне поля, 1 — код поля, 2 — результат поля
        bool resultShown = false;
        string? FieldValue(string? code)
        {
            if (code is null) return null;
            string c = code.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? "";
            return c switch
            {
                "PAGE" => fc.PageNumber.ToString(),
                "NUMPAGES" or "SECTIONPAGES" => fc.TotalPages?.ToString(),
                _ => null,
            };
        }
        double Wd(int style, char c) { var s = styles[style]; return m.Advance(s.Font, s.Size, c); }

        foreach (var inl in p.Inlines)
        {
            switch (inl)
            {
                case Run r:
                {
                    int st = styleOf(r.Props, r.Link);
                    string? fieldVal = state == 2 ? FieldValue(instr) : null;
                    foreach (char c in r.Text)
                    {
                        if (fieldVal is not null)
                        {
                            if (!resultShown) { list.Add(new Ch { C = c, Kind = K.Object, Style = st, Disp = fieldVal, W = m.Width(styles[st].Font, styles[st].Size, fieldVal) }); resultShown = true; }
                            else list.Add(new Ch { C = c, Kind = K.Hidden, Style = st });
                            continue;
                        }
                        var kind = c switch { ' ' => K.Space, '­' or '​' => K.ZeroBreak, _ => K.Text };
                        list.Add(new Ch { C = c, Kind = kind, Style = st, W = kind == K.ZeroBreak ? 0 : Wd(st, c) });
                    }
                    break;
                }
                case TabChar t:
                    list.Add(new Ch { C = '\t', Kind = K.Tab, Style = styleOf(t.Props, t.Link) });
                    break;
                case Break b:
                    list.Add(new Ch { C = '\n', Kind = K.Break, Style = styleOf(b.Props, b.Link), BreakKind = b.Kind });
                    break;
                case InlineImage img:
                    list.Add(new Ch { C = '￼', Kind = K.Image, Style = styleOf(img.Props, img.Link), Img = img, W = img.Cx / EmuPerPt });
                    break;
                case RawInline raw when raw.Visible:
                {
                    string disp = raw.Xml.Name == W + "fldSimple" && FieldValue((string?)raw.Xml.Attribute(W + "instr")) is { } v ? v : raw.DisplayText ?? "◊";
                    var rPr = raw.Xml.Descendants(W + "rPr").FirstOrDefault();
                    int st = rPr is null ? markStyle : styleOf(Docx.DocxReader.ReadRunProps(rPr), raw.Link);
                    list.Add(new Ch { C = '￼', Kind = K.Object, Style = st, Disp = disp, W = m.Width(styles[st].Font, styles[st].Size, disp) });
                    break;
                }
                case RawInline raw:
                    foreach (var e in raw.Xml.DescendantsAndSelf())
                    {
                        if (e.Name == W + "fldChar")
                        {
                            switch ((string?)e.Attribute(W + "fldCharType"))
                            {
                                case "begin": state = 1; instr = ""; resultShown = false; break;
                                case "separate": state = 2; break;
                                case "end": state = 0; instr = null; break;
                            }
                        }
                        else if (e.Name == W + "instrText" && state == 1) instr += e.Value;
                    }
                    break;
            }
        }
        return list;
    }

    // ── табуляция ────────────────────────────────────────────────────────────

    private sealed record TabStop(double Pos, string Align);

    private List<TabStop> TabStops(Paragraph p)
    {
        var stops = new SortedDictionary<double, TabStop>();
        void Apply(IEnumerable<XElement> extra)
        {
            foreach (var t in extra.Where(e => e.Name == W + "tabs").SelectMany(e => e.Elements(W + "tab")))
            {
                double pos = (Int(t, "pos") ?? 0) / Tw;
                string val = (string?)t.Attribute(W + "val") ?? "left";
                if (val == "clear") stops.Remove(pos);
                else stops[pos] = new TabStop(pos, val);
            }
        }
        foreach (var st in doc.Styles.Chain(p.Props.StyleId ?? doc.Styles.DefaultParagraphStyleId)) Apply(st.Para.RawExtra);
        Apply(p.Props.RawExtra);
        return [.. stops.Values];
    }

    private static double NextDefault(double abs, double def) => (Math.Floor(abs / def + 1e-6) + 1) * def;

    private double TabWidth(int j, double x, double lineOffset, List<TabStop> tabs, double defTab, List<Ch> chars, EffectivePara ep, double indLeft)
    {
        double abs = lineOffset + x;
        var stop = tabs.FirstOrDefault(t => t.Pos > abs + 0.01 && t.Align is not "bar");
        double target;
        string kind = "left";
        if (ep.IndFirst < 0 && abs < indLeft - 0.01 && (stop is null || stop.Pos > indLeft)) target = indLeft;
        else if (stop is not null) { target = stop.Pos; kind = stop.Align; }
        else target = NextDefault(abs, defTab);
        if (kind is "right" or "end" or "center" or "decimal")
        {
            double after = 0;
            for (int k = j + 1; k < chars.Count && chars[k].Kind is not (K.Tab or K.Break); k++) after += chars[k].W;
            double w = kind == "center" ? target - abs - after / 2 : target - abs - after;
            return Math.Max(0, w);
        }
        return Math.Max(0, target - abs);
    }

    // ── вспомогательное ──────────────────────────────────────────────────────

    private bool HasFlag(Paragraph p, string name)
    {
        bool? v = null;
        foreach (var st in doc.Styles.Chain(p.Props.StyleId ?? doc.Styles.DefaultParagraphStyleId))
            if (st.Para.RawExtra.FirstOrDefault(e => e.Name.LocalName == name) is { } e) v = OnOff(e) != false;
        if (p.Props.RawExtra.FirstOrDefault(e => e.Name.LocalName == name) is { } d) v = OnOff(d) != false;
        return v == true;
    }

    private static bool IsSymbolFont(string f) => f.Contains("Symbol", StringComparison.OrdinalIgnoreCase) || f.Contains("Wingdings", StringComparison.OrdinalIgnoreCase);

    /// <summary>Маркеры из символьных шрифтов (Symbol, Wingdings) заменяются на Юникод.</summary>
    internal static (string Text, bool Mapped) MapBullet(string label, string? font)
    {
        if (label.Length != 1) return (label, false);
        char c = label[0];
        int code = c >= 0xF000 && c <= 0xF0FF ? c - 0xF000 : c;
        bool sym = font is not null && IsSymbolFont(font);
        if (!sym && c < 0xF000) return (label, false);
        string mapped = code switch
        {
            0xB7 => "•", 0xA7 => "▪", 0xD8 => "➢", 0xFC => "✓", 0x76 => "❖", 0x6E => "■", 0x71 => "❑", 0x6C => "●", 0xA8 => "□",
            0x6F => "o", _ => "•",
        };
        return (mapped, true);
    }
}

internal static class Fluent
{
    public static T Also<T>(this T t, Action<T> a) { a(t); return t; }
}

/// <summary>Сравнение пар (свойства, ссылка) по ссылкам на объекты.</summary>
internal sealed class ReferenceEqualityComparerPair : IEqualityComparer<(RunProps, LinkRef?)>
{
    public static readonly ReferenceEqualityComparerPair Instance = new();
    public bool Equals((RunProps, LinkRef?) a, (RunProps, LinkRef?) b) => ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2);
    public int GetHashCode((RunProps, LinkRef?) v) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v.Item1), v.Item2 is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v.Item2));
}
