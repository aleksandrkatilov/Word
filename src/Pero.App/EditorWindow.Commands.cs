using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Pero.Core.Packaging;
using Pero.Doc.Docx;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using Orientation = System.Windows.Controls.Orientation;
using Pero.Services;
using Pero.Ui;

namespace Pero;

/// <summary>Команда приложения: одно имя для меню, панели, поиска «Меню» и подсказки с сочетанием клавиш.</summary>
public sealed class AppCommand(string name, string? gesture, Action run, bool edits = false, Func<bool>? isChecked = null)
{
    public string Name { get; } = name;
    public string? Gesture { get; } = gesture;
    public Action Run { get; } = run;
    /// <summary>Команда меняет документ — недоступна в режиме только чтения.</summary>
    public bool Edits { get; } = edits;
    public Func<bool>? IsChecked { get; } = isChecked;
}

public sealed partial class EditorWindow
{
    private readonly Dictionary<string, AppCommand> _commands = [];
    private IEnumerable<AppCommand> AllCommands => _commands.Values;

    private AppCommand C(string name, string? gesture, Action run, bool edits = false, Func<bool>? isChecked = null)
    {
        var c = new AppCommand(name, gesture, run, edits, isChecked);
        _commands[name] = c;
        return c;
    }

    /// <summary>Выполнить команду: в режиме только чтения правки не выполняются; фокус возвращается в текст.</summary>
    private void Exec(AppCommand c)
    {
        Log.Info("UI command: " + c.Name);
        if (c.Edits && ReadOnly) { ShowMessage(ReadOnlyReason(), "Сохранить копию", () => _ = SaveAsync(true)); return; }
        try { c.Run(); }
        catch (NotSupportedException ex) { MessageBox.Show(this, ex.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information); }
        UpdateFormatUi();
        if (Keyboard.FocusedElement is not TextBox || c.Edits) _view.Focus();
    }

    private void Exec(string name) { if (_commands.TryGetValue(name, out var c)) Exec(c); }

    private void BuildCommands()
    {
        // Файл
        C("Создать", "Ctrl+N", NewDocument);
        C("Открыть…", "Ctrl+O", ShowOpenDialog);
        C("Сохранить", "Ctrl+S", () => _ = SaveAsync(false));
        C("Сохранить как…", "Ctrl+Shift+S", () => _ = SaveAsync(true));
        C("Предыдущие версии файла…", null, ShowVersions);
        C("Экспорт PDF…", null, () => _ = ExportPdfAsync());
        C("Экспорт в текст (TXT)…", null, ExportTxt);
        C("Печать…", "Ctrl+P", Print);
        C("Выход", "Alt+F4", Close);
        // Правка
        C("Отменить", "Ctrl+Z", () => Ed.Undo(), edits: true);
        C("Повторить", "Ctrl+Y", () => Ed.Redo(), edits: true);
        C("Вырезать", "Ctrl+X", Cut, edits: true);
        C("Копировать", "Ctrl+C", Copy);
        C("Вставить", "Ctrl+V", Paste, edits: true);
        C("Вставить только текст", "Ctrl+Shift+V", PasteText, edits: true);
        C("Выделить все", "Ctrl+A", () => Ed.SelectAll());
        C("Найти", "Ctrl+F", () => ShowFind(false));
        C("Найти и заменить", "Ctrl+H", () => ShowFind(true));
        // Вид
        foreach (int z in new[] { 50, 75, 90, 100, 125, 150, 200 }) C($"Масштаб {z}%", null, () => SetZoom(z / 100.0));
        C("По ширине", null, () => { _view.FitWidth(); SyncZoomCombo(); });
        C("Горизонтальная линейка", null, () => { S.Settings.ShowHorizontalRuler = !S.Settings.ShowHorizontalRuler; S.Settings.Save(); ApplyRulerSettings(); }, isChecked: () => S.Settings.ShowHorizontalRuler);
        C("Вертикальная линейка", null, () => { S.Settings.ShowVerticalRuler = !S.Settings.ShowVerticalRuler; S.Settings.Save(); ApplyRulerSettings(); }, isChecked: () => S.Settings.ShowVerticalRuler);
        C("Навигация", null, ToggleNavigation, isChecked: () => _navColumn.Width.Value > 0);
        C("Непечатаемые знаки", "Ctrl+Shift+8", () => _view.ShowMarks = !_view.ShowMarks, isChecked: () => _view.ShowMarks);
        // Вставка
        C("Изображение…", null, InsertImage, edits: true);
        C("Таблица…", null, InsertTable, edits: true);
        C("Ссылка…", "Ctrl+K", EditLink, edits: true);
        C("Удалить ссылку", null, () => Ed.RemoveLink(), edits: true);
        C("Разрыв страницы", "Ctrl+Enter", () => Ed.PageBreak(), edits: true);
        C("Верхний колонтитул", null, () => _view.EnterHeaderFooter(HfKind.Header, _view.CaretPage), edits: true);
        C("Нижний колонтитул", null, () => _view.EnterHeaderFooter(HfKind.Footer, _view.CaretPage), edits: true);
        C("Номер страницы", null, InsertPageNumber, edits: true);
        C("Особый колонтитул первой страницы", null, () => Ed.SetTitlePage(!Ed.Doc.Section.TitlePage), edits: true, isChecked: () => Ed.Doc.Section.TitlePage);
        C("Закрыть колонтитулы", "Esc", () => _view.ExitStory());
        // Формат
        C("Полужирный", "Ctrl+B", () => Ed.ToggleBold(), edits: true, isChecked: () => Ed.GetFormatState().Bold == true);
        C("Курсив", "Ctrl+I", () => Ed.ToggleItalic(), edits: true, isChecked: () => Ed.GetFormatState().Italic == true);
        C("Подчёркнутый", "Ctrl+U", () => Ed.ToggleUnderline(), edits: true, isChecked: () => Ed.GetFormatState().Underline == true);
        C("Шрифт…", "Ctrl+D", ShowFontDialog, edits: true);
        C("Абзац…", null, ShowParagraphDialog, edits: true);
        C("Увеличить размер шрифта", "Ctrl+]", () => Ed.StepSize(1), edits: true);
        C("Уменьшить размер шрифта", "Ctrl+[", () => Ed.StepSize(-1), edits: true);
        C("По левому краю", "Ctrl+L", () => Ed.SetAlign(Align.Left), edits: true);
        C("По центру", "Ctrl+E", () => Ed.SetAlign(Align.Center), edits: true);
        C("По правому краю", "Ctrl+R", () => Ed.SetAlign(Align.Right), edits: true);
        C("По ширине (выравнивание)", "Ctrl+J", () => Ed.SetAlign(Align.Justify), edits: true);
        foreach (var (title, v) in new[] { ("Интервал 1", 240), ("Интервал 1,15", 276), ("Интервал 1,5", 360), ("Интервал 2", 480) })
            C(title, null, () => Ed.SetLineSpacing(v), edits: true);
        C("Маркированный список", "Ctrl+Shift+L", () => Ed.ToggleList(bullet: true), edits: true);
        C("Нумерованный список", null, () => Ed.ToggleList(bullet: false), edits: true);
        C("Убрать список", null, RemoveList, edits: true);
        C("Уменьшить отступ", "Ctrl+Shift+M", () => Ed.Indent(-360), edits: true);
        C("Увеличить отступ", "Ctrl+M", () => Ed.Indent(360), edits: true);
        C("Очистить форматирование", "Ctrl+\\", () => Ed.ClearFormatting(), edits: true);
        C("Копировать форматирование", null, TogglePainter, edits: true);
        foreach (var (title, id) in StyleList) C("Стиль: " + title, null, () => Ed.ApplyStyle(id), edits: true);
        C("Параметры страницы…", null, ShowPageSetup, edits: true);
        C("Книжная ориентация", null, () => Ed.SetOrientation(Doc.Model.Orientation.Portrait), edits: true);
        C("Альбомная ориентация", null, () => Ed.SetOrientation(Doc.Model.Orientation.Landscape), edits: true);
        // Таблица
        C("Вставить строку выше", null, () => Ed.InsertRow(below: false), edits: true);
        C("Вставить строку ниже", null, () => Ed.InsertRow(below: true), edits: true);
        C("Вставить столбец слева", null, () => Ed.InsertColumn(right: false), edits: true);
        C("Вставить столбец справа", null, () => Ed.InsertColumn(right: true), edits: true);
        C("Удалить строку", null, () => Ed.DeleteRow(), edits: true);
        C("Удалить столбец", null, () => Ed.DeleteColumn(), edits: true);
        C("Удалить таблицу", null, () => Ed.DeleteTable(), edits: true);
        C("Объединить ячейки", null, () => Ed.MergeCellsInRow(), edits: true);
        C("Ширина столбца…", null, ColumnWidth, edits: true);
        // Инструменты
        C("Число слов", null, () => MessageBox.Show(this, $"Слов: {Ed.WordCount():N0}\nСтраниц: {_view.Layout.Pages.Count}", "Число слов", MessageBoxButton.OK, MessageBoxImage.Information));
        C("Настройки…", null, ShowSettings);
        C("Отчёт о совместимости", null, ShowReport);
        C("Проверка обмена документа", null, RoundTripCheck);
        // Справка
        C("Горячие клавиши", "F1", ShowShortcuts);
        C("О программе", null, () => MessageBox.Show(this,
            $"«Перо», версия {typeof(App).Assembly.GetName().Version}\n\nРедактор документов Word (DOCX) с собственным ядром: модель документа, правка, разметка страниц, " +
            "чтение и запись DOCX, печать и PDF. Без коммерческих библиотек, лицензионных ключей и подключения к интернету.",
            "О программе", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    internal static readonly (string Title, string Id)[] StyleList =
        [("Обычный", "Normal"), ("Название", "Title"), ("Заголовок 1", "Heading1"), ("Заголовок 2", "Heading2"), ("Заголовок 3", "Heading3")];

    // ── меню ─────────────────────────────────────────────────────────────────

    private MenuItem Item(string name, string? header = null)
    {
        var c = _commands[name];
        var mi = new MenuItem { Header = header ?? c.Name, InputGestureText = c.Gesture ?? "" };
        if (c.IsChecked is not null) mi.IsCheckable = false;
        mi.Click += (_, _) => Exec(c);
        mi.SubmenuOpened += (_, _) => { };
        return mi;
    }

    private Menu BuildMenu()
    {
        var menu = new Menu { Background = System.Windows.Media.Brushes.Transparent, Margin = new Thickness(46, 0, 0, 0), Height = 26, FontSize = 13 };
        MenuItem Top(string header, params object[] items)
        {
            var m = new MenuItem { Header = header, Padding = new Thickness(8, 3, 8, 3) };
            foreach (var i in items) m.Items.Add(i is string s ? (s == "-" ? new Separator() : Item(s)) : i);
            m.SubmenuOpened += (_, _) => RefreshChecks(m);
            return m;
        }

        var recent = new MenuItem { Header = "Последние" };
        recent.SubmenuOpened += (_, _) => FillRecent(recent);
        recent.Items.Add(new MenuItem { Header = "(пусто)", IsEnabled = false });

        var zoom = new MenuItem { Header = "Масштаб" };
        foreach (int z in new[] { 50, 75, 90, 100, 125, 150, 200 }) zoom.Items.Add(Item($"Масштаб {z}%", $"{z}%"));

        var styles = new MenuItem { Header = "Стили" };
        foreach (var (t, _) in StyleList) styles.Items.Add(Item("Стиль: " + t, t));
        var lists = new MenuItem { Header = "Списки" };
        foreach (var n in new[] { "Маркированный список", "Нумерованный список", "Убрать список", "-", "Увеличить отступ", "Уменьшить отступ" })
            lists.Items.Add(n == "-" ? new Separator() : Item(n));
        var align = new MenuItem { Header = "Выравнивание" };
        foreach (var n in new[] { "По левому краю", "По центру", "По правому краю" }) align.Items.Add(Item(n));
        align.Items.Add(Item("По ширине (выравнивание)", "По ширине"));
        var spacing = new MenuItem { Header = "Межстрочный интервал" };
        foreach (var n in new[] { "Интервал 1", "Интервал 1,15", "Интервал 1,5", "Интервал 2" }) spacing.Items.Add(Item(n, n.Replace("Интервал ", "")));
        var table = new MenuItem { Header = "Таблица" };
        foreach (var n in new[] { "Вставить строку выше", "Вставить строку ниже", "Вставить столбец слева", "Вставить столбец справа", "-", "Удалить строку", "Удалить столбец", "Удалить таблицу", "-", "Объединить ячейки", "Ширина столбца…" })
            table.Items.Add(n == "-" ? new Separator() : Item(n));
        var orientation = new MenuItem { Header = "Ориентация" };
        orientation.Items.Add(Item("Книжная ориентация", "Книжная"));
        orientation.Items.Add(Item("Альбомная ориентация", "Альбомная"));

        menu.Items.Add(Top("Файл", "Создать", "Открыть…", recent, "-", "Сохранить", "Сохранить как…", "Предыдущие версии файла…", "-", "Экспорт PDF…", "Экспорт в текст (TXT)…", "Печать…", "-", "Выход"));
        menu.Items.Add(Top("Правка", "Отменить", "Повторить", "-", "Вырезать", "Копировать", "Вставить", "Вставить только текст", "-", "Выделить все", "-", "Найти", "Найти и заменить"));
        menu.Items.Add(Top("Вид", zoom, "По ширине", "-", "Горизонтальная линейка", "Вертикальная линейка", "Навигация", "Непечатаемые знаки"));
        menu.Items.Add(Top("Вставка", "Изображение…", "Таблица…", "Ссылка…", "Разрыв страницы", "-", "Верхний колонтитул", "Нижний колонтитул", "Номер страницы", "Особый колонтитул первой страницы"));
        menu.Items.Add(Top("Формат", "Шрифт…", "Абзац…", styles, lists, align, spacing, "-", table, "-", "Очистить форматирование", "Копировать форматирование", "-", "Параметры страницы…", orientation));
        menu.Items.Add(Top("Инструменты", "Число слов", "-", "Отчёт о совместимости", "Проверка обмена документа", "-", "Настройки…"));
        menu.Items.Add(Top("Справка", "Горячие клавиши", "О программе"));
        return menu;
    }

    private void RefreshChecks(ItemsControl menu)
    {
        foreach (var o in menu.Items)
        {
            if (o is not MenuItem mi) continue;
            var c = AllCommands.FirstOrDefault(x => x.Name == (mi.Header as string) || ("Масштаб " + mi.Header) == x.Name || ("Стиль: " + mi.Header) == x.Name);
            if (c?.IsChecked is { } f) { mi.IsCheckable = true; mi.IsChecked = f(); }
            if (c is not null) mi.IsEnabled = !(c.Edits && ReadOnly) || c.Name is "Отменить" or "Повторить";
            if (mi.Header as string == "Таблица") mi.IsEnabled = Ed.CurrentCell() is not null;
            if (mi.Items.Count > 0) RefreshChecks(mi);
        }
        if (_commands.TryGetValue("Отменить", out _))
            foreach (var mi in menu.Items.OfType<MenuItem>())
            {
                if (mi.Header as string == "Отменить") mi.IsEnabled = Ed.CanUndo && !ReadOnly;
                if (mi.Header as string == "Повторить") mi.IsEnabled = Ed.CanRedo && !ReadOnly;
            }
    }

    private void FillRecent(MenuItem recent)
    {
        recent.Items.Clear();
        foreach (var f in S.Recent.Items.Take(10))
        {
            var mi = new MenuItem { Header = Path.GetFileName(f).Replace("_", "__"), ToolTip = f };
            string file = f;
            mi.Click += (_, _) =>
            {
                if (File.Exists(file)) OpenInNewWindow(file, this);
                else { MessageBox.Show(this, string.Format(Strings.RecentMissing, file), Strings.AppName); S.Recent.Remove(file); }
            };
            recent.Items.Add(mi);
        }
        if (recent.Items.Count == 0) recent.Items.Add(new MenuItem { Header = "(пусто)", IsEnabled = false });
    }

    // ── сочетания клавиш (работают и в русской раскладке: привязка к клавишам, а не к буквам) ──

    private void BindShortcuts()
    {
        void Bind(Key key, ModifierKeys mods, string command)
        {
            var rc = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(rc, (_, e) => { e.Handled = true; Exec(command); }));
            InputBindings.Add(new KeyBinding(rc, key, mods));
        }
        var ctrl = ModifierKeys.Control;
        var cs = ModifierKeys.Control | ModifierKeys.Shift;
        Bind(Key.N, ctrl, "Создать"); Bind(Key.O, ctrl, "Открыть…"); Bind(Key.S, ctrl, "Сохранить"); Bind(Key.S, cs, "Сохранить как…");
        Bind(Key.F12, ModifierKeys.None, "Сохранить как…"); Bind(Key.P, ctrl, "Печать…");
        Bind(Key.Z, ctrl, "Отменить"); Bind(Key.Y, ctrl, "Повторить"); Bind(Key.Z, cs, "Повторить");
        Bind(Key.X, ctrl, "Вырезать"); Bind(Key.C, ctrl, "Копировать"); Bind(Key.V, ctrl, "Вставить"); Bind(Key.V, cs, "Вставить только текст");
        Bind(Key.Insert, ModifierKeys.Control, "Копировать"); Bind(Key.Insert, ModifierKeys.Shift, "Вставить"); Bind(Key.Delete, ModifierKeys.Shift, "Вырезать");
        Bind(Key.A, ctrl, "Выделить все"); Bind(Key.F, ctrl, "Найти"); Bind(Key.H, ctrl, "Найти и заменить");
        Bind(Key.B, ctrl, "Полужирный"); Bind(Key.I, ctrl, "Курсив"); Bind(Key.U, ctrl, "Подчёркнутый"); Bind(Key.K, ctrl, "Ссылка…");
        Bind(Key.D, ctrl, "Шрифт…");
        Bind(Key.L, ctrl, "По левому краю"); Bind(Key.E, ctrl, "По центру"); Bind(Key.R, ctrl, "По правому краю"); Bind(Key.J, ctrl, "По ширине (выравнивание)");
        Bind(Key.OemCloseBrackets, ctrl, "Увеличить размер шрифта"); Bind(Key.OemOpenBrackets, ctrl, "Уменьшить размер шрифта");
        Bind(Key.L, cs, "Маркированный список"); Bind(Key.M, ctrl, "Увеличить отступ"); Bind(Key.M, cs, "Уменьшить отступ");
        Bind(Key.Oem5, ctrl, "Очистить форматирование"); Bind(Key.Space, ctrl, "Очистить форматирование");
        Bind(Key.D8, cs, "Непечатаемые знаки"); Bind(Key.F1, ModifierKeys.None, "Горячие клавиши");
        Bind(Key.Add, ctrl, "Увеличить размер шрифта"); Bind(Key.Subtract, ctrl, "Уменьшить размер шрифта");
    }

    // ── контекстное меню ─────────────────────────────────────────────────────

    private void BuildContextMenu(ContextMenu m)
    {
        m.Items.Clear();
        foreach (var n in new[] { "Вырезать", "Копировать", "Вставить", "Вставить только текст" }) m.Items.Add(Item(n));
        m.Items.Add(new Separator());
        var st = Ed.GetFormatState();
        m.Items.Add(Item("Ссылка…", st.Link is null ? "Вставить ссылку…" : "Изменить ссылку…"));
        if (st.Link is not null) m.Items.Add(Item("Удалить ссылку"));
        if (_view.SelectedImage() is not null)
        {
            m.Items.Add(new Separator());
            var del = new MenuItem { Header = "Удалить изображение", InputGestureText = "Delete" };
            del.Click += (_, _) => Exec(new AppCommand("Удалить изображение", null, () => Ed.DeleteSelection(), edits: true));
            m.Items.Add(del);
        }
        if (Ed.CurrentCell() is not null)
        {
            m.Items.Add(new Separator());
            foreach (var n in new[] { "Вставить строку выше", "Вставить строку ниже", "Вставить столбец слева", "Вставить столбец справа", "Удалить строку", "Удалить столбец", "Удалить таблицу" })
                m.Items.Add(Item(n));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item("Шрифт…"));
        m.Items.Add(Item("Абзац…"));
        m.Items.Add(Item("Очистить форматирование"));
        if (Ed.Story is not null) { m.Items.Add(new Separator()); m.Items.Add(Item("Закрыть колонтитулы")); }
        foreach (var mi in m.Items.OfType<MenuItem>())
            if (_commands.TryGetValue(mi.Header as string ?? "", out var c) && c.Edits && ReadOnly) mi.IsEnabled = false;
    }

    // ── буфер обмена (EDIT-01) ───────────────────────────────────────────────

    private const string FragmentFormat = "Pero.Fragment.v1";
    private static Fragment? _clipFragment;
    private static string? _clipText;

    private void Copy()
    {
        var f = Ed.CopyFragment();
        if (f is null) return;
        _clipFragment = f;
        _clipText = f.PlainText;
        try
        {
            var data = new DataObject();
            data.SetText(f.PlainText, TextDataFormat.UnicodeText);
            data.SetData(FragmentFormat, Environment.ProcessId.ToString());
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception ex) { Log.Error("clipboard-copy", ex); }
    }

    private void Cut()
    {
        if (Ed.Selection.IsEmpty) return;
        Copy();
        Ed.DeleteSelection();
    }

    private void Paste()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            // фрагмент «Пера» с оформлением, если буфер не менялся другой программой
            if (_clipFragment is not null && data?.GetDataPresent(FragmentFormat) == true && Clipboard.ContainsText() && Clipboard.GetText() == _clipText)
            {
                Ed.PasteFragment(_clipFragment);
                return;
            }
            if (data?.GetDataPresent(DataFormats.Bitmap) == true && !Clipboard.ContainsText() && Clipboard.GetImage() is { } img)
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                Ed.InsertImage(ms.ToArray(), "png", img.PixelWidth, img.PixelHeight);
                return;
            }
            PasteText();
        }
        catch (Exception ex) { Log.Error("clipboard-paste", ex); }
    }

    /// <summary>Вставка только текста: оформление берётся у позиции курсора.</summary>
    private void PasteText()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            Ed.InsertPlainText(Clipboard.GetText());
        }
        catch (Exception ex) { Log.Error("clipboard-text", ex); }
    }

    // ── действия ─────────────────────────────────────────────────────────────

    private void Print()
    {
        try
        {
            var drawer = new Doc.Render.PageDrawer(Doc.Fonts.FontCatalog.Shared, (img, story) => Ed.Doc.ImageBytes(img.RelId, story?.PartPath));
            Editor.LayoutPaginator.Print(this, _view.Layout, drawer, _displayName);
        }
        catch (Exception ex)
        {
            Log.Error("print", ex);
            MessageBox.Show(this, "Не удалось напечатать: " + ex.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertImage()
    {
        var dlg = new OpenFileDialog { Filter = "Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            byte[] bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 50 * 1024 * 1024) { MessageBox.Show(this, "Изображение больше 50 МБ.", Strings.AppName); return; }
            var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            // размер в пикселях при 96 dpi — как Word вставляет картинку «в натуральную величину»
            int w = (int)Math.Round(frame.Width), h = (int)Math.Round(frame.Height);
            Ed.InsertImage(bytes, Path.GetExtension(dlg.FileName), Math.Max(1, w), Math.Max(1, h));
        }
        catch (NotSupportedException) { throw; }
        catch (Exception ex)
        {
            Log.Error("insert-image", ex);
            MessageBox.Show(this, "Не удалось вставить изображение: файл повреждён или это не PNG/JPEG.", Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InsertTable()
    {
        var dlg = new TableDialog(this);
        if (dlg.ShowDialog() == true) Ed.InsertTable(dlg.RowsCount, dlg.Cols);
    }

    private void EditLink()
    {
        var st = Ed.GetFormatState();
        bool hasSel = !Ed.Selection.IsEmpty;
        var dlg = new LinkDialog(this, hasSel ? Ed.SelectedText() : null, st.Link?.Url, textEditable: !hasSel);
        if (dlg.ShowDialog() != true) return;
        string url = dlg.Url;
        if (!url.Contains(':') && url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        Ed.SetLink(url, hasSel ? null : dlg.LinkText ?? url);
    }

    private void InsertPageNumber()
    {
        _view.EnterHeaderFooter(HfKind.Footer, _view.CaretPage);
        Ed.Select(Selection.At(Ed.Index.DocumentEnd));
        Ed.InsertPageField();
        Ed.SetAlign(Align.Center);
    }

    private void RemoveList()
    {
        var st = Ed.GetFormatState();
        if (st.NumId is null) return;
        Ed.ToggleList(bullet: st.IsBulletList);
    }

    private void ShowFontDialog()
    {
        var st = Ed.GetFormatState();
        var dlg = new FontDialog(this, Doc.Fonts.FontCatalog.Shared.Families, st);
        if (dlg.ShowDialog() != true) return;
        string family = dlg.Family;
        double size = dlg.SizePt;
        bool? b = dlg.Bold, i = dlg.Italic, u = dlg.Underline, s = dlg.Strike;
        Ed.ApplyRun("Шрифт", r =>
        {
            if (family.Length > 0 && family != st.Font) { r.Font = family; r.FontTheme = null; }
            if (size > 0 && size != st.SizePt) r.SizeHalfPt = (int)Math.Round(size * 2);
            if (b is not null && b != st.Bold) r.Bold = b;
            if (i is not null && i != st.Italic) r.Italic = i;
            if (u is not null && u != st.Underline) r.Underline = u == true ? "single" : "none";
            if (s == true) r.Strike = true;
        });
    }

    private void ShowParagraphDialog()
    {
        var p = Ed.Index[Ed.Selection.Active.Para].Paragraph;
        var ep = Resolve.Para(Ed.Doc, p);
        var dlg = new ParagraphDialog(this, ep);
        if (dlg.ShowDialog() != true) return;
        Ed.ApplyPara("Абзац", x =>
        {
            x.Props.Align = dlg.Align;
            x.Props.IndLeft = dlg.IndLeft;
            x.Props.IndRight = dlg.IndRight;
            x.Props.IndFirst = dlg.IndFirst;
            x.Props.SpaceBefore = dlg.SpaceBefore;
            x.Props.SpaceAfter = dlg.SpaceAfter;
            x.Props.Line = dlg.Line240;
            x.Props.LineRule = LineRule.Auto;
        });
    }

    private void ShowPageSetup()
    {
        var dlg = new PageSetupDialog(this, Ed.Doc.Section);
        if (dlg.ShowDialog() != true) return;
        var (pw, ph) = dlg.PaperPortrait;
        var o = dlg.Orientation;
        int top = dlg.MarginTopTw, bottom = dlg.MarginBottomTw, left = dlg.MarginLeftTw, right = dlg.MarginRightTw;
        Ed.Execute("Параметры страницы", () =>
        {
            var s = Ed.Doc.Section;
            s.Orientation = o;
            s.PageWidth = o == Doc.Model.Orientation.Landscape ? ph : pw;
            s.PageHeight = o == Doc.Model.Orientation.Landscape ? pw : ph;
            s.MarginTop = top; s.MarginBottom = bottom; s.MarginLeft = left; s.MarginRight = right;
            return Ed.Selection;
        });
    }

    private void ColumnWidth()
    {
        if (Ed.CurrentCell() is not var (t, r, c)) return;
        int gridCol = t.Rows[r].Cells.Take(c).Sum(x => x.GridSpan);
        if (gridCol >= t.Grid.Count) return;
        var dlg = new FormDialog(this, "Ширина столбца");
        var box = dlg.AddRow("Ширина, см:", FormDialog.Box(FormDialog.Num(t.Grid[gridCol] / 566.929), 80));
        if (dlg.ShowDialog() != true) return;
        if (!FormDialog.TryNum(box.Text, out double cm) || cm < 0.3 || cm > 50) { MessageBox.Show(this, "Ширина — от 0,3 до 50 см.", Strings.AppName); return; }
        Ed.SetColumnWidth(t, gridCol, (int)Math.Round(cm * 566.929));
    }

    private void ShowSettings()
    {
        var dlg = new FormDialog(this, "Настройки");
        var box = dlg.AddRow("Автосохранение копии, секунд:", FormDialog.Box(S.Settings.AutoRecoverSeconds.ToString(), 80));
        dlg.AddNote("Копия для восстановления после сбоя сохраняется отдельно и не заменяет ваш файл. Резервные версии файла (до 20) создаются при каждом сохранении.");
        if (dlg.ShowDialog() != true) return;
        if (int.TryParse(box.Text, out int sec) && sec is >= 10 and <= 600)
        {
            S.Settings.AutoRecoverSeconds = sec;
            S.Settings.Save();
            _recoveryTimer.Interval = TimeSpan.FromSeconds(sec);
        }
    }

    private void ShowReport()
    {
        if (_report is null) { MessageBox.Show(this, Strings.NeedSavedFile, Strings.AppName); return; }
        var sb = new StringBuilder(_report.ToUserText()).AppendLine();
        foreach (var f in _report.Findings)
            sb.AppendLine($"{f.Entry.Title}: {f.Count} — чтение {f.Entry.Read}, отображение {f.Entry.Display}, правка {f.Entry.Edit}, сохранение {f.Entry.Save}");
        new TextWindow(this, Strings.CompatReportTitle, sb.ToString()).ShowDialog();
    }

    /// <summary>REL-04 для текущего документа: записать DOCX и сравнить со снимком при открытии.</summary>
    private void RoundTripCheck()
    {
        if (_openedCopy is null || !File.Exists(_openedCopy)) { MessageBox.Show(this, Strings.NeedSavedFile, Strings.AppName); return; }
        try
        {
            using var ms = new MemoryStream();
            DocxWriter.Write(Ed.Doc, ms);
            var diffs = DocxFingerprint.FromFile(_openedCopy).CompareTo(DocxFingerprint.FromStream(new MemoryStream(ms.ToArray())));
            string text = diffs.Count == 0 ? Strings.RoundTripOk
                : string.Format(Strings.RoundTripLoss, string.Join(Environment.NewLine, diffs.Select(d => $"{(d.IsLoss ? "✗" : "~")} {d.Property}: {d.Before} → {d.After}")));
            new TextWindow(this, Strings.RoundTrip, text + (Dirty ? Environment.NewLine + Environment.NewLine + Strings.UnsavedEditsNote : "")).ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Strings.AppName); }
    }

    private void ShowShortcuts()
    {
        var sb = new StringBuilder("Сочетания клавиш работают и в русской раскладке.\n\n");
        foreach (var c in AllCommands.Where(c => c.Gesture is not null)) sb.AppendLine($"{c.Gesture,-16}  {c.Name}");
        sb.AppendLine().AppendLine("Enter — новый абзац; Shift+Enter — перенос строки; Ctrl+Enter — новая страница.");
        sb.AppendLine("Tab в таблице — следующая ячейка; в начале пункта списка — уровень ниже.");
        sb.AppendLine("Двойной щелчок по полю колонтитула — правка колонтитула; Esc — возврат к тексту.");
        sb.AppendLine("Ctrl+щелчок по ссылке — открыть ссылку; Ctrl+колесо мыши — масштаб.");
        new TextWindow(this, "Горячие клавиши", sb.ToString()).ShowDialog();
    }

    private void ShowVersions()
    {
        if (_path is null) { MessageBox.Show(this, "Резервные версии есть только у сохранённого файла.", Strings.AppName); return; }
        var versions = S.Backups.List(_path);
        if (versions.Count == 0) { MessageBox.Show(this, "Резервных версий пока нет: они появляются при повторных сохранениях.", Strings.AppName); return; }
        var list = new ListBox { Height = 260, Width = 380 };
        foreach (var v in versions) list.Items.Add(new ListBoxItem { Content = $"{v.SavedUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss} — {v.Length / 1024.0:N0} КБ", Tag = v });
        list.SelectedIndex = 0;
        var dlg = new FormDialog(this, "Предыдущие версии файла");
        dlg.AddRow("Версия:", list);
        dlg.AddNote("Версия откроется в новом окне как отдельный документ; текущий файл не изменится.");
        if (dlg.ShowDialog() != true || list.SelectedItem is not ListBoxItem { Tag: Pero.Core.Files.BackupVersion ver }) return;
        var w = new EditorWindow();
        w.Show();
        _ = w.LoadAsync(ver.Path, null, $"{Path.GetFileNameWithoutExtension(_path)} (версия от {ver.SavedUtc.ToLocalTime():dd.MM HH:mm})");
    }

    private void SetZoom(double z)
    {
        _view.Zoom = z;
        SyncZoomCombo();
    }
}
