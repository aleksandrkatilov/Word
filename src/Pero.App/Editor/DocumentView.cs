using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Pero.Doc.Editing;
using Pero.Doc.Fonts;
using Pero.Doc.Layout;
using Pero.Doc.Model;
using Pero.Doc.Render;
using Pero.Services;

namespace Pero.Editor;

/// <summary>
/// Поверхность редактирования (EDIT-01, PAGE-01): страницы, курсор, выделение, ввод с клавиатуры и мыши.
/// Разметка — собственная (Pero.Doc.Layout), отрисовка — общая со страницей печати и PDF.
/// Прокрутка — через IScrollInfo: рисуются только видимые страницы.
/// </summary>
public sealed partial class DocumentView : FrameworkElement, IScrollInfo
{
    public const double Gap = 16;          // промежуток между страницами, DIP
    public const double Pad = 24;          // поля вокруг страниц по горизонтали, DIP
    public const double DipPerPt = 96.0 / 72.0;

    private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF8));
    private static readonly Brush PageBrush = System.Windows.Media.Brushes.White;
    private static readonly Pen PageBorder = new(new SolidColorBrush(Color.FromRgb(0xD6, 0xDB, 0xE3)), 1);
    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x7B, 0xAE, 0xF5));
    private static readonly Brush MatchBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xD5, 0x4F));
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Pen HfLine = new(new SolidColorBrush(Color.FromRgb(0x4A, 0x7B, 0xD0)), 1) { DashStyle = DashStyles.Dash };
    private static readonly Pen GuidePen = new(new SolidColorBrush(Color.FromRgb(0x4A, 0x7B, 0xD0)), 1) { DashStyle = DashStyles.Dot };
    private static readonly Pen HandlePen = new(new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)), 1);

    static DocumentView()
    {
        foreach (var f in new Freezable[] { Background, PageBorder, SelectionBrush, MatchBrush, DimBrush, HfLine, GuidePen, HandlePen }) f.Freeze();
        FocusableProperty.OverrideMetadata(typeof(DocumentView), new FrameworkPropertyMetadata(true));
        CursorProperty.OverrideMetadata(typeof(DocumentView), new FrameworkPropertyMetadata(Cursors.IBeam));
    }

    private readonly Dictionary<int, DrawingGroup> _pageCache = [];
    private readonly DispatcherTimer _caretTimer;
    private bool _caretOn = true;
    private double[] _pageTops = [];
    private double _maxPageWidth;
    private double _zoom = 1.0;
    private ITextMeasurer _measurer;
    private PageDrawer _drawer;

    public DocEditor Editor { get; private set; }
    public LayoutResult Layout { get; private set; }
    public bool IsReadOnly { get; set; }
    /// <summary>Страница, на которой правится колонтитул (колонтитул один на многие страницы).</summary>
    public int StoryPage { get; private set; }
    /// <summary>Совпадения поиска для подсветки.</summary>
    public List<Selection> Matches { get; } = [];
    /// <summary>Вертикальная направляющая при перетаскивании маркера линейки (пункты страницы), или null.</summary>
    public (int Page, double X)? GuideX { get; set; }
    public (int Page, double Y)? GuideY { get; set; }

    /// <summary>Разметка пересчитана (линейкам и строке состояния).</summary>
    public event EventHandler? LayoutChanged;
    /// <summary>Прокрутка или масштаб изменились.</summary>
    public event EventHandler? ViewChanged;
    /// <summary>Курсор переместился.</summary>
    public event EventHandler? CaretMoved;
    /// <summary>Двойной щелчок по колонтитулу — вход в режим правки колонтитула.</summary>
    public event EventHandler? StoryChanged;
    /// <summary>Попытка правки документа только для чтения.</summary>
    public event EventHandler? ReadOnlyAttempt;

    public DocumentView(DocEditor editor)
    {
        Editor = editor;
        _measurer = new FontMeasurer(FontCatalog.Shared);
        _drawer = new PageDrawer(FontCatalog.Shared, (img, story) => Editor.Doc.ImageBytes(img.RelId, story?.PartPath));
        Layout = DocumentLayout.Build(editor.Doc, _measurer);
        Hook(editor);
        InputMethod.SetIsInputMethodEnabled(this, true);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(530), DispatcherPriority.Background, (_, _) => { _caretOn = !_caretOn; InvalidateVisual(); }, Dispatcher);
        GotKeyboardFocus += (_, _) => { _caretOn = true; _caretTimer.Start(); InvalidateVisual(); };
        LostKeyboardFocus += (_, _) => { _caretTimer.Stop(); _caretOn = false; InvalidateVisual(); };
        Relayout();
    }

    private void Hook(DocEditor ed)
    {
        ed.Changed += (_, _) => { Relayout(); ScrollToCaret(); };
        ed.SelectionChanged += (_, _) => { _caretOn = true; InvalidateVisual(); CaretMoved?.Invoke(this, EventArgs.Empty); };
    }

    /// <summary>Подключить другой документ (открытие файла в пустом окне).</summary>
    public void Attach(DocEditor editor)
    {
        Editor = editor;
        Hook(editor);
        StoryPage = 0;
        Matches.Clear();
        Relayout();
        SetVerticalOffset(0);
        CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ViewPeer(this);

    private sealed class ViewPeer(DocumentView owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Document;
        protected override string GetClassNameCore() => "PeroDocumentView";
        protected override bool IsKeyboardFocusableCore() => true;
    }

    public bool ShowMarks
    {
        get => _drawer.ShowMarks;
        set { _drawer.ShowMarks = value; _pageCache.Clear(); InvalidateVisual(); }
    }

    public double Scale => _zoom * DipPerPt;

    /// <summary>Масштаб, 0,25–5.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            double z = Math.Clamp(value, 0.25, 5);
            if (Math.Abs(z - _zoom) < 0.001) return;
            double centerY = _viewport.Height > 0 ? (_offset.Y + _viewport.Height / 2) / Math.Max(1, ExtentHeight) : 0;
            _zoom = z;
            UpdateGeometry();
            _offset.Y = Math.Max(0, centerY * ExtentHeight - _viewport.Height / 2);
            ClampOffsets();
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Масштаб «По ширине»: страница занимает ширину окна.</summary>
    public void FitWidth()
    {
        if (_viewport.Width <= 0 || Layout.Pages.Count == 0) return;
        double w = Layout.Pages.Max(p => p.Width) * DipPerPt;
        Zoom = Math.Max(0.25, (_viewport.Width - 2 * Pad) / w);
    }

    public void Relayout()
    {
        using (Log.Measure("layout"))
            Layout = DocumentLayout.Build(Editor.Doc, _measurer);
        _pageCache.Clear();
        if (StoryPage >= Layout.Pages.Count) StoryPage = Math.Max(0, Layout.Pages.Count - 1);
        UpdateGeometry();
        ClampOffsets();
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateGeometry()
    {
        double s = Scale;
        _pageTops = new double[Layout.Pages.Count];
        double y = Gap;
        _maxPageWidth = 0;
        for (int i = 0; i < Layout.Pages.Count; i++)
        {
            _pageTops[i] = y;
            y += Layout.Pages[i].Height * s + Gap;
            _maxPageWidth = Math.Max(_maxPageWidth, Layout.Pages[i].Width * s);
        }
        _extent = new Size(_maxPageWidth + 2 * Pad, y);
    }

    // ── геометрия страниц ────────────────────────────────────────────────────

    /// <summary>Прямоугольник страницы в координатах элемента (с учётом прокрутки).</summary>
    public Rect PageRect(int i)
    {
        if (i < 0 || i >= Layout.Pages.Count) return Rect.Empty;
        var p = Layout.Pages[i];
        double w = p.Width * Scale, h = p.Height * Scale;
        double left = Math.Max(Pad, (Math.Max(_viewport.Width, _extent.Width) - w) / 2) - _offset.X;
        return new Rect(left, _pageTops[i] - _offset.Y, w, h);
    }

    public Point ToView(int page, double xPt, double yPt)
    {
        var r = PageRect(page);
        return new Point(r.X + xPt * Scale, r.Y + yPt * Scale);
    }

    /// <summary>Страница и точка в пунктах для точки элемента; ближайшая страница, если точка между страницами.</summary>
    public (int Page, double X, double Y) ToPage(Point p)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < Layout.Pages.Count; i++)
        {
            var r = PageRect(i);
            double d = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
            if (d < bestD) { bestD = d; best = i; }
            if (d == 0) break;
        }
        var pr = PageRect(best);
        return (best, (p.X - pr.X) / Scale, (p.Y - pr.Y) / Scale);
    }

    /// <summary>Страница, которую видно в середине окна.</summary>
    public int VisiblePage => Layout.Pages.Count == 0 ? 0 : ToPage(new Point(_viewport.Width / 2, _viewport.Height / 2)).Page;

    /// <summary>Положение курсора (страница, x, y, высота строки) в пунктах.</summary>
    public CaretBox? Caret => Layout.Caret(Editor.Story, Editor.Selection.Active, Editor.Story is null ? null : StoryPage);

    /// <summary>Страница с курсором.</summary>
    public int CaretPage => Caret?.Page ?? VisiblePage;

    /// <summary>Строка с курсором.</summary>
    public LineBox? CaretLine => Layout.LineAt(Editor.Story, Editor.Selection.Active, Editor.Story is null ? null : StoryPage);

    // ── отрисовка ────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        if (Layout.Pages.Count == 0) return;
        double s = Scale;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var sel = Editor.Selection;
        var selRects = sel.IsEmpty ? new List<(int Page, RectPt Rect)>() : Layout.SelectionRects(Editor.Story, sel.Anchor, sel.Active, Editor.Story is null ? null : StoryPage);
        var caret = Caret;

        for (int i = 0; i < Layout.Pages.Count; i++)
        {
            var r = PageRect(i);
            if (r.Bottom < -Gap || r.Top > _viewport.Height + Gap) continue;
            dc.DrawRectangle(PageBrush, PageBorder, r);
            dc.PushClip(new RectangleGeometry(r));
            dc.PushTransform(new MatrixTransform(s, 0, 0, s, r.X, r.Y));
            if (!_pageCache.TryGetValue(i, out var group))
            {
                group = new DrawingGroup();
                using (var ctx = group.Open()) _drawer.Draw(Layout.Pages[i], new WpfCanvas(ctx, ppd), headersFooters: true, dimHeadersFooters: Editor.Story is null);
                group.Freeze();
                _pageCache[i] = group;
            }
            dc.DrawDrawing(group);
            var page = Layout.Pages[i];
            if (Editor.Story is not null)
            {
                // правка колонтитула: основной текст приглушён, граница колонтитулов отмечена
                dc.DrawRectangle(DimBrush, null, new Rect(0, page.Body.Y, page.Width, page.Body.H));
                dc.DrawLine(HfLine, new Point(0, page.HeaderArea.Bottom), new Point(page.Width, page.HeaderArea.Bottom));
                dc.DrawLine(HfLine, new Point(0, page.FooterArea.Y), new Point(page.Width, page.FooterArea.Y));
            }
            foreach (var m in Matches)
                foreach (var (pg, rect) in Layout.SelectionRects(Editor.Story, m.Start, m.End, Editor.Story is null ? null : StoryPage))
                    if (pg == i) dc.DrawRectangle(MatchBrush, null, new Rect(rect.X, rect.Y, rect.W, rect.H));
            foreach (var (pg, rect) in selRects)
                if (pg == i) dc.DrawRectangle(SelectionBrush, null, new Rect(rect.X, rect.Y, rect.W, rect.H));
            if (SelectedImage() is { } img && img.Line.Page == i) DrawHandles(dc, img.Seg, img.Line, s);
            if (caret is { } c && c.Page == i && sel.IsEmpty && _caretOn && IsKeyboardFocused)
                dc.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(c.X - 0.5 / s, c.Y, Math.Max(1.2 / s, 0.75), c.Height));
            if (GuideX is { } gx && gx.Page == i) dc.DrawLine(GuidePen, new Point(gx.X, 0), new Point(gx.X, page.Height));
            if (GuideY is { } gy && gy.Page == i) dc.DrawLine(GuidePen, new Point(0, gy.Y), new Point(page.Width, gy.Y));
            if (_resize is { } rz && rz.Line.Page == i)
                dc.DrawRectangle(null, HandlePen, new Rect(rz.Seg.X, rz.Line.Y + rz.Line.Baseline - rz.NewH, rz.NewW, rz.NewH));
            dc.Pop();
            dc.Pop();
        }
    }

    private const double HandleSize = 7;

    private static void DrawHandles(DrawingContext dc, Seg seg, LineBox line, double s)
    {
        double top = line.Y + line.Baseline - seg.Height;
        var r = new Rect(seg.X, top, seg.Width, seg.Height);
        dc.DrawRectangle(null, HandlePen, r);
        double h = HandleSize / s;
        foreach (var pt in new[] { r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight })
            dc.DrawRectangle(System.Windows.Media.Brushes.White, HandlePen, new Rect(pt.X - h / 2, pt.Y - h / 2, h, h));
    }

    /// <summary>Выделенное изображение (выделение ровно из одного объекта-изображения).</summary>
    public (LineBox Line, Seg Seg)? SelectedImage()
    {
        var s = Editor.Selection;
        if (s.IsEmpty || s.Start.Para != s.End.Para || s.End.Offset - s.Start.Offset != 1) return null;
        var line = Layout.LineAt(Editor.Story, s.Start, Editor.Story is null ? null : StoryPage);
        var seg = line?.Segs.FirstOrDefault(x => x.Kind == SegKind.Image && x.Start == s.Start.Offset);
        return line is null || seg is null ? null : (line, seg);
    }

    // ── прокрутка (IScrollInfo) ──────────────────────────────────────────────

    private Size _extent;
    private Size _viewport;
    private Vector _offset;

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
                            double.IsInfinity(availableSize.Height) ? Math.Min(_extent.Height, 800) : availableSize.Height);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_viewport != finalSize)
        {
            _viewport = finalSize;
            ClampOffsets();
            ScrollOwner?.InvalidateScrollInfo();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        return finalSize;
    }

    private void ClampOffsets()
    {
        _offset.X = Math.Max(0, Math.Min(_offset.X, _extent.Width - _viewport.Width));
        _offset.Y = Math.Max(0, Math.Min(_offset.Y, _extent.Height - _viewport.Height));
    }

    public void SetHorizontalOffset(double offset)
    {
        _offset.X = offset;
        ClampOffsets();
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetVerticalOffset(double offset)
    {
        _offset.Y = offset;
        ClampOffsets();
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - 40);
    public void LineDown() => SetVerticalOffset(_offset.Y + 40);
    public void LineLeft() => SetHorizontalOffset(_offset.X - 40);
    public void LineRight() => SetHorizontalOffset(_offset.X + 40);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height * 0.9);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height * 0.9);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width * 0.9);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width * 0.9);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - 60);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + 60);
    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - 60);
    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + 60);
    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    /// <summary>Прокрутить так, чтобы курсор был виден.</summary>
    public void ScrollToCaret()
    {
        if (Caret is not { } c) return;
        var top = ToView(c.Page, c.X, c.Y);
        double h = c.Height * Scale;
        if (top.Y < 0) SetVerticalOffset(_offset.Y + top.Y - 20);
        else if (top.Y + h > _viewport.Height) SetVerticalOffset(_offset.Y + top.Y + h - _viewport.Height + 20);
        if (top.X < 0) SetHorizontalOffset(_offset.X + top.X - 20);
        else if (top.X > _viewport.Width) SetHorizontalOffset(_offset.X + top.X - _viewport.Width + 40);
    }

    /// <summary>Показать страницу (навигация).</summary>
    public void ScrollToPage(int page) { if (page >= 0 && page < _pageTops.Length) SetVerticalOffset(_pageTops[page] - Gap); }
}
