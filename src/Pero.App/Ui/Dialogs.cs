using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pero.Doc.Model;
using Orientation = System.Windows.Controls.Orientation;

namespace Pero.Ui;

/// <summary>Простые диалоги: строятся кодом, подписи по-русски, Enter — ОК, Esc — отмена.</summary>
public class FormDialog : Window
{
    private readonly Grid _grid = new() { Margin = new Thickness(16, 14, 16, 8) };
    private readonly Func<string?>? _validate;
    protected int Rows;

    public FormDialog(Window owner, string title, Func<string?>? validate = null)
    {
        Owner = owner;
        Title = title;
        _validate = validate;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 4, 16, 14) };
        var ok = new Button { Content = "ОК", IsDefault = true, MinWidth = 84, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 84, Height = 28 };
        ok.Click += (_, _) =>
        {
            if (Validate() is { } problem) { MessageBox.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_grid);
        Content = root;
    }

    protected virtual string? Validate() => _validate?.Invoke();

    public T AddRow<T>(string label, T control) where T : FrameworkElement
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = new Label { Content = label, Target = control, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(l, Rows); Grid.SetColumn(l, 0);
        control.Margin = new Thickness(0, 4, 0, 4);
        Grid.SetRow(control, Rows); Grid.SetColumn(control, 1);
        _grid.Children.Add(l);
        _grid.Children.Add(control);
        Rows++;
        return control;
    }

    public void AddNote(string text)
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var t = new TextBlock { Text = text, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 6, 0, 2) };
        Grid.SetRow(t, Rows); Grid.SetColumnSpan(t, 2);
        _grid.Children.Add(t);
        Rows++;
    }

    public static TextBox Box(string text = "", double width = 0) => new() { Text = text, Height = 26, VerticalContentAlignment = VerticalAlignment.Center, MinWidth = width };

    public static string Num(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

    public static bool TryNum(string s, out double v) =>
        double.TryParse(s.Replace('.', ',').Trim(), NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out v);
}

/// <summary>Ссылка: текст и адрес (OBJ-01).</summary>
public sealed class LinkDialog : FormDialog
{
    private readonly TextBox _text, _url;
    public string Url => _url.Text.Trim();
    public string? LinkText => _text.Text.Length == 0 ? null : _text.Text;

    public LinkDialog(Window owner, string? text, string? url, bool textEditable) : base(owner, "Ссылка")
    {
        _text = AddRow("Текст:", Box(text ?? "", 300));
        _text.IsEnabled = textEditable;
        _url = AddRow("Адрес:", Box(url ?? "https://", 300));
        AddNote("Ссылки открываются по Ctrl+щелчку. Разрешены адреса http, https и mailto.");
        Loaded += (_, _) => { _url.Focus(); _url.SelectAll(); };
    }

    protected override string? Validate()
    {
        var (decision, _, reason) = Pero.Core.Security.LinkPolicy.Evaluate(Url);
        return decision == Pero.Core.Security.LinkDecision.Block ? "Адрес не подходит: " + reason + "." : null;
    }
}

/// <summary>Размер новой таблицы.</summary>
public sealed class TableDialog : FormDialog
{
    private readonly TextBox _rows, _cols;
    public int RowsCount => int.Parse(_rows.Text);
    public int Cols => int.Parse(_cols.Text);

    public TableDialog(Window owner) : base(owner, "Вставить таблицу")
    {
        _rows = AddRow("Строк:", Box("3", 80));
        _cols = AddRow("Столбцов:", Box("3", 80));
        Loaded += (_, _) => { _rows.Focus(); _rows.SelectAll(); };
    }

    protected override string? Validate() =>
        !int.TryParse(_rows.Text, out int r) || r is < 1 or > 100 ? "Число строк — от 1 до 100."
        : !int.TryParse(_cols.Text, out int c) || c is < 1 or > 30 ? "Число столбцов — от 1 до 30."
        : null;
}

/// <summary>Параметры страницы (PAGE-01): размер, ориентация, поля в сантиметрах.</summary>
public sealed class PageSetupDialog : FormDialog
{
    private const double TwPerCm = 566.929;
    private readonly ComboBox _size = new() { Height = 26 };
    private readonly ComboBox _orient = new() { Height = 26 };
    private readonly TextBox _top, _bottom, _left, _right;
    public (int W, int H) PaperPortrait { get; private set; }
    public Pero.Doc.Model.Orientation Orientation => _orient.SelectedIndex == 1 ? Pero.Doc.Model.Orientation.Landscape : Pero.Doc.Model.Orientation.Portrait;
    public int Top => Tw(_top.Text);
    public int Bottom => Tw(_bottom.Text);
    public int Left => Tw(_left.Text);
    public int Right => Tw(_right.Text);

    private static int Tw(string s) => TryNum(s, out double v) ? (int)Math.Round(v * TwPerCm) : 0;

    public PageSetupDialog(Window owner, SectionProps s) : base(owner, "Параметры страницы")
    {
        _size.Items.Add("A4 (21 × 29,7 см)");
        _size.Items.Add("Letter (21,59 × 27,94 см)");
        int w = Math.Min(s.PageWidth, s.PageHeight), h = Math.Max(s.PageWidth, s.PageHeight);
        bool letter = Math.Abs(w - 12240) < 60 && Math.Abs(h - 15840) < 60;
        bool a4 = Math.Abs(w - 11906) < 60 && Math.Abs(h - 16838) < 60;
        if (!letter && !a4) _size.Items.Add($"Текущий ({Num(w / TwPerCm)} × {Num(h / TwPerCm)} см)");
        _size.SelectedIndex = letter ? 1 : a4 ? 0 : 2;
        _orient.Items.Add("Книжная");
        _orient.Items.Add("Альбомная");
        _orient.SelectedIndex = s.Orientation == Pero.Doc.Model.Orientation.Landscape ? 1 : 0;
        AddRow("Размер бумаги:", _size);
        AddRow("Ориентация:", _orient);
        _top = AddRow("Верхнее поле, см:", Box(Num(s.MarginTop / TwPerCm), 80));
        _bottom = AddRow("Нижнее поле, см:", Box(Num(s.MarginBottom / TwPerCm), 80));
        _left = AddRow("Левое поле, см:", Box(Num(s.MarginLeft / TwPerCm), 80));
        _right = AddRow("Правое поле, см:", Box(Num(s.MarginRight / TwPerCm), 80));
        PaperPortrait = (w, h);
        _size.SelectionChanged += (_, _) => PaperPortrait = _size.SelectedIndex switch { 0 => (11906, 16838), 1 => (12240, 15840), _ => (w, h) };
    }

    protected override string? Validate()
    {
        foreach (var b in new[] { _top, _bottom, _left, _right })
            if (!TryNum(b.Text, out double v) || v < 0 || v > 20) return "Поля — числа от 0 до 20 см.";
        var (pw, ph) = Orientation == Pero.Doc.Model.Orientation.Landscape ? (PaperPortrait.H, PaperPortrait.W) : PaperPortrait;
        if (Left + Right > pw - 1440 || Top + Bottom > ph - 1440) return "Поля не помещаются на странице.";
        return null;
    }
}

/// <summary>Абзац: выравнивание, отступы, интервалы (Формат → Абзац).</summary>
public sealed class ParagraphDialog : FormDialog
{
    private const double TwPerCm = 566.929;
    private readonly ComboBox _align = new() { Height = 26 };
    private readonly TextBox _left, _right, _first, _before, _after;
    private readonly ComboBox _line = new() { Height = 26, IsEditable = true };
    public Align Align => (Align)_align.SelectedIndex;
    public int IndLeft => (int)Math.Round(N(_left.Text) * TwPerCm);
    public int IndRight => (int)Math.Round(N(_right.Text) * TwPerCm);
    public int IndFirst => (int)Math.Round(N(_first.Text) * TwPerCm);
    public int SpaceBefore => (int)Math.Round(N(_before.Text) * 20);
    public int SpaceAfter => (int)Math.Round(N(_after.Text) * 20);
    public int Line240 => (int)Math.Round(N(_line.Text) * 240);

    private static double N(string s) => TryNum(s, out double v) ? v : 0;

    public ParagraphDialog(Window owner, EffectivePara p) : base(owner, "Абзац")
    {
        foreach (var a in new[] { "По левому краю", "По центру", "По правому краю", "По ширине" }) _align.Items.Add(a);
        _align.SelectedIndex = (int)p.Align;
        AddRow("Выравнивание:", _align);
        _left = AddRow("Отступ слева, см:", Box(Num(p.IndLeft / TwPerCm), 80));
        _right = AddRow("Отступ справа, см:", Box(Num(p.IndRight / TwPerCm), 80));
        _first = AddRow("Первая строка, см:", Box(Num(p.IndFirst / TwPerCm), 80));
        _before = AddRow("Интервал перед, пт:", Box(Num(p.SpaceBefore / 20.0), 80));
        _after = AddRow("Интервал после, пт:", Box(Num(p.SpaceAfter / 20.0), 80));
        foreach (var v in new[] { "1", "1,15", "1,5", "2" }) _line.Items.Add(v);
        _line.Text = p.LineRule == LineRule.Auto ? Num(p.Line / 240.0) : "1";
        AddRow("Межстрочный (множитель):", _line);
        AddNote("Отрицательное значение первой строки — выступ.");
    }

    protected override string? Validate()
    {
        foreach (var b in new[] { _left, _right, _first }) if (!TryNum(b.Text, out double v) || Math.Abs(v) > 30) return "Отступы — числа от −30 до 30 см.";
        foreach (var b in new[] { _before, _after }) if (!TryNum(b.Text, out double v) || v < 0 || v > 500) return "Интервалы — от 0 до 500 пт.";
        if (!TryNum(_line.Text, out double l) || l < 0.5 || l > 10) return "Межстрочный интервал — от 0,5 до 10.";
        return null;
    }
}

/// <summary>Шрифт: семейство, размер и начертание (Формат → Шрифт).</summary>
public sealed class FontDialog : FormDialog
{
    private readonly ComboBox _family = new() { Height = 26, IsEditable = true, MaxDropDownHeight = 320 };
    private readonly TextBox _size;
    private readonly CheckBox _bold = new() { Content = "Полужирный" }, _italic = new() { Content = "Курсив" }, _underline = new() { Content = "Подчёркнутый" }, _strike = new() { Content = "Зачёркнутый" };
    public string Family => _family.Text.Trim();
    public double SizePt => TryNum(_size.Text, out double v) ? v : 0;
    public bool? Bold => _bold.IsChecked;
    public bool? Italic => _italic.IsChecked;
    public bool? Underline => _underline.IsChecked;
    public bool? Strike => _strike.IsChecked;

    public FontDialog(Window owner, IEnumerable<string> families, Pero.Doc.Editing.FormatState st) : base(owner, "Шрифт")
    {
        foreach (var f in families) _family.Items.Add(f);
        _family.Text = st.Font ?? "";
        AddRow("Шрифт:", _family);
        _size = AddRow("Размер, пт:", Box(st.SizePt is double s ? Num(s) : "", 80));
        foreach (var c in new[] { _bold, _italic, _underline, _strike }) c.IsThreeState = true;
        _bold.IsChecked = st.Bold; _italic.IsChecked = st.Italic; _underline.IsChecked = st.Underline; _strike.IsChecked = false;
        var panel = new WrapPanel();
        foreach (var c in new[] { _bold, _italic, _underline, _strike }) { c.Margin = new Thickness(0, 0, 14, 0); panel.Children.Add(c); }
        AddRow("Начертание:", panel);
    }

    protected override string? Validate() =>
        _size.Text.Trim().Length > 0 && (SizePt < 1 || SizePt > 1638) ? "Размер шрифта — от 1 до 1638 пт." : null;
}

/// <summary>Окно с текстом (отчёты, горячие клавиши).</summary>
public sealed class TextWindow : Window
{
    public TextWindow(Window owner, string title, string text)
    {
        Owner = owner;
        Title = title;
        Width = 620;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Padding = new Thickness(14),
        };
    }
}
