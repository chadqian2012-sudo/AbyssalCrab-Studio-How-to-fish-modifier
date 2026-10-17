using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

/// <summary>
/// Renders the themed ProgressBar in isolation at 0 % / 40 % / 100 % so the rounded track, the
/// indicator fill and the width scaling can be inspected without the app layout interfering.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class ProgressProbe
{
    private static Application? _application;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);

        _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ResourceDictionary theme = (ResourceDictionary)System.Windows.Markup.XamlReader.Load(
            File.OpenRead(args.Length > 1 ? args[1] : "theme.xaml"));
        _application.Resources.MergedDictionaries.Add(theme);

        StackPanel panel = new StackPanel { Margin = new Thickness(16), Background = Brushes.White };
        List<(string Label, ProgressBar Bar)> bars = new List<(string, ProgressBar)>();
        foreach ((string label, double value, string brush) in new (string, double, string)[]
                 {
                     ("生命 40%", 40, "SuccessBrush"),
                     ("饱食度 100%", 100, "GoldBrush"),
                     ("属性 0%", 0, "AccentBrush"),
                     ("属性 65%", 65, "AccentBrush"),
                 })
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 6, 0, 3) });
            ProgressBar bar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = value,
                Width = 300,
            };
            bar.SetResourceReference(Control.ForegroundProperty, brush);
            bars.Add((label, bar));
            panel.Children.Add(bar);
        }

        Window window = new Window
        {
            Content = panel,
            Width = 350,
            Height = 260,
            Background = Brushes.White,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -4000,
            Top = -4000,
            ShowActivated = false,
        };
        window.Show();
        Pump(TimeSpan.FromMilliseconds(700));

        List<string> lines = new List<string>();
        Console.WriteLine($"window: visible={window.IsVisible} size={window.ActualWidth:0}x{window.ActualHeight:0}");

        // Render the whole window once and crop each bar out of it. Rendering an element on its own
        // produced an all-black bitmap here, so the window is the reliable capture surface.
        int windowWidth = (int)Math.Ceiling(window.ActualWidth);
        int windowHeight = (int)Math.Ceiling(window.ActualHeight);
        const int scale = 3;
        RenderTargetBitmap full = new RenderTargetBitmap(windowWidth * scale, windowHeight * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        full.Render(window);
        byte[] fullPixels = new byte[full.PixelWidth * full.PixelHeight * 4];
        full.CopyPixels(fullPixels, full.PixelWidth * 4, 0);
        int cleanPixels = 0;
        for (int index = 0; index + 3 < fullPixels.Length; index += 4)
        {
            if (fullPixels[index] != 0 || fullPixels[index + 1] != 0 || fullPixels[index + 2] != 0)
            {
                cleanPixels++;
            }
        }

        Console.WriteLine($"window render: {full.PixelWidth}x{full.PixelHeight} nonBlackPixels={cleanPixels}");

        foreach ((string label, ProgressBar bar) in bars)
        {
            bar.UpdateLayout();
            int width = (int)Math.Ceiling(bar.ActualWidth);
            int height = (int)Math.Ceiling(bar.ActualHeight);
            Point origin = bar.TranslatePoint(new Point(0, 0), window);
            int cropX = Math.Max(0, (int)(origin.X * scale));
            int cropY = Math.Max(0, (int)(origin.Y * scale));
            int cropW = Math.Min(width * scale, full.PixelWidth - cropX);
            int cropH = Math.Min(height * scale, full.PixelHeight - cropY);
            if (cropW <= 0 || cropH <= 0)
            {
                Console.WriteLine($"{label}: crop outside window (origin {origin.X:0.#},{origin.Y:0.#})");
                continue;
            }

            byte[] pixels = new byte[cropW * cropH * 4];
            for (int row = 0; row < cropH; row++)
            {
                Array.Copy(fullPixels, ((cropY + row) * full.PixelWidth + cropX) * 4,
                    pixels, row * cropW * 4, cropW * 4);
            }

            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index + 3 < pixels.Length; index += 4)
            {
                string key = $"{pixels[index + 2]:X2}{pixels[index + 1]:X2}{pixels[index]:X2}";
                counts[key] = counts.TryGetValue(key, out int value) ? value + 1 : 1;
            }

            // Measure the filled span on the middle scan line: how many pixels are the fill colour.
            int midRow = cropH / 2;
            string fill = ((SolidColorBrush)bar.Foreground).Color.ToString().Substring(3);
            int filled = 0;
            for (int x = 0; x < cropW; x++)
            {
                int offset = (midRow * cropW + x) * 4;
                string key = $"{pixels[offset + 2]:X2}{pixels[offset + 1]:X2}{pixels[offset]:X2}";
                if (key == fill)
                {
                    filled++;
                }
            }

            double fillRatio = cropW == 0 ? 0 : (double)filled / cropW;
            string shot = Path.Combine(outDir, $"probe-{label.Replace(" ", "-").Replace("%", "pct")}.png");
            BitmapSource crop = new CroppedBitmap(full, new Int32Rect(cropX, cropY, cropW, cropH));
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(crop));
            using (FileStream stream = File.Create(shot))
            {
                encoder.Save(stream);
            }

            string text = $"{label,-14} value={bar.Value,5:0}  distinct={counts.Count,2}  " +
                          $"fillColour={fill}  filledRatio={fillRatio:0.000}  expected≈{bar.Value/bar.Maximum:0.000}  " +
                          $"top={string.Join(",", counts.OrderByDescending(p => p.Value).Take(3).Select(p => $"{p.Key}({p.Value})"))}";
            lines.Add(text);
        }

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
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
