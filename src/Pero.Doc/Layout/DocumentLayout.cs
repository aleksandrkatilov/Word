using System.Xml.Linq;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Layout;

/// <summary>
/// Разметка документа на страницы (LAYOUT-03): абзацы, таблицы с объединёнными ячейками, повтор строк
/// заголовка, разрывы страниц, колонтитулы с номерами страниц. Одна и та же разметка — для экрана, печати и PDF.
/// </summary>
public static class DocumentLayout
{
    public static LayoutResult Build(Document doc, ITextMeasurer measurer) => new Layouter(doc, new CachedMeasurer(measurer)).Run();
}

internal abstract class Unit
{
    public double Before { get; set; }
    public double After { get; set; }
    public abstract double Height { get; }
}

internal sealed class LineUnit(LineBox line) : Unit
{
    public LineBox Line { get; } = line;
    public bool BreakBefore { get; init; }
    public bool KeepWithNext { get; init; }
    public override double Height => Line.Height;
}

internal sealed class RowUnit : Unit
{
    public List<LineBox> Lines { get; } = [];
    public List<Decor> Decor { get; } = [];
    public double RowHeight { get; set; }
    public bool IsHeader { get; init; }
    public List<RowUnit> Headers { get; init; } = [];
    public override double Height => RowHeight;
}

/// <summary>Содержимое, уложенное без разбиения на страницы (ячейка, колонтитул).</summary>
internal sealed class Stack
{
    public List<LineBox> Lines { get; } = [];
    public List<Decor> Decor { get; } = [];
    public double Height { get; set; }
}

internal sealed class Layouter(Document doc, CachedMeasurer m)
{
    private const double Tw = 20.0;
    private readonly ParagraphLayout _pl = new(doc, m);
    private readonly Dictionary<Paragraph, int> _paraNo = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Paragraph, string?> _labels = new(ReferenceEqualityComparer.Instance);
    private readonly List<PageBox> _pages = [];

    private SectionProps S => doc.Section;

    public LayoutResult Run()
    {
        Number(doc.Body);
        foreach (var hf in doc.HeadersFooters) Number(hf.Blocks);

        double left = S.MarginLeft / Tw, width = Math.Max(36, (S.PageWidth - S.MarginLeft - S.MarginRight) / Tw);
        var units = FlowBlocks(doc.Body, null, left, width, new FieldContext(1, null), paging: true);
        Paginate(units);
        // колонтитулы — после разбиения, когда известно число страниц
        foreach (var page in _pages) PlaceHeaderFooter(page, _pages.Count);
        return new LayoutResult(_pages);
    }

    private void Number(List<Block> story)
    {
        var index = new ParaIndex(story);
        var counter = new NumberingCounter(doc);
        for (int i = 0; i < index.Count; i++)
        {
            var p = index[i].Paragraph;
            _paraNo[p] = i;
            _labels[p] = counter.Next(p);
        }
    }

    // ── поток блоков ─────────────────────────────────────────────────────────

    private List<Unit> FlowBlocks(List<Block> blocks, HeaderFooter? story, double x, double w, FieldContext fc, bool paging, string? tableStyle = null)
    {
        var units = new List<Unit>();
        ParaFlow? prev = null;
        LineUnit? prevLast = null;
        foreach (var b in blocks)
        {
            switch (b)
            {
                case Paragraph p:
                {
                    var flow = _pl.Layout(p, _paraNo.GetValueOrDefault(p), story, x, w, _labels.GetValueOrDefault(p), fc, tableStyle);
                    double before = flow.SpaceBefore;
                    if (prev is not null && flow.Contextual && prev.Contextual && prev.StyleId == flow.StyleId)
                    {
                        before = 0;
                        if (prevLast is not null) prevLast.After = 0;
                    }
                    for (int i = 0; i < flow.Lines.Count; i++)
                    {
                        var line = flow.Lines[i];
                        line.Y = 0;
                        var u = new LineUnit(line)
                        {
                            Before = i == 0 ? before : 0,
                            After = i == flow.Lines.Count - 1 ? flow.SpaceAfter : 0,
                            BreakBefore = i == 0 && flow.PageBreakBefore && paging,
                            KeepWithNext = i == flow.Lines.Count - 1 && flow.KeepNext,
                        };
                        units.Add(u);
                        prevLast = u;
                    }
                    prev = flow;
                    break;
                }
                case Table t:
                    units.AddRange(FlowTable(t, story, x, w, fc));
                    prev = null; prevLast = null;
                    break;
                // нераспознанные блоки (sdt, altChunk…) — на экране не показываются, в файле сохраняются
            }
        }
        return units;
    }

    private Stack StackUnits(List<Unit> units)
    {
        var st = new Stack();
        double y = 0;
        foreach (var u in units)
        {
            y += u.Before;
            if (u is LineUnit lu) { lu.Line.Shift(0, y); st.Lines.Add(lu.Line); }
            else if (u is RowUnit ru)
            {
                foreach (var l in ru.Lines) { l.Shift(0, y); st.Lines.Add(l); }
                foreach (var d in ru.Decor) st.Decor.Add(d.Shifted(0, y));
            }
            y += u.Height + u.After;
        }
        st.Height = y;
        return st;
    }

    // ── таблицы ──────────────────────────────────────────────────────────────

    private sealed record Border(double Width, string Color);

    private sealed class Borders
    {
        public Border? Top, Left, Bottom, Right, InsideH, InsideV;
    }

    private List<RowUnit> FlowTable(Table t, HeaderFooter? story, double areaX, double areaW, FieldContext fc)
    {
        var styleTblPr = TableStyle(t)?.RawTblPr;
        var tblPr = t.RawTblPr;
        int cols = Math.Max(1, Math.Max(t.Grid.Count, t.Rows.Count == 0 ? 1 : t.Rows.Max(r => r.Cells.Sum(c => c.GridSpan))));
        var grid = new double[cols];
        for (int i = 0; i < cols; i++) grid[i] = i < t.Grid.Count ? t.Grid[i] / Tw : 0;
        if (grid.All(g => g <= 0)) for (int i = 0; i < cols; i++) grid[i] = areaW / cols;
        else for (int i = 0; i < cols; i++) if (grid[i] <= 0) grid[i] = 20;
        var colX = new double[cols + 1];
        double indent = (Int(tblPr?.Element(W + "tblInd"), "w") ?? Int(styleTblPr?.Element(W + "tblInd"), "w") ?? 0) / Tw;
        string? jc = (string?)(tblPr?.Element(W + "jc") ?? styleTblPr?.Element(W + "jc"))?.Attribute(W + "val");
        double total = grid.Sum();
        colX[0] = jc switch { "center" => areaX + Math.Max(0, (areaW - total) / 2), "right" or "end" => areaX + Math.Max(0, areaW - total), _ => areaX + indent };
        for (int i = 0; i < cols; i++) colX[i + 1] = colX[i] + grid[i];

        var mar = tblPr?.Element(W + "tblCellMar") ?? styleTblPr?.Element(W + "tblCellMar");
        double Mar(string side, string alt, double def) => (Int(mar?.Element(W + side) ?? mar?.Element(W + alt), "w") is int v ? v / Tw : def);
        double mL = Mar("left", "start", 5.4), mR = Mar("right", "end", 5.4), mT = Mar("top", "top", 0), mB = Mar("bottom", "bottom", 0);
        var borders = ReadBorders(styleTblPr?.Element(W + "tblBorders"), new Borders());
        borders = ReadBorders(tblPr?.Element(W + "tblBorders"), borders);

        int nRows = t.Rows.Count;
        var heights = new double[nRows];
        var cellStacks = new Dictionary<(int Row, int Cell), (Stack St, int Col, double Content)>();
        for (int r = 0; r < nRows; r++)
        {
            var row = t.Rows[r];
            int col = 0;
            double rowMax = 0;
            for (int c = 0; c < row.Cells.Count; c++)
            {
                var cell = row.Cells[c];
                int span = Math.Max(1, cell.GridSpan);
                if (col >= cols) break;
                int lastCol = Math.Min(cols, col + span);
                {
                    double cx = colX[col] + CellMar(cell, "left", "start", mL), cw = Math.Max(4, colX[lastCol] - colX[col] - CellMar(cell, "left", "start", mL) - CellMar(cell, "right", "end", mR));
                    var st = StackUnits(FlowBlocks(cell.Blocks, story, cx, cw, fc, paging: false, (string?)t.RawTblPr?.Element(W + "tblStyle")?.Attribute(W + "val") ?? doc.Styles.ById.Values.FirstOrDefault(x => x.Type == StyleType.Table && x.IsDefault)?.Id));
                    double content = st.Height + mT + mB;
                    cellStacks[(r, c)] = (st, col, content);
                    // продолжение объединения: содержимое (обычно пустой абзац) размечается, но высоту не задаёт
                    if (cell.VMerge == VMerge.None) rowMax = Math.Max(rowMax, content);
                }
                col = lastCol;
            }
            var trH = row.RawTrPr?.Element(W + "trHeight");
            double minH = (Int(trH, "val") ?? 0) / Tw;
            bool exact = (string?)trH?.Attribute(W + "hRule") == "exact";
            heights[r] = exact && minH > 0 ? minH : Math.Max(Math.Max(rowMax, minH), 4);
        }
        // вертикальные объединения: высота охваченных строк должна вместить содержимое
        var spans = new Dictionary<(int Row, int Cell), int>();
        foreach (var ((r, c), (st, col, content)) in cellStacks)
        {
            if (t.Rows[r].Cells[c].VMerge != VMerge.Restart) continue;
            int k = 1;
            while (r + k < nRows && CellAtCol(t.Rows[r + k], col) is { VMerge: VMerge.Continue }) k++;
            spans[(r, c)] = k;
            double sum = 0;
            for (int i = r; i < r + k; i++) sum += heights[i];
            if (content > sum) heights[r + k - 1] += content - sum;
        }

        var units = new List<RowUnit>();
        var headerRows = new List<RowUnit>();
        for (int r = 0; r < nRows; r++)
        {
            var row = t.Rows[r];
            bool isHeader = row.IsHeader && headerRows.Count == r;
            var unit = new RowUnit { RowHeight = heights[r], IsHeader = isHeader, Headers = isHeader ? [] : [.. headerRows] };
            int col = 0;
            for (int c = 0; c < row.Cells.Count && col < cols; c++)
            {
                var cell = row.Cells[c];
                int lastCol = Math.Min(cols, col + Math.Max(1, cell.GridSpan));
                if (cell.VMerge == VMerge.Continue && cellStacks.TryGetValue((r, c), out var hidden))
                {
                    foreach (var l in hidden.St.Lines) { l.Shift(0, mT); unit.Lines.Add(l); }
                }
                else
                {
                    int span = spans.GetValueOrDefault((r, c), 1);
                    double h = 0;
                    for (int i = r; i < r + span; i++) h += heights[i];
                    var tcPr = cell.RawTcPr;
                    // заливка
                    string? fill = (string?)tcPr?.Element(W + "shd")?.Attribute(W + "fill");
                    if (fill is not null && fill != "auto" && fill.Length == 6)
                        unit.Decor.Insert(0, new Decor { Kind = DecorKind.Fill, X1 = colX[col], Y1 = 0, X2 = colX[lastCol], Y2 = h, Color = fill });
                    // содержимое с вертикальным выравниванием
                    if (cellStacks.TryGetValue((r, c), out var cs))
                    {
                        string? va = (string?)tcPr?.Element(W + "vAlign")?.Attribute(W + "val");
                        double free = Math.Max(0, h - cs.Content);
                        double dy = mT + (va == "center" ? free / 2 : va == "bottom" ? free : 0);
                        foreach (var l in cs.St.Lines) { l.Shift(0, dy); unit.Lines.Add(l); }
                        foreach (var d in cs.St.Decor) unit.Decor.Add(d.Shifted(0, dy));
                    }
                    // границы ячейки
                    var cb = ReadBorders(tcPr?.Element(W + "tcBorders"), new Borders
                    {
                        Top = r == 0 ? borders.Top : borders.InsideH,
                        Bottom = r + span >= nRows ? borders.Bottom : borders.InsideH,
                        Left = col == 0 ? borders.Left : borders.InsideV,
                        Right = lastCol >= cols ? borders.Right : borders.InsideV,
                    });
                    double x1 = colX[col], x2 = colX[lastCol];
                    AddLine(unit.Decor, cb.Top, x1, 0, x2, 0);
                    AddLine(unit.Decor, cb.Bottom, x1, h, x2, h);
                    AddLine(unit.Decor, cb.Left, x1, 0, x1, h);
                    AddLine(unit.Decor, cb.Right, x2, 0, x2, h);
                }
                col = lastCol;
            }
            units.Add(unit);
            if (isHeader) headerRows.Add(unit);
        }
        return units;
    }

    private static TableCell? CellAtCol(TableRow row, int gridCol)
    {
        int col = 0;
        foreach (var c in row.Cells) { if (col == gridCol) return c; col += Math.Max(1, c.GridSpan); if (col > gridCol) return null; }
        return null;
    }

    private static double CellMar(TableCell cell, string side, string alt, double def)
    {
        var mar = cell.RawTcPr?.Element(W + "tcMar");
        return Int(mar?.Element(W + side) ?? mar?.Element(W + alt), "w") is int v ? v / Tw : def;
    }

    private static void AddLine(List<Decor> list, Border? b, double x1, double y1, double x2, double y2)
    {
        if (b is null) return;
        list.Add(new Decor { Kind = DecorKind.Line, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Thickness = b.Width, Color = b.Color });
    }

    private static Borders ReadBorders(XElement? el, Borders into)
    {
        if (el is null) return into;
        Border? B(string name, string? alt, Border? current)
        {
            var e = el.Element(W + name) ?? (alt is null ? null : el.Element(W + alt));
            if (e is null) return current;
            string val = (string?)e.Attribute(W + "val") ?? "single";
            if (val is "nil" or "none") return null;
            double sz = Math.Max(0.25, (Int(e, "sz") ?? 4) / 8.0);
            string color = (string?)e.Attribute(W + "color") is { Length: 6 } c && c != "auto" ? c : "000000";
            return new Border(sz, color);
        }
        return new Borders
        {
            Top = B("top", null, into.Top), Bottom = B("bottom", null, into.Bottom),
            Left = B("left", "start", into.Left), Right = B("right", "end", into.Right),
            InsideH = B("insideH", null, into.InsideH), InsideV = B("insideV", null, into.InsideV),
        };
    }

    private Style? TableStyle(Table t)
    {
        string? id = (string?)t.RawTblPr?.Element(W + "tblStyle")?.Attribute(W + "val");
        foreach (var s in doc.Styles.Chain(id).Reverse()) if (s.RawTblPr?.Element(W + "tblBorders") is not null) return s;
        return id is not null && doc.Styles.ById.TryGetValue(id, out var st) ? st : null;
    }

    // ── страницы ─────────────────────────────────────────────────────────────

    private PageBox NewPage()
    {
        var page = new PageBox { Index = _pages.Count, Width = S.PageWidth / Tw, Height = S.PageHeight / Tw };
        // область текста зависит от высоты колонтитулов этой страницы
        double top = S.MarginTop / Tw, bottom = page.Height - S.MarginBottom / Tw;
        if (doc.HeaderFooterFor(HfKind.Header, page.Index) is { } h)
        {
            var st = HfStack(h, page.Index, null);
            top = Math.Max(top, S.HeaderDistance / Tw + st.Height);
        }
        if (doc.HeaderFooterFor(HfKind.Footer, page.Index) is { } f)
        {
            var st = HfStack(f, page.Index, null);
            bottom = Math.Min(bottom, page.Height - S.FooterDistance / Tw - st.Height);
        }
        if (bottom - top < 36) bottom = top + 36;
        page.Body = new RectPt(S.MarginLeft / Tw, top, Math.Max(36, page.Width - (S.MarginLeft + S.MarginRight) / Tw), bottom - top);
        _pages.Add(page);
        return page;
    }

    private Stack HfStack(HeaderFooter hf, int pageIndex, int? total)
    {
        double left = S.MarginLeft / Tw, width = Math.Max(36, (S.PageWidth - S.MarginLeft - S.MarginRight) / Tw);
        return StackUnits(FlowBlocks(hf.Blocks, hf, left, width, new FieldContext(pageIndex + 1, total), paging: false));
    }

    private void PlaceHeaderFooter(PageBox page, int total)
    {
        if (doc.HeaderFooterFor(HfKind.Header, page.Index) is { } h)
        {
            var st = HfStack(h, page.Index, total);
            double y = S.HeaderDistance / Tw;
            foreach (var l in st.Lines) { l.Shift(0, y); l.Page = page.Index; page.HeaderLines.Add(l); }
            foreach (var d in st.Decor) page.HeaderDecor.Add(d.Shifted(0, y));
            page.Header = h;
            page.HeaderArea = new RectPt(0, 0, page.Width, Math.Max(page.Body.Y, y + st.Height));
        }
        else page.HeaderArea = new RectPt(0, 0, page.Width, page.Body.Y);
        if (doc.HeaderFooterFor(HfKind.Footer, page.Index) is { } f)
        {
            var st = HfStack(f, page.Index, total);
            double y = page.Height - S.FooterDistance / Tw - st.Height;
            foreach (var l in st.Lines) { l.Shift(0, y); l.Page = page.Index; page.FooterLines.Add(l); }
            foreach (var d in st.Decor) page.FooterDecor.Add(d.Shifted(0, y));
            page.Footer = f;
            double top = Math.Min(page.Body.Bottom, y);
            page.FooterArea = new RectPt(0, top, page.Width, page.Height - top);
        }
        else page.FooterArea = new RectPt(0, page.Body.Bottom, page.Width, page.Height - page.Body.Bottom);
    }

    private void Paginate(List<Unit> units)
    {
        var page = NewPage();
        double y = page.Body.Y;
        bool content = false;

        void Next()
        {
            page = NewPage();
            y = page.Body.Y;
            content = false;
        }

        void PlaceLines(IEnumerable<LineBox> lines, double dy, PageBox pg, bool repeat = false)
        {
            foreach (var l in lines)
            {
                var line = repeat ? l.CopyShifted(0, dy) : l;
                if (!repeat) line.Shift(0, dy);
                line.Repeat = repeat;
                line.Page = pg.Index;
                pg.Lines.Add(line);
            }
        }

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u is LineUnit lu)
            {
                if (lu.BreakBefore && content) Next();
                double need = u.Before + u.Height;
                if (lu.KeepWithNext && i + 1 < units.Count && units[i + 1] is LineUnit nx && !nx.BreakBefore)
                    need += u.After + nx.Before + nx.Height;
                if (y + need > page.Body.Bottom + 0.01 && content) Next();
                double top = y + u.Before;
                PlaceLines([lu.Line], top, page);
                y = top + u.Height + u.After;
                content = true;
                if (lu.Line.BreakAfter == BreakKind.Page) Next();
            }
            else if (u is RowUnit ru)
            {
                if (y + ru.Height > page.Body.Bottom + 0.01 && content)
                {
                    Next();
                    foreach (var hr in ru.Headers)
                    {
                        PlaceLines(hr.Lines, y, page, repeat: true);
                        foreach (var d in hr.Decor) page.Decor.Add(d.Shifted(0, y));
                        y += hr.Height;
                    }
                }
                if (ru.Height <= page.Body.Bottom - y + 0.01 || ru.Height <= 0)
                {
                    PlaceLines(ru.Lines, y, page);
                    foreach (var d in ru.Decor) page.Decor.Add(d.Shifted(0, y));
                    y += ru.Height;
                    content = true;
                    continue;
                }
                // строка выше страницы: переносим текст по строкам, границы — на первой странице
                double rowTop = y;
                foreach (var d in ru.Decor) page.Decor.Add(Clip(d.Shifted(0, y), page.Body.Bottom));
                double shift = y;
                foreach (var l in ru.Lines.OrderBy(l => l.Y))
                {
                    if (shift + l.Y + l.Height > page.Body.Bottom + 0.01 && shift + l.Y > page.Body.Y)
                    {
                        Next();
                        shift = page.Body.Y - l.Y;
                    }
                    PlaceLines([l], shift, page);
                    y = Math.Max(y, l.Y + l.Height);
                }
                content = true;
                _ = rowTop;
            }
        }
    }

    private static Decor Clip(Decor d, double bottom) =>
        new() { Kind = d.Kind, X1 = d.X1, X2 = d.X2, Y1 = Math.Min(d.Y1, bottom), Y2 = Math.Min(d.Y2, bottom), Thickness = d.Thickness, Color = d.Color };
}
