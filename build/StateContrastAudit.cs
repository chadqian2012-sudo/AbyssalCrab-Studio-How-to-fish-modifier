using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Samples the rendered colours of the most frequent card state chips (不可用 / 等待连接 / 状态词)
/// and reports WCAG-style contrast against their own background, so "is this readable" is answered
/// with numbers instead of opinion.
/// </summary>
internal static class StateContrastAudit
{
    private static Application? _application;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string assemblyDir = args.Length > 0 ? args[0] : ".";
        string outDir = args.Length > 1 ? args[1] : assemblyDir;
        Directory.CreateDirectory(outDir);

        AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
        {
            string simple = new AssemblyName(eventArgs.Name).Name + ".dll";
            string candidate = Path.Combine(assemblyDir, simple);
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Assembly trainer = Assembly.LoadFrom(Path.Combine(assemblyDir, "HowToFishTrainer.dll"));
        if (Application.LoadComponent(new Uri("/HowToFishTrainer;component/themes/theme.xaml", UriKind.Relative))
            is ResourceDictionary theme)
        {
            _application.Resources.MergedDictionaries.Add(theme);
        }

        Type licenseInterface = trainer.GetType("HowToFishTrainer.App.Licensing.ILicenseClient", true)!;
        Type licenseStatus = trainer.GetType("HowToFishTrainer.App.Licensing.LicenseStatus", true)!;
        object fake = FakeLicenseClient.Create(licenseInterface, licenseStatus, sponsor: true);
        Window window = (Window)Activator.CreateInstance(trainer.GetType("HowToFishTrainer.App.MainWindow", true)!, new[] { fake })!;
        (window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as DispatcherTimer)?.Stop();
        window.Left = -4000;
        window.Top = -4000;
        window.Show();
        Pump(TimeSpan.FromMilliseconds(1400));

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        if (nav == null)
        {
            Console.WriteLine("FAIL TopCategories missing");
            return 3;
        }

        List<object> findings = new List<object>();
        List<string> lines = new List<string>();

        // Look at the first three pages: they hold the ordinary two-column cards with the
        // 不可用 chip, the 等待连接 status line and the muted descriptors.
        foreach (int page in new[] { 0, 1, 2 })
        {
            nav.SelectedIndex = page;
            Pump(TimeSpan.FromMilliseconds(700));

            foreach (TextBlock block in EnumerateVisual<TextBlock>(window))
            {
                string text = block.Text?.Trim() ?? string.Empty;
                if (!block.IsVisible || text.Length == 0 || text.Length > 24)
                {
                    continue;
                }

                if (text is not ("不可用" or "等待连接" or "实时控制" or "内存实时" or "关" or "开"))
                {
                    continue;
                }

                Color foreground = ResolveColor(block.Foreground);
                double effectiveOpacity = EffectiveOpacity(block);
                Color? background = FindBackgroundBehind(block);
                double? contrast = background == null ? null : Contrast(Composite(foreground, effectiveOpacity, background.Value), background.Value);

                findings.Add(new
                {
                    page,
                    text,
                    font_size = block.FontSize,
                    foreground = foreground.ToString(),
                    background = background?.ToString(),
                    inherited_opacity = Math.Round(effectiveOpacity, 3),
                    contrast_ratio = contrast == null ? (double?)null : Math.Round(contrast.Value, 2),
                    verdict = contrast == null ? "unknown"
                        : contrast.Value >= 4.5 ? "ok"
                        : contrast.Value >= 3.0 ? "large-text-only"
                        : "low",
                });
                lines.Add($"p{page} {text,-6} fg={foreground} bg={background} opacity={effectiveOpacity:0.##} " +
                          $"contrast={(contrast?.ToString("0.00") ?? "-")} {(contrast == null ? "" : contrast < 3 ? "<-- LOW" : "")}");
            }
        }

        // Distinct rows only
        var distinct = findings
            .GroupBy(f => JsonSerializer.Serialize(f))
            .Select(g => g.First())
            .ToList();

        File.WriteAllText(Path.Combine(outDir, "state-contrast.json"),
            JsonSerializer.Serialize(distinct, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        foreach (string line in lines.Distinct())
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"distinct samples: {distinct.Count}");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private static double EffectiveOpacity(DependencyObject element)
    {
        double opacity = 1;
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is UIElement ui)
            {
                opacity *= ui.Opacity;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return opacity;
    }

    private static Color? FindBackgroundBehind(DependencyObject element)
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is Border border && border.Background is SolidColorBrush brush && brush.Color.A > 200)
            {
                return brush.Color;
            }

            if (current is Panel panel && panel.Background is SolidColorBrush panelBrush && panelBrush.Color.A > 200)
            {
                return panelBrush.Color;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Color ResolveColor(Brush? brush)
    {
        return brush is SolidColorBrush solid ? solid.Color : Colors.Black;
    }

    private static Color Composite(Color foreground, double opacity, Color background)
    {
        double alpha = (foreground.A / 255.0) * opacity;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        double hi = Math.Max(la, lb);
        double lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color color)
    {
        double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    private static IEnumerable<T> EnumerateVisual<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T nested in EnumerateVisual<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void Pump(TimeSpan duration)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            System.Threading.Thread.Sleep(10);
        }
    }
}
