using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pero.Doc.Editing;
using Pero.Doc.Model;
using Pero.Services;
using Pero.Ui;

namespace Pero;

/// <summary>Поиск и замена (FIND-01) и область навигации по заголовкам.</summary>
public sealed partial class EditorWindow
{
    private Border _findBar = null!;
    private TextBox _findBox = null!, _replaceBox = null!;
    private TextBlock _findCount = null!;
    private CheckBox _matchCase = null!;
    private StackPanel _replaceRow = null!;
    private ListBox _navList = null!;

    private UIElement BuildFindBar()
    {
        _findBox = new TextBox { Width = 220, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        _replaceBox = new TextBox { Width = 220, Height = 26, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_findBox, "Найти");
        AutomationProperties.SetName(_replaceBox, "Заменить на");
        _findCount = new TextBlock { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Muted, MinWidth = 90 };
        AutomationProperties.SetAutomationId(_findCount, "FindCount");
        _matchCase = new CheckBox { Content = "Учитывать регистр", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        var row1 = new StackPanel { Orientation = Orientation.Horizontal };
        row1.Children.Add(new Label { Content = "Найти:", Target = _findBox, Width = 86, VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(_findBox);
        row1.Children.Add(Theme.ToolButton("Предыдущее совпадение", Theme.IconPath(Theme.Icon.Up), "Shift+Enter", (_, _) => FindStep(false)));
        row1.Children.Add(Theme.ToolButton("Следующее совпадение", Theme.IconPath(Theme.Icon.Down), "Enter", (_, _) => FindStep(true)));
        row1.Children.Add(_findCount);
        row1.Children.Add(_matchCase);

        _replaceRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _replaceRow.Children.Add(new Label { Content = "Заменить на:", Target = _replaceBox, Width = 86, VerticalAlignment = VerticalAlignment.Center });
        _replaceRow.Children.Add(_replaceBox);
        var replace = new Button { Content = "Заменить", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        var replaceAll = new Button { Content = "Заменить все", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        replace.Click += (_, _) => ReplaceOne();
        replaceAll.Click += (_, _) => ReplaceAll();
        _replaceRow.Children.Add(replace);
        _replaceRow.Children.Add(replaceAll);

        var close = Theme.ToolButton("Закрыть поиск", Theme.IconPath(Theme.Icon.Close, 14), "Esc", (_, _) => HideFind());
        var dock = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        close.VerticalAlignment = VerticalAlignment.Top;
        dock.Children.Add(close);
        var rows = new StackPanel();
        rows.Children.Add(row1);
        rows.Children.Add(_replaceRow);
        dock.Children.Add(rows);
        _findBar = new Border
        {
            Child = dock, Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(8, 0, 8, 6), CornerRadius = new CornerRadius(8),
            Background = Brushes.White, BorderBrush = Theme.Separator, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetAutomationId(_findBar, "FindBar");

        _findBox.TextChanged += (_, _) => RefreshMatches();
        _matchCase.Click += (_, _) => RefreshMatches();
        _findBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindStep(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
            if (e.Key == Key.Escape) { HideFind(); e.Handled = true; }
        };
        _replaceBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ReplaceOne(); e.Handled = true; }
            if (e.Key == Key.Escape) { HideFind(); e.Handled = true; }
        };
        return _findBar;
    }

    private void ShowFind(bool replace)
    {
        _findBar.Visibility = Visibility.Visible;
        _replaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        var sel = Ed.Selection;
        if (!sel.IsEmpty && sel.Start.Para == sel.End.Para) _findBox.Text = Ed.SelectedText();
        _findBox.Focus();
        _findBox.SelectAll();
        RefreshMatches();
    }

    private void HideFind()
    {
        _findBar.Visibility = Visibility.Collapsed;
        _view.Matches.Clear();
        _view.InvalidateVisual();
        _view.Focus();
    }

    private void RefreshMatches()
    {
        _view.Matches.Clear();
        string q = _findBox.Text;
        if (q.Length > 0) _view.Matches.AddRange(Ed.FindAll(q, _matchCase.IsChecked == true));
        _findCount.Text = q.Length == 0 ? "" : _view.Matches.Count == 0 ? Strings.NoResults : string.Format(Strings.ResultsCount, _view.Matches.Count);
        _view.InvalidateVisual();
    }

    private void FindStep(bool forward)
    {
        if (_findBox.Text.Length == 0) return;
        var hit = Ed.FindNext(_findBox.Text, forward, _matchCase.IsChecked == true);
        if (hit is null) { _findCount.Text = Strings.NoResults; return; }
        _view.ScrollToCaret();
        int index = _view.Matches.FindIndex(m => m.Start == hit.Value.Start);
        _findCount.Text = $"{index + 1} из {_view.Matches.Count}";
    }

    private void ReplaceOne()
    {
        if (ReadOnly) { ShowMessage(ReadOnlyReason(), null, null); return; }
        if (_findBox.Text.Length == 0) return;
        Ed.ReplaceCurrent(_findBox.Text, _replaceBox.Text, _matchCase.IsChecked == true);
        _view.ScrollToCaret();
        RefreshMatches();
    }

    private void ReplaceAll()
    {
        if (ReadOnly) { ShowMessage(ReadOnlyReason(), null, null); return; }
        if (_findBox.Text.Length == 0) return;
        int n = Ed.ReplaceAll(_findBox.Text, _replaceBox.Text, _matchCase.IsChecked == true);
        RefreshMatches();
        _findCount.Text = $"Заменено: {n}";
    }

    // ── навигация по заголовкам ──────────────────────────────────────────────

    private UIElement BuildNavigation()
    {
        _navList = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(_navList, Strings.NavHeadings);
        _navList.SelectionChanged += (_, _) =>
        {
            if (_navList.SelectedItem is ListBoxItem { Tag: Paragraph p })
            {
                if (Ed.Story is not null) _view.ExitStory();
                int i = Ed.Index.IndexOf(p);
                if (i >= 0) { Ed.Select(Selection.At(new DocPos(i, 0))); _view.ScrollToCaret(); }
            }
        };
        var header = new TextBlock { Text = Strings.NavHeadings, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 8, 10, 6), Foreground = Theme.Ink };
        var panel = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xFB)) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(_navList);
        return new Border { Child = panel, BorderBrush = Theme.Separator, BorderThickness = new Thickness(0, 0, 1, 0) };
    }

    private void ToggleNavigation()
    {
        _navColumn.Width = new GridLength(_navColumn.Width.Value > 0 ? 0 : 220);
        RefreshNavigation();
    }

    private void RefreshNavigation()
    {
        if (_navList is null || _navColumn.Width.Value <= 0) return;
        _navList.Items.Clear();
        foreach (var p in Ed.Doc.Body.OfType<Paragraph>())
        {
            int level = HeadingLevel(p);
            if (level < 0 || p.Text.Trim().Length == 0) continue;
            _navList.Items.Add(new ListBoxItem
            {
                Content = p.Text.Replace("￼", "").Trim(),
                Tag = p,
                Padding = new Thickness(10 + 14 * level, 3, 8, 3),
                FontWeight = level == 0 ? FontWeights.SemiBold : FontWeights.Normal,
            });
        }
        if (_navList.Items.Count == 0) _navList.Items.Add(new ListBoxItem { Content = Strings.NoHeadings, IsEnabled = false });
    }

    /// <summary>Уровень заголовка: «Название» и «Заголовок 1» — 0, «Заголовок 2» — 1… ; −1 — не заголовок.</summary>
    private int HeadingLevel(Paragraph p)
    {
        foreach (var st in Ed.Doc.Styles.Chain(p.Props.StyleId).Reverse())
        {
            if (st.Id == "Title" || st.Name.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 0;
            var name = st.Name.ToLowerInvariant();
            if (name.StartsWith("heading ") && int.TryParse(name[8..], out int n)) return Math.Clamp(n - 1, 0, 5);
        }
        return -1;
    }
}
