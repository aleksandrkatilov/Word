using System.Xml.Linq;
using Pero.Doc.Docx;
using Pero.Doc.Model;
using static Pero.Doc.Docx.Ns;

namespace Pero.Doc.Editing;

/// <summary>Списки, ссылки, изображения, таблицы, параметры страницы (FMT-02, OBJ-01, PAGE-01).</summary>
public sealed partial class DocEditor
{
    // ── списки ───────────────────────────────────────────────────────────────

    /// <summary>Включить/выключить маркированный или нумерованный список для выделенных абзацев.</summary>
    public void ToggleList(bool bullet)
    {
        var state = GetFormatState();
        bool off = state.NumId is int cur && Doc.Numbering.IsBullet(cur) == bullet;
        int numId = off ? 0 : EnsureNumbering(bullet);
        ApplyPara(bullet ? "Маркированный список" : "Нумерованный список", p =>
        {
            if (off) { p.Props.NumId = null; p.Props.Ilvl = null; }
            else { p.Props.NumId = numId; p.Props.Ilvl ??= 0; }
        });
    }

    /// <summary>Найти подходящее определение списка или добавить его в numbering.xml.</summary>
    internal int EnsureNumbering(bool bullet)
    {
        foreach (var (num, abs) in Doc.Numbering.NumToAbstract.OrderBy(k => k.Key))
            if (Doc.Numbering.Abstract.TryGetValue(abs, out var lv) && lv.Count > 0 && (lv[0].Format == "bullet") == bullet &&
                (bullet || lv[0].Format == "decimal"))
                return num;

        var store = Doc.Package;
        string? part = store.FindRelTarget(RelNumbering);
        var blank = XDocument.Parse(BlankTemplates.NumberingXml).Root!;
        XDocument xml;
        if (part is null)
        {
            part = (store.MainDir.Length == 0 ? "" : store.MainDir + "/") + "numbering.xml";
            xml = new XDocument(new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName)));
            store.EnsureRelationship(RelNumbering, "numbering.xml", external: false);
            store.EnsureContentTypeOverride(part, "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml");
        }
        else xml = store.LoadXml(part)!;

        var root = xml.Root!;
        int absId = (root.Elements(W + "abstractNum").Select(a => Int(a, "abstractNumId") ?? 0).DefaultIfEmpty(-1).Max()) + 1;
        int numId = (root.Elements(W + "num").Select(n => Int(n, "numId") ?? 0).DefaultIfEmpty(0).Max()) + 1;
        var template = new XElement(blank.Elements(W + "abstractNum").ElementAt(bullet ? 0 : 1));
        template.SetAttributeValue(W + "abstractNumId", absId);
        // abstractNum должны идти перед num (порядок схемы)
        var lastAbs = root.Elements(W + "abstractNum").LastOrDefault();
        if (lastAbs is not null) lastAbs.AddAfterSelf(template); else root.AddFirst(template);
        root.Add(new XElement(W + "num", new XAttribute(W + "numId", numId), new XElement(W + "abstractNumId", new XAttribute(W + "val", absId))));
        store.SaveXml(part, xml);
        Doc.Numbering = StylesReader.ReadNumbering(store);
        return numId;
    }

    // ── ссылки ───────────────────────────────────────────────────────────────

    /// <summary>Сделать выделение ссылкой; при пустом выделении вставить адрес как текст ссылки.</summary>
    public void SetLink(string url, string? text = null)
    {
        EnsureCharStyle("Hyperlink");
        var link = new LinkRef(url, null);
        var sel = Selection;
        Execute("Ссылка", () =>
        {
            if (sel.IsEmpty)
            {
                var at = sel.Active;
                var p = P(at.Para);
                string t = string.IsNullOrEmpty(text) ? url : text;
                var props = InlineOps.PropsAt(p, at.Offset).Props;
                props.StyleId = "Hyperlink";
                InlineOps.InsertInlines(p, at.Offset, [new Run { Text = t, Props = props, Link = link }]);
                return Selection.At(new DocPos(at.Para, at.Offset + t.Length));
            }
            for (int i = sel.Start.Para; i <= sel.End.Para; i++)
            {
                var p = P(i);
                int a = i == sel.Start.Para ? sel.Start.Offset : 0, b = i == sel.End.Para ? sel.End.Offset : p.Length;
                InlineOps.SetLink(p, a, b, link);
                InlineOps.ApplyProps(p, a, b, r => r.StyleId = "Hyperlink");
            }
            return sel;
        });
    }

    public void RemoveLink()
    {
        var sel = Selection;
        Execute("Удалить ссылку", () =>
        {
            for (int i = sel.Start.Para; i <= sel.End.Para; i++)
            {
                var p = P(i);
                foreach (var inl in p.Inlines.Where(x => x.Link is not null).ToList())
                {
                    inl.Link = null;
                    if (inl is Run r && r.Props.StyleId == "Hyperlink") r.Props.StyleId = null;
                }
                InlineOps.Normalize(p);
            }
            return sel;
        });
    }

    private void EnsureCharStyle(string id)
    {
        if (Doc.Styles.ById.ContainsKey(id)) return;
        var template = XDocument.Parse(BlankTemplates.Styles).Root!.Elements(W + "style").First(s => (string?)s.Attribute(W + "styleId") == id);
        Doc.Styles.Added.Add(template);
        Doc.Styles.ById[id] = new Style { Id = id, Name = id, Type = StyleType.Character, Run = DocxReader.ReadRunProps(template.Element(W + "rPr")) };
    }

    // ── изображения ──────────────────────────────────────────────────────────

    /// <summary>Вставить PNG/JPEG в позицию курсора. Ширина ограничивается шириной текста страницы.</summary>
    public void InsertImage(byte[] bytes, string extension, int pixelWidth, int pixelHeight)
    {
        string ext = extension.TrimStart('.').ToLowerInvariant() is "jpg" ? "jpeg" : extension.TrimStart('.').ToLowerInvariant();
        if (ext is not ("png" or "jpeg")) throw new NotSupportedException("Поддерживаются изображения PNG и JPEG.");
        var store = Doc.Package;
        string dir = store.MainDir.Length == 0 ? "" : store.MainDir + "/";
        int n = 1;
        while (store.Parts.ContainsKey($"{dir}media/pero{n}.{ext}")) n++;
        string part = $"{dir}media/pero{n}.{ext}";
        store.Parts[part] = bytes;
        store.Order.Add(part);
        store.EnsureContentTypeDefault(ext, ext == "png" ? "image/png" : "image/jpeg");
        string relId = store.EnsureRelationship(RelImage, $"media/pero{n}.{ext}", external: false, StoryPart);

        const long EmuPerPx = 9525; // 96 dpi
        long cx = pixelWidth * EmuPerPx, cy = pixelHeight * EmuPerPx;
        long maxCx = (Doc.Section.PageWidth - Doc.Section.MarginLeft - Doc.Section.MarginRight) * 635L; // twips → EMU
        if (cx > maxCx && maxCx > 0) { cy = cy * maxCx / cx; cx = maxCx; }
        int docPr = NextDrawingId();
        var drawing = XElement.Parse($"""
<w:drawing xmlns:w="{W}" xmlns:wp="{WP}" xmlns:a="{A}" xmlns:pic="{Pic}" xmlns:r="{R}"><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="{cx}" cy="{cy}"/><wp:effectExtent l="0" t="0" r="0" b="0"/><wp:docPr id="{docPr}" name="Рисунок {docPr}"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect="1"/></wp:cNvGraphicFramePr><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="pero{n}.{ext}"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="{relId}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing>
""");
        Execute("Изображение", () =>
        {
            var at = DeleteSelectionCore();
            var p = P(at.Para);
            InlineOps.InsertInlines(p, at.Offset, [new InlineImage { RelId = relId, Cx = cx, Cy = cy, DrawingXml = drawing, Props = InlineOps.PropsAt(p, at.Offset).Props }]);
            return new Selection(at, new DocPos(at.Para, at.Offset + 1));
        });
    }

    private int NextDrawingId()
    {
        int max = 0;
        void Scan(XElement x)
        {
            foreach (var d in x.DescendantsAndSelf().Where(e => e.Name.LocalName == "docPr"))
                if (int.TryParse((string?)d.Attribute("id"), out int id)) max = Math.Max(max, id);
        }
        foreach (var p in Doc.AllParagraphs())
            foreach (var inl in p.Inlines)
                if (inl is InlineImage im) Scan(im.DrawingXml); else if (inl is RawInline r) Scan(r.Xml);
        return max + 1;
    }

    /// <summary>Пропорционально изменить размер изображения в позиции (новая ширина в EMU).</summary>
    public void ResizeImage(DocPos at, long newCx)
    {
        Execute("Размер изображения", () =>
        {
            var p = P(at.Para);
            int pos = 0;
            foreach (var inl in p.Inlines)
            {
                if (pos == at.Offset && inl is InlineImage im && im.Cx > 0)
                {
                    long cx = Math.Max(9525 * 8, newCx);
                    im.Cy = im.Cy * cx / im.Cx;
                    im.Cx = cx;
                    break;
                }
                pos += inl.Length;
            }
            return Selection;
        });
    }

    /// <summary>Байты изображения по связи; owner — часть, где стоит изображение (null — основная).</summary>
    public byte[]? ImageBytes(string relId, string? owner = null) =>
        Doc.Package.ResolvePartPath(relId, owner) is { } part && Doc.Package.Parts.TryGetValue(part, out var b) ? b : null;

    // ── таблицы ──────────────────────────────────────────────────────────────

    public void InsertTable(int rows, int cols)
    {
        rows = Math.Clamp(rows, 1, 100); cols = Math.Clamp(cols, 1, 30);
        EnsureTableStyle();
        Execute("Таблица", () =>
        {
            var at = DeleteSelectionCore();
            var loc = Index[at.Para];
            int insertAt = loc.Index + 1;
            if (loc.Paragraph.Length > 0 && at.Offset < loc.Paragraph.Length) { SplitCore(at); loc = Index[at.Para]; }
            else if (loc.Paragraph.Length == 0) insertAt = loc.Index; // пустой абзац заменяется таблицей…
            int width = Doc.Section.PageWidth - Doc.Section.MarginLeft - Doc.Section.MarginRight;
            var t = new Table();
            for (int c = 0; c < cols; c++) t.Grid.Add(width / cols);
            for (int r = 0; r < rows; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < cols; c++) { var cell = new TableCell(); cell.Blocks.Add(new Paragraph()); row.Cells.Add(cell); }
                t.Rows.Add(row);
            }
            loc.Container.Insert(insertAt, t);
            // …но после таблицы всегда остаётся абзац, чтобы можно было продолжить текст
            if (insertAt + 1 >= loc.Container.Count || loc.Container[insertAt + 1] is not Paragraph) loc.Container.Insert(insertAt + 1, new Paragraph());
            Rebuild();
            var firstCell = (Paragraph)t.Rows[0].Cells[0].Blocks[0];
            return Selection.At(new DocPos(Index.IndexOf(firstCell), 0));
        });
    }

    private void EnsureTableStyle()
    {
        if (Doc.Styles.ById.ContainsKey("TableGrid")) return;
        var styles = XDocument.Parse(BlankTemplates.Styles).Root!.Elements(W + "style");
        foreach (var id in new[] { "TableNormal", "TableGrid" })
            if (!Doc.Styles.ById.ContainsKey(id))
            {
                var t = styles.First(s => (string?)s.Attribute(W + "styleId") == id);
                Doc.Styles.Added.Add(t);
                Doc.Styles.ById[id] = new Style { Id = id, Name = id, Type = StyleType.Table };
            }
    }

    /// <summary>Таблица и координаты ячейки под курсором.</summary>
    public (Table Table, int Row, int Cell)? CurrentCell()
    {
        var loc = Index[Selection.Active.Para];
        return loc.Table is null ? null : (loc.Table, loc.Row, loc.CellIndex);
    }

    private static int GridColumnOf(TableRow row, int cellIndex) => row.Cells.Take(cellIndex).Sum(c => c.GridSpan);

    public void InsertRow(bool below)
    {
        if (CurrentCell() is not var (t, r, _)) return;
        Execute(below ? "Вставить строку ниже" : "Вставить строку выше", () =>
        {
            var src = t.Rows[r];
            var row = new TableRow { RawTrPr = src.RawTrPr is null ? null : new XElement(src.RawTrPr) };
            row.RawTrPr?.Element(W + "tblHeader")?.Remove();
            int target = below ? r + 1 : r;
            foreach (var c in src.Cells)
            {
                var cell = new TableCell { RawTcPr = c.RawTcPr is null ? null : new XElement(c.RawTcPr), GridSpan = c.GridSpan };
                // внутри вертикального объединения новая ячейка продолжает его
                bool inside = below ? c.VMerge != VMerge.None && NextIsContinue(t, r, GridColumnOf(src, src.Cells.IndexOf(c)))
                                    : c.VMerge == VMerge.Continue;
                cell.VMerge = inside ? VMerge.Continue : VMerge.None;
                cell.Blocks.Add(new Paragraph { Props = FirstPara(c)?.Props.Clone() ?? new ParaProps() });
                row.Cells.Add(cell);
            }
            t.Rows.Insert(target, row);
            Rebuild();
            return Selection.At(new DocPos(Index.IndexOf((Paragraph)row.Cells[0].Blocks[0]), 0));
        });
    }

    private static bool NextIsContinue(Table t, int r, int gridCol)
    {
        if (r + 1 >= t.Rows.Count) return false;
        var next = t.Rows[r + 1];
        int col = 0;
        foreach (var c in next.Cells) { if (col == gridCol) return c.VMerge == VMerge.Continue; col += c.GridSpan; }
        return false;
    }

    private static Paragraph? FirstPara(TableCell c) => c.Blocks.OfType<Paragraph>().FirstOrDefault();

    public void DeleteRow()
    {
        if (CurrentCell() is not var (t, r, _)) return;
        if (t.Rows.Count == 1) { DeleteTable(); return; }
        Execute("Удалить строку", () =>
        {
            // если удаляется начало вертикального объединения, следующая строка его начинает
            if (r + 1 < t.Rows.Count)
            {
                var row = t.Rows[r];
                for (int i = 0; i < row.Cells.Count; i++)
                    if (row.Cells[i].VMerge == VMerge.Restart)
                    {
                        int gc = GridColumnOf(row, i), col = 0;
                        foreach (var c in t.Rows[r + 1].Cells) { if (col == gc && c.VMerge == VMerge.Continue) c.VMerge = VMerge.Restart; col += c.GridSpan; }
                    }
            }
            t.Rows.RemoveAt(r);
            FixLoneMerges(t);
            Rebuild();
            var cell = t.Rows[Math.Min(r, t.Rows.Count - 1)].Cells[0];
            return Selection.At(new DocPos(Index.IndexOf((Paragraph)cell.Blocks[0]), 0));
        });
    }

    /// <summary>Вставить столбец слева/справа от текущего.</summary>
    public void InsertColumn(bool right)
    {
        if (CurrentCell() is not var (t, r, ci)) return;
        Execute(right ? "Вставить столбец справа" : "Вставить столбец слева", () =>
        {
            var curRow = t.Rows[r];
            int gc = GridColumnOf(curRow, ci) + (right ? curRow.Cells[ci].GridSpan : 0);
            int width = gc < t.Grid.Count ? t.Grid[gc] : t.Grid[^1];
            int newW = Math.Max(400, width / 2);
            if (gc < t.Grid.Count) t.Grid[gc] = Math.Max(400, width - newW); else t.Grid[^1] = Math.Max(400, t.Grid[^1] - newW);
            t.Grid.Insert(gc, newW);
            TableCell? inserted = null;
            foreach (var row in t.Rows)
            {
                int col = 0, i = 0;
                for (; i < row.Cells.Count; i++)
                {
                    if (col == gc) break;
                    if (col + row.Cells[i].GridSpan > gc) { row.Cells[i].GridSpan++; i = -1; break; } // колонка внутри объединённой ячейки
                    col += row.Cells[i].GridSpan;
                }
                if (i < 0) continue;
                var cell = new TableCell();
                cell.Blocks.Add(new Paragraph());
                row.Cells.Insert(Math.Min(i, row.Cells.Count), cell);
                if (ReferenceEquals(row, curRow)) inserted = cell;
            }
            Rebuild();
            var target = inserted ?? curRow.Cells[ci];
            return Selection.At(new DocPos(Index.IndexOf((Paragraph)target.Blocks[0]), 0));
        });
    }

    public void DeleteColumn()
    {
        if (CurrentCell() is not var (t, r, ci)) return;
        if (t.Grid.Count <= 1) { DeleteTable(); return; }
        Execute("Удалить столбец", () =>
        {
            int gc = GridColumnOf(t.Rows[r], ci);
            foreach (var row in t.Rows)
            {
                int col = 0;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    var c = row.Cells[i];
                    if (gc >= col && gc < col + c.GridSpan)
                    {
                        if (c.GridSpan > 1) c.GridSpan--; else row.Cells.RemoveAt(i);
                        break;
                    }
                    col += c.GridSpan;
                }
            }
            t.Grid.RemoveAt(gc);
            Rebuild();
            var row0 = t.Rows[r];
            var cell = row0.Cells[Math.Min(ci, row0.Cells.Count - 1)];
            return Selection.At(new DocPos(Index.IndexOf((Paragraph)cell.Blocks[0]), 0));
        });
    }

    /// <summary>Ширина колонки сетки (twips); соседняя справа компенсирует, общая ширина сохраняется.</summary>
    public void SetColumnWidth(Table t, int gridCol, int twips)
    {
        Execute("Ширина столбца", () =>
        {
            int old = t.Grid[gridCol];
            int w = Math.Max(300, twips);
            if (gridCol + 1 < t.Grid.Count)
            {
                int pair = old + t.Grid[gridCol + 1];
                w = Math.Min(w, pair - 300);
                t.Grid[gridCol + 1] = pair - w;
            }
            t.Grid[gridCol] = w;
            // явные ширины ячеек tcW пересчитываются из сетки при записи
            foreach (var row in t.Rows) foreach (var c in row.Cells) c.RawTcPr?.Element(W + "tcW")?.Remove();
            return Selection;
        });
    }

    public void DeleteTable()
    {
        if (CurrentCell() is not var (t, _, _)) return;
        Execute("Удалить таблицу", () =>
        {
            var container = FindContainer(StoryBlocks, t)!;
            int i = container.IndexOf(t);
            container.RemoveAt(i);
            if (container.Count == 0 || i >= container.Count || container[Math.Min(i, container.Count - 1)] is not Paragraph)
                container.Insert(Math.Min(i, container.Count), new Paragraph());
            Rebuild();
            var p = container.Skip(Math.Min(i, container.Count - 1)).OfType<Paragraph>().First();
            return Selection.At(new DocPos(Index.IndexOf(p), 0));
        });
    }

    /// <summary>Объединить выделенные ячейки одной строки по горизонтали.</summary>
    public void MergeCellsInRow()
    {
        var a = Index[Selection.Start.Para];
        var b = Index[Selection.End.Para];
        if (a.Table is null || !ReferenceEquals(a.Table, b.Table) || a.Row != b.Row || a.CellIndex == b.CellIndex) return;
        Execute("Объединить ячейки", () =>
        {
            var row = a.Table.Rows[a.Row];
            var first = row.Cells[a.CellIndex];
            for (int i = b.CellIndex; i > a.CellIndex; i--)
            {
                var c = row.Cells[i];
                first.GridSpan += c.GridSpan;
                foreach (var blk in c.Blocks) if (blk is not Paragraph { Length: 0 }) first.Blocks.Add(blk);
                row.Cells.RemoveAt(i);
            }
            Rebuild();
            return Selection.At(new DocPos(Index.IndexOf((Paragraph)first.Blocks[0]), 0));
        });
    }

    private static void FixLoneMerges(Table t)
    {
        // «продолжение» без начала сверху становится обычной ячейкой
        for (int r = 0; r < t.Rows.Count; r++)
        {
            int col = 0;
            foreach (var c in t.Rows[r].Cells)
            {
                if (c.VMerge == VMerge.Continue && (r == 0 || CellAtGrid(t.Rows[r - 1], col) is not { VMerge: not VMerge.None }))
                    c.VMerge = VMerge.None;
                col += c.GridSpan;
            }
        }
    }

    private static TableCell? CellAtGrid(TableRow row, int gridCol)
    {
        int col = 0;
        foreach (var c in row.Cells) { if (col == gridCol) return c; col += c.GridSpan; }
        return null;
    }

    private static List<Block>? FindContainer(List<Block> blocks, Table target)
    {
        if (blocks.Contains(target)) return blocks;
        foreach (var t in blocks.OfType<Table>())
            foreach (var row in t.Rows)
                foreach (var c in row.Cells)
                    if (FindContainer(c.Blocks, target) is { } found) return found;
        return null;
    }

    // ── страница ─────────────────────────────────────────────────────────────

    public void SetPageSize(int widthTwips, int heightTwips) => Execute("Размер страницы", () =>
    {
        bool landscape = Doc.Section.Orientation == Orientation.Landscape;
        var (w, h) = landscape == (widthTwips > heightTwips) ? (widthTwips, heightTwips) : (heightTwips, widthTwips);
        Doc.Section.PageWidth = w; Doc.Section.PageHeight = h;
        return Selection;
    });

    public void SetOrientation(Orientation o) => Execute("Ориентация", () =>
    {
        var s = Doc.Section;
        if (s.Orientation == o) return Selection;
        (s.PageWidth, s.PageHeight) = (s.PageHeight, s.PageWidth);
        // поля поворачиваются вместе со страницей, как в Word
        (s.MarginTop, s.MarginRight, s.MarginBottom, s.MarginLeft) = (s.MarginLeft, s.MarginTop, s.MarginRight, s.MarginBottom);
        s.Orientation = o;
        return Selection;
    });

    public void SetMargins(int? top, int? right, int? bottom, int? left) => Execute("Поля", () =>
    {
        var s = Doc.Section;
        if (top is not null) s.MarginTop = Math.Max(0, top.Value);
        if (right is not null) s.MarginRight = Math.Max(0, right.Value);
        if (bottom is not null) s.MarginBottom = Math.Max(0, bottom.Value);
        if (left is not null) s.MarginLeft = Math.Max(0, left.Value);
        return Selection;
    });

    // ── колонтитулы и номера страниц ─────────────────────────────────────────

    /// <summary>Колонтитул для правки: существующий для страницы или новый «обычный» (отменяемо).</summary>
    public HeaderFooter OpenHeaderFooter(HfKind kind, int pageIndex)
    {
        if (Doc.HeaderFooterFor(kind, pageIndex) is { } existing) { EnterStory(existing); return existing; }
        string type = pageIndex == 0 && Doc.Section.TitlePage ? "first" : "default";
        EnterStory(null);
        HeaderFooter? created = null;
        Execute(kind == HfKind.Header ? "Верхний колонтитул" : "Нижний колонтитул", () =>
        {
            string dir = Doc.Package.MainDir.Length == 0 ? "" : Doc.Package.MainDir + "/";
            string prefix = dir + (kind == HfKind.Header ? "header" : "footer");
            int n = 1;
            while (Doc.Package.Parts.ContainsKey($"{prefix}{n}.xml") || Doc.HeadersFooters.Any(h => h.PartPath == $"{prefix}{n}.xml")) n++;
            created = new HeaderFooter { Kind = kind, Type = type, PartPath = $"{prefix}{n}.xml", Modified = true };
            var p = new Paragraph();
            if (Doc.Styles.ById.ContainsKey(kind == HfKind.Header ? "Header" : "Footer")) p.Props.StyleId = kind == HfKind.Header ? "Header" : "Footer";
            created.Blocks.Add(p);
            Doc.HeadersFooters.Add(created);
            return Selection;
        });
        EnterStory(created);
        return created!;
    }

    /// <summary>Особый колонтитул первой страницы.</summary>
    public void SetTitlePage(bool on)
    {
        if (Doc.Section.TitlePage == on) return;
        Execute("Особый колонтитул первой страницы", () => { Doc.Section.TitlePage = on; return Selection; });
    }

    /// <summary>Вставить поле номера страницы (PAGE) или числа страниц (NUMPAGES) в позицию курсора.</summary>
    public void InsertPageField(bool totalPages = false)
    {
        Execute("Номер страницы", () =>
        {
            var at = DeleteSelectionCore();
            var p = P(at.Para);
            var (props, _) = InlineOps.PropsAt(p, at.Offset);
            var run = new XElement(W + "r");
            if (DocxWriter.BuildRPr(props) is { } rPr) run.Add(rPr);
            run.Add(new XElement(W + "t", "1"));
            var fld = new XElement(W + "fldSimple", new XAttribute(W + "instr", totalPages ? " NUMPAGES " : " PAGE "), run);
            InlineOps.InsertInlines(p, at.Offset, [new RawInline(fld, visible: true) { DisplayText = "1" }]);
            return Selection.At(new DocPos(at.Para, at.Offset + 1));
        });
    }
}
