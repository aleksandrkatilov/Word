using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Syncfusion.Windows.Controls.RichTextBoxAdv;

namespace Pero.Lab;

/// <summary>Окно с лентой и редактором как в приложении: дерево элементов ленты и скриншот.</summary>
public static class RibbonProbe
{
    public static async Task Run(string outDir, Action<string> log)
    {
        Directory.CreateDirectory(outDir);
        var editor = new SfRichTextBoxAdv { LayoutType = LayoutType.Pages, Background = Brushes.Gainsboro };
        var ribbon = new SfRichTextRibbon();
        ribbon.SetBinding(FrameworkElement.DataContextProperty, new System.Windows.Data.Binding { Source = editor });
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(ribbon);
        Grid.SetRow(editor, 1);
        grid.Children.Add(editor);

        var win = new Syncfusion.Windows.Tools.Controls.RibbonWindow { Width = 1280, Height = 800, Content = grid, Title = "probe" };
        win.Show();
        await Lab.Idle();
        await Task.Delay(1500);
        await Lab.Idle();

        log($"SfRichTextRibbon base chain: {Chain(ribbon.GetType())}");
        var inner = Lab.FindDescendant<Syncfusion.Windows.Tools.Controls.Ribbon>(ribbon);
        log($"inner Ribbon found: {inner is not null}");

        var sb = new StringBuilder();
        DumpLogical(sb, ribbon, 0);
        File.WriteAllText(Path.Combine(outDir, "ribbon-logical.txt"), sb.ToString());
        sb.Clear();
        DumpVisual(sb, ribbon, 0, 14);
        File.WriteAllText(Path.Combine(outDir, "ribbon-visual.txt"), sb.ToString());

        Lab.Shot(win, Path.Combine(outDir, "ribbon-default.png"));

        // Попытка добавить вкладку, как делает приложение
        if (inner is not null)
        {
            try
            {
                var bar = new Syncfusion.Windows.Tools.Controls.RibbonBar { Header = "Проверка" };
                bar.Items.Add(new Syncfusion.Windows.Tools.Controls.RibbonButton { Label = "Кнопка" });
                var tab = new Syncfusion.Windows.Tools.Controls.RibbonTab { Caption = "Этап 0" };
                tab.Items.Add(bar);
                inner.Items.Add(tab);
                await Lab.Idle();
                log("custom tab added OK");
            }
            catch (Exception ex) { log("custom tab FAILED: " + ex); }
        }
        win.Close();
    }

    private static string Chain(Type t)
    {
        var parts = new List<string>();
        for (var c = t; c is not null; c = c.BaseType) parts.Add(c.FullName ?? c.Name);
        return string.Join(" → ", parts);
    }

    private static readonly string[] Props = ["Label", "Header", "Caption", "Text", "Title", "Content", "ToolTip", "Name"];

    private static string Describe(object o)
    {
        var sb = new StringBuilder(o.GetType().Name);
        foreach (var p in Props)
        {
            var pi = o.GetType().GetProperty(p);
            if (pi is null) continue;
            object? v;
            try { v = pi.GetValue(o); } catch { continue; }
            if (v is string s && s.Length > 0) sb.Append($" {p}=«{(s.Length > 60 ? s[..60] : s)}»");
        }
        if (o is ICommandSource cs && cs.Command is RoutedCommand rc) sb.Append($" Command={rc.OwnerType.Name}.{rc.Name}");
        else if (o is ICommandSource cs2 && cs2.Command is not null) sb.Append($" Command={cs2.Command.GetType().Name}");
        if (o is UIElement u && u.Visibility != Visibility.Visible) sb.Append($" [{u.Visibility}]");
        return sb.ToString();
    }

    private static int _lines;

    private static void DumpLogical(StringBuilder sb, object node, int depth)
    {
        if (++_lines > 6000 || depth > 30) return;
        sb.Append(' ', depth * 2).AppendLine(Describe(node));
        if (node is DependencyObject d)
            foreach (var child in LogicalTreeHelper.GetChildren(d))
                if (child is DependencyObject) DumpLogical(sb, child, depth + 1);
    }

    private static void DumpVisual(StringBuilder sb, DependencyObject node, int depth, int maxDepth)
    {
        if (depth > maxDepth || sb.Length > 400_000) return;
        sb.Append(' ', depth * 2).AppendLine(Describe(node));
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++) DumpVisual(sb, VisualTreeHelper.GetChild(node, i), depth + 1, maxDepth);
    }
}
