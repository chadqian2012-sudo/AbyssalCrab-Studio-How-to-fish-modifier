using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Renders each ProgressBar found in the window and reports whether its pixels are a single flat
/// colour (already themed) or show the native gradient/border chrome, plus a preview image.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class ProgressAudit
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
        List<object> rows = new List<object>();
        List<string> lines = new List<string>();

        for (int page = 0; page < (nav?.Items.Count ?? 0); page++)
        {
            nav!.SelectedIndex = page;
            Pump(TimeSpan.FromMilliseconds(520));
            foreach (ProgressBar bar in EnumerateVisual<ProgressBar>(window).Where(b => b.IsVisible && b.ActualWidth > 20))
            {
                object report = Inspect(bar, page, outDir);
                rows.Add(report);
                foreach (System.Reflection.PropertyInfo property in report.GetType().GetProperties())
                {
                    _ = property;
                }

                lines.Add(JsonSerializer.Serialize(report));
            }
        }

        File.WriteAllText(Path.Combine(outDir, "progress-audit.json"),
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        // Live-value check: the offline window shows 0 %, so drive a bar to 40 % and 100 % to prove
        // the themed indicator actually grows and follows the requested colour.
        nav!.SelectedIndex = 7;
        Pump(TimeSpan.FromMilliseconds(600));
        ProgressBar? sample = EnumerateVisual<ProgressBar>(window).FirstOrDefault(b => b.IsVisible && b.ActualWidth > 100);
        if (sample != null)
        {
            foreach (double fraction in new[] { 0.4, 1.0 })
            {
                sample.Value = sample.Maximum * fraction;
                Pump(TimeSpan.FromMilliseconds(400));
                object report = Inspect(sample, 90 + (int)(fraction * 10), outDir);
                rows.Add(report);
                Console.WriteLine(JsonSerializer.Serialize(report));
            }

            sample.Value = 0;
            Pump(TimeSpan.FromMilliseconds(300));
            rows.Add(Inspect(sample, 99, outDir));
            Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
        }

        Console.WriteLine($"progress bars inspected: {rows.Count}");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private static object Inspect(ProgressBar bar, int page, string outDir)
    {
        int width = (int)Math.Ceiling(bar.ActualWidth);
        int height = (int)Math.Ceiling(bar.ActualHeight);
        int distinct = 0;
        string dominant = "-";
        string shot = string.Empty;
        if (width > 0 && height > 0)
        {
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width * 3, height * 3, 288, 288, PixelFormats.Pbgra32);
            bitmap.Render(bar);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            for (int index = 0; index + 3 < pixels.Length; index += 4)
            {
                string key = $"{pixels[index + 2]:X2}{pixels[index + 1]:X2}{pixels[index]:X2}";
                counts[key] = counts.TryGetValue(key, out int value) ? value + 1 : 1;
            }

            distinct = counts.Count;
            dominant = counts.OrderByDescending(pair => pair.Value).First().Key;
            shot = Path.Combine(outDir, $"progress-p{page}-{width}x{height}.png");
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(shot);
            encoder.Save(stream);
        }

        object? track = bar.Template?.FindName("PART_Track", bar);
        object? indicator = bar.Template?.FindName("PART_Indicator", bar);
        return new
        {
            page,
            width,
            height,
            value = bar.Value,
            maximum = bar.Maximum,
            foreground = (bar.Foreground as SolidColorBrush)?.Color.ToString(),
            background = (bar.Background as SolidColorBrush)?.Color.ToString(),
            template_target = bar.Template?.TargetType.Name,
            has_part_track = track != null,
            has_part_indicator = indicator != null,
            distinct_colours = distinct,
            flat_fill = distinct <= 2,
            dominant = dominant,
            screenshot = Path.GetFileName(shot),
        };
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
