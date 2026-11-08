using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Pero.Services;

/// <summary>Иконки ленты из системного шрифта Segoe MDL2 Assets (есть в Windows 10/11) — без графических файлов.</summary>
public static class Icons
{
    public const char Page = '';
    public const char AlignLeft = '';
    public const char Paste = '';
    public const char Search = '';
    public const char PageBreak = '';

    private static readonly Dictionary<(char, double, bool), ImageSource> Cache = [];

    public static ImageSource Glyph(char glyph, double size = 16, bool rotate = false)
    {
        if (Cache.TryGetValue((glyph, size, rotate), out var cached)) return cached;
        var typeface = new Typeface(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size,
            new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        if (rotate) geometry.Transform = new RotateTransform(90, size / 2, size / 2);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        Cache[(glyph, size, rotate)] = image;
        return image;
    }
}
