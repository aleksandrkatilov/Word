namespace Pero.Doc.Model;

/// <summary>Итоговое оформление символа после наследования: умолчания → стиль абзаца → стиль знака → прямое.</summary>
public sealed record EffectiveRun(string Font, double SizePt, bool Bold, bool Italic, bool Underline, bool Strike,
    string Color, string? Highlight, string? VertAlign);

/// <summary>Итоговое оформление абзаца (интервалы и отступы в twips).</summary>
public sealed record EffectivePara(string? StyleId, Align Align, int SpaceBefore, int SpaceAfter, int Line, LineRule LineRule,
    int IndLeft, int IndRight, int IndFirst, int? NumId, int Ilvl, bool PageBreakBefore, bool KeepNext);

public static class Resolve
{
    public static EffectivePara Para(Document doc, Paragraph p, string? tableStyle = null)
    {
        var s = doc.Styles;
        var acc = new ParaProps();
        Merge(acc, s.DefaultPara);
        if (tableStyle is not null) foreach (var st in s.Chain(tableStyle)) Merge(acc, st.Para);
        string? styleId = p.Props.StyleId ?? s.DefaultParagraphStyleId;
        foreach (var st in s.Chain(styleId)) Merge(acc, st.Para);
        // отступы нумерации: после стиля, до прямого оформления
        int? numId = p.Props.NumId ?? acc.NumId;
        int ilvl = p.Props.Ilvl ?? acc.Ilvl ?? 0;
        if (numId is int n && n != 0 && doc.Numbering.Level(n, ilvl) is { } lvl)
        {
            if (lvl.IndLeft is int l) acc.IndLeft = l;
            if (lvl.IndHanging is int h) acc.IndFirst = -h;
        }
        Merge(acc, p.Props);
        return new EffectivePara(styleId, acc.Align ?? Align.Left, acc.SpaceBefore ?? 0, acc.SpaceAfter ?? 0,
            acc.Line ?? 240, acc.LineRule ?? LineRule.Auto, acc.IndLeft ?? 0, acc.IndRight ?? 0, acc.IndFirst ?? 0,
            numId is 0 ? null : numId, ilvl, acc.PageBreakBefore, acc.KeepNext);
    }

    public static EffectiveRun Run(Document doc, Paragraph p, RunProps direct, string? tableStyle = null)
    {
        var s = doc.Styles;
        var acc = new RunProps();
        MergeRun(acc, s.DefaultRun);
        if (tableStyle is not null) foreach (var st in s.Chain(tableStyle)) MergeRun(acc, st.Run);
        foreach (var st in s.Chain(p.Props.StyleId ?? s.DefaultParagraphStyleId)) MergeRun(acc, st.Run);
        if (direct.StyleId is not null) foreach (var st in s.Chain(direct.StyleId)) MergeRun(acc, st.Run);
        MergeRun(acc, direct);
        string font = acc.Font ?? (acc.FontTheme?.StartsWith("major", StringComparison.Ordinal) == true ? s.MajorFont
                    : acc.FontTheme is not null ? s.MinorFont : "Times New Roman");
        string color = acc.Color is null or "auto" ? "000000" : acc.Color;
        return new EffectiveRun(font, (acc.SizeHalfPt ?? 20) / 2.0, acc.Bold == true, acc.Italic == true,
            acc.Underline is not null and not "none", acc.Strike == true, color, acc.Highlight is "none" ? null : acc.Highlight, acc.VertAlign);
    }

    private static void Merge(ParaProps acc, ParaProps src)
    {
        if (src.StyleId is not null) acc.StyleId = src.StyleId;
        if (src.Align is not null) acc.Align = src.Align;
        if (src.SpaceBefore is not null) acc.SpaceBefore = src.SpaceBefore;
        if (src.SpaceAfter is not null) acc.SpaceAfter = src.SpaceAfter;
        if (src.Line is not null) { acc.Line = src.Line; acc.LineRule = src.LineRule ?? LineRule.Auto; }
        if (src.IndLeft is not null) acc.IndLeft = src.IndLeft;
        if (src.IndRight is not null) acc.IndRight = src.IndRight;
        if (src.IndFirst is not null) acc.IndFirst = src.IndFirst;
        if (src.NumId is not null) { acc.NumId = src.NumId; acc.Ilvl = src.Ilvl; }
        if (src.PageBreakBefore) acc.PageBreakBefore = true;
        if (src.KeepNext) acc.KeepNext = true;
    }

    private static void MergeRun(RunProps acc, RunProps src)
    {
        if (src.Font is not null) { acc.Font = src.Font; acc.FontTheme = null; }
        else if (src.FontTheme is not null) { acc.FontTheme = src.FontTheme; acc.Font = null; }
        if (src.SizeHalfPt is not null) acc.SizeHalfPt = src.SizeHalfPt;
        if (src.Bold is not null) acc.Bold = src.Bold;
        if (src.Italic is not null) acc.Italic = src.Italic;
        if (src.Underline is not null) acc.Underline = src.Underline;
        if (src.Strike is not null) acc.Strike = src.Strike;
        if (src.Color is not null) acc.Color = src.Color;
        if (src.Highlight is not null) acc.Highlight = src.Highlight;
        if (src.VertAlign is not null) acc.VertAlign = src.VertAlign;
    }
}
