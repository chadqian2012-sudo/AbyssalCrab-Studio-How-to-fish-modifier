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
/// Measures text contrast against the ACTUAL COMPOSITED background, not the declared brush value.
///
/// The hand-off asks for 4.5:1 body-text contrast "verified on the final composited material, not
/// just the raw RGBA value". This renders the window once, then for each visible text element reads
/// the background pixels immediately around/behind it from that render and computes the real ratio.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class CompositedContrastAudit
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
        Pump(TimeSpan.FromMilliseconds(1600));

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        List<object> rows = new List<object>();
        List<string> lines = new List<string>();
        int belowBody = 0;
        int belowLarge = 0;

        for (int page = 0; page < (nav?.Items.Count ?? 0); page++)
        {
            nav!.SelectedIndex = page;
            Pump(TimeSpan.FromMilliseconds(520));

            window.UpdateLayout();
            int width = (int)Math.Ceiling(window.ActualWidth);
            int height = (int)Math.Ceiling(window.ActualHeight);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            byte[] pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);

            foreach (TextBlock block in EnumerateVisual<TextBlock>(window))
            {
                if (!block.IsVisible || string.IsNullOrWhiteSpace(block.Text) || block.ActualWidth < 8 || block.ActualHeight < 6)
                {
                    continue;
                }

                if (block.Foreground is not SolidColorBrush foreground)
                {
                    continue;
                }

                Point origin = block.TranslatePoint(new Point(0, 0), window);
                int x0 = (int)Math.Round(origin.X);
                int y0 = (int)Math.Round(origin.Y);
                int w = (int)Math.Round(block.ActualWidth);
                int h = (int)Math.Round(block.ActualHeight);
                if (x0 < 1 || y0 < 1 || x0 + w > width - 1 || y0 + h > height - 1 || w < 4 || h < 4)
                {
                    continue;
                }

                // Background estimate: the most common colour on the block's border ring. Inside the
                // text area most pixels are background too, so the ring is a safe sample of what the
                // glyphs actually sit on.
                Dictionary<int, int> histogram = new Dictionary<int, int>();
                void Sample(int x, int y)
                {
                    int offset = (y * width + x) * 4;
                    int key = (pixels[offset + 2] << 16) | (pixels[offset + 1] << 8) | pixels[offset];
                    histogram[key] = histogram.TryGetValue(key, out int value) ? value + 1 : 1;
                }

                for (int x = x0; x < x0 + w; x++)
                {
                    Sample(x, y0);
                    Sample(x, y0 + h - 1);
                }

                for (int y = y0; y < y0 + h; y++)
                {
                    Sample(x0, y);
                    Sample(x0 + w - 1, y);
                }

                int dominant = histogram.OrderByDescending(pair => pair.Value).First().Key;
                Color background = Color.FromRgb((byte)(dominant >> 16), (byte)(dominant >> 8), (byte)dominant);

                // Effective opacity from the element chain, so a faded disabled card is measured as seen.
                double opacity = 1;
                DependencyObject? current = block;
                while (current != null)
                {
                    if (current is UIElement ui)
                    {
                        opacity *= ui.Opacity;
                    }

                    current = VisualTreeHelper.GetParent(current);
                }

                Color composited = Composite(foreground.Color, opacity, background);
                double ratio = Contrast(composited, background);
                double fontPx = block.FontSize * VisualTreeHelper.GetDpi(block).DpiScaleY;
                bool largeText = fontPx >= 24 || (fontPx >= 18.66 && block.FontWeight >= FontWeights.Bold);
                double required = largeText ? 3.0 : 4.5;
                bool ok = ratio >= required;
                if (!ok)
                {
                    if (largeText) belowLarge++; else belowBody++;
                }

                if (!ok)
                {
                    rows.Add(new
                    {
                        page = nav.Items[page]?.GetType().GetProperty("Title")?.GetValue(nav.Items[page])?.ToString(),
                        text = block.Text.Length > 40 ? block.Text.Substring(0, 40) : block.Text,
                        font_size = block.FontSize,
                        effective_opacity = Math.Round(opacity, 3),
                        foreground = foreground.Color.ToString(),
                        composited_background = background.ToString(),
                        contrast = Math.Round(ratio, 2),
                        required,
                        large_text = largeText,
                    });
                }
            }
        }

        // Summary by requirement band so the result reads as "how many fail" not just a list.
        int totalBelow = rows.Count;
        lines.Add($"text elements below their contrast target: {totalBelow} (body {belowBody}, large {belowLarge})");
        foreach (var row in rows.GroupBy(r => r.GetType().GetProperty("text")!.GetValue(r)!.ToString()).OrderByDescending(g => g.Count()).Take(20))
        {
            var first = row.First();
            lines.Add($"   x{row.Count(),-3} ratio={first.GetType().GetProperty("contrast")!.GetValue(first),-6} " +
                      $"required={first.GetType().GetProperty("required")!.GetValue(first),-4} " +
                      $"fs={first.GetType().GetProperty("font_size")!.GetValue(first),-5} " +
                      $"opacity={first.GetType().GetProperty("effective_opacity")!.GetValue(first),-6} " +
                      $"bg={first.GetType().GetProperty("composited_background")!.GetValue(first)}  " +
                      $"{first.GetType().GetProperty("text")!.GetValue(first)}");
        }

        File.WriteAllText(Path.Combine(outDir, "composited-contrast-audit.json"),
            JsonSerializer.Serialize(new
            {
                method = "rendered the window, sampled each text block's border ring for the real background colour, composited the foreground through its inherited opacity, then applied the WCAG ratio",
                target = "4.5:1 body text / 3:1 large text, measured on the final composited material",
                below_target_total = totalBelow,
                below_target_body = belowBody,
                below_target_large = belowLarge,
                failures = rows,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        _application.Shutdown();
        return 0;
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
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
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
            System.Threading.Thread.Sleep(6);
        }
    }
}
