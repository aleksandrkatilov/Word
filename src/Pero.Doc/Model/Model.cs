using System.Xml.Linq;

namespace Pero.Doc.Model;

/// <summary>
/// Модель документа (CORE-01). Известные свойства — типизированные поля; всё, что ядро не понимает,
/// хранится исходным XML (<see cref="RawExtra"/>, <see cref="RawInline"/>, <see cref="RawBlock"/>) и
/// записывается обратно без изменений.
/// </summary>
public sealed class Document
{
    public List<Block> Body { get; } = [];
    /// <summary>Свойства последнего раздела документа (w:body/w:sectPr).</summary>
    public SectionProps Section { get; set; } = new();
    public StyleSheet Styles { get; set; } = new();
    public Numbering Numbering { get; set; } = new();
    /// <summary>Исходный пакет: все части, кроме основной разметки, переносятся без изменений.</summary>
    internal Docx.PackageStore Package { get; set; } = Docx.PackageStore.Blank();

    public Document Clone()
    {
        var d = new Document { Section = Section.Clone(), Styles = Styles, Numbering = Numbering, Package = Package };
        d.Body.AddRange(Body.Select(b => b.Clone()));
        return d;
    }

    public IEnumerable<Paragraph> AllParagraphs() => Walk(Body);

    private static IEnumerable<Paragraph> Walk(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            if (b is Paragraph p) yield return p;
            else if (b is Table t)
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var inner in Walk(cell.Blocks)) yield return inner;
        }
    }

    public static Document CreateBlank()
    {
        var d = new Document();
        d.Body.Add(new Paragraph());
        return d;
    }
}

public abstract class Block
{
    public abstract Block Clone();
}

/// <summary>Блок, который ядро не редактирует (например, w:sdt) — переносится как есть.</summary>
public sealed class RawBlock(XElement xml) : Block
{
    public XElement Xml { get; } = xml;
    public override Block Clone() => new RawBlock(new XElement(Xml));
}

public enum Align { Left, Center, Right, Justify }
public enum LineRule { Auto, AtLeast, Exact }

public sealed class ParaProps
{
    public string? StyleId { get; set; }
    public Align? Align { get; set; }
    /// <summary>Интервалы в twips (1/20 пт).</summary>
    public int? SpaceBefore { get; set; }
    public int? SpaceAfter { get; set; }
    /// <summary>Межстрочный: для Auto — 240 = одинарный.</summary>
    public int? Line { get; set; }
    public LineRule? LineRule { get; set; }
    public int? IndLeft { get; set; }
    public int? IndRight { get; set; }
    /// <summary>Отступ первой строки (&gt;0) или выступ (&lt;0), twips.</summary>
    public int? IndFirst { get; set; }
    public int? NumId { get; set; }
    public int? Ilvl { get; set; }
    public bool PageBreakBefore { get; set; }
    public bool KeepNext { get; set; }
    /// <summary>Исходные w:spacing / w:ind — при записи переписываются только известные атрибуты.</summary>
    public XElement? RawSpacing { get; set; }
    public XElement? RawInd { get; set; }
    /// <summary>Нераспознанные дочерние элементы w:pPr (табуляции, границы, sectPr раздела и т.п.).</summary>
    public List<XElement> RawExtra { get; private set; } = [];
    /// <summary>Свойства знака абзаца (w:pPr/w:rPr) — переносятся как есть.</summary>
    public XElement? MarkRunProps { get; set; }

    public ParaProps Clone()
    {
        var c = (ParaProps)MemberwiseClone();
        c.RawExtra = RawExtra.Select(x => new XElement(x)).ToList();
        c.MarkRunProps = MarkRunProps is null ? null : new XElement(MarkRunProps);
        c.RawSpacing = RawSpacing is null ? null : new XElement(RawSpacing);
        c.RawInd = RawInd is null ? null : new XElement(RawInd);
        return c;
    }
}

public sealed class Paragraph : Block
{
    public ParaProps Props { get; set; } = new();
    public List<Inline> Inlines { get; } = [];

    /// <summary>Длина абзаца в позициях: символ текста = 1, объект = 1.</summary>
    public int Length => Inlines.Sum(i => i.Length);

    public string Text => string.Concat(Inlines.Select(i => i is Run r ? r.Text : i is TabChar ? "\t" : i is Break ? "\n" : "￼"));

    public override Block Clone()
    {
        var p = new Paragraph { Props = Props.Clone() };
        p.Inlines.AddRange(Inlines.Select(i => i.Clone()));
        return p;
    }
}

public sealed class RunProps : IEquatable<RunProps>
{
    public string? StyleId { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    /// <summary>Значение w:u (single, double…); "none" — явно без подчёркивания.</summary>
    public string? Underline { get; set; }
    public bool? Strike { get; set; }
    /// <summary>Цвет RRGGBB или "auto".</summary>
    public string? Color { get; set; }
    /// <summary>Маркер: имя цвета Word (yellow…) или RRGGBB через w:shd.</summary>
    public string? Highlight { get; set; }
    public string? Font { get; set; }
    /// <summary>Размер в полупунктах (w:sz).</summary>
    public int? SizeHalfPt { get; set; }
    public string? VertAlign { get; set; }
    /// <summary>Тема шрифта (minorHAnsi/majorHAnsi), если шрифт задан темой.</summary>
    public string? FontTheme { get; set; }
    /// <summary>Исходные w:rFonts / w:color / w:u — при записи меняются только известные атрибуты.</summary>
    public XElement? RawFonts { get; set; }
    public XElement? RawColor { get; set; }
    public XElement? RawUnderline { get; set; }
    /// <summary>Нераспознанные элементы w:rPr в исходном порядке.</summary>
    public List<XElement> RawExtra { get; private set; } = [];

    public RunProps Clone()
    {
        var c = (RunProps)MemberwiseClone();
        c.RawExtra = RawExtra.Select(x => new XElement(x)).ToList();
        c.RawFonts = RawFonts is null ? null : new XElement(RawFonts);
        c.RawColor = RawColor is null ? null : new XElement(RawColor);
        c.RawUnderline = RawUnderline is null ? null : new XElement(RawUnderline);
        return c;
    }

    public bool Equals(RunProps? o) =>
        o is not null && StyleId == o.StyleId && Bold == o.Bold && Italic == o.Italic && Underline == o.Underline &&
        Strike == o.Strike && Color == o.Color && Highlight == o.Highlight && Font == o.Font && SizeHalfPt == o.SizeHalfPt &&
        VertAlign == o.VertAlign && FontTheme == o.FontTheme && Same(RawFonts, o.RawFonts) && Same(RawColor, o.RawColor) &&
        Same(RawUnderline, o.RawUnderline) && RawExtra.Count == o.RawExtra.Count &&
        RawExtra.Zip(o.RawExtra).All(z => XNode.DeepEquals(z.First, z.Second));

    private static bool Same(XElement? a, XElement? b) => a is null ? b is null : b is not null && XNode.DeepEquals(a, b);

    public override bool Equals(object? obj) => Equals(obj as RunProps);
    public override int GetHashCode() => HashCode.Combine(StyleId, Bold, Italic, Underline, Font, SizeHalfPt, Color);
}

public abstract class Inline
{
    /// <summary>Внешняя гиперссылка (адрес) или закладка (#имя), к которой относится элемент.</summary>
    public LinkRef? Link { get; set; }
    public virtual int Length => 1;
    public abstract Inline Clone();
    protected T CopyBase<T>(T target) where T : Inline { target.Link = Link; return target; }
}

public sealed record LinkRef(string? Url, string? Anchor, XElement? RawHyperlinkProps = null)
{
    public bool SameAs(LinkRef? other) => other is not null && Url == other.Url && Anchor == other.Anchor;
}

public sealed class Run : Inline
{
    public string Text { get; set; } = "";
    public RunProps Props { get; set; } = new();
    public override int Length => Text.Length;
    public override Inline Clone() => CopyBase(new Run { Text = Text, Props = Props.Clone() });
}

public sealed class TabChar : Inline
{
    public RunProps Props { get; set; } = new();
    public override Inline Clone() => CopyBase(new TabChar { Props = Props.Clone() });
}

public enum BreakKind { Line, Page, Column }

public sealed class Break : Inline
{
    public BreakKind Kind { get; set; }
    public RunProps Props { get; set; } = new();
    public override Inline Clone() => CopyBase(new Break { Kind = Kind, Props = Props.Clone() });
}

/// <summary>Встроенное изображение (wp:inline). Размер в EMU; исходная разметка drawing сохраняется.</summary>
public sealed class InlineImage : Inline
{
    public string RelId { get; set; } = "";
    public long Cx { get; set; }
    public long Cy { get; set; }
    public XElement DrawingXml { get; set; } = new("drawing");
    public RunProps Props { get; set; } = new();
    public override Inline Clone() =>
        CopyBase(new InlineImage { RelId = RelId, Cx = Cx, Cy = Cy, DrawingXml = new XElement(DrawingXml), Props = Props.Clone() });
}

/// <summary>
/// Элемент, который ядро не редактирует, но обязано сохранить на своём месте: поля (fldChar/instrText),
/// закладки, плавающие объекты, примечания и т.п. Занимает одну позицию, если видим (<see cref="Visible"/>).
/// </summary>
public sealed class RawInline(XElement xml, bool visible) : Inline
{
    public XElement Xml { get; } = xml;
    public bool Visible { get; } = visible;
    /// <summary>Текст для отображения (например, результат поля), если есть.</summary>
    public string? DisplayText { get; init; }
    public override int Length => Visible ? 1 : 0;
    public override Inline Clone() => CopyBase(new RawInline(new XElement(Xml), Visible) { DisplayText = DisplayText });
}

public sealed class Table : Block
{
    public XElement? RawTblPr { get; set; }
    /// <summary>Ширины колонок сетки, twips.</summary>
    public List<int> Grid { get; } = [];
    public List<TableRow> Rows { get; } = [];
    /// <summary>Прочие дочерние элементы w:tbl (например, вложенные sdt) — в конце таблицы.</summary>
    public List<XElement> Extra { get; } = [];

    public override Block Clone()
    {
        var t = new Table { RawTblPr = RawTblPr is null ? null : new XElement(RawTblPr) };
        t.Grid.AddRange(Grid);
        t.Extra.AddRange(Extra.Select(x => new XElement(x)));
        t.Rows.AddRange(Rows.Select(r => r.Clone()));
        return t;
    }
}

public sealed class TableRow
{
    public XElement? RawTrPr { get; set; }
    public bool IsHeader { get; set; }
    public List<TableCell> Cells { get; } = [];
    /// <summary>Прочие дочерние элементы w:tr (tblPrEx и т.п.) — перед ячейками.</summary>
    public List<XElement> Extra { get; } = [];

    public TableRow Clone()
    {
        var r = new TableRow { RawTrPr = RawTrPr is null ? null : new XElement(RawTrPr), IsHeader = IsHeader };
        r.Extra.AddRange(Extra.Select(x => new XElement(x)));
        r.Cells.AddRange(Cells.Select(c => c.Clone()));
        return r;
    }
}

public enum VMerge { None, Restart, Continue }

public sealed class TableCell
{
    public XElement? RawTcPr { get; set; }
    public int GridSpan { get; set; } = 1;
    public VMerge VMerge { get; set; }
    public List<Block> Blocks { get; } = [];

    public TableCell Clone()
    {
        var c = new TableCell { RawTcPr = RawTcPr is null ? null : new XElement(RawTcPr), GridSpan = GridSpan, VMerge = VMerge };
        c.Blocks.AddRange(Blocks.Select(b => b.Clone()));
        return c;
    }
}

public enum Orientation { Portrait, Landscape }

/// <summary>Параметры страницы в twips. Исходный w:sectPr сохраняется, известные поля переписываются.</summary>
public sealed class SectionProps
{
    public int PageWidth { get; set; } = 11906;   // A4
    public int PageHeight { get; set; } = 16838;
    public Orientation Orientation { get; set; }
    public int MarginTop { get; set; } = 1134;
    public int MarginBottom { get; set; } = 1134;
    public int MarginLeft { get; set; } = 1701;
    public int MarginRight { get; set; } = 850;
    public int HeaderDistance { get; set; } = 708;
    public int FooterDistance { get; set; } = 708;
    public XElement? RawSectPr { get; set; }

    public SectionProps Clone()
    {
        var c = (SectionProps)MemberwiseClone();
        c.RawSectPr = RawSectPr is null ? null : new XElement(RawSectPr);
        return c;
    }
}
