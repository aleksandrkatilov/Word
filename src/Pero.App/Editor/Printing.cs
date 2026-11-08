using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Pero.Doc.Layout;
using Pero.Doc.Render;

namespace Pero.Editor;

/// <summary>Печать (PAGE-01, CORE-04): страницы рисуются из той же разметки и тем же кодом, что на экране и в PDF.</summary>
public sealed class LayoutPaginator(LayoutResult layout, PageDrawer drawer) : DocumentPaginator
{
    private const double DipPerPt = 96.0 / 72.0;

    public override bool IsPageCountValid => true;
    public override int PageCount => layout.Pages.Count;
    public override IDocumentPaginatorSource? Source => null;

    public override Size PageSize
    {
        get => layout.Pages.Count == 0 ? new Size(793.7, 1122.5) : new Size(layout.Pages[0].Width * DipPerPt, layout.Pages[0].Height * DipPerPt);
        set { }
    }

    public override DocumentPage GetPage(int pageNumber)
    {
        var page = layout.Pages[pageNumber];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(DipPerPt, DipPerPt));
            drawer.Draw(page, new WpfCanvas(dc, 1.0));
            dc.Pop();
        }
        var size = new Size(page.Width * DipPerPt, page.Height * DipPerPt);
        return new DocumentPage(visual, size, new Rect(size), new Rect(size));
    }

    /// <summary>Диалог печати и печать всего документа или выбранных страниц.</summary>
    public static bool Print(Window owner, LayoutResult layout, PageDrawer drawer, string title)
    {
        var dlg = new PrintDialog { UserPageRangeEnabled = true, MinPage = 1, MaxPage = (uint)Math.Max(1, layout.Pages.Count) };
        if (layout.Pages.Count > 0)
        {
            var p = layout.Pages[0];
            try { dlg.PrintTicket.PageOrientation = p.Width > p.Height ? PageOrientation.Landscape : PageOrientation.Portrait; } catch { }
        }
        if (dlg.ShowDialog() != true) return false;
        DocumentPaginator paginator = new LayoutPaginator(layout, drawer);
        if (dlg.PageRangeSelection == PageRangeSelection.UserPages)
            paginator = new RangePaginator(paginator, dlg.PageRange.PageFrom - 1, dlg.PageRange.PageTo - 1);
        dlg.PrintDocument(paginator, title);
        return true;
    }

    private sealed class RangePaginator(DocumentPaginator inner, int from, int to) : DocumentPaginator
    {
        private readonly int _from = Math.Max(0, from);
        private readonly int _to = Math.Min(inner.PageCount - 1, Math.Max(from, to));
        public override bool IsPageCountValid => true;
        public override int PageCount => Math.Max(0, _to - _from + 1);
        public override Size PageSize { get => inner.PageSize; set { } }
        public override IDocumentPaginatorSource? Source => null;
        public override DocumentPage GetPage(int pageNumber) => inner.GetPage(_from + pageNumber);
    }
}
