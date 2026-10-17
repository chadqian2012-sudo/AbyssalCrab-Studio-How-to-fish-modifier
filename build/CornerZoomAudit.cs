using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Magnifies one feature card corner so a detached edge highlight or a corner-clipping artefact can
/// be judged visually instead of argued about. Renders the whole window once and crops, because
/// rendering a child element on its own produces an all-black bitmap in this codebase.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class CornerZoomAudit
{
    private static Application? _application;

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string assemblyDir = args.Length > 0 ? args[0] : ".";
        string outDir = args.Length > 1 ? args[1] : assemblyDir;
        int zoom = args.Length > 2 && int.TryParse(args[2], out int parsed) ? parsed : 6;
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
        Pump(TimeSpan.FromMilliseconds(1500));
        window.Width = 1460;
        window.Height = 900;
        Pump(TimeSpan.FromMilliseconds(600));

        int width = (int)Math.Ceiling(window.ActualWidth);
        int height = (int)Math.Ceiling(window.ActualHeight);
        RenderTargetBitmap full = new RenderTargetBitmap(width * zoom, height * zoom, 96 * zoom, 96 * zoom, PixelFormats.Pbgra32);
        full.Render(window);

        // Find a feature card: identified by the style value it resolves (MinHeight 132), not by
        // tree order - the window's own rounded surface is also a rounded Border.
        List<Border> cards = EnumerateVisual<Border>(window)
            .Where(b => b.IsVisible && b.ActualWidth > 300 && b.ActualHeight > 90
                        && Math.Abs(b.MinHeight - 132) < 0.01)
            .OrderBy(b => b.TranslatePoint(new Point(0, 0), window).Y)
            .ThenBy(b => b.TranslatePoint(new Point(0, 0), window).X)
            .ToList();

        Console.WriteLine($"cards found: {cards.Count}");
        if (cards.Count == 0)
        {
            window.Close();
            return 3;
        }

        Border card = cards[0];
        Point origin = card.TranslatePoint(new Point(0, 0), window);
        Console.WriteLine($"first card: {card.ActualWidth:0.#}x{card.ActualHeight:0.#} at {origin.X:0.#},{origin.Y:0.#} " +
                          $"radius={card.CornerRadius.TopLeft:0.#} border={card.BorderThickness} minH={card.MinHeight:0.#}");

        // Top-left corner and bottom-right corner of that card.
        Dump(full, outDir, "corner-card-topleft", origin.X, origin.Y, 34, 34, zoom);
        Dump(full, outDir, "corner-card-bottomright", origin.X + card.ActualWidth - 34, origin.Y + card.ActualHeight - 34, 34, 34, zoom);

        // Window's own top-left rounded corner.
        Dump(full, outDir, "corner-window-topleft", 0, 0, 34, 34, zoom);

        // A full-width horizontal slice through the nav band, to judge the glass band edges.
        Dump(full, outDir, "slice-nav-band", 0, 60, Math.Min(240, width), 56, Math.Min(zoom, 4));

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private static void Dump(RenderTargetBitmap full, string outDir, string name, double x, double y, double w, double h, int zoom)
    {
        int cx = Math.Max(0, (int)Math.Round(x * zoom));
        int cy = Math.Max(0, (int)Math.Round(y * zoom));
        int cw = Math.Min((int)Math.Round(w * zoom), full.PixelWidth - cx);
        int ch = Math.Min((int)Math.Round(h * zoom), full.PixelHeight - cy);
        if (cw <= 1 || ch <= 1)
        {
            Console.WriteLine($"  {name}: crop outside bitmap (x={cx} y={cy} w={cw} h={ch})");
            return;
        }

        CroppedBitmap crop = new CroppedBitmap(full, new Int32Rect(cx, cy, cw, ch));
        string file = Path.Combine(outDir, $"{name}.png");
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(crop));
        using (FileStream stream = File.Create(file))
        {
            encoder.Save(stream);
        }

        // Report the extreme colours in the crop so "is the highlight visible" has a number.
        byte[] pixels = new byte[cw * ch * 4];
        crop.CopyPixels(pixels, cw * 4, 0);
        int lightest = 0;
        int darkest = 255 * 3;
        string darkestKey = "-";
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            int sum = pixels[index] + pixels[index + 1] + pixels[index + 2];
            if (sum > lightest) lightest = sum;
            if (sum < darkest)
            {
                darkest = sum;
                darkestKey = $"{pixels[index + 2]:X2}{pixels[index + 1]:X2}{pixels[index]:X2}";
            }
        }

        Console.WriteLine($"  {name}: {cw}x{ch} bytes={new FileInfo(file).Length} " +
                          $"lightestSum={lightest} darkestSum={darkest} darkest={darkestKey}");
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
