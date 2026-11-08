using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pero.Doc.Fonts;
using Pero.Doc.Layout;
using Pero.Doc.Render;
using Pero.Services;

namespace Pero.Editor;

/// <summary>
/// Холст WPF для общей отрисовки страниц (экран и печать). Координаты — пункты; масштаб задаёт вызывающий.
/// Глифы берутся из того же файла шрифта, по которому разметка мерила ширины.
/// </summary>
public sealed class WpfCanvas(DrawingContext dc, double pixelsPerDip) : IPageCanvas
{
    private static readonly Dictionary<(string Path, int Index, bool Bold, bool Italic), GlyphTypeface?> Typefaces = [];
    private static readonly Dictionary<byte[], BitmapSource?> Bitmaps = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<(Rgb, double), Pen> Pens = [];
    private static readonly Dictionary<Rgb, SolidColorBrush> Brushes = [];

    public static SolidColorBrush Brush(Rgb c)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(c, out var b))
            {
                b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                b.Freeze();
                Brushes[c] = b;
            }
            return b;
        }
    }

    private static Pen PenFor(Rgb c, double w)
    {
        lock (Pens)
        {
            if (!Pens.TryGetValue((c, w), out var p))
            {
                p = new Pen(Brush(c), w) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
                p.Freeze();
                Pens[(c, w)] = p;
            }
            return p;
        }
    }

    public static GlyphTypeface? TypefaceFor(ResolvedFont f)
    {
        var key = (f.Face.Path, f.Face.FaceIndex, f.SimulateBold, f.SimulateItalic);
        lock (Typefaces)
        {
            if (Typefaces.TryGetValue(key, out var gt)) return gt;
            try
            {
                var sim = StyleSimulations.None;
                if (f.SimulateBold) sim |= StyleSimulations.BoldSimulation;
                if (f.SimulateItalic) sim |= StyleSimulations.ItalicSimulation;
                var uri = new Uri(f.Face.Path + (f.Face.FaceIndex > 0 ? "#" + f.Face.FaceIndex : ""));
                gt = new GlyphTypeface(uri, sim);
            }
            catch (Exception ex)
            {
                Log.Error("glyph-typeface " + f.Face, ex);
                gt = null;
            }
            Typefaces[key] = gt;
            return gt;
        }
    }

    public void FillRect(RectPt r, Rgb color) => dc.DrawRectangle(Brush(color), null, new Rect(r.X, r.Y, Math.Max(0, r.W), Math.Max(0, r.H)));

    public void Line(double x1, double y1, double x2, double y2, double width, Rgb color) =>
        dc.DrawLine(PenFor(color, width), new Point(x1, y1), new Point(x2, y2));

    public void Glyphs(GlyphRunInfo run)
    {
        var gt = TypefaceFor(run.Font);
        if (gt is null || run.Glyphs.Length == 0) return;
        try
        {
            var glyphs = run.Glyphs.Select(g => g < gt.GlyphCount ? g : (ushort)0).ToArray();
            var gr = new GlyphRun(gt, 0, false, run.SizePt, (float)pixelsPerDip, glyphs, new Point(run.X, run.BaselineY),
                run.Advances, null, null, null, null, null, null);
            dc.DrawGlyphRun(Brush(run.Color), gr);
        }
        catch (Exception ex) { Log.Error("glyph-run", ex); }
    }

    public void Image(byte[] data, RectPt r)
    {
        BitmapSource? bmp;
        lock (Bitmaps)
        {
            if (!Bitmaps.TryGetValue(data, out bmp))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(data);
                    bi.EndInit();
                    bi.Freeze();
                    bmp = bi;
                }
                catch (Exception ex) { Log.Error("image-decode", ex); bmp = null; }
                Bitmaps[data] = bmp;
            }
        }
        var rect = new Rect(r.X, r.Y, Math.Max(1, r.W), Math.Max(1, r.H));
        if (bmp is null) dc.DrawRectangle(System.Windows.Media.Brushes.LightGray, null, rect);
        else dc.DrawImage(bmp, rect);
    }
}
