using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syncfusion.Windows.Controls.RichTextBoxAdv;

namespace Pero.Lab;

/// <summary>
/// Поиск причины зависания при смене формата раздела в приложении. Каждый вариант запускается
/// в отдельном процессе; сторожевой поток завершает процесс через 25 с и пишет HANG.
/// </summary>
public static class HangProbe
{
    public static async Task Run(string variant, string samples, Action<string> log)
    {
        bool ribbon = variant.Contains("ribbon"), theme = variant.Contains("theme"), direct = variant.Contains("direct"), spell = variant.Contains("spell");
        var done = new ManualResetEventSlim();
        new Thread(() =>
        {
            if (!done.Wait(TimeSpan.FromSeconds(25)))
            {
                log($"HANG variant={variant}");
                Environment.Exit(3);
            }
        }) { IsBackground = true }.Start();

        var editor = new SfRichTextBoxAdv { LayoutType = LayoutType.Pages, Background = Brushes.Gainsboro };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        if (ribbon)
        {
            var r = new SfRichTextRibbon();
            r.SetBinding(FrameworkElement.DataContextProperty, new System.Windows.Data.Binding { Source = editor });
            grid.Children.Add(r);
        }
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);
        var win = new Syncfusion.Windows.Tools.Controls.RibbonWindow { Width = 1280, Height = 800, Content = grid, Title = variant };
        if (theme) Syncfusion.SfSkinManager.SfSkinManager.SetTheme(win, new Syncfusion.SfSkinManager.Theme("Windows11Light"));
        if (spell)
        {
            editor.SpellChecker ??= new SpellChecker();
            editor.SpellChecker.UseFrameworkSpellCheck = true;
            editor.Language = System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU");
            editor.SpellChecker.IsEnabled = true;
        }
        win.Show();
        editor.Load(Path.Combine(samples, "01-styles-lists-multipage.docx"));
        for (int i = 0; i < 20; i++) { await Lab.Idle(); await Task.Delay(100); }

        var sw = Stopwatch.StartNew();
        if (direct)
        {
            foreach (SectionAdv s in editor.Document.Sections)
                s.SectionFormat.PageSize = new Size(s.SectionFormat.PageSize.Height, s.SectionFormat.PageSize.Width);
        }
        else if (variant.Contains("toggle"))
        {
            editor.SpellChecker!.IsEnabled = false;
            Pero.Services.PageSetup.SetOrientation(editor, landscape: true);
            for (int i = 0; i < 10; i++) { await Lab.Idle(); await Task.Delay(100); }
            editor.SpellChecker.IsEnabled = true;
        }
        else
        {
            Pero.Services.PageSetup.SetOrientation(editor, landscape: true);
        }
        for (int i = 0; i < 10; i++) { await Lab.Idle(); await Task.Delay(100); }
        var size = editor.Document.Sections.Cast<SectionAdv>().First().SectionFormat.PageSize;
        log($"OK variant={variant} in {sw.ElapsedMilliseconds} ms, page={size}, pages={editor.PageCount}");
        done.Set();
        win.Close();
    }
}
