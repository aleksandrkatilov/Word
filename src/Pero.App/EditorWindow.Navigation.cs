using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Pero.Core.Security;
using Pero.Services;
using Syncfusion.Windows.Controls.RichTextBoxAdv;
using MessageBox = System.Windows.MessageBox;

namespace Pero;

/// <summary>Область навигации (UI-07), поиск (FIND-01), гиперссылки (LINK-01), орфография (P1).</summary>
public partial class EditorWindow
{
    private DispatcherTimer? _searchTimer;
    private object? _lastSearch;

    private sealed record HeadingItem(int Level, string Text, ParagraphAdv Paragraph)
    {
        public override string ToString() => new string(' ', (Level - 1) * 4) + Text;
    }

    private sealed record SearchHit(string Display, TextPosition Start, TextPosition End)
    {
        public override string ToString() => Display;
    }

    private void InitNavigation()
    {
        NavTitle.Text = Strings.NavPane;
        System.Windows.Automation.AutomationProperties.SetName(NavSearch, Strings.SearchDocument);
        System.Windows.Automation.AutomationProperties.SetName(NavHeadings, Strings.NavHeadings);
        System.Windows.Automation.AutomationProperties.SetName(NavResults, Strings.SearchResults);
        _searchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => RunSearch(), Dispatcher) { IsEnabled = false };

        // Ctrl+F — область навигации с поиском, как в Word; Ctrl+H — диалог замены движка.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                SetNavPane(true);
                NavSearch.Focus();
                NavSearch.SelectAll();
            }
            else if (e.Key == Key.Escape && NavSearch.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                Editor.Focus();
            }
        };
        AddShortcut(Key.H, ModifierKeys.Control, () => SfRichTextBoxAdv.ShowFindAndReplaceDialogCommand.Execute(null, Editor));

        Editor.RequestNavigate += OnRequestNavigate;
    }

    private void SetNavPane(bool visible)
    {
        NavPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavSplitter.Visibility = NavPane.Visibility;
        NavColumn.Width = visible ? new GridLength(280) : new GridLength(0);
        if (_navToggle is not null) _navToggle.Label = (visible ? "✓ " : "") + Strings.NavPane;
        if (visible) RefreshHeadings();
    }

    private void NavClose_Click(object sender, RoutedEventArgs e) => SetNavPane(false);

    /// <summary>Заголовки — абзацы со стилями «Heading N» / «Заголовок N» / «Title».</summary>
    private void RefreshHeadings()
    {
        if (NavPane.Visibility != Visibility.Visible) return;
        var items = new List<HeadingItem>();
        try
        {
            foreach (SectionAdv section in Editor.Document.Sections)
                foreach (var block in section.Blocks)
                    if (block is ParagraphAdv p && HeadingLevel(p.ParagraphFormat?.StyleName) is int level)
                    {
                        string text = ParagraphText(p).Trim();
                        if (text.Length > 0) items.Add(new HeadingItem(level, text.Length > 80 ? text[..80] + "…" : text, p));
                    }
        }
        catch (Exception ex) { Log.Error("nav-headings", ex); }
        NavHeadings.Items.Clear();
        foreach (var i in items) NavHeadings.Items.Add(i);
        if (NavResults.Visibility != Visibility.Visible)
            NavStatus.Text = items.Count == 0 ? Strings.NoHeadings : "";
    }

    private static int? HeadingLevel(string? style)
    {
        if (string.IsNullOrEmpty(style)) return null;
        if (style is "Title" or "Название") return 1;
        foreach (var prefix in new[] { "Heading ", "heading ", "Заголовок " })
            if (style.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(style[prefix.Length..], out int n) && n is >= 1 and <= 9)
                return n;
        return null;
    }

    private static string ParagraphText(ParagraphAdv p)
    {
        var sb = new StringBuilder();
        foreach (var inline in p.Inlines)
            if (inline is SpanAdv s) sb.Append(s.Text);
        return sb.ToString();
    }

    private void NavHeadings_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavHeadings.SelectedItem is not HeadingItem h) return;
        try
        {
            var pos = Editor.Document.GetTextPosition(h.Paragraph, 0);
            Editor.Selection.Select(pos, pos);
        }
        catch (Exception ex) { Log.Error("nav-go", ex); }
    }

    private void NavSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer?.Stop();
        _searchTimer?.Start();
    }

    private void NavSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _searchTimer?.Stop();
        RunSearch();
        // Enter — к следующему результату
        if (NavResults.Items.Count > 0)
            NavResults.SelectedIndex = (NavResults.SelectedIndex + 1) % NavResults.Items.Count;
    }

    private void RunSearch()
    {
        _searchTimer?.Stop();
        string q = NavSearch.Text;
        try { (_lastSearch as IDisposable)?.Dispose(); } catch { }
        _lastSearch = null;
        NavResults.Items.Clear();
        if (string.IsNullOrWhiteSpace(q))
        {
            NavResults.Visibility = Visibility.Collapsed;
            NavHeadings.Visibility = Visibility.Visible;
            RefreshHeadings();
            return;
        }
        NavHeadings.Visibility = Visibility.Collapsed;
        NavResults.Visibility = Visibility.Visible;
        try
        {
            TextSearchResults results = Editor.FindAll(q, FindOptions.None);
            _lastSearch = results;
            int count = results?.Count ?? 0;
            dynamic dyn = results!;
            for (int i = 0; i < count && i < 500; i++)
            {
                TextSearchResult r = dyn[i];
                string context = ParagraphText(r.Start.Paragraph).Replace('\n', ' ');
                int at = context.IndexOf(q, StringComparison.CurrentCultureIgnoreCase);
                string snippet = at < 0 ? context : context.Substring(Math.Max(0, at - 30), Math.Min(context.Length - Math.Max(0, at - 30), q.Length + 70));
                NavResults.Items.Add(new SearchHit(snippet.Trim(), r.Start, r.End));
            }
            NavStatus.Text = count == 0 ? Strings.NoResults : string.Format(Strings.ResultsCount, count);
        }
        catch (Exception ex)
        {
            Log.Error("search", ex);
            NavStatus.Text = Strings.NoResults;
        }
    }

    private void NavResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavResults.SelectedItem is not SearchHit hit) return;
        try { Editor.Selection.Select(hit.Start, hit.End); }
        catch (Exception ex) { Log.Error("search-go", ex); }
    }

    // ── LINK-01 ──────────────────────────────────────────────────────────────

    private void OnRequestNavigate(object obj, RequestNavigateEventArgs args)
    {
        var link = args.Hyperlink;
        if (link is null || link.LinkType == HyperlinkType.Bookmark) return; // переход по закладке выполняет движок
        var (decision, uri, reason) = LinkPolicy.Evaluate(link.NavigationLink);
        if (decision == LinkDecision.Block || uri is null)
        {
            MessageBox.Show(this, string.Format(Strings.LinkBlocked, link.NavigationLink, reason), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (decision == LinkDecision.Confirm &&
            MessageBox.Show(this, string.Format(Strings.LinkConfirm, uri.IsFile ? uri.LocalPath : uri.AbsoluteUri), Strings.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try { Process.Start(new ProcessStartInfo(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("link", ex); MessageBox.Show(this, ex.Message, Strings.AppName); }
    }

    // ── Орфография ───────────────────────────────────────────────────────────

    private void ApplySpellCheck()
    {
        try
        {
            Editor.SpellChecker ??= new SpellChecker();
            var sc = Editor.SpellChecker;
            sc.UseFrameworkSpellCheck = true; // словари Windows: русский и английский
            sc.IgnoreURIs = true;
            sc.IgnoreAlphaNumericWords = true;
            string dic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pero", "custom.dic");
            Directory.CreateDirectory(Path.GetDirectoryName(dic)!);
            if (!File.Exists(dic)) File.WriteAllText(dic, "");
            sc.CustomDictionaries ??= [];
            if (!sc.CustomDictionaries.Contains(dic)) sc.CustomDictionaries.Add(dic);
            Editor.Language = XmlLanguage.GetLanguage(Strings.IsRussian ? "ru-RU" : "en-US");
            sc.IsEnabled = S.Settings.SpellCheck;
        }
        catch (Exception ex) { Log.Error("spellcheck", ex); }
    }
}
