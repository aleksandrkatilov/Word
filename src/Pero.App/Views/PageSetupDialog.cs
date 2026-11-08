using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Pero.Services;

namespace Pero.Views;

/// <summary>Диалог «Параметры страницы»: размер и поля в сантиметрах (PAGE-01, настраиваемые значения).</summary>
public sealed class PageSetupDialog : Window
{
    private readonly TextBox _w = Box(), _h = Box(), _top = Box(), _right = Box(), _bottom = Box(), _left = Box();
    public double WidthMm { get; private set; }
    public double HeightMm { get; private set; }
    public double TopMm { get; private set; }
    public double RightMm { get; private set; }
    public double BottomMm { get; private set; }
    public double LeftMm { get; private set; }

    public PageSetupDialog(Window owner, Size pagePx, Thickness marginPx)
    {
        Owner = owner;
        Title = Strings.PageSetupTitle;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _w.Text = Cm(pagePx.Width); _h.Text = Cm(pagePx.Height);
        _top.Text = Cm(marginPx.Top); _right.Text = Cm(marginPx.Right);
        _bottom.Text = Cm(marginPx.Bottom); _left.Text = Cm(marginPx.Left);

        var grid = new Grid { Margin = new Thickness(16) };
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Row(int r, string l1, TextBox b1, string l2, TextBox b2)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(new Label { Content = l1, Target = b1 }, r, 0); Add(b1, r, 1);
            Add(new Label { Content = l2, Target = b2, Margin = new Thickness(16, 0, 0, 0) }, r, 2); Add(b2, r, 3);
        }
        void Add(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); grid.Children.Add(e); }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(new TextBlock { Text = Strings.PaperSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, 0, 0);
        Row(1, Strings.WidthCm, _w, Strings.HeightCm, _h);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(new TextBlock { Text = Strings.MarginsCm, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) }, 2, 0);
        Row(3, Strings.Top, _top, Strings.Bottom, _bottom);
        Row(4, Strings.Left, _left, Strings.Right, _right);

        var ok = new Button { Content = Strings.Ok, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = Strings.Cancel, IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => Accept();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumnSpan(buttons, 4);
        Add(buttons, 5, 0);
        Content = grid;
        Loaded += (_, _) => { _w.Focus(); _w.SelectAll(); };
    }

    private static TextBox Box() => new() { Width = 70, Margin = new Thickness(4, 2, 0, 2), VerticalContentAlignment = VerticalAlignment.Center };

    private static string Cm(double px) => (PageSetup.ToMm(px) / 10).ToString("0.##", CultureInfo.CurrentCulture);

    private void Accept()
    {
        if (!Parse(_w, 1, 200, out double w) || !Parse(_h, 1, 200, out double h) ||
            !Parse(_top, 0, 50, out double t) || !Parse(_right, 0, 50, out double r) ||
            !Parse(_bottom, 0, 50, out double b) || !Parse(_left, 0, 50, out double l))
            return;
        if (l + r >= w - 1 || t + b >= h - 1)
        {
            MessageBox.Show(this, Strings.MarginsTooLarge, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WidthMm = w * 10; HeightMm = h * 10; TopMm = t * 10; RightMm = r * 10; BottomMm = b * 10; LeftMm = l * 10;
        DialogResult = true;
    }

    private bool Parse(TextBox box, double min, double max, out double value)
    {
        string s = box.Text.Trim().Replace(',', '.');
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max) return true;
        MessageBox.Show(this, string.Format(Strings.ValueOutOfRange, min, max), Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        box.Focus(); box.SelectAll();
        return false;
    }
}
