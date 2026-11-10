using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Pero.Ui;

/// <summary>Оформление в духе образца (UI-03): светлая серо-голубая панель, тёмные значки, скругления.</summary>
public static class Theme
{
    public static readonly Brush Window = Frozen(Color.FromRgb(0xF9, 0xFB, 0xFD));
    public static readonly Brush Toolbar = Frozen(Color.FromRgb(0xED, 0xF2, 0xFA));
    public static readonly Brush Ink = Frozen(Color.FromRgb(0x44, 0x47, 0x46));
    public static readonly Brush Muted = Frozen(Color.FromRgb(0x5F, 0x63, 0x68));
    public static readonly Brush Hover = Frozen(Color.FromRgb(0xDF, 0xE5, 0xEE));
    public static readonly Brush Active = Frozen(Color.FromRgb(0xD3, 0xE3, 0xFD));
    public static readonly Brush Separator = Frozen(Color.FromRgb(0xC7, 0xCD, 0xD6));
    public static readonly Brush Accent = Frozen(Color.FromRgb(0x1A, 0x73, 0xE8));
    public static readonly Brush Banner = Frozen(Color.FromRgb(0xFE, 0xF7, 0xE0));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Контурные значки 16×16 (собственные, без шрифтов значков).</summary>
    public static class Icon
    {
        public const string Undo = "M5.5,3.5 L2.5,6.5 L5.5,9.5 M2.5,6.5 H10 A3.5,3.5 0 0 1 10,13.5 H7";
        public const string Redo = "M10.5,3.5 L13.5,6.5 L10.5,9.5 M13.5,6.5 H6 A3.5,3.5 0 0 0 6,13.5 H9";
        public const string Print = "M4.5,6 V2.5 H11.5 V6 M4.5,11.5 H2 V6 H14 V11.5 H11.5 M4.5,9 H11.5 V14 H4.5 Z";
        public const string Paint = "M3,2.5 H11.5 V6 H3 Z M11.5,4.2 H13.5 V8 H7.5 V9.5 M6.5,9.5 H8.5 V14 H6.5 Z";
        public const string Search = "M10.2,10.2 L14,14 M11,6.5 A4.5,4.5 0 1 1 2,6.5 A4.5,4.5 0 1 1 11,6.5";
        public const string Link = "M6.5,9.5 L9.5,6.5 M7.5,4.5 L9,3 A2.5,2.5 0 0 1 13,7 L11.5,8.5 M8.5,11.5 L7,13 A2.5,2.5 0 0 1 3,9 L4.5,7.5";
        public const string Image = "M2,2.5 H14 V13.5 H2 Z M2,11 L5.5,7.5 L8.5,10.5 L10.5,8.5 L14,12 M10.5,5.2 A1,1 0 1 1 10.49,5.2";
        public const string AlignLeft = "M2,3.5 H14 M2,6.5 H10 M2,9.5 H14 M2,12.5 H10";
        public const string AlignCenter = "M2,3.5 H14 M4,6.5 H12 M2,9.5 H14 M4,12.5 H12";
        public const string AlignRight = "M2,3.5 H14 M6,6.5 H14 M2,9.5 H14 M6,12.5 H14";
        public const string AlignJustify = "M2,3.5 H14 M2,6.5 H14 M2,9.5 H14 M2,12.5 H14";
        public const string Spacing = "M7,3.5 H14 M7,6.5 H14 M7,9.5 H14 M7,12.5 H14 M3.5,2.5 V13.5 M2,4 L3.5,2.5 L5,4 M2,12 L3.5,13.5 L5,12";
        public const string Bullets = "M6,4 H14 M6,8 H14 M6,12 H14 M2.8,4 H3 M2.8,8 H3 M2.8,12 H3";
        public const string Numbers = "M6.5,4 H14 M6.5,8 H14 M6.5,12 H14 M2.2,2.6 L3.2,2.2 V5.5 M1.8,7 Q3.6,6.2 3.8,7.6 L1.8,9.6 H4 M1.8,11 H3.8 L2.8,12.2 Q4.2,12.6 3.6,13.6 Q2.8,14.2 1.8,13.6";
        public const string IndentLess = "M7,4 H14 M7,8 H14 M7,12 H14 M5,6 L2,8 L5,10";
        public const string IndentMore = "M7,4 H14 M7,8 H14 M7,12 H14 M2,6 L5,8 L2,10";
        public const string ClearFormat = "M3,3.5 H11 M7,3.5 V13 M9.5,9.5 L14,14 M14,9.5 L9.5,14";
        public const string Highlight = "M3.5,10.5 L9.5,4.5 L12,7 L6,13 H3.5 Z M8.5,5.5 L11,8";
        public const string Minus = "M4,8 H12";
        public const string Plus = "M4,8 H12 M8,4 V12";
        public const string Chevron = "M4,6.5 L8,10 L12,6.5";
        public const string Close = "M4,4 L12,12 M12,4 L4,12";
        public const string Up = "M4,10 L8,6 L12,10";
        public const string Down = "M4,6 L8,10 L12,6";
        public const string Pen = "M3,13 L4,9.5 L11,2.5 L13.5,5 L6.5,12 Z M9.8,3.7 L12.3,6.2 M3,13 L5.5,12.2";
    }

    public static Path IconPath(string data, double size = 16, Brush? stroke = null) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke ?? Ink,
        StrokeThickness = 1.4,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.None,
        SnapsToDevicePixels = true,
    };

    /// <summary>Кнопка панели 28×28 с подсказкой «Название (сочетание)» и именем для UI Automation.</summary>
    public static Button ToolButton(string name, object content, string? gesture, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = content,
            ToolTip = gesture is null ? name : $"{name} ({gesture})",
            MinWidth = 28,
            Height = 28,
            Padding = new Thickness(4, 0, 4, 0),
            Margin = new Thickness(1, 0, 1, 0),
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Ink,
            Style = FlatStyle,
        };
        AutomationProperties.SetName(b, name);
        b.Click += click;
        return b;
    }

    public static ToggleButton ToolToggle(string name, object content, string? gesture, RoutedEventHandler click)
    {
        var b = new ToggleButton
        {
            Content = content,
            ToolTip = gesture is null ? name : $"{name} ({gesture})",
            Width = 28,
            Height = 28,
            Margin = new Thickness(1, 0, 1, 0),
            Focusable = false,
            Foreground = Ink,
            Style = FlatToggleStyle,
        };
        AutomationProperties.SetName(b, name);
        b.Click += click;
        return b;
    }

    public static TextBlock Letter(string text, FontWeight? weight = null, FontStyle? style = null, bool underline = false, string family = "Segoe UI") => new()
    {
        Text = text,
        FontFamily = new FontFamily(family),
        FontSize = 15,
        FontWeight = weight ?? FontWeights.Normal,
        FontStyle = style ?? FontStyles.Normal,
        TextDecorations = underline ? TextDecorations.Underline : null,
        Foreground = Ink,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    public static Border Divider() => new()
    {
        Width = 1,
        Height = 20,
        Margin = new Thickness(5, 0, 5, 0),
        Background = Separator,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Значок с цветной полосой снизу (цвет текста, маркер).</summary>
    public static FrameworkElement ColorIcon(FrameworkElement glyph, out Rectangle bar)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        glyph.Margin = new Thickness(0, -2, 0, 0);
        panel.Children.Add(glyph);
        bar = new Rectangle { Width = 14, Height = 3, Fill = Brushes.Black, Margin = new Thickness(0, 0, 0, 1) };
        panel.Children.Add(bar);
        return panel;
    }

    /// <summary>Кнопка со стрелкой, открывающая компактное меню (UI-05).</summary>
    public static Button DropButton(string name, FrameworkElement icon, Func<ContextMenu> menu)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(IconPath(Icon.Chevron, 12));
        var b = ToolButton(name, content, null, (s, _) => { });
        b.Click += (s, _) =>
        {
            var m = menu();
            m.PlacementTarget = (UIElement)s;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        };
        return b;
    }

    private static Style? _flat, _flatToggle;

    public static Style FlatStyle => _flat ??= MakeFlat(typeof(Button));
    public static Style FlatToggleStyle => _flatToggle ??= MakeFlat(typeof(ToggleButton));

    /// <summary>Плоская кнопка со скруглением; наведение и нажатое состояние — заливкой.</summary>
    private static Style MakeFlat(Type type)
    {
        var template = new ControlTemplate(type);
        var border = new FrameworkElementFactory(typeof(Border), "Bd");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "Bd"));
        template.Triggers.Add(hover);
        if (type == typeof(ToggleButton))
        {
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, Active, "Bd"));
            template.Triggers.Add(on);
        }
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Active, "Bd"));
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4));
        template.Triggers.Add(disabled);
        var style = new Style(type);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Arrow));
        return style;
    }

    /// <summary>Палитра цветов текста (как в Word/Google Docs): 8×5.</summary>
    public static readonly string[] TextColors =
    [
        "000000", "434343", "666666", "999999", "B7B7B7", "CCCCCC", "EFEFEF", "FFFFFF",
        "980000", "FF0000", "FF9900", "FFFF00", "00FF00", "00FFFF", "4A86E8", "0000FF",
        "E6B8AF", "F4CCCC", "FCE5CD", "FFF2CC", "D9EAD3", "D0E0E3", "C9DAF8", "CFE2F3",
        "CC4125", "E06666", "F6B26B", "FFD966", "93C47D", "76A5AF", "6D9EEB", "6FA8DC",
        "85200C", "990000", "B45F06", "BF9000", "38761D", "134F5C", "1155CC", "0B5394",
    ];

    /// <summary>Цвета маркера, доступные в Word (w:highlight).</summary>
    public static readonly (string Name, string Hex, string Title)[] HighlightColors =
    [
        ("yellow", "FFFF00", Loc.L("Жёлтый")), ("green", "00FF00", Loc.L("Ярко-зелёный")), ("cyan", "00FFFF", Loc.L("Бирюзовый")), ("magenta", "FF00FF", Loc.L("Розовый")),
        ("blue", "0000FF", Loc.L("Синий")), ("red", "FF0000", Loc.L("Красный")), ("darkBlue", "00008B", Loc.L("Тёмно-синий")), ("darkCyan", "008B8B", Loc.L("Сине-зелёный")),
        ("darkGreen", "006400", Loc.L("Зелёный")), ("darkMagenta", "8B008B", Loc.L("Фиолетовый")), ("darkRed", "8B0000", Loc.L("Тёмно-красный")), ("darkYellow", "808000", Loc.L("Коричнево-жёлтый")),
        ("darkGray", "A9A9A9", Loc.L("Серый 50%")), ("lightGray", "D3D3D3", Loc.L("Серый 25%")), ("black", "000000", Loc.L("Чёрный")),
    ];

    public static Color Hex(string hex) => Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
}
