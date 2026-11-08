using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Pero.Doc.Model;

namespace Pero.Editor;

/// <summary>
/// Линейки (UI-07): горизонтальная — левое и правое поля, отступы абзаца и первой строки; вертикальная —
/// верхнее и нижнее поля. Шкала в сантиметрах с учётом масштаба и текущей страницы. Перетаскивание маркеров
/// выполняет обычные команды ядра — изменения отменяются и сохраняются в DOCX.
/// </summary>
public abstract class RulerBase : FrameworkElement
{
    protected const double TwipsPerCm = 566.929;
    protected static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF8)));
    protected static readonly Brush MarginBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xDA, 0xDF, 0xE7)));
    protected static readonly Brush TextArea = Brushes.White;
    protected static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)));
    protected static readonly Brush MarkerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x73, 0xE8)));
    protected static readonly Pen TickPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x80, 0x86, 0x8B))), 1));
    protected static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    protected static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    protected DocumentView View { get; }
    protected string? Dragging { get; set; }
    protected double DragValue { get; set; }

    protected RulerBase(DocumentView view)
    {
        View = view;
        view.LayoutChanged += (_, _) => InvalidateVisual();
        view.ViewChanged += (_, _) => InvalidateVisual();
        view.CaretMoved += (_, _) => InvalidateVisual();
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

    protected int Page => Math.Clamp(View.CaretPage, 0, Math.Max(0, View.Layout.Pages.Count - 1));

    protected void Label(DrawingContext dc, string text, Point center, bool rotate = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 9, Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (rotate)
        {
            dc.PushTransform(new RotateTransform(-90, center.X, center.Y));
            dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
            dc.Pop();
        }
        else dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    /// <summary>Привязка к шагу 0,25 см (Alt — без привязки).</summary>
    protected static int Snap(double twips) =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? (int)Math.Round(twips) : (int)(Math.Round(twips / (TwipsPerCm / 4)) * (TwipsPerCm / 4));

    protected static string Cm(double twips) => (twips / TwipsPerCm).ToString("0.##", CultureInfo.CurrentCulture) + " см";
}

public sealed class HorizontalRuler : RulerBase
{
    public HorizontalRuler(DocumentView view) : base(view) { Height = 22; }

    /// <summary>Текстовая область строки с курсором (ячейка таблицы или поля страницы), пункты страницы.</summary>
    private (double Left, double Right) Area()
    {
        var page = View.Layout.Pages[Page];
        if (View.CaretLine is { } l) return (l.X, l.X + l.AreaWidth);
        return (page.Body.X, page.Body.Right);
    }

    private EffectivePara? Para()
    {
        var s = View.Editor.Selection.Active;
        if (s.Para >= View.Editor.Index.Count) return null;
        return Resolve.Para(View.Editor.Doc, View.Editor.Index[s.Para].Paragraph);
    }

    private double X(double pt) => View.PageRect(Page).X + pt * View.Scale;
    private double Pt(double x) => (x - View.PageRect(Page).X) / View.Scale;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (View.Layout.Pages.Count == 0) return;
        var page = View.Layout.Pages[Page];
        var sec = View.Editor.Doc.Section;
        double h = ActualHeight;
        double left = sec.MarginLeft / 20.0, right = page.Width - sec.MarginRight / 20.0;
        if (Dragging == "ml") left = DragValue / 20.0;
        if (Dragging == "mr") right = page.Width - DragValue / 20.0;
        double x0 = X(0), xw = X(page.Width);
        dc.DrawRectangle(MarginBrush, null, new Rect(x0, 3, Math.Max(0, xw - x0), h - 6));
        dc.DrawRectangle(TextArea, null, new Rect(X(left), 3, Math.Max(0, X(right) - X(left)), h - 6));
        // шкала: от левого поля, по 0,25 см, подписи каждый сантиметр
        double cmPt = TwipsPerCm / 20.0;
        double cmPx = cmPt * View.Scale;
        int labelEvery = cmPx < 18 ? 2 : 1;
        for (int k = -(int)(left / cmPt * 4) - 1; k <= (int)((page.Width - left) / cmPt * 4) + 1; k++)
        {
            double x = X(left + k * cmPt / 4);
            if (x < x0 - 1 || x > xw + 1) continue;
            if (k % 4 == 0)
            {
                int cm = Math.Abs(k / 4);
                if (cm != 0 && cm % labelEvery == 0) Label(dc, cm.ToString(CultureInfo.CurrentCulture), new Point(x, h / 2));
            }
            else
            {
                double len = k % 2 == 0 ? 5 : 3;
                dc.DrawLine(TickPen, new Point(Math.Round(x) + 0.5, h / 2 - len / 2), new Point(Math.Round(x) + 0.5, h / 2 + len / 2));
            }
        }
        // маркеры отступов абзаца
        if (Para() is { } ep)
        {
            var (aL, aR) = Area();
            double indL = ep.IndLeft / 20.0, indR = ep.IndRight / 20.0, first = (ep.IndLeft + ep.IndFirst) / 20.0;
            if (Dragging == "il") { indL = DragValue / 20.0; first = indL + ep.IndFirst / 20.0; }
            if (Dragging == "if") first = DragValue / 20.0;
            if (Dragging == "ir") indR = DragValue / 20.0;
            Triangle(dc, X(aL + first), 3, down: true);
            Triangle(dc, X(aL + indL), h - 3, down: false);
            Triangle(dc, X(aR - indR), h - 3, down: false);
        }
    }

    private static void Triangle(DrawingContext dc, double x, double y, bool down)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double d = down ? 6 : -6;
            c.BeginFigure(new Point(x - 5, y), true, true);
            c.LineTo(new Point(x + 5, y), true, false);
            c.LineTo(new Point(x, y + d), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(MarkerBrush, null, g);
    }

    private string? HitTest(Point p)
    {
        if (View.Layout.Pages.Count == 0) return null;
        var page = View.Layout.Pages[Page];
        var sec = View.Editor.Doc.Section;
        var (aL, aR) = Area();
        if (Para() is { } ep)
        {
            double first = X(aL + (ep.IndLeft + ep.IndFirst) / 20.0), indL = X(aL + ep.IndLeft / 20.0), indR = X(aR - ep.IndRight / 20.0);
            if (p.Y < ActualHeight / 2 && Math.Abs(p.X - first) <= 6) return "if";
            if (p.Y >= ActualHeight / 2 && Math.Abs(p.X - indL) <= 6) return "il";
            if (p.Y >= ActualHeight / 2 && Math.Abs(p.X - indR) <= 6) return "ir";
        }
        if (Math.Abs(p.X - X(sec.MarginLeft / 20.0)) <= 4) return "ml";
        if (Math.Abs(p.X - X(page.Width - sec.MarginRight / 20.0)) <= 4) return "mr";
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (Dragging is null)
        {
            var hit = HitTest(p);
            Cursor = hit is "ml" or "mr" ? Cursors.SizeWE : hit is not null ? Cursors.Hand : Cursors.Arrow;
            ToolTip = hit switch
            {
                "ml" => "Левое поле", "mr" => "Правое поле", "if" => "Отступ первой строки",
                "il" => "Отступ слева", "ir" => "Отступ справа", _ => null,
            };
            return;
        }
        var page = View.Layout.Pages[Page];
        var (aL, aR) = Area();
        double pt = Pt(p.X);
        DragValue = Dragging switch
        {
            "ml" => Snap(Math.Clamp(pt, 0, page.Width - 36) * 20),
            "mr" => Snap(Math.Clamp(page.Width - pt, 0, page.Width - 36) * 20),
            "il" or "if" => Snap((pt - aL) * 20),
            "ir" => Snap((aR - pt) * 20),
            _ => 0,
        };
        double guide = Dragging switch
        {
            "ml" => DragValue / 20.0, "mr" => page.Width - DragValue / 20.0,
            "il" or "if" => aL + DragValue / 20.0, "ir" => aR - DragValue / 20.0, _ => 0,
        };
        View.GuideX = (Page, guide);
        ToolTip = Cm(DragValue);
        View.InvalidateVisual();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Dragging = HitTest(e.GetPosition(this));
        if (Dragging is null) return;
        if (View.IsReadOnly) { Dragging = null; return; }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Dragging is null) return;
        string what = Dragging;
        Dragging = null;
        ReleaseMouseCapture();
        View.GuideX = null;
        int v = (int)DragValue;
        var ed = View.Editor;
        var sec = ed.Doc.Section;
        var ep = Para();
        switch (what)
        {
            case "ml" when v != sec.MarginLeft && sec.PageWidth - v - sec.MarginRight > 720: ed.SetMargins(null, null, null, v); break;
            case "mr" when v != sec.MarginRight && sec.PageWidth - v - sec.MarginLeft > 720: ed.SetMargins(null, v, null, null); break;
            case "il" when ep is not null: ed.SetIndents(Math.Max(-1440, v), null, null); break;
            case "if" when ep is not null: ed.SetIndents(null, null, v - ep.IndLeft); break;
            case "ir" when ep is not null: ed.SetIndents(null, Math.Max(-1440, v), null); break;
        }
        View.InvalidateVisual();
        InvalidateVisual();
        View.Focus();
    }
}

public sealed class VerticalRuler : RulerBase
{
    public VerticalRuler(DocumentView view) : base(view) { Width = 22; }

    private double Y(double pt) => View.PageRect(Page).Y + pt * View.Scale;
    private double Pt(double y) => (y - View.PageRect(Page).Y) / View.Scale;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (View.Layout.Pages.Count == 0) return;
        var page = View.Layout.Pages[Page];
        var sec = View.Editor.Doc.Section;
        double w = ActualWidth;
        double top = page.Body.Y, bottom = page.Body.Bottom;
        if (Dragging == "mt") top = DragValue / 20.0;
        if (Dragging == "mb") bottom = page.Height - DragValue / 20.0;
        double y0 = Y(0), yh = Y(page.Height);
        dc.DrawRectangle(MarginBrush, null, new Rect(3, y0, w - 6, Math.Max(0, yh - y0)));
        dc.DrawRectangle(TextArea, null, new Rect(3, Y(top), w - 6, Math.Max(0, Y(bottom) - Y(top))));
        double cmPt = TwipsPerCm / 20.0;
        int labelEvery = cmPt * View.Scale < 18 ? 2 : 1;
        double origin = sec.MarginTop / 20.0;
        for (int k = -(int)(origin / cmPt * 4) - 1; k <= (int)((page.Height - origin) / cmPt * 4) + 1; k++)
        {
            double y = Y(origin + k * cmPt / 4);
            if (y < y0 - 1 || y > yh + 1) continue;
            if (k % 4 == 0)
            {
                int cm = Math.Abs(k / 4);
                if (cm != 0 && cm % labelEvery == 0) Label(dc, cm.ToString(CultureInfo.CurrentCulture), new Point(w / 2, y), rotate: true);
            }
            else
            {
                double len = k % 2 == 0 ? 5 : 3;
                dc.DrawLine(TickPen, new Point(w / 2 - len / 2, Math.Round(y) + 0.5), new Point(w / 2 + len / 2, Math.Round(y) + 0.5));
            }
        }
    }

    private string? HitTest(Point p)
    {
        if (View.Layout.Pages.Count == 0) return null;
        var page = View.Layout.Pages[Page];
        var sec = View.Editor.Doc.Section;
        if (Math.Abs(p.Y - Y(sec.MarginTop / 20.0)) <= 4) return "mt";
        if (Math.Abs(p.Y - Y(page.Height - sec.MarginBottom / 20.0)) <= 4) return "mb";
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (Dragging is null)
        {
            var hit = HitTest(p);
            Cursor = hit is not null ? Cursors.SizeNS : Cursors.Arrow;
            ToolTip = hit switch { "mt" => "Верхнее поле", "mb" => "Нижнее поле", _ => null };
            return;
        }
        var page = View.Layout.Pages[Page];
        double pt = Pt(p.Y);
        DragValue = Dragging == "mt" ? Snap(Math.Clamp(pt, 0, page.Height - 72) * 20) : Snap(Math.Clamp(page.Height - pt, 0, page.Height - 72) * 20);
        View.GuideY = (Page, Dragging == "mt" ? DragValue / 20.0 : page.Height - DragValue / 20.0);
        ToolTip = Cm(DragValue);
        View.InvalidateVisual();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Dragging = HitTest(e.GetPosition(this));
        if (Dragging is null) return;
        if (View.IsReadOnly) { Dragging = null; return; }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Dragging is null) return;
        string what = Dragging;
        Dragging = null;
        ReleaseMouseCapture();
        View.GuideY = null;
        int v = (int)DragValue;
        var ed = View.Editor;
        var sec = ed.Doc.Section;
        if (what == "mt" && v != sec.MarginTop && sec.PageHeight - v - sec.MarginBottom > 1440) ed.SetMargins(v, null, null, null);
        if (what == "mb" && v != sec.MarginBottom && sec.PageHeight - v - sec.MarginTop > 1440) ed.SetMargins(null, null, v, null);
        View.InvalidateVisual();
        InvalidateVisual();
        View.Focus();
    }
}
