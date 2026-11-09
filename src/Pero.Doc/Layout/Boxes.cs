using Pero.Doc.Model;

namespace Pero.Doc.Layout;

/// <summary>Прямоугольник в пунктах относительно левого верхнего угла страницы.</summary>
public readonly record struct RectPt(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

public enum SegKind { Text, Image, Object }

/// <summary>Отрезок строки с одинаковым оформлением: текст, изображение или нераспознанный объект.</summary>
public sealed class Seg
{
    public SegKind Kind { get; init; }
    /// <summary>Смещение первого символа в абзаце и число символов.</summary>
    public int Start { get; init; }
    public int Length { get; init; }
    /// <summary>X начала, относительно страницы.</summary>
    public double X { get; set; }
    public double Width { get; set; }
    /// <summary>Рисуемый текст (табуляции и служебные символы заменены пробелами).</summary>
    public string Text { get; init; } = "";
    /// <summary>Ширины символов Text, с учётом выключки по ширине.</summary>
    public double[] Advances { get; init; } = [];
    public FontKey Font { get; init; }
    public double SizePt { get; init; }
    /// <summary>Сдвиг базовой линии (верхний/нижний индекс), пункты вверх.</summary>
    public double Rise { get; init; }
    public EffectiveRun? Style { get; init; }
    public LinkRef? Link { get; init; }
    public InlineImage? Image { get; init; }
    public double Height { get; init; }
}

/// <summary>Строка разметки.</summary>
public sealed class LineBox
{
    /// <summary>null — основной текст, иначе колонтитул.</summary>
    public HeaderFooter? Story { get; init; }
    /// <summary>Номер абзаца в сквозном перечне своей части (как в ParaIndex).</summary>
    public int Para { get; set; }
    public Paragraph Paragraph { get; init; } = null!;
    public int Start { get; init; }
    public int End { get; init; }
    public int Page { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Ширина области, доступной строке (для выделения до правого края).</summary>
    public double AreaWidth { get; init; }
    public double Height { get; init; }
    public double Baseline { get; init; }
    public List<Seg> Segs { get; } = [];
    /// <summary>X каждой позиции курсора Start..End (длина End−Start+1), относительно страницы.</summary>
    public double[] CaretX { get; set; } = [];
    public bool LastInParagraph { get; init; }
    /// <summary>Строка закончилась принудительным разрывом (Shift+Enter, разрыв страницы).</summary>
    public bool EndsWithBreak { get; init; }
    /// <summary>Вид принудительного разрыва в конце строки.</summary>
    public BreakKind? BreakAfter { get; init; }
    /// <summary>Копия строки заголовка таблицы, повторённая на следующей странице.</summary>
    public bool Repeat { get; internal set; }
    public string? Label { get; init; }
    public double LabelX { get; set; }
    public FontKey LabelFont { get; init; }
    public double LabelSize { get; init; }
    public string LabelColor { get; init; } = "000000";

    internal void Shift(double dx, double dy)
    {
        X += dx; Y += dy; LabelX += dx;
        foreach (var s in Segs) s.X += dx;
        for (int i = 0; i < CaretX.Length; i++) CaretX[i] += dx;
    }

    internal LineBox CopyShifted(double dx, double dy)
    {
        var c = new LineBox
        {
            Story = Story, Para = Para, Paragraph = Paragraph, Start = Start, End = End, Page = Page, X = X, Y = Y, AreaWidth = AreaWidth,
            Height = Height, Baseline = Baseline, CaretX = (double[])CaretX.Clone(), LastInParagraph = LastInParagraph,
            EndsWithBreak = EndsWithBreak, BreakAfter = BreakAfter, Label = Label, LabelX = LabelX, LabelFont = LabelFont, LabelSize = LabelSize, LabelColor = LabelColor,
        };
        foreach (var s in Segs)
            c.Segs.Add(new Seg
            {
                Kind = s.Kind, Start = s.Start, Length = s.Length, X = s.X, Width = s.Width, Text = s.Text, Advances = s.Advances, Font = s.Font,
                SizePt = s.SizePt, Rise = s.Rise, Style = s.Style, Link = s.Link, Image = s.Image, Height = s.Height,
            });
        c.Shift(dx, dy);
        return c;
    }
}

public enum DecorKind { Line, Fill }

/// <summary>Оформление вне текста: границы и заливка ячеек.</summary>
public sealed class Decor
{
    public DecorKind Kind { get; init; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double Thickness { get; init; } = 0.5;
    public string Color { get; init; } = "000000";

    internal Decor Shifted(double dx, double dy) => new() { Kind = Kind, X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy, Thickness = Thickness, Color = Color };
}

public sealed class PageBox
{
    public int Index { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Область основного текста.</summary>
    public RectPt Body { get; set; }
    public List<LineBox> Lines { get; } = [];
    public List<Decor> Decor { get; } = [];
    public List<LineBox> HeaderLines { get; } = [];
    public List<LineBox> FooterLines { get; } = [];
    public List<Decor> HeaderDecor { get; } = [];
    public List<Decor> FooterDecor { get; } = [];
    public HeaderFooter? Header { get; set; }
    public HeaderFooter? Footer { get; set; }
    public RectPt HeaderArea { get; set; }
    public RectPt FooterArea { get; set; }
}

/// <summary>Положение курсора: страница, x, верх и высота строки.</summary>
public readonly record struct CaretBox(int Page, double X, double Y, double Height);
