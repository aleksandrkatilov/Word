using System.Xml.Linq;
using Pero.Doc.Docx;
using Pero.Doc.Model;

namespace Pero.Doc.Editing;

/// <summary>Состояние оформления выделения для панели (AC-03). null — смешанное значение.</summary>
public sealed record FormatState(string? Font, double? SizePt, bool? Bold, bool? Italic, bool? Underline, string? Color,
    Align? Align, string? StyleId, int? LineSpacing, int? NumId, bool IsBulletList, LinkRef? Link);

/// <summary>Оформление символов и абзацев, стили, списки, очистка и копирование формата (FMT-01, FMT-02).</summary>
public sealed partial class DocEditor
{
    public static readonly string[] HighlightNames =
        ["yellow", "green", "cyan", "magenta", "blue", "red", "darkBlue", "darkCyan", "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray", "black"];

    // ── состояние ────────────────────────────────────────────────────────────

    public FormatState GetFormatState()
    {
        var s = Selection;
        var runs = new List<EffectiveRun>();
        LinkRef? link = null;
        bool first = true, sameLink = true;
        if (s.IsEmpty)
        {
            var p = P(s.Active.Para);
            var (props, l) = InlineOps.PropsAt(p, s.Active.Offset);
            runs.Add(Resolve.Run(Doc, p, PendingProps ?? props));
            link = AtLink(p, s.Active.Offset);
        }
        else
        {
            for (int i = s.Start.Para; i <= s.End.Para; i++)
            {
                var p = P(i);
                int a = i == s.Start.Para ? s.Start.Offset : 0, b = i == s.End.Para ? s.End.Offset : p.Length, pos = 0;
                foreach (var inl in p.Inlines)
                {
                    int len = inl.Length;
                    if (len > 0 && pos + len > a && pos < b)
                    {
                        if (inl is Run r) runs.Add(Resolve.Run(Doc, p, r.Props));
                        if (first) { link = inl.Link; first = false; } else if (!SameLink(link, inl.Link)) sameLink = false;
                    }
                    pos += len;
                }
            }
            if (runs.Count == 0) { var p = P(s.Start.Para); runs.Add(Resolve.Run(Doc, p, InlineOps.PropsAt(p, s.Start.Offset).Props)); }
        }
        T? All<T>(Func<EffectiveRun, T> f) where T : class { var v = f(runs[0]); return runs.All(r => Equals(f(r), v)) ? v : null; }
        TS? AllS<TS>(Func<EffectiveRun, TS> f) where TS : struct { var v = f(runs[0]); return runs.All(r => Equals(f(r), v)) ? v : null; }

        var paras = SelectedParagraphs().Select(p => Resolve.Para(Doc, p)).ToList();
        Align? align = paras.All(p => p.Align == paras[0].Align) ? paras[0].Align : null;
        string? style = paras.All(p => p.StyleId == paras[0].StyleId) ? paras[0].StyleId : null;
        int? line = paras.All(p => p.Line == paras[0].Line && p.LineRule == LineRule.Auto) ? paras[0].Line : null;
        int? num = paras.All(p => p.NumId == paras[0].NumId) ? paras[0].NumId : null;
        return new FormatState(All(r => r.Font), AllS(r => r.SizePt), AllS(r => r.Bold), AllS(r => r.Italic), AllS(r => r.Underline),
            All(r => r.Color), align, style, line, num, num is int n && Doc.Numbering.IsBullet(n), sameLink ? link : null);
    }

    private static bool SameLink(LinkRef? a, LinkRef? b) => a is null ? b is null : a.SameAs(b);

    private static LinkRef? AtLink(Paragraph p, int offset)
    {
        int pos = 0;
        foreach (var inl in p.Inlines)
        {
            if (inl.Length > 0 && offset >= pos && offset <= pos + inl.Length && inl.Link is not null) return inl.Link;
            pos += inl.Length;
        }
        return null;
    }

    // ── символы ──────────────────────────────────────────────────────────────

    /// <summary>Изменить оформление выделения; при пустом выделении — оформление следующего ввода.</summary>
    public void ApplyRun(string name, Action<RunProps> change)
    {
        if (Selection.IsEmpty)
        {
            var p = P(Selection.Active.Para);
            var props = (PendingProps ?? InlineOps.PropsAt(p, Selection.Active.Offset).Props).Clone();
            change(props);
            PendingProps = props;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        var sel = Selection;
        Execute(name, () =>
        {
            for (int i = sel.Start.Para; i <= sel.End.Para; i++)
            {
                var p = P(i);
                int a = i == sel.Start.Para ? sel.Start.Offset : 0, b = i == sel.End.Para ? sel.End.Offset : p.Length;
                InlineOps.ApplyProps(p, a, b, change);
            }
            return sel;
        });
    }

    public void ToggleBold() { bool on = GetFormatState().Bold != true; ApplyRun(Loc.L("Полужирный"), r => r.Bold = on); }
    public void ToggleItalic() { bool on = GetFormatState().Italic != true; ApplyRun(Loc.L("Курсив"), r => r.Italic = on); }
    public void ToggleUnderline() { bool on = GetFormatState().Underline != true; ApplyRun(Loc.L("Подчёркивание"), r => r.Underline = on ? "single" : "none"); }
    public void SetFont(string font) => ApplyRun(Loc.L("Шрифт"), r => { r.Font = font; r.FontTheme = null; });
    public void SetSize(double pt) => ApplyRun(Loc.L("Размер"), r => r.SizeHalfPt = (int)Math.Round(Math.Clamp(pt, 1, 1638) * 2));
    public void SetColor(string rrggbb) => ApplyRun(Loc.L("Цвет текста"), r => { r.Color = rrggbb; r.RawColor = null; });
    public void SetHighlight(string? name) => ApplyRun(Loc.L("Маркер"), r => r.Highlight = name ?? "none");

    /// <summary>Шаг размера шрифта (−/+ на панели): по 1 пт.</summary>
    public void StepSize(int delta)
    {
        double cur = GetFormatState().SizePt ?? 11;
        SetSize(Math.Clamp(Math.Round(cur) + delta, 1, 1638));
    }

    // ── абзацы ───────────────────────────────────────────────────────────────

    public void ApplyPara(string name, Action<Paragraph> change)
    {
        var sel = Selection;
        Execute(name, () =>
        {
            for (int i = sel.Start.Para; i <= sel.End.Para; i++) change(P(i));
            return sel;
        });
    }

    public void SetAlign(Align a) => ApplyPara(Loc.L("Выравнивание"), p => p.Props.Align = a);

    /// <summary>Межстрочный интервал: 240 — одинарный, 276 — 1,15, 360 — 1,5, 480 — двойной.</summary>
    public void SetLineSpacing(int line240) => ApplyPara(Loc.L("Межстрочный интервал"), p => { p.Props.Line = line240; p.Props.LineRule = LineRule.Auto; });

    /// <summary>Отступ на 1,25 см (708 twips — шаг табуляции по умолчанию в русском Word).</summary>
    public void Indent(int deltaTwips) => ApplyPara(deltaTwips > 0 ? Loc.L("Увеличить отступ") : Loc.L("Уменьшить отступ"), p =>
    {
        if (p.Props.NumId is not null)
        {
            p.Props.Ilvl = Math.Clamp((p.Props.Ilvl ?? 0) + Math.Sign(deltaTwips), 0, 8);
            return;
        }
        int cur = Resolve.Para(Doc, p).IndLeft;
        p.Props.IndLeft = Math.Max(0, cur + deltaTwips);
    });

    public void SetIndents(int? left, int? right, int? first) => ApplyPara(Loc.L("Отступы"), p =>
    {
        if (left is not null) p.Props.IndLeft = Math.Max(0, left.Value);
        if (right is not null) p.Props.IndRight = Math.Max(0, right.Value);
        if (first is not null) p.Props.IndFirst = first;
    });

    /// <summary>Применить стиль абзаца (Normal, Title, Heading1…3). Отсутствующий стиль добавляется в документ.</summary>
    public void ApplyStyle(string styleId)
    {
        EnsureStyle(styleId);
        bool normal = styleId == "Normal" || styleId == Doc.Styles.DefaultParagraphStyleId;
        ApplyPara(Loc.L("Стиль"), p => p.Props.StyleId = normal ? null : styleId);
    }

    public void ClearFormatting()
    {
        var sel = Selection;
        Execute(Loc.L("Очистить форматирование"), () =>
        {
            for (int i = sel.Start.Para; i <= sel.End.Para; i++)
            {
                var p = P(i);
                int a = sel.IsEmpty ? 0 : i == sel.Start.Para ? sel.Start.Offset : 0;
                int b = sel.IsEmpty ? p.Length : i == sel.End.Para ? sel.End.Offset : p.Length;
                InlineOps.ApplyProps(p, a, b, r =>
                {
                    var keepStyle = r.StyleId == "Hyperlink" ? r.StyleId : null;
                    r.Bold = r.Italic = r.Strike = null; r.Underline = null; r.Color = null; r.Highlight = null;
                    r.Font = null; r.FontTheme = null; r.SizeHalfPt = null; r.VertAlign = null;
                    r.RawFonts = r.RawColor = r.RawUnderline = null; r.StyleId = keepStyle;
                    r.RawExtra.RemoveAll(x => x.Name.LocalName is "szCs" or "bCs" or "iCs" or "shd" or "caps" or "smallCaps" or "spacing");
                });
                p.Props.Align = null; p.Props.Line = null; p.Props.LineRule = null;
                p.Props.IndLeft = p.Props.IndRight = p.Props.IndFirst = null;
            }
            return sel;
        });
        PendingProps = null;
    }

    // ── копирование формата ──────────────────────────────────────────────────

    private (RunProps Run, ParaProps Para)? _painter;
    public bool HasCopiedFormat => _painter is not null;

    public void CopyFormat()
    {
        var p = P(Selection.Start.Para);
        var props = InlineOps.PropsAt(p, Selection.IsEmpty ? Selection.Active.Offset : Selection.Start.Offset + 1).Props;
        _painter = (props.Clone(), p.Props.Clone());
    }

    public void PasteFormat()
    {
        if (_painter is not { } f) return;
        var sel = Selection;
        Execute(Loc.L("Копирование формата"), () =>
        {
            for (int i = sel.Start.Para; i <= sel.End.Para; i++)
            {
                var p = P(i);
                int a = i == sel.Start.Para ? sel.Start.Offset : 0, b = i == sel.End.Para ? sel.End.Offset : p.Length;
                InlineOps.ApplyProps(p, a, b, r =>
                {
                    r.Bold = f.Run.Bold; r.Italic = f.Run.Italic; r.Underline = f.Run.Underline; r.Strike = f.Run.Strike;
                    r.Color = f.Run.Color; r.RawColor = f.Run.RawColor; r.Highlight = f.Run.Highlight;
                    r.Font = f.Run.Font; r.FontTheme = f.Run.FontTheme; r.RawFonts = f.Run.RawFonts; r.SizeHalfPt = f.Run.SizeHalfPt;
                });
                p.Props.StyleId = f.Para.StyleId; p.Props.Align = f.Para.Align; p.Props.Line = f.Para.Line; p.Props.LineRule = f.Para.LineRule;
            }
            return sel;
        });
        _painter = null;
    }

    // ── стили и нумерация в пакете ───────────────────────────────────────────

    internal void EnsureStyle(string styleId)
    {
        if (Doc.Styles.ById.ContainsKey(styleId)) return;
        var template = XDocument.Parse(BlankTemplates.Styles).Root!.Elements(Ns.W + "style")
            .FirstOrDefault(s => (string?)s.Attribute(Ns.W + "styleId") == styleId);
        if (template is null) return;
        Doc.Styles.Added.Add(template);
        Doc.Styles.ById[styleId] = new Style
        {
            Id = styleId,
            Name = (string?)template.Element(Ns.W + "name")?.Attribute(Ns.W + "val") ?? styleId,
            Type = StyleType.Paragraph,
            BasedOn = (string?)template.Element(Ns.W + "basedOn")?.Attribute(Ns.W + "val"),
            Next = (string?)template.Element(Ns.W + "next")?.Attribute(Ns.W + "val"),
            Run = DocxReader.ReadRunProps(template.Element(Ns.W + "rPr")),
            Para = DocxReader.ReadParaProps(template.Element(Ns.W + "pPr")),
        };
        if (styleId == "Hyperlink") return;
    }
}
