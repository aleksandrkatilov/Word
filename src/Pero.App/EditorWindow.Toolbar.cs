using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using Orientation = System.Windows.Controls.Orientation;
using Pero.Services;
using Pero.Ui;

namespace Pero;

/// <summary>Компактная панель (UI-03…UI-06): порядок групп как на образце, состояние отражает выделение.</summary>
public sealed partial class EditorWindow
{
    private ToolBar _toolbar = null!;
    private ComboBox _zoomCombo = null!, _styleCombo = null!, _fontCombo = null!, _sizeCombo = null!;
    private ToggleButton _bold = null!, _italic = null!, _underline = null!, _painter = null!;
    private Button _undo = null!, _redo = null!;
    private Rectangle _colorBar = null!, _highlightBar = null!;
    private ContentControl _alignIcon = null!, _listIcon = null!;
    private TextBox _commandBox = null!;
    private Popup _commandPopup = null!;
    private ListBox _commandList = null!;
    private bool _updatingUi;

    private UIElement BuildToolbar()
    {
        _toolbar = new ToolBar { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Height = 38, Padding = new Thickness(0) };
        AutomationProperties.SetName(_toolbar, "Панель форматирования");
        var tray = new ToolBarTray { IsLocked = true, Background = Brushes.Transparent };
        tray.ToolBars.Add(_toolbar);
        var host = new Border
        {
            Background = Theme.Toolbar,
            CornerRadius = new CornerRadius(19),
            Margin = new Thickness(8, 2, 8, 6),
            Padding = new Thickness(10, 0, 6, 0),
            Child = tray,
        };

        void Add(UIElement e, bool keep = false)
        {
            ToolBar.SetOverflowMode(e, keep ? OverflowMode.Never : OverflowMode.AsNeeded);
            _toolbar.Items.Add(e);
        }
        Button Btn(string command, string icon, string? tip = null)
        {
            var c = _commands[command];
            return Theme.ToolButton(tip ?? c.Name, Theme.IconPath(icon), c.Gesture, (_, _) => Exec(c));
        }

        // поиск команд «Меню»
        Add(BuildCommandSearch(), keep: true);
        Add(Theme.Divider(), keep: true);
        // отмена, повтор, печать, копирование формата
        _undo = Btn("Отменить", Theme.Icon.Undo); Add(_undo, keep: true);
        _redo = Btn("Повторить", Theme.Icon.Redo); Add(_redo, keep: true);
        Add(Btn("Печать…", Theme.Icon.Print, "Печать"));
        _painter = Theme.ToolToggle("Копировать форматирование", Theme.IconPath(Theme.Icon.Paint), null, (_, _) => Exec("Копировать форматирование"));
        Add(_painter);
        Add(Theme.Divider());
        // масштаб
        _zoomCombo = new ComboBox { Width = 74, Height = 26, ToolTip = "Масштаб", Focusable = true, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var z in new[] { "50%", "75%", "90%", "100%", "125%", "150%", "200%", "По ширине" }) _zoomCombo.Items.Add(z);
        _zoomCombo.SelectedIndex = 3;
        AutomationProperties.SetName(_zoomCombo, "Масштаб");
        _zoomCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || _zoomCombo.SelectedItem is not string s) return;
            if (s == "По ширине") _view.FitWidth();
            else if (double.TryParse(s.TrimEnd('%'), out double z)) _view.Zoom = z / 100;
            _view.Focus();
        };
        Add(_zoomCombo);
        Add(Theme.Divider());
        // стиль
        _styleCombo = new ComboBox { Width = 118, Height = 26, ToolTip = "Стиль абзаца", VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var (t, _) in StyleList) _styleCombo.Items.Add(t);
        AutomationProperties.SetName(_styleCombo, "Стиль");
        _styleCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || _styleCombo.SelectedIndex < 0) return;
            Exec("Стиль: " + StyleList[_styleCombo.SelectedIndex].Title);
        };
        Add(_styleCombo, keep: true);
        Add(Theme.Divider(), keep: true);
        // шрифт
        _fontCombo = new ComboBox { Width = 132, Height = 26, IsEditable = true, ToolTip = "Шрифт", MaxDropDownHeight = 420, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_fontCombo, "Шрифт");
        _fontCombo.DropDownOpened += (_, _) => FillFonts();
        _fontCombo.DropDownClosed += (_, _) => { if (_fontCombo.SelectedItem is string f) ApplyFont(f); };
        _fontCombo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplyFont(_fontCombo.Text); e.Handled = true; }
            if (e.Key == Key.Escape) { UpdateFormatUi(); _view.Focus(); e.Handled = true; }
        };
        Add(_fontCombo, keep: true);
        Add(Theme.Divider(), keep: true);
        // размер −/+
        Add(Theme.ToolButton("Уменьшить размер шрифта", Theme.IconPath(Theme.Icon.Minus), "Ctrl+[", (_, _) => Exec("Уменьшить размер шрифта")), keep: true);
        _sizeCombo = new ComboBox { Width = 52, Height = 26, IsEditable = true, ToolTip = "Размер шрифта (8–72 из списка, 1–1638 вручную)", VerticalContentAlignment = VerticalAlignment.Center };
        foreach (int s in new[] { 8, 9, 10, 11, 12, 14, 18, 24, 30, 36, 48, 60, 72 }) _sizeCombo.Items.Add(s.ToString(CultureInfo.CurrentCulture));
        AutomationProperties.SetName(_sizeCombo, "Размер шрифта");
        _sizeCombo.DropDownClosed += (_, _) => { if (_sizeCombo.SelectedItem is string s) ApplySize(s); };
        _sizeCombo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ApplySize(_sizeCombo.Text); e.Handled = true; }
            if (e.Key == Key.Escape) { UpdateFormatUi(); _view.Focus(); e.Handled = true; }
        };
        Add(_sizeCombo, keep: true);
        Add(Theme.ToolButton("Увеличить размер шрифта", Theme.IconPath(Theme.Icon.Plus), "Ctrl+]", (_, _) => Exec("Увеличить размер шрифта")), keep: true);
        Add(Theme.Divider(), keep: true);
        // полужирный, курсив, подчёркивание
        _bold = Theme.ToolToggle("Полужирный", Theme.Letter("B", FontWeights.Bold), "Ctrl+B", (_, _) => Exec("Полужирный"));
        _italic = Theme.ToolToggle("Курсив", Theme.Letter("I", FontWeights.SemiBold, FontStyles.Italic, family: "Georgia"), "Ctrl+I", (_, _) => Exec("Курсив"));
        _underline = Theme.ToolToggle("Подчёркнутый", Theme.Letter("U", underline: true), "Ctrl+U", (_, _) => Exec("Подчёркнутый"));
        Add(_bold, keep: true); Add(_italic, keep: true); Add(_underline, keep: true);
        // цвет текста и маркер
        var colorIcon = Theme.ColorIcon(Theme.Letter("A", FontWeights.SemiBold), out _colorBar);
        Add(Theme.DropButton("Цвет текста", colorIcon, ColorMenu), keep: true);
        var hlIcon = Theme.ColorIcon(Theme.IconPath(Theme.Icon.Highlight, 16), out _highlightBar);
        _highlightBar.Fill = new SolidColorBrush(Theme.Hex("FFFF00"));
        Add(Theme.DropButton("Цвет маркера", hlIcon, HighlightMenu));
        Add(Theme.Divider());
        // ссылка и изображение
        Add(Btn("Ссылка…", Theme.Icon.Link, "Вставить ссылку"));
        Add(Btn("Изображение…", Theme.Icon.Image, "Вставить изображение"));
        Add(Theme.Divider());
        // выравнивание, интервал, списки
        _alignIcon = new ContentControl { Content = Theme.IconPath(Theme.Icon.AlignLeft), Focusable = false };
        Add(Theme.DropButton("Выравнивание", _alignIcon, AlignMenu));
        Add(Theme.DropButton("Межстрочный интервал", Theme.IconPath(Theme.Icon.Spacing), SpacingMenu));
        _listIcon = new ContentControl { Content = Theme.IconPath(Theme.Icon.Bullets), Focusable = false };
        Add(Theme.DropButton("Списки", _listIcon, ListMenu));
        Add(Theme.Divider());
        // отступы и очистка форматирования
        Add(Btn("Уменьшить отступ", Theme.Icon.IndentLess));
        Add(Btn("Увеличить отступ", Theme.Icon.IndentMore));
        Add(Btn("Очистить форматирование", Theme.Icon.ClearFormat));
        return host;
    }

    /// <summary>Кнопка переполнения панели: при узком окне второстепенные команды уходят в «Ещё».</summary>
    private void TuneToolbarOverflow()
    {
        if (_toolbar.Template?.FindName("OverflowButton", _toolbar) is ToggleButton more)
        {
            more.ToolTip = "Ещё";
            AutomationProperties.SetName(more, "Ещё");
        }
        if (_toolbar.Template?.FindName("ToolBarThumb", _toolbar) is FrameworkElement thumb) thumb.Visibility = Visibility.Collapsed;
    }

    private void FillFonts()
    {
        if (_fontCombo.Items.Count > 0) return;
        foreach (var f in Doc.Fonts.FontCatalog.Shared.Families) _fontCombo.Items.Add(f);
    }

    private void ApplyFont(string font)
    {
        font = font.Trim();
        if (_updatingUi || font.Length == 0) { _view.Focus(); return; }
        if (font != Ed.GetFormatState().Font)
        {
            if (!Doc.Fonts.FontCatalog.Shared.HasFamily(font))
                ShowMessage($"Шрифт «{font}» не установлен на этом компьютере. Он будет записан в документ, а показан похожим шрифтом.", null, null);
            Exec(new AppCommand("Шрифт", null, () => Ed.SetFont(font), edits: true));
        }
        _view.Focus();
    }

    private void ApplySize(string text)
    {
        if (_updatingUi) return;
        if (Ui.FormDialog.TryNum(text, out double pt) && pt >= 1 && pt <= 1638)
        {
            if (Ed.GetFormatState().SizePt != pt) Exec(new AppCommand("Размер шрифта", null, () => Ed.SetSize(pt), edits: true));
        }
        else MessageBox.Show(this, "Размер шрифта — число от 1 до 1638.", Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateFormatUi();
        _view.Focus();
    }

    private ContextMenu DropMenu(params (string Header, string Command, string? Icon)[] items)
    {
        var m = new ContextMenu();
        foreach (var (h, cmd, icon) in items)
        {
            if (h == "-") { m.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = h, InputGestureText = _commands[cmd].Gesture ?? "" };
            if (icon is not null) mi.Icon = Theme.IconPath(icon);
            string c = cmd;
            mi.Click += (_, _) => Exec(c);
            m.Items.Add(mi);
        }
        m.Closed += (_, _) => _view.Focus();
        return m;
    }

    private ContextMenu AlignMenu() => DropMenu(
        ("По левому краю", "По левому краю", Theme.Icon.AlignLeft), ("По центру", "По центру", Theme.Icon.AlignCenter),
        ("По правому краю", "По правому краю", Theme.Icon.AlignRight), ("По ширине", "По ширине (выравнивание)", Theme.Icon.AlignJustify));

    private ContextMenu SpacingMenu() => DropMenu(
        ("Одинарный (1)", "Интервал 1", null), ("1,15", "Интервал 1,15", null), ("Полуторный (1,5)", "Интервал 1,5", null), ("Двойной (2)", "Интервал 2", null),
        ("-", "", null), ("Абзац…", "Абзац…", null));

    private ContextMenu ListMenu() => DropMenu(
        ("Маркированный список", "Маркированный список", Theme.Icon.Bullets), ("Нумерованный список", "Нумерованный список", Theme.Icon.Numbers),
        ("Убрать список", "Убрать список", null));

    private ContextMenu ColorMenu()
    {
        var m = new ContextMenu();
        var grid = new UniformGrid { Columns = 8, Margin = new Thickness(4) };
        foreach (var hex in Theme.TextColors)
        {
            string h = hex;
            var b = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(2), Background = new SolidColorBrush(Theme.Hex(h)), BorderBrush = Theme.Separator,
                BorderThickness = new Thickness(1), ToolTip = "#" + h, Focusable = false,
            };
            AutomationProperties.SetName(b, "Цвет #" + h);
            b.Click += (_, _) => { m.IsOpen = false; Exec(new AppCommand("Цвет текста", null, () => Ed.SetColor(h), edits: true)); };
            grid.Children.Add(b);
        }
        var auto = new MenuItem { Header = "Авто (чёрный)" };
        auto.Click += (_, _) => Exec(new AppCommand("Цвет текста", null, () => Ed.ApplyRun("Цвет текста", r => { r.Color = null; r.RawColor = null; }), edits: true));
        m.Items.Add(auto);
        m.Items.Add(new MenuItem { Header = grid, StaysOpenOnClick = true, Focusable = false });
        m.Closed += (_, _) => _view.Focus();
        return m;
    }

    private ContextMenu HighlightMenu()
    {
        var m = new ContextMenu();
        var none = new MenuItem { Header = "Нет цвета" };
        none.Click += (_, _) => Exec(new AppCommand("Маркер", null, () => Ed.SetHighlight(null), edits: true));
        m.Items.Add(none);
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(4) };
        foreach (var (name, hex, title) in Theme.HighlightColors)
        {
            string n = name;
            var b = new Button
            {
                Width = 22, Height = 22, Margin = new Thickness(2), Background = new SolidColorBrush(Theme.Hex(hex)), BorderBrush = Theme.Separator,
                BorderThickness = new Thickness(1), ToolTip = title, Focusable = false,
            };
            AutomationProperties.SetName(b, "Маркер: " + title);
            b.Click += (_, _) =>
            {
                m.IsOpen = false;
                _highlightBar.Fill = new SolidColorBrush(Theme.Hex(hex));
                Exec(new AppCommand("Маркер", null, () => Ed.SetHighlight(n), edits: true));
            };
            grid.Children.Add(b);
        }
        m.Items.Add(new MenuItem { Header = grid, StaysOpenOnClick = true, Focusable = false });
        m.Closed += (_, _) => _view.Focus();
        return m;
    }

    /// <summary>Значения панели по выделению: смешанные значения — пустое поле (UI-05, AC-03).</summary>
    private void UpdateFormatUi()
    {
        if (_bold is null) return;
        _updatingUi = true;
        try
        {
            var st = Ed.GetFormatState();
            _fontCombo.Text = st.Font ?? "";
            _sizeCombo.Text = st.SizePt is double s ? s.ToString("0.#", CultureInfo.CurrentCulture) : "";
            int si = Array.FindIndex(StyleList, x => x.Id == (st.StyleId ?? "Normal"));
            if (si >= 0) _styleCombo.SelectedIndex = si;
            else { _styleCombo.SelectedIndex = -1; _styleCombo.Text = st.StyleId is null ? "" : Ed.Doc.Styles.ById.GetValueOrDefault(st.StyleId)?.Name ?? st.StyleId; }
            _bold.IsChecked = st.Bold == true;
            _italic.IsChecked = st.Italic == true;
            _underline.IsChecked = st.Underline == true;
            _colorBar.Fill = st.Color is { Length: 6 } c && c != "auto" ? new SolidColorBrush(Theme.Hex(c)) : Brushes.Black;
            _alignIcon.Content = Theme.IconPath(st.Align switch
            {
                Align.Center => Theme.Icon.AlignCenter, Align.Right => Theme.Icon.AlignRight, Align.Justify => Theme.Icon.AlignJustify, _ => Theme.Icon.AlignLeft,
            });
            _listIcon.Content = Theme.IconPath(st.NumId is not null && !st.IsBulletList ? Theme.Icon.Numbers : Theme.Icon.Bullets);
            _undo.IsEnabled = Ed.CanUndo && !ReadOnly;
            _redo.IsEnabled = Ed.CanRedo && !ReadOnly;
            _painter.IsChecked = Ed.HasCopiedFormat && _painterArmed;
            SyncZoomCombo();
        }
        finally { _updatingUi = false; }
    }

    private void SyncZoomCombo()
    {
        if (_zoomCombo is null) return;
        bool was = _updatingUi;
        _updatingUi = true;
        string z = Math.Round(_view.Zoom * 100) + "%";
        int i = _zoomCombo.Items.IndexOf(z);
        if (i >= 0) _zoomCombo.SelectedIndex = i;
        else { _zoomCombo.SelectedIndex = -1; _zoomCombo.Text = z; }
        _updatingUi = was;
    }

    // ── копирование форматирования ───────────────────────────────────────────

    private bool _painterArmed;

    private void TogglePainter()
    {
        if (_painterArmed) { _painterArmed = false; UpdateFormatUi(); return; }
        Ed.CopyFormat();
        _painterArmed = true;
        ShowMessage("Выделите текст, к которому нужно применить скопированное оформление.", null, null);
        UpdateFormatUi();
    }

    private void PainterApply()
    {
        if (!_painterArmed || Ed.Selection.IsEmpty || Mouse.LeftButton == MouseButtonState.Pressed) return;
        _painterArmed = false;
        _messageBar.Visibility = Visibility.Collapsed;
        if (!ReadOnly) Ed.PasteFormat();
        UpdateFormatUi();
    }

    // ── поиск команд «Меню» (UI-06) ──────────────────────────────────────────

    private UIElement BuildCommandSearch()
    {
        var grid = new Grid { Width = 112, Height = 28, Margin = new Thickness(0, 0, 2, 0) };
        _commandBox = new TextBox
        {
            Height = 26, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(22, 0, 4, 0),
            Background = Brushes.White, BorderBrush = Theme.Separator, ToolTip = "Поиск команд меню",
        };
        AutomationProperties.SetName(_commandBox, "Меню");
        var placeholder = new TextBlock { Text = "Меню", Foreground = Theme.Muted, Margin = new Thickness(25, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var icon = Theme.IconPath(Theme.Icon.Search, 14);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.Margin = new Thickness(5, 0, 0, 0);
        icon.IsHitTestVisible = false;
        grid.Children.Add(_commandBox);
        grid.Children.Add(placeholder);
        grid.Children.Add(icon);
        _commandList = new ListBox { MaxHeight = 320, MinWidth = 260, BorderBrush = Theme.Separator };
        AutomationProperties.SetName(_commandList, "Найденные команды");
        _commandPopup = new Popup { PlacementTarget = _commandBox, Placement = PlacementMode.Bottom, StaysOpen = false, Child = _commandList, AllowsTransparency = false };
        _commandBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _commandBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FilterCommands();
        };
        _commandBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _commandList.Items.Count > 0)
            {
                _commandList.SelectedIndex = 0;
                (_commandList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { RunFoundCommand(_commandList.SelectedItem ?? (_commandList.Items.Count > 0 ? _commandList.Items[0] : null)); e.Handled = true; }
            else if (e.Key == Key.Escape) { _commandBox.Text = ""; _commandPopup.IsOpen = false; _view.Focus(); e.Handled = true; }
        };
        _commandList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { RunFoundCommand(_commandList.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Escape) { _commandPopup.IsOpen = false; _commandBox.Focus(); e.Handled = true; }
        };
        _commandList.MouseLeftButtonUp += (_, _) => RunFoundCommand(_commandList.SelectedItem);
        return grid;
    }

    private void FilterCommands()
    {
        string q = _commandBox.Text.Trim();
        _commandList.Items.Clear();
        if (q.Length == 0) { _commandPopup.IsOpen = false; return; }
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var c in AllCommands.Where(c => words.All(w => c.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase))).Take(14))
            _commandList.Items.Add(new ListBoxItem { Content = c.Gesture is null ? c.Name : $"{c.Name}   ({c.Gesture})", Tag = c, Padding = new Thickness(8, 4, 8, 4) });
        if (_commandList.Items.Count == 0) _commandList.Items.Add(new ListBoxItem { Content = "Команда не найдена", IsEnabled = false });
        _commandPopup.IsOpen = true;
    }

    private void RunFoundCommand(object? item)
    {
        if (item is not ListBoxItem { Tag: AppCommand c }) return;
        _commandPopup.IsOpen = false;
        _commandBox.Text = "";
        _view.Focus();
        Exec(c);
    }
}
