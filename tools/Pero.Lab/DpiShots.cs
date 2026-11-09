using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pero.Lab;

/// <summary>
/// AC-01: окно «Пера» при 150 % и 200 %. WPF раскладывает интерфейс в независимых пикселях, поэтому окно
/// рисуется в памяти с DPI 144 и 192 в тех логических размерах, которые получаются на реальных экранах.
/// Дополнительно проверяется, что элементы панели не перекрываются.
/// </summary>
public static class DpiShots
{
    public static int Run(string samples, string outDir, Action<string> log)
    {
        Directory.CreateDirectory(outDir);
        int failures = 0;
        var app = new Pero.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string sample = Path.Combine(samples, "01-styles-lists-multipage.docx");
        string copy = Path.Combine(Path.GetTempPath(), "pero-dpi-01.docx");
        File.Copy(sample, copy, true);
        (string Name, double W, double H, double Dpi)[] cases =
        [
            ("dpi100-1366x768", 1366, 768, 96),
            ("dpi150-1366x768-screen2049", 1366, 768, 144),
            ("dpi150-910x512-on-1366x768", 910, 512, 144),
            ("dpi200-1366x768-screen2732", 1366, 768, 192),
            ("dpi200-1024x640", 1024, 640, 192),
        ];
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                foreach (var (name, w, h, dpi) in cases)
                {
                    var win = new Pero.EditorWindow { Width = w, Height = h, Left = 0, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                    win.MinWidth = Math.Min(win.MinWidth, w);
                    win.MinHeight = Math.Min(win.MinHeight, h);
                    win.Show();
                    Pero.EditorWindow.OpenInNewWindow(copy, win);
                    await Task.Delay(3000);
                    var content = (FrameworkElement)win.Content;
                    content.UpdateLayout();
                    var bmp = Render(content, dpi);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var fs = File.Create(Path.Combine(outDir, $"ui-{name}.png"))) enc.Save(fs);
                    var overlaps = Overlaps(content);
                    log($"dpi {name}: {(overlaps.Count == 0 ? "OK" : "OVERLAP " + string.Join("; ", overlaps))}");
                    if (overlaps.Count > 0) failures++;
                    win.Close();
                    await Task.Delay(300);
                }
            }
            catch (Exception ex) { failures++; log("dpi FAILED: " + ex); }
            finally { app.Shutdown(); }
        });
        app.Run();
        return failures;
    }

    private static BitmapSource Render(FrameworkElement e, double dpi)
    {
        double w = e.ActualWidth, h = e.ActualHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(e) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * dpi / 96), (int)Math.Ceiling(h * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return rtb;
    }

    /// <summary>Пары видимых элементов панели и строки меню, которые перекрываются больше чем на 1 пиксель.</summary>
    private static List<string> Overlaps(FrameworkElement root)
    {
        var res = new List<string>();
        foreach (var bar in Descendants<ItemsControl>(root).Where(c => c is ToolBar or Menu))
        {
            var items = bar.Items.OfType<FrameworkElement>()
                .Where(i => i.IsVisible && i.ActualWidth > 0 && (bar is not ToolBar || ToolBar.GetIsOverflowItem(i) == false))
                .Select(i => (Item: i, Rect: i.TransformToAncestor(root).TransformBounds(new Rect(0, 0, i.ActualWidth, i.ActualHeight))))
                .ToList();
            for (int a = 0; a < items.Count; a++)
                for (int b = a + 1; b < items.Count; b++)
                {
                    var r = Rect.Intersect(items[a].Rect, items[b].Rect);
                    if (!r.IsEmpty && r.Width > 1 && r.Height > 1)
                        res.Add($"{Describe(items[a].Item)} × {Describe(items[b].Item)}");
                }
            // элементы не должны выходить за правый край окна
            foreach (var (item, rect) in items)
                if (rect.Right > root.ActualWidth + 1) res.Add($"{Describe(item)} за краем окна");
        }
        return res;
    }

    private static string Describe(FrameworkElement e) =>
        System.Windows.Automation.AutomationProperties.GetName(e) is { Length: > 0 } n ? n : e is HeaderedItemsControl h ? h.Header?.ToString() ?? e.GetType().Name : e.GetType().Name;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }
}
