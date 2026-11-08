using Pero.Doc.Fonts;
using Pero.Doc.Layout;
using Pero.Doc.Model;

namespace Pero.Doc.Render;

/// <summary>Цвет RGB из шестнадцатеричной строки Word.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string? hex, Rgb fallback)
    {
        if (hex is null || hex.Length != 6) return fallback;
        try { return new Rgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)); }
        catch { return fallback; }
    }

    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb Gray = new(120, 120, 120);
}

/// <summary>Отрезок глифов одного начертания: номера глифов и их ширины в пунктах.</summary>
public sealed record GlyphRunInfo(ResolvedFont Font, double SizePt, double X, double BaselineY, ushort[] Glyphs, double[] Advances, string Text, Rgb Color);

/// <summary>
/// Холст страницы: экран (WPF), печать и PDF реализуют одни и те же примитивы.
/// Координаты — пункты от левого верхнего угла страницы.
/// </summary>
public interface IPageCanvas
{
    void FillRect(RectPt r, Rgb color);
    void Line(double x1, double y1, double x2, double y2, double width, Rgb color);
    void Glyphs(GlyphRunInfo run);
    void Image(byte[] data, RectPt r);
}

/// <summary>Рисование размеченной страницы на любом холсте (RENDER-01).</summary>
public sealed class PageDrawer(FontCatalog fonts, Func<InlineImage, HeaderFooter?, byte[]?> imageBytes)
{
    /// <summary>Показывать знаки абзаца и табуляции.</summary>
    public bool ShowMarks { get; set; }

    private static readonly Dictionary<string, Rgb> Highlights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yellow"] = new(255, 255, 0), ["green"] = new(0, 255, 0), ["cyan"] = new(0, 255, 255), ["magenta"] = new(255, 0, 255),
        ["blue"] = new(0, 0, 255), ["red"] = new(255, 0, 0), ["darkBlue"] = new(0, 0, 139), ["darkCyan"] = new(0, 139, 139),
        ["darkGreen"] = new(0, 100, 0), ["darkMagenta"] = new(139, 0, 139), ["darkRed"] = new(139, 0, 0), ["darkYellow"] = new(128, 128, 0),
        ["darkGray"] = new(169, 169, 169), ["lightGray"] = new(211, 211, 211), ["black"] = new(0, 0, 0), ["white"] = new(255, 255, 255),
    };

    public static Rgb? HighlightColor(string? name) => name is not null && Highlights.TryGetValue(name, out var c) ? c : null;

    public void Draw(PageBox page, IPageCanvas c, bool headersFooters = true, bool dimHeadersFooters = false)
    {
        DrawDecor(page.Decor, c);
        foreach (var l in page.Lines) DrawLine(l, c, dim: false);
        if (!headersFooters) return;
        DrawDecor(page.HeaderDecor, c);
        DrawDecor(page.FooterDecor, c);
        foreach (var l in page.HeaderLines.Concat(page.FooterLines)) DrawLine(l, c, dimHeadersFooters);
    }

    private static void DrawDecor(List<Decor> decor, IPageCanvas c)
    {
        foreach (var d in decor.Where(d => d.Kind == DecorKind.Fill))
            c.FillRect(new RectPt(d.X1, d.Y1, d.X2 - d.X1, d.Y2 - d.Y1), Rgb.Parse(d.Color, new Rgb(255, 255, 255)));
        foreach (var d in decor.Where(d => d.Kind == DecorKind.Line))
            c.Line(d.X1, d.Y1, d.X2, d.Y2, d.Thickness, Rgb.Parse(d.Color, Rgb.Black));
    }

    public void DrawLine(LineBox l, IPageCanvas c, bool dim)
    {
        double baseY = l.Y + l.Baseline;
        // маркер (выделение цветом) — под текстом
        foreach (var s in l.Segs)
            if (s.Style?.Highlight is { } h && HighlightColor(h) is { } hc && s.Width > 0)
            {
                double asc = s.SizePt * 0.95, desc = s.SizePt * 0.25;
                c.FillRect(new RectPt(s.X, baseY - asc, s.Width, asc + desc), hc);
            }
        if (l.Label is { Length: > 0 } label)
        {
            var color = Rgb.Parse(l.LabelColor is "000000" or null ? null : l.LabelColor, Rgb.Black);
            DrawText(label, null, l.LabelFont, l.LabelSize, l.LabelX, baseY, dim ? Dim(color) : color, c);
        }
        foreach (var s in l.Segs)
        {
            switch (s.Kind)
            {
                case SegKind.Image when s.Image is { } img:
                {
                    var r = new RectPt(s.X, baseY - s.Height, s.Width, s.Height);
                    var bytes = imageBytes(img, l.Story);
                    if (bytes is not null) c.Image(bytes, r);
                    else
                    {
                        c.Line(r.X, r.Y, r.Right, r.Y, 0.5, Rgb.Gray); c.Line(r.Right, r.Y, r.Right, r.Bottom, 0.5, Rgb.Gray);
                        c.Line(r.X, r.Bottom, r.Right, r.Bottom, 0.5, Rgb.Gray); c.Line(r.X, r.Y, r.X, r.Bottom, 0.5, Rgb.Gray);
                    }
                    break;
                }
                case SegKind.Text or SegKind.Object:
                {
                    var color = Rgb.Parse(s.Style?.Color, Rgb.Black);
                    if (s.Kind == SegKind.Object && s.Text == "◊") color = Rgb.Gray;
                    if (dim) color = Dim(color);
                    double y = baseY - s.Rise;
                    DrawText(s.Text, s.Kind == SegKind.Text ? s.Advances : null, s.Font, s.SizePt, s.X, y, color, c);
                    if (s.Style is { } st && (st.Underline || st.Strike) && s.Width > 0)
                    {
                        double w = Math.Max(0.5, s.SizePt / 18);
                        // подчёркивание без хвостовых пробелов строки
                        double width = s.Width;
                        if (s == l.Segs[^1]) for (int k = s.Text.Length - 1; k >= 0 && s.Text[k] == ' '; k--) width -= s.Advances.Length > k ? s.Advances[k] : 0;
                        if (st.Underline) c.Line(s.X, y + s.SizePt * 0.12, s.X + width, y + s.SizePt * 0.12, w, color);
                        if (st.Strike) c.Line(s.X, y - s.SizePt * 0.3, s.X + width, y - s.SizePt * 0.3, w, color);
                    }
                    break;
                }
            }
        }
        if (ShowMarks && l.CaretX.Length > 0)
        {
            string mark = l.LastInParagraph ? "¶" : l.BreakAfter == BreakKind.Line ? "↵" : "";
            if (mark.Length > 0) DrawText(mark, null, new FontKey(FontCatalog.DefaultFamily, false, false), 10, l.CaretX[^1] + 1, baseY, new Rgb(140, 140, 200), c);
        }
    }

    private static Rgb Dim(Rgb c) => new((byte)(c.R + (255 - c.R) * 0.55), (byte)(c.G + (255 - c.G) * 0.55), (byte)(c.B + (255 - c.B) * 0.55));

    /// <summary>Текст разбивается на отрезки по фактическому шрифту символа (основной или запасной).</summary>
    private void DrawText(string text, double[]? advances, FontKey key, double size, double x, double baseline, Rgb color, IPageCanvas c)
    {
        if (text.Length == 0) return;
        int i = 0;
        double cx = x;
        while (i < text.Length)
        {
            var face = Face(key, text, i);
            if (face is null) return;
            var glyphs = new List<ushort>();
            var adv = new List<double>();
            var sb = new System.Text.StringBuilder();
            double startX = cx;
            while (i < text.Length)
            {
                var f = Face(key, text, i);
                if (f is null || !ReferenceEquals(f.Face, face.Face)) break;
                int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
                int units = char.IsSurrogatePair(text, i) ? 2 : 1;
                int drawCp = cp is 0xA0 && !face.Face.HasGlyph(0xA0) ? ' ' : cp;
                ushort g = face.Face.GlyphId(drawCp);
                double a = advances is not null && i < advances.Length ? advances[i] : face.Face.Advance(drawCp, size);
                if (units == 2 && advances is not null && i + 1 < advances.Length) a += advances[i + 1];
                glyphs.Add(g);
                adv.Add(a);
                sb.Append(text, i, units);
                cx += a;
                i += units;
            }
            if (glyphs.Count == 0) { i++; continue; }
            c.Glyphs(new GlyphRunInfo(face, size, startX, baseline, [.. glyphs], [.. adv], sb.ToString(), color));
        }
    }

    private ResolvedFont? Face(FontKey key, string text, int i)
    {
        int cp = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
        return fonts.FaceFor(key, cp == 0xA0 ? ' ' : cp);
    }
}
