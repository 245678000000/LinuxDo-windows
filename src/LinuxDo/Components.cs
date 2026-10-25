using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinuxDo;

static class Components
{
    static readonly Color BrandGold = Color.FromRgb(0xC9, 0x8F, 0x1F);

    /// Taskbar overlay for the unread count in the page title, e.g. "(3) 最新".
    public static ImageSource RenderBadge(string text)
    {
        const double size = 32;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface,
            text.Length > 2 ? 12 : 17, Brushes.White, 1.0);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawEllipse(new SolidColorBrush(BrandGold), new Pen(Brushes.White, 2), new Point(size / 2, size / 2), size / 2 - 1, size / 2 - 1);
            dc.DrawText(label, new Point((size - label.Width) / 2, (size - label.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap((int)size, (int)size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

static class Dialogs
{
    /// Returns true only when the user picks the second, proceeding choice. Enter and Esc keep the safe one.
    public static bool Confirm(Window owner, string title, string message, string stay, string proceed)
    {
        var dialog = Create(owner, title, message, out var buttons);
        var result = false;
        var proceedButton = new Button { Content = proceed, MinWidth = 104, Padding = new Thickness(12, 6, 12, 6) };
        proceedButton.Click += (_, _) => { result = true; dialog.Close(); };
        var stayButton = new Button { Content = stay, MinWidth = 104, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        stayButton.Click += (_, _) => dialog.Close();
        stayButton.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        buttons.Children.Add(proceedButton);
        buttons.Children.Add(stayButton);
        dialog.ShowDialog();
        return result;
    }

    public static void Info(Window owner, string title, string message)
    {
        var dialog = Create(owner, title, message, out var buttons);
        var ok = new Button { Content = "好", MinWidth = 104, Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        ok.Click += (_, _) => dialog.Close();
        ok.SetResourceReference(FrameworkElement.StyleProperty, "AccentButtonStyle");
        buttons.Children.Add(ok);
        dialog.ShowDialog();
    }

    static Window Create(Window owner, string title, string message, out StackPanel buttons)
    {
        var heading = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var body = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 20 };
        body.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(heading); panel.Children.Add(body); panel.Children.Add(buttons);
        var window = new Window
        {
            Title = AppConfig.AppName, Content = panel, Width = 440, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); } };
        if (owner.IsVisible) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }
}
