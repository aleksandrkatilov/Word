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
        if (c.Edits && ReadOnly) { ShowMessage(ReadOnlyReason(), Loc.L("Сохранить копию"), () => _ = SaveAsync(true)); return; }
        try { c.Run(); }
        catch (NotSupportedException ex) { MessageBox.Show(this, ex.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information); }
        UpdateFormatUi();
        if (Keyboard.FocusedElement is not TextBox || c.Edits) _view.Focus();
    }

    private void Exec(string name) { if (_commands.TryGetValue(name, out var c)) Exec(c); }

    private void BuildCommands()
    {
        // Файл
        C(Loc.L("Создать"), "Ctrl+N", NewDocument);
        C(Loc.L("Открыть…"), "Ctrl+O", ShowOpenDialog);
        C(Loc.L("Сохранить"), "Ctrl+S", () => _ = SaveAsync(false));
        C(Loc.L("Сохранить как…"), "Ctrl+Shift+S", () => _ = SaveAsync(true));
        C(Loc.L("Предыдущие версии файла…"), null, ShowVersions);
        C(Loc.L("Экспорт PDF…"), null, () => _ = ExportPdfAsync());
        C(Loc.L("Экспорт в текст (TXT)…"), null, ExportTxt);
        C(Loc.L("Печать…"), "Ctrl+P", Print);
        C(Loc.L("Выход"), "Alt+F4", Close);
        // Правка
        C(Loc.L("Отменить"), "Ctrl+Z", () => Ed.Undo(), edits: true);
        C(Loc.L("Повторить"), "Ctrl+Y", () => Ed.Redo(), edits: true);
        C(Loc.L("Вырезать"), "Ctrl+X", Cut, edits: true);
        C(Loc.L("Копировать"), "Ctrl+C", Copy);
        C(Loc.L("Вставить"), "Ctrl+V", Paste, edits: true);
        C(Loc.L("Вставить только текст"), "Ctrl+Shift+V", PasteText, edits: true);
        C(Loc.L("Выделить все"), "Ctrl+A", () => Ed.SelectAll());
        C(Loc.L("Найти"), "Ctrl+F", () => ShowFind(false));
        C(Loc.L("Найти и заменить"), "Ctrl+H", () => ShowFind(true));
        // Вид
        foreach (int z in new[] { 50, 75, 90, 100, 125, 150, 200 }) C(Loc.F("Масштаб {0}%", z), null, () => SetZoom(z / 100.0));
        C(Loc.L("По ширине"), null, () => { _view.FitWidth(); SyncZoomCombo(); });
        C(Loc.L("Горизонтальная линейка"), null, () => { S.Settings.ShowHorizontalRuler = !S.Settings.ShowHorizontalRuler; S.Settings.Save(); ApplyRulerSettings(); }, isChecked: () => S.Settings.ShowHorizontalRuler);
        C(Loc.L("Вертикальная линейка"), null, () => { S.Settings.ShowVerticalRuler = !S.Settings.ShowVerticalRuler; S.Settings.Save(); ApplyRulerSettings(); }, isChecked: () => S.Settings.ShowVerticalRuler);
        C(Loc.L("Навигация"), null, ToggleNavigation, isChecked: () => _navColumn.Width.Value > 0);
        C(Loc.L("Непечатаемые знаки"), "Ctrl+Shift+8", () => _view.ShowMarks = !_view.ShowMarks, isChecked: () => _view.ShowMarks);
        // Вставка
        C(Loc.L("Изображение…"), null, InsertImage, edits: true);
        C(Loc.L("Таблица…"), null, InsertTable, edits: true);
        C(Loc.L("Ссылка…"), "Ctrl+K", EditLink, edits: true);
        C(Loc.L("Удалить ссылку"), null, () => Ed.RemoveLink(), edits: true);
        C(Loc.L("Разрыв страницы"), "Ctrl+Enter", () => Ed.PageBreak(), edits: true);
        C(Loc.L("Верхний колонтитул"), null, () => _view.EnterHeaderFooter(HfKind.Header, _view.CaretPage), edits: true);
        C(Loc.L("Нижний колонтитул"), null, () => _view.EnterHeaderFooter(HfKind.Footer, _view.CaretPage), edits: true);
        C(Loc.L("Номер страницы"), null, InsertPageNumber, edits: true);
        C(Loc.L("Особый колонтитул первой страницы"), null, () => Ed.SetTitlePage(!Ed.Doc.Section.TitlePage), edits: true, isChecked: () => Ed.Doc.Section.TitlePage);
        C(Loc.L("Закрыть колонтитулы"), "Esc", () => _view.ExitStory());
        // Формат
        C(Loc.L("Полужирный"), "Ctrl+B", () => Ed.ToggleBold(), edits: true, isChecked: () => Ed.GetFormatState().Bold == true);
        C(Loc.L("Курсив"), "Ctrl+I", () => Ed.ToggleItalic(), edits: true, isChecked: () => Ed.GetFormatState().Italic == true);
        C(Loc.L("Подчёркнутый"), "Ctrl+U", () => Ed.ToggleUnderline(), edits: true, isChecked: () => Ed.GetFormatState().Underline == true);
        C(Loc.L("Шрифт…"), "Ctrl+D", ShowFontDialog, edits: true);
        C(Loc.L("Абзац…"), null, ShowParagraphDialog, edits: true);
        C(Loc.L("Увеличить размер шрифта"), "Ctrl+]", () => Ed.StepSize(1), edits: true);
        C(Loc.L("Уменьшить размер шрифта"), "Ctrl+[", () => Ed.StepSize(-1), edits: true);
        C(Loc.L("По левому краю"), "Ctrl+L", () => Ed.SetAlign(Align.Left), edits: true);
        C(Loc.L("По центру"), "Ctrl+E", () => Ed.SetAlign(Align.Center), edits: true);
        C(Loc.L("По правому краю"), "Ctrl+R", () => Ed.SetAlign(Align.Right), edits: true);
        C(Loc.L("По ширине (выравнивание)"), "Ctrl+J", () => Ed.SetAlign(Align.Justify), edits: true);
        foreach (var (title, v) in new[] { (Loc.L("Интервал 1"), 240), (Loc.L("Интервал 1,15"), 276), (Loc.L("Интервал 1,5"), 360), (Loc.L("Интервал 2"), 480) })
            C(title, null, () => Ed.SetLineSpacing(v), edits: true);
        C(Loc.L("Маркированный список"), "Ctrl+Shift+L", () => Ed.ToggleList(bullet: true), edits: true);
        C(Loc.L("Нумерованный список"), null, () => Ed.ToggleList(bullet: false), edits: true);
        C(Loc.L("Убрать список"), null, RemoveList, edits: true);
        C(Loc.L("Уменьшить отступ"), "Ctrl+Shift+M", () => Ed.Indent(-360), edits: true);
        C(Loc.L("Увеличить отступ"), "Ctrl+M", () => Ed.Indent(360), edits: true);
        C(Loc.L("Очистить форматирование"), "Ctrl+\\", () => Ed.ClearFormatting(), edits: true);
        C(Loc.L("Копировать форматирование"), null, TogglePainter, edits: true);
        foreach (var (title, id) in StyleList) C(Loc.L("Стиль: ") + title, null, () => Ed.ApplyStyle(id), edits: true);
        C(Loc.L("Параметры страницы…"), null, ShowPageSetup, edits: true);
        C(Loc.L("Книжная ориентация"), null, () => Ed.SetOrientation(Doc.Model.Orientation.Portrait), edits: true);
        C(Loc.L("Альбомная ориентация"), null, () => Ed.SetOrientation(Doc.Model.Orientation.Landscape), edits: true);
        // Таблица
        C(Loc.L("Вставить строку выше"), null, () => Ed.InsertRow(below: false), edits: true);
        C(Loc.L("Вставить строку ниже"), null, () => Ed.InsertRow(below: true), edits: true);
        C(Loc.L("Вставить столбец слева"), null, () => Ed.InsertColumn(right: false), edits: true);
        C(Loc.L("Вставить столбец справа"), null, () => Ed.InsertColumn(right: true), edits: true);
        C(Loc.L("Удалить строку"), null, () => Ed.DeleteRow(), edits: true);
        C(Loc.L("Удалить столбец"), null, () => Ed.DeleteColumn(), edits: true);
        C(Loc.L("Удалить таблицу"), null, () => Ed.DeleteTable(), edits: true);
        C(Loc.L("Объединить ячейки"), null, () => Ed.MergeCells(), edits: true);
        C(Loc.L("Разделить ячейку"), null, () => Ed.SplitCell(), edits: true);
        C(Loc.L("Ширина столбца…"), null, ColumnWidth, edits: true);
        // Инструменты
        C(Loc.L("Число слов"), null, () => MessageBox.Show(this, Loc.F("Слов: {0:N0}\nСтраниц: {1}", Ed.WordCount(), _view.Layout.Pages.Count), Loc.L("Число слов"), MessageBoxButton.OK, MessageBoxImage.Information));
        C(Loc.L("Настройки…"), null, ShowSettings);
        C(Loc.L("Отчёт о совместимости"), null, ShowReport);
        C(Loc.L("Проверка обмена документа"), null, RoundTripCheck);
        // Справка
        C(Loc.L("Горячие клавиши"), "F1", ShowShortcuts);
        C(Loc.L("О программе"), null, () => MessageBox.Show(this,
            Loc.F("RWrite, версия {0}\n\nРедактор документов Word (DOCX) с собственным ядром: модель документа, правка, разметка страниц, ", typeof(App).Assembly.GetName().Version) +
            Loc.L("чтение и запись DOCX, печать и PDF. Без коммерческих библиотек, лицензионных ключей и подключения к интернету."),
            Loc.L("О программе"), MessageBoxButton.OK, MessageBoxImage.Information));
    }

    internal static readonly (string Title, string Id)[] StyleList =
        [(Loc.L("Обычный"), "Normal"), (Loc.L("Название"), "Title"), (Loc.L("Заголовок 1"), "Heading1"), (Loc.L("Заголовок 2"), "Heading2"), (Loc.L("Заголовок 3"), "Heading3")];

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

        var recent = new MenuItem { Header = Loc.L("Последние") };
        recent.SubmenuOpened += (_, _) => FillRecent(recent);
        recent.Items.Add(new MenuItem { Header = Loc.L("(пусто)"), IsEnabled = false });

        var zoom = new MenuItem { Header = Loc.L("Масштаб") };
        foreach (int z in new[] { 50, 75, 90, 100, 125, 150, 200 }) zoom.Items.Add(Item(Loc.F("Масштаб {0}%", z), $"{z}%"));

        var styles = new MenuItem { Header = Loc.L("Стили") };
        foreach (var (t, _) in StyleList) styles.Items.Add(Item(Loc.L("Стиль: ") + t, t));
        var lists = new MenuItem { Header = Loc.L("Списки") };
        foreach (var n in new[] { Loc.L("Маркированный список"), Loc.L("Нумерованный список"), Loc.L("Убрать список"), "-", Loc.L("Увеличить отступ"), Loc.L("Уменьшить отступ") })
            lists.Items.Add(n == "-" ? new Separator() : Item(n));
        var align = new MenuItem { Header = Loc.L("Выравнивание") };
        foreach (var n in new[] { Loc.L("По левому краю"), Loc.L("По центру"), Loc.L("По правому краю") }) align.Items.Add(Item(n));
        align.Items.Add(Item(Loc.L("По ширине (выравнивание)"), Loc.L("По ширине")));
        var spacing = new MenuItem { Header = Loc.L("Межстрочный интервал") };
        foreach (var n in new[] { Loc.L("Интервал 1"), Loc.L("Интервал 1,15"), Loc.L("Интервал 1,5"), Loc.L("Интервал 2") }) spacing.Items.Add(Item(n, n.Replace(Loc.L("Интервал "), "")));
        var table = new MenuItem { Header = Loc.L("Таблица") };
        foreach (var n in new[] { Loc.L("Вставить строку выше"), Loc.L("Вставить строку ниже"), Loc.L("Вставить столбец слева"), Loc.L("Вставить столбец справа"), "-", Loc.L("Удалить строку"), Loc.L("Удалить столбец"), Loc.L("Удалить таблицу"), "-", Loc.L("Объединить ячейки"), Loc.L("Разделить ячейку"), Loc.L("Ширина столбца…") })
            table.Items.Add(n == "-" ? new Separator() : Item(n));
        var orientation = new MenuItem { Header = Loc.L("Ориентация") };
        orientation.Items.Add(Item(Loc.L("Книжная ориентация"), Loc.L("Книжная")));
        orientation.Items.Add(Item(Loc.L("Альбомная ориентация"), Loc.L("Альбомная")));

        menu.Items.Add(Top(Loc.L("Файл"), Loc.L("Создать"), Loc.L("Открыть…"), recent, "-", Loc.L("Сохранить"), Loc.L("Сохранить как…"), Loc.L("Предыдущие версии файла…"), "-", Loc.L("Экспорт PDF…"), Loc.L("Экспорт в текст (TXT)…"), Loc.L("Печать…"), "-", Loc.L("Выход")));
        menu.Items.Add(Top(Loc.L("Правка"), Loc.L("Отменить"), Loc.L("Повторить"), "-", Loc.L("Вырезать"), Loc.L("Копировать"), Loc.L("Вставить"), Loc.L("Вставить только текст"), "-", Loc.L("Выделить все"), "-", Loc.L("Найти"), Loc.L("Найти и заменить")));
        menu.Items.Add(Top(Loc.L("Вид"), zoom, Loc.L("По ширине"), "-", Loc.L("Горизонтальная линейка"), Loc.L("Вертикальная линейка"), Loc.L("Навигация"), Loc.L("Непечатаемые знаки")));
        menu.Items.Add(Top(Loc.L("Вставка"), Loc.L("Изображение…"), Loc.L("Таблица…"), Loc.L("Ссылка…"), Loc.L("Разрыв страницы"), "-", Loc.L("Верхний колонтитул"), Loc.L("Нижний колонтитул"), Loc.L("Номер страницы"), Loc.L("Особый колонтитул первой страницы")));
        menu.Items.Add(Top(Loc.L("Формат"), Loc.L("Шрифт…"), Loc.L("Абзац…"), styles, lists, align, spacing, "-", table, "-", Loc.L("Очистить форматирование"), Loc.L("Копировать форматирование"), "-", Loc.L("Параметры страницы…"), orientation));
        menu.Items.Add(Top(Loc.L("Инструменты"), Loc.L("Число слов"), "-", Loc.L("Отчёт о совместимости"), Loc.L("Проверка обмена документа"), "-", Loc.L("Настройки…")));
        menu.Items.Add(Top(Loc.L("Справка"), Loc.L("Горячие клавиши"), Loc.L("О программе")));
        return menu;
    }

    private void RefreshChecks(ItemsControl menu)
    {
        foreach (var o in menu.Items)
        {
            if (o is not MenuItem mi) continue;
            var c = AllCommands.FirstOrDefault(x => x.Name == (mi.Header as string) || (Loc.L("Масштаб ") + mi.Header) == x.Name || (Loc.L("Стиль: ") + mi.Header) == x.Name);
            if (c?.IsChecked is { } f) { mi.IsCheckable = true; mi.IsChecked = f(); }
            if (c is not null) mi.IsEnabled = !(c.Edits && ReadOnly) || (c.Name == Loc.L("Отменить") || c.Name == Loc.L("Повторить"));
            if (mi.Header as string == Loc.L("Таблица")) mi.IsEnabled = Ed.CurrentCell() is not null;
            if (mi.Items.Count > 0) RefreshChecks(mi);
        }
        if (_commands.TryGetValue(Loc.L("Отменить"), out _))
            foreach (var mi in menu.Items.OfType<MenuItem>())
            {
                if (mi.Header as string == Loc.L("Отменить")) mi.IsEnabled = Ed.CanUndo && !ReadOnly;
                if (mi.Header as string == Loc.L("Повторить")) mi.IsEnabled = Ed.CanRedo && !ReadOnly;
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
        if (recent.Items.Count == 0) recent.Items.Add(new MenuItem { Header = Loc.L("(пусто)"), IsEnabled = false });
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
        Bind(Key.N, ctrl, Loc.L("Создать")); Bind(Key.O, ctrl, Loc.L("Открыть…")); Bind(Key.S, ctrl, Loc.L("Сохранить")); Bind(Key.S, cs, Loc.L("Сохранить как…"));
        Bind(Key.F12, ModifierKeys.None, Loc.L("Сохранить как…")); Bind(Key.P, ctrl, Loc.L("Печать…"));
        Bind(Key.Z, ctrl, Loc.L("Отменить")); Bind(Key.Y, ctrl, Loc.L("Повторить")); Bind(Key.Z, cs, Loc.L("Повторить"));
        Bind(Key.X, ctrl, Loc.L("Вырезать")); Bind(Key.C, ctrl, Loc.L("Копировать")); Bind(Key.V, ctrl, Loc.L("Вставить")); Bind(Key.V, cs, Loc.L("Вставить только текст"));
        Bind(Key.Insert, ModifierKeys.Control, Loc.L("Копировать")); Bind(Key.Insert, ModifierKeys.Shift, Loc.L("Вставить")); Bind(Key.Delete, ModifierKeys.Shift, Loc.L("Вырезать"));
        Bind(Key.A, ctrl, Loc.L("Выделить все")); Bind(Key.F, ctrl, Loc.L("Найти")); Bind(Key.H, ctrl, Loc.L("Найти и заменить"));
        Bind(Key.B, ctrl, Loc.L("Полужирный")); Bind(Key.I, ctrl, Loc.L("Курсив")); Bind(Key.U, ctrl, Loc.L("Подчёркнутый")); Bind(Key.K, ctrl, Loc.L("Ссылка…"));
        Bind(Key.D, ctrl, Loc.L("Шрифт…"));
        Bind(Key.L, ctrl, Loc.L("По левому краю")); Bind(Key.E, ctrl, Loc.L("По центру")); Bind(Key.R, ctrl, Loc.L("По правому краю")); Bind(Key.J, ctrl, Loc.L("По ширине (выравнивание)"));
        Bind(Key.OemCloseBrackets, ctrl, Loc.L("Увеличить размер шрифта")); Bind(Key.OemOpenBrackets, ctrl, Loc.L("Уменьшить размер шрифта"));
        Bind(Key.L, cs, Loc.L("Маркированный список")); Bind(Key.M, ctrl, Loc.L("Увеличить отступ")); Bind(Key.M, cs, Loc.L("Уменьшить отступ"));
        Bind(Key.Oem5, ctrl, Loc.L("Очистить форматирование")); Bind(Key.Space, ctrl, Loc.L("Очистить форматирование"));
        Bind(Key.D8, cs, Loc.L("Непечатаемые знаки")); Bind(Key.F1, ModifierKeys.None, Loc.L("Горячие клавиши"));
        Bind(Key.Add, ctrl, Loc.L("Увеличить размер шрифта")); Bind(Key.Subtract, ctrl, Loc.L("Уменьшить размер шрифта"));
    }

    // ── контекстное меню ─────────────────────────────────────────────────────

    private void BuildContextMenu(ContextMenu m)
    {
        m.Items.Clear();
        foreach (var n in new[] { Loc.L("Вырезать"), Loc.L("Копировать"), Loc.L("Вставить"), Loc.L("Вставить только текст") }) m.Items.Add(Item(n));
        m.Items.Add(new Separator());
        var st = Ed.GetFormatState();
        m.Items.Add(Item(Loc.L("Ссылка…"), st.Link is null ? Loc.L("Вставить ссылку…") : Loc.L("Изменить ссылку…")));
        if (st.Link is not null) m.Items.Add(Item(Loc.L("Удалить ссылку")));
        if (_view.SelectedImage() is not null)
        {
            m.Items.Add(new Separator());
            var del = new MenuItem { Header = Loc.L("Удалить изображение"), InputGestureText = "Delete" };
            del.Click += (_, _) => Exec(new AppCommand(Loc.L("Удалить изображение"), null, () => Ed.DeleteSelection(), edits: true));
            m.Items.Add(del);
        }
        if (Ed.CurrentCell() is not null)
        {
            m.Items.Add(new Separator());
            foreach (var n in new[] { Loc.L("Вставить строку выше"), Loc.L("Вставить строку ниже"), Loc.L("Вставить столбец слева"), Loc.L("Вставить столбец справа"), Loc.L("Удалить строку"), Loc.L("Удалить столбец"), Loc.L("Удалить таблицу"), Loc.L("Объединить ячейки"), Loc.L("Разделить ячейку") })
                m.Items.Add(Item(n));
        }
        m.Items.Add(new Separator());
        m.Items.Add(Item(Loc.L("Шрифт…")));
        m.Items.Add(Item(Loc.L("Абзац…")));
        m.Items.Add(Item(Loc.L("Очистить форматирование")));
        if (Ed.Story is not null) { m.Items.Add(new Separator()); m.Items.Add(Item(Loc.L("Закрыть колонтитулы"))); }
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
            MessageBox.Show(this, Loc.L("Не удалось напечатать: ") + ex.Message, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertImage()
    {
        var dlg = new OpenFileDialog { Filter = Loc.L("Изображения (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg") };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            byte[] bytes = File.ReadAllBytes(dlg.FileName);
            if (bytes.Length > 50 * 1024 * 1024) { MessageBox.Show(this, Loc.L("Изображение больше 50 МБ."), Strings.AppName); return; }
            var frame = BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            // размер в пикселях при 96 dpi — как Word вставляет картинку «в натуральную величину»
            int w = (int)Math.Round(frame.Width), h = (int)Math.Round(frame.Height);
            Ed.InsertImage(bytes, Path.GetExtension(dlg.FileName), Math.Max(1, w), Math.Max(1, h));
        }
        catch (NotSupportedException) { throw; }
        catch (Exception ex)
        {
            Log.Error("insert-image", ex);
            MessageBox.Show(this, Loc.L("Не удалось вставить изображение: файл повреждён или это не PNG/JPEG."), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
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
        Ed.ApplyRun(Loc.L("Шрифт"), r =>
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
        Ed.ApplyPara(Loc.L("Абзац"), x =>
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
        Ed.Execute(Loc.L("Параметры страницы"), () =>
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
        var dlg = new FormDialog(this, Loc.L("Ширина столбца"));
        var box = dlg.AddRow(Loc.L("Ширина, см:"), FormDialog.Box(FormDialog.Num(t.Grid[gridCol] / 566.929), 80));
        if (dlg.ShowDialog() != true) return;
        if (!FormDialog.TryNum(box.Text, out double cm) || cm < 0.3 || cm > 50) { MessageBox.Show(this, Loc.L("Ширина — от 0,3 до 50 см."), Strings.AppName); return; }
        Ed.SetColumnWidth(t, gridCol, (int)Math.Round(cm * 566.929));
    }

    private void ShowSettings()
    {
        var dlg = new FormDialog(this, Loc.L("Настройки"));
        var box = dlg.AddRow(Loc.L("Автосохранение копии, секунд:"), FormDialog.Box(S.Settings.AutoRecoverSeconds.ToString(), 80));
        var lang = dlg.AddRow(Strings.OptLanguage, new ComboBox { Height = 26, MinWidth = 160 });
        lang.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        lang.Items.Add(new ComboBoxItem { Content = "Русский", Tag = "ru" });
        lang.SelectedIndex = Loc.English ? 0 : 1;
        dlg.AddNote(Loc.L("Копия для восстановления после сбоя сохраняется отдельно и не заменяет ваш файл. Резервные версии файла (до 20) создаются при каждом сохранении."));
        if (dlg.ShowDialog() != true) return;
        if ((lang.SelectedItem as ComboBoxItem)?.Tag is string code && code != Loc.Code)
        {
            S.Settings.Language = code;
            S.Settings.Save();
            MessageBox.Show(this, Strings.OptRestart, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
            sb.AppendLine(Loc.F("{0}: {1} — чтение {2}, отображение {3}, правка {4}, сохранение {5}", f.Entry.Title, f.Count, f.Entry.Read, f.Entry.Display, f.Entry.Edit, f.Entry.Save));
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
        var sb = new StringBuilder(Loc.L("Сочетания клавиш работают и в русской раскладке.\n\n"));
        foreach (var c in AllCommands.Where(c => c.Gesture is not null)) sb.AppendLine($"{c.Gesture,-16}  {c.Name}");
        sb.AppendLine().AppendLine(Loc.L("Enter — новый абзац; Shift+Enter — перенос строки; Ctrl+Enter — новая страница."));
        sb.AppendLine(Loc.L("Tab в таблице — следующая ячейка; в начале пункта списка — уровень ниже."));
        sb.AppendLine(Loc.L("Двойной щелчок по полю колонтитула — правка колонтитула; Esc — возврат к тексту."));
        sb.AppendLine(Loc.L("Ctrl+щелчок по ссылке — открыть ссылку; Ctrl+колесо мыши — масштаб."));
        new TextWindow(this, Loc.L("Горячие клавиши"), sb.ToString()).ShowDialog();
    }

    private void ShowVersions()
    {
        if (_path is null) { MessageBox.Show(this, Loc.L("Резервные версии есть только у сохранённого файла."), Strings.AppName); return; }
        var versions = S.Backups.List(_path);
        if (versions.Count == 0) { MessageBox.Show(this, Loc.L("Резервных версий пока нет: они появляются при повторных сохранениях."), Strings.AppName); return; }
        var list = new ListBox { Height = 260, Width = 380 };
        foreach (var v in versions) list.Items.Add(new ListBoxItem { Content = Loc.F("{0:dd.MM.yyyy HH:mm:ss} — {1:N0} КБ", v.SavedUtc.ToLocalTime(), v.Length / 1024.0), Tag = v });
        list.SelectedIndex = 0;
        var dlg = new FormDialog(this, Loc.L("Предыдущие версии файла"));
        dlg.AddRow(Loc.L("Версия:"), list);
        dlg.AddNote(Loc.L("Версия откроется в новом окне как отдельный документ; текущий файл не изменится."));
        if (dlg.ShowDialog() != true || list.SelectedItem is not ListBoxItem { Tag: Pero.Core.Files.BackupVersion ver }) return;
        var w = new EditorWindow();
        w.Show();
        _ = w.LoadAsync(ver.Path, null, Loc.F("{0} (версия от {1:dd.MM HH:mm})", Path.GetFileNameWithoutExtension(_path), ver.SavedUtc.ToLocalTime()));
    }

    private void SetZoom(double z)
    {
        _view.Zoom = z;
        SyncZoomCombo();
    }
}
