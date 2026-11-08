using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pero.Services;
using Pero.Views;
using Syncfusion.Windows.Controls.RichTextBoxAdv;
using Syncfusion.Windows.Tools;
using Syncfusion.Windows.Tools.Controls;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Pero;

/// <summary>
/// Доработка готовой ленты движка:
/// — своё меню «Файл» (UI-02) вместо стандартного: в нём нет команд вне P0 (шифрование,
///   ограничение редактирования, сохранение в HTML/XAML/DOC, онлайн-справка) — SCOPE-01;
/// — вкладка «Макет» (UI-03): поля, ориентация, размер, абзац, разрывы;
/// — на вкладке «Вид» — область навигации (UI-07).
/// </summary>
public partial class EditorWindow
{
    private Ribbon? _ribbon;
    private TextBlock? _infoText;
    private ListBox? _recentList;
    private RibbonButton? _navToggle;

    private void CustomizeRibbon()
    {
        _ribbon = FindDescendant<Ribbon>(RichRibbon);
        if (_ribbon is null) { Log.Info("ribbon: inner Ribbon not found"); return; }
        Step("backstage", BuildBackstage);
        Step("layout-tab", AddLayoutTab);
        Step("view-tab", AddViewItems);
        Step("automation", NameRibbonForAutomation);

        static void Step(string name, Action a)
        {
            try { a(); Log.Info($"ribbon: {name} OK"); }
            catch (Exception ex) { Log.Error("ribbon-" + name, ex); }
        }
    }

    /// <summary>Вкладки ленты и кнопка «Файл» получают имена для экранного диктора и UI-тестов.</summary>
    private void NameRibbonForAutomation()
    {
        foreach (var tab in _ribbon!.Items.OfType<RibbonTab>())
            if (tab.Caption is string caption) System.Windows.Automation.AutomationProperties.SetName(tab, caption);
        if (_ribbon.BackStageButton is { } bsb) System.Windows.Automation.AutomationProperties.SetName(bsb, Strings.FileTab);
    }

    private RibbonTab? FindTab(string partName) =>
        _ribbon?.Items.OfType<RibbonTab>().FirstOrDefault(t => t.Name == partName);

    private static RibbonButton RButton(string label, Action action, bool large = false)
    {
        var b = new RibbonButton { Label = label, SizeForm = large ? SizeForm.Large : SizeForm.Small };
        System.Windows.Automation.AutomationProperties.SetName(b, label);
        b.Click += (_, _) => action();
        return b;
    }

    // ── Файл ─────────────────────────────────────────────────────────────────

    private void BuildBackstage()
    {
        var bs = new Backstage();
        var info = new BackstageTabItem { Header = Strings.BsInfo, Content = BuildInfoPanel(), IsSelected = true };
        bs.Items.Add(info);
        bs.Items.Add(Command(Strings.BsNew, NewDocument));
        bs.Items.Add(new BackstageTabItem { Header = Strings.BsOpen, Content = BuildOpenPanel() });
        bs.Items.Add(Command(Strings.Save, () => _ = SaveAsync(saveAs: false)));
        bs.Items.Add(Command(Strings.BsSaveAs, () => _ = SaveAsync(saveAs: true)));
        bs.Items.Add(Command(Strings.BsPrint, Print));
        bs.Items.Add(new BackstageTabItem { Header = Strings.BsExport, Content = BuildExportPanel() });
        bs.Items.Add(new BackstageTabItem { Header = Strings.BsOptions, Content = BuildOptionsPanel() });
        bs.Items.Add(Command(Strings.BsClose, Close));
        _ribbon!.BackStage = bs;
        _ribbon.BackStageHeader = Strings.FileTab;
    }

    private BackStageCommandButton Command(string header, Action action)
    {
        var b = new BackStageCommandButton { Header = header };
        System.Windows.Automation.AutomationProperties.SetName(b, header);
        b.Click += (_, _) => { CloseBackstage(); action(); };
        return b;
    }

    private void CloseBackstage()
    {
        if (_ribbon is not null) _ribbon.IsBackStageVisible = false;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 28, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 16) };

    private static Button PanelButton(string text, Action action)
    {
        var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => action();
        return b;
    }

    private FrameworkElement BuildInfoPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(Heading(Strings.BsInfo));
        _infoText = new TextBlock { TextWrapping = System.Windows.TextWrapping.Wrap, FontSize = 14, LineHeight = 22, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(_infoText);
        var buttons = new WrapPanel();
        buttons.Children.Add(PanelButton(Strings.CompatReport, () => { CloseBackstage(); ShowReport_Click(this, new RoutedEventArgs()); }));
        buttons.Children.Add(PanelButton(Strings.RoundTrip, () => { CloseBackstage(); RoundTrip_Click(this, new RoutedEventArgs()); }));
        panel.Children.Add(buttons);
        panel.IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) UpdateInfoPanel(); };
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void UpdateInfoPanel()
    {
        if (_infoText is null) return;
        var lines = new List<string> { _displayName };
        if (_path is not null)
        {
            lines.Add(_path);
            try { lines.Add(string.Format(Strings.InfoSize, new FileInfo(_path).Length / 1024.0)); } catch { }
        }
        lines.Add(string.Format(Strings.StatusPage, Math.Max(1, Editor.CurrentPageNumber), Math.Max(1, Editor.PageCount)));
        lines.Add(WordStatus.Text);
        lines.Add(TitleState());
        if (_report is not null) lines.Add(Strings.InfoCompat + ": " + _report.Status switch
        {
            Pero.Core.Packaging.InspectionStatus.Ok => Strings.CompatOk,
            Pero.Core.Packaging.InspectionStatus.OkWithWarnings => Strings.CompatWarn,
            _ => Strings.CompatReadOnly,
        });
        _infoText.Text = string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
    }

    private FrameworkElement BuildOpenPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(32) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        top.Children.Add(Heading(Strings.BsOpen));
        top.Children.Add(PanelButton(Strings.Browse, () => { CloseBackstage(); ShowOpenDialog(); }));
        top.Children.Add(new TextBlock { Text = Strings.RecentFiles, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        panel.Children.Add(top);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Children.Add(PanelButton(Strings.RemoveFromList, () =>
        {
            if (_recentList?.SelectedItem is RecentItem ri) { S.Recent.Remove(ri.Path); FillRecent(); }
        }));
        panel.Children.Add(bottom);

        _recentList = new ListBox { MinHeight = 200, BorderThickness = new Thickness(0) };
        _recentList.MouseDoubleClick += (_, _) => OpenRecentSelected();
        _recentList.KeyDown += (_, e) => { if (e.Key == Key.Enter) OpenRecentSelected(); };
        panel.Children.Add(_recentList);
        panel.IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) FillRecent(); };
        return panel;
    }

    private sealed record RecentItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileName(Path) + "   —   " + System.IO.Path.GetDirectoryName(Path);
    }

    private void FillRecent()
    {
        if (_recentList is null) return;
        _recentList.Items.Clear();
        foreach (var f in S.Recent.Items) _recentList.Items.Add(new RecentItem(f));
    }

    private void OpenRecentSelected()
    {
        if (_recentList?.SelectedItem is not RecentItem ri) return;
        CloseBackstage();
        if (!File.Exists(ri.Path))
        {
            MessageBox.Show(this, string.Format(Strings.RecentMissing, ri.Path), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenInNewWindow(ri.Path, this);
    }

    private FrameworkElement BuildExportPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(Heading(Strings.BsExport));
        panel.Children.Add(PanelButton(Strings.ExportPdf + "…", () => { CloseBackstage(); ExportPdf_Click(this, new RoutedEventArgs()); }));
        panel.Children.Add(PanelButton(Strings.ExportTxt + "…", () => { CloseBackstage(); _ = ExportTxtAsync(); }));
        return panel;
    }

    private FrameworkElement BuildOptionsPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(32), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(Heading(Strings.BsOptions));

        panel.Children.Add(new Label { Content = Strings.OptLanguage, Padding = new Thickness(0, 4, 0, 4) });
        var lang = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        lang.Items.Add("Русский");
        lang.Items.Add("English");
        lang.SelectedIndex = S.Settings.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        lang.SelectionChanged += (_, _) =>
        {
            S.Settings.Language = lang.SelectedIndex == 1 ? "en" : "ru";
            S.Settings.Save();
            MessageBox.Show(this, Strings.OptRestart, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        };
        panel.Children.Add(lang);

        panel.Children.Add(new Label { Content = Strings.OptAutoRecover, Padding = new Thickness(0, 16, 0, 4) });
        var seconds = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Text = S.Settings.AutoRecoverSeconds.ToString() };
        seconds.LostFocus += (_, _) =>
        {
            if (int.TryParse(seconds.Text, out int v) && v is >= 10 and <= 600)
            {
                S.Settings.AutoRecoverSeconds = v;
                S.Settings.Save();
                _recoveryTimer.Interval = TimeSpan.FromSeconds(v);
            }
            else seconds.Text = S.Settings.AutoRecoverSeconds.ToString();
        };
        panel.Children.Add(seconds);

        var spell = new CheckBox { Content = Strings.OptSpell, IsChecked = S.Settings.SpellCheck, Margin = new Thickness(0, 16, 0, 0) };
        spell.Click += (_, _) =>
        {
            S.Settings.SpellCheck = spell.IsChecked == true;
            S.Settings.Save();
            ApplySpellCheck();
        };
        panel.Children.Add(spell);
        return panel;
    }

    private void Print()
    {
        try { SfRichTextBoxAdv.PrintDocumentCommand.Execute(null, Editor); }
        catch (Exception ex) { Log.Error("print", ex); MessageBox.Show(this, ex.Message, Strings.AppName); }
    }

    // ── Макет ────────────────────────────────────────────────────────────────

    private void AddLayoutTab()
    {
        var tab = new RibbonTab { Caption = Strings.LayoutTab };

        var margins = new RibbonBar { Header = Strings.MarginsGroup };
        foreach (var m in PageSetup.Margins)
            margins.Items.Add(RButton(Strings.MarginName(m.Key), () => Layout(() => PageSetup.SetMargins(Editor, m.TopMm, m.RightMm, m.BottomMm, m.LeftMm))));
        margins.Items.Add(RButton(Strings.CustomMargins, ShowPageSetupDialog));
        tab.Items.Add(margins);

        var orient = new RibbonBar { Header = Strings.Orientation };
        orient.Items.Add(RButton(Strings.Portrait, () => Layout(() => PageSetup.SetOrientation(Editor, landscape: false))));
        orient.Items.Add(RButton(Strings.Landscape, () => Layout(() => PageSetup.SetOrientation(Editor, landscape: true))));
        tab.Items.Add(orient);

        var size = new RibbonBar { Header = Strings.PaperSize };
        foreach (var p in PageSetup.Sizes)
            size.Items.Add(RButton($"{p.Name} ({p.WidthMm / 10:0.#}×{p.HeightMm / 10:0.#} см)", () => Layout(() => PageSetup.SetSize(Editor, p.WidthMm, p.HeightMm))));
        size.Items.Add(RButton(Strings.OtherSize, ShowPageSetupDialog));
        tab.Items.Add(size);

        var para = new RibbonBar { Header = Strings.ParagraphGroup };
        var paraButton = new RibbonButton { Label = Strings.ParagraphSettings, SizeForm = SizeForm.Small, Command = SfRichTextBoxAdv.ShowParagraphDialogCommand, CommandTarget = Editor };
        para.Items.Add(paraButton);
        para.Items.Add(RButton(Strings.PageBreak, () => Layout(() => Editor.Selection.InsertBreak(BreakType.PageBreak))));
        tab.Items.Add(para);

        // Лента отображает только добавление в конец, поэтому «Макет» добавляется последним,
        // а вкладки после «Вставки» переносятся за него.
        var insert = FindTab("PART_InsertTab");
        int index = insert is null ? 1 : _ribbon!.Items.IndexOf(insert) + 1;
        var tail = _ribbon!.Items.Cast<object>().Skip(index).ToList();
        _ribbon.Items.Add(tab);
        foreach (var t in tail) { _ribbon.Items.Remove(t); _ribbon.Items.Add(t); }
    }

    private void Layout(Action change)
    {
        if (Editor.IsReadOnly) return;
        try { change(); }
        catch (Exception ex) { Log.Error("layout", ex); MessageBox.Show(this, ex.Message, Strings.AppName); }
        Editor.Focus();
    }

    private void ShowPageSetupDialog()
    {
        if (Editor.IsReadOnly) return;
        var (page, margin) = PageSetup.Current(Editor);
        var dlg = new PageSetupDialog(this, page, margin);
        if (dlg.ShowDialog() != true) return;
        Layout(() =>
        {
            PageSetup.SetSize(Editor, dlg.WidthMm, dlg.HeightMm);
            PageSetup.SetMargins(Editor, dlg.TopMm, dlg.RightMm, dlg.BottomMm, dlg.LeftMm);
        });
    }

    // ── Вид ──────────────────────────────────────────────────────────────────

    private void AddViewItems()
    {
        var view = FindTab("PART_ViewTab") ?? throw new InvalidOperationException("нет вкладки «Вид»");
        var show = new RibbonBar { Header = Strings.ShowGroup };
        _navToggle = RButton(Strings.NavPane, () => SetNavPane(NavPane.Visibility != Visibility.Visible));
        show.Items.Add(_navToggle);
        view.Items.Add(show);
    }
}
