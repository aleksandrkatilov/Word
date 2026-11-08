using Pero.Doc.Editing;
using Pero.Doc.Model;

namespace Pero.Doc.Layout;

/// <summary>Результат разметки: страницы и сопоставление позиций с координатами (курсор, щелчок, выделение).</summary>
public sealed class LayoutResult
{
    public IReadOnlyList<PageBox> Pages { get; }

    internal LayoutResult(List<PageBox> pages) => Pages = pages;

    /// <summary>Строки части документа в порядке чтения (без повторённых заголовков таблиц).</summary>
    public IEnumerable<LineBox> StoryLines(HeaderFooter? story) => story is null
        ? Pages.SelectMany(p => p.Lines).Where(l => !l.Repeat)
        : Pages.SelectMany(p => p.HeaderLines.Concat(p.FooterLines)).Where(l => ReferenceEquals(l.Story, story));

    private IEnumerable<LineBox> PageStoryLines(HeaderFooter? story, int page)
    {
        var p = Pages[page];
        return story is null ? p.Lines : p.HeaderLines.Concat(p.FooterLines).Where(l => ReferenceEquals(l.Story, story));
    }

    /// <summary>Строка, на которой стоит позиция. preferPage — для колонтитулов, которые есть на многих страницах.</summary>
    public LineBox? LineAt(HeaderFooter? story, DocPos pos, int? preferPage = null)
    {
        IEnumerable<LineBox> lines = preferPage is int pp && pp >= 0 && pp < Pages.Count && story is not null
            ? PageStoryLines(story, pp).Where(l => !l.Repeat)
            : StoryLines(story);
        LineBox? lastOfPara = null;
        foreach (var l in lines)
        {
            if (l.Para != pos.Para) { if (lastOfPara is not null) break; continue; }
            lastOfPara = l;
            if (pos.Offset >= l.Start && pos.Offset < l.End) return l;
            if (pos.Offset == l.End && l.LastInParagraph) return l;
        }
        return lastOfPara;
    }

    public CaretBox? Caret(HeaderFooter? story, DocPos pos, int? preferPage = null)
    {
        var l = LineAt(story, pos, preferPage);
        if (l is null) return null;
        int k = Math.Clamp(pos.Offset - l.Start, 0, l.CaretX.Length - 1);
        return new CaretBox(l.Page, l.CaretX[k], l.Y, l.Height);
    }

    public int PageOf(HeaderFooter? story, DocPos pos) => LineAt(story, pos)?.Page ?? 0;

    /// <summary>Какой части документа принадлежит точка страницы: колонтитулу или основному тексту.</summary>
    public HfKind? ZoneAt(int page, double y)
    {
        var p = Pages[page];
        if (y < p.Body.Y) return HfKind.Header;
        if (y > p.Body.Bottom) return HfKind.Footer;
        return null;
    }

    /// <summary>Ближайшая к точке позиция в части документа на странице page.</summary>
    public DocPos? Hit(HeaderFooter? story, int page, double x, double y)
    {
        if (page < 0 || page >= Pages.Count) return null;
        var lines = PageStoryLines(story, page).ToList();
        if (lines.Count == 0)
        {
            // на странице нет строк этой части — берём ближайшую страницу
            for (int d = 1; d < Pages.Count; d++)
            {
                if (page - d >= 0 && PageStoryLines(story, page - d).Any()) return Hit(story, page - d, x, double.MaxValue);
                if (page + d < Pages.Count && PageStoryLines(story, page + d).Any()) return Hit(story, page + d, x, double.MinValue);
            }
            return null;
        }
        // строки, в чью полосу по вертикали попадает точка; среди них — та, что ближе по горизонтали
        LineBox? best = null;
        double bestScore = double.MaxValue;
        foreach (var l in lines)
        {
            double dy = y < l.Y ? l.Y - y : y > l.Y + l.Height ? y - l.Y - l.Height : 0;
            double dx = x < l.X ? l.X - x : x > l.X + l.AreaWidth ? x - l.X - l.AreaWidth : 0;
            double score = dy * 1000 + dx;
            if (score < bestScore) { bestScore = score; best = l; }
        }
        return best is null ? null : PosInLine(best, x);
    }

    public static DocPos PosInLine(LineBox l, double x)
    {
        int max = l.CaretX.Length - 1;
        // после принудительного разрыва и в конце перенесённой строки перед пробелом курсор остаётся на этой строке
        if (!l.LastInParagraph && max > 0)
        {
            bool trailingSpace = l.Paragraph.Text is { } t && l.End - 1 < t.Length && l.End >= 1 && t[l.End - 1] is ' ' or '\n';
            if (l.EndsWithBreak || trailingSpace) max--;
        }
        int bestK = 0;
        double bestD = double.MaxValue;
        for (int k = 0; k <= max; k++)
        {
            double d = Math.Abs(l.CaretX[k] - x);
            if (d < bestD) { bestD = d; bestK = k; }
        }
        return new DocPos(l.Para, l.Start + bestK);
    }

    /// <summary>Начало и конец визуальной строки (Home/End).</summary>
    public DocPos? LineHome(HeaderFooter? story, DocPos pos, int? page = null) => LineAt(story, pos, page) is { } l ? new DocPos(l.Para, l.Start) : null;

    public DocPos? LineEndPos(HeaderFooter? story, DocPos pos, int? page = null)
    {
        if (LineAt(story, pos, page) is not { } l) return null;
        return PosInLine(l, l.CaretX[^1] + 1000);
    }

    /// <summary>Позиция на строке выше (dir = −1) или ниже (dir = +1) при желаемом x.</summary>
    public DocPos? Vertical(HeaderFooter? story, DocPos pos, int dir, double x, int? page = null)
    {
        var cur = LineAt(story, pos, page);
        if (cur is null) return null;
        IEnumerable<LineBox> scope = story is null ? StoryLines(null) : PageStoryLines(story, cur.Page).Where(l => !l.Repeat);
        LineBox? best = null;
        foreach (var l in scope)
        {
            if (ReferenceEquals(l, cur)) continue;
            bool after = l.Page > cur.Page || l.Page == cur.Page && l.Y >= cur.Y + cur.Height - 0.01;
            bool before = l.Page < cur.Page || l.Page == cur.Page && l.Y + l.Height <= cur.Y + 0.01;
            if (dir > 0 ? !after : !before) continue;
            if (best is null) { best = l; continue; }
            // ближайшая по вертикали; при равенстве — та, что накрывает x
            int cmp = dir > 0 ? Cmp(l, best) : Cmp(best, l);
            if (cmp < 0 || cmp == 0 && Covers(l, x) && !Covers(best, x)) best = l;
        }
        return best is null ? null : PosInLine(best, x);
    }

    private static int Cmp(LineBox a, LineBox b) => a.Page != b.Page ? a.Page.CompareTo(b.Page) : Math.Abs(a.Y - b.Y) < 0.01 ? 0 : a.Y.CompareTo(b.Y);
    private static bool Covers(LineBox l, double x) => x >= l.X - 0.5 && x <= l.X + l.AreaWidth + 0.5;

    /// <summary>Прямоугольники выделения по страницам.</summary>
    public List<(int Page, RectPt Rect)> SelectionRects(HeaderFooter? story, DocPos a, DocPos b, int? page = null)
    {
        var res = new List<(int, RectPt)>();
        if (a == b) return res;
        if (b < a) (a, b) = (b, a);
        IEnumerable<LineBox> lines = story is not null && page is int pg ? PageStoryLines(story, pg).Where(l => !l.Repeat) : StoryLines(story);
        foreach (var l in lines)
        {
            if (l.Para < a.Para || l.Para > b.Para) continue;
            var ls = new DocPos(l.Para, l.Start);
            var le = new DocPos(l.Para, l.End);
            if (le < a || ls > b || (le == a && !l.LastInParagraph)) continue;
            int from = l.Para == a.Para ? Math.Max(a.Offset, l.Start) : l.Start;
            int to = l.Para == b.Para ? Math.Min(b.Offset, l.End) : l.End;
            if (from > l.End) continue;
            double x1 = l.CaretX[Math.Clamp(from - l.Start, 0, l.CaretX.Length - 1)];
            double x2 = l.CaretX[Math.Clamp(to - l.Start, 0, l.CaretX.Length - 1)];
            // выделение продолжается за концом абзаца — показываем знак абзаца
            if (l.LastInParagraph && b.Para > l.Para) x2 += 4;
            if (x2 > x1) res.Add((l.Page, new RectPt(x1, l.Y, x2 - x1, l.Height)));
        }
        return res;
    }

    /// <summary>Изображение в точке (для выделения и изменения размера).</summary>
    public (LineBox Line, Seg Seg)? ImageAt(HeaderFooter? story, int page, double x, double y)
    {
        if (page < 0 || page >= Pages.Count) return null;
        foreach (var l in PageStoryLines(story, page))
            foreach (var s in l.Segs)
                if (s.Kind == SegKind.Image && x >= s.X && x <= s.X + s.Width && y <= l.Y + l.Baseline && y >= l.Y + l.Baseline - s.Height)
                    return (l, s);
        return null;
    }

    /// <summary>Ссылка в точке.</summary>
    public LinkRef? LinkAt(HeaderFooter? story, int page, double x, double y)
    {
        if (page < 0 || page >= Pages.Count) return null;
        foreach (var l in PageStoryLines(story, page))
        {
            if (y < l.Y || y > l.Y + l.Height) continue;
            foreach (var s in l.Segs) if (s.Link is not null && x >= s.X && x <= s.X + s.Width) return s.Link;
        }
        return null;
    }
}
