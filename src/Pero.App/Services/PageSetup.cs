using System.Windows;
using Syncfusion.Windows.Controls.RichTextBoxAdv;

namespace Pero.Services;

/// <summary>
/// Параметры страницы (PAGE-01, UI-03 «Макет»). В P0 размер, ориентация и поля едины для документа,
/// поэтому применяются ко всему документу через выделение — так правка попадает в историю отмены движка.
/// Единицы движка — независимые от устройства пиксели (1/96 дюйма).
/// </summary>
public static class PageSetup
{
    public const double PxPerMm = 96.0 / 25.4;

    public sealed record PaperSize(string Name, double WidthMm, double HeightMm);

    public static readonly PaperSize[] Sizes =
    [
        new("A4", 210, 297),
        new("A5", 148, 210),
        new("A3", 297, 420),
        new("Letter", 215.9, 279.4),
        new("Legal", 215.9, 355.6),
    ];

    public sealed record MarginPreset(string Key, double TopMm, double RightMm, double BottomMm, double LeftMm);

    public static readonly MarginPreset[] Margins =
    [
        new("Normal", 20, 15, 20, 30),     // Word (рус.) «Обычные»: 2 / 1,5 / 2 / 3 см
        new("Narrow", 12.7, 12.7, 12.7, 12.7),
        new("Moderate", 25.4, 19.1, 25.4, 19.1),
        new("Wide", 25.4, 50.8, 25.4, 50.8),
    ];

    public static (Size Page, Thickness Margin) Current(SfRichTextBoxAdv editor)
    {
        var sf = editor.Document.Sections.Cast<SectionAdv>().First().SectionFormat;
        return (sf.PageSize, sf.PageMargin);
    }

    public static void SetSize(SfRichTextBoxAdv editor, double widthMm, double heightMm)
    {
        var (page, _) = Current(editor);
        bool landscape = page.Width > page.Height;
        double w = widthMm * PxPerMm, h = heightMm * PxPerMm;
        if (landscape != (w > h)) (w, h) = (h, w);
        ApplyToDocument(editor, sf => sf.PageSize = new Size(w, h));
    }

    public static void SetOrientation(SfRichTextBoxAdv editor, bool landscape)
    {
        var (page, margin) = Current(editor);
        if ((page.Width > page.Height) == landscape) return;
        ApplyToDocument(editor, sf =>
        {
            sf.PageSize = new Size(page.Height, page.Width);
            // поля поворачиваются вместе со страницей, как в Word
            sf.PageMargin = new Thickness(margin.Bottom, margin.Left, margin.Top, margin.Right);
        });
    }

    public static void SetMargins(SfRichTextBoxAdv editor, double topMm, double rightMm, double bottomMm, double leftMm) =>
        ApplyToDocument(editor, sf => sf.PageMargin = new Thickness(leftMm * PxPerMm, topMm * PxPerMm, rightMm * PxPerMm, bottomMm * PxPerMm));

    /// <summary>Выделить весь документ, изменить формат раздела через выделение, вернуть выделение.</summary>
    private static void ApplyToDocument(SfRichTextBoxAdv editor, Action<SelectionSectionFormat> change)
    {
        var sel = editor.Selection;
        var start = sel.Start;
        var end = sel.End;
        sel.Select(editor.Document.DocumentStart, editor.Document.DocumentEnd);
        try { change(sel.SectionFormat); }
        finally { sel.Select(start, end); }
    }

    public static double ToMm(double px) => Math.Round(px / PxPerMm, 1);
}
