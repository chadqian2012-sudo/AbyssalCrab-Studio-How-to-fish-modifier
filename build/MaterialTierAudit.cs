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
/// Verifies all three material-strength tiers: each dictionary loads through the URI the app uses,
/// each overrides the glass tokens, and - critically - each produces a DIFFERENT rendered window.
/// Renders the same window three times with only the material dictionary swapped, then compares.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class MaterialTierAudit
{
    private static Application? _application;

    private static readonly (string Name, string Path)[] Tiers =
    {
        ("Solid", "themes/MaterialSolid.xaml"),
        ("Tinted", "themes/MaterialTinted.xaml"),
        ("Clear", "themes/MaterialClear.xaml"),
    };

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
        ResourceDictionary theme = (ResourceDictionary)Application.LoadComponent(
            new Uri("/HowToFishTrainer;component/themes/theme.xaml", UriKind.Relative));
        _application.Resources.MergedDictionaries.Add(theme);

        Type licenseInterface = trainer.GetType("HowToFishTrainer.App.Licensing.ILicenseClient", true)!;
        Type licenseStatus = trainer.GetType("HowToFishTrainer.App.Licensing.LicenseStatus", true)!;
        object fake = FakeLicenseClient.Create(licenseInterface, licenseStatus, sponsor: true);
        Window window = (Window)Activator.CreateInstance(trainer.GetType("HowToFishTrainer.App.MainWindow", true)!, new[] { fake })!;
        (window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as DispatcherTimer)?.Stop();
        window.Left = -4000;
        window.Top = -4000;
        window.Show();
        Pump(TimeSpan.FromMilliseconds(1500));

        List<object> rows = new List<object>();
        List<string> lines = new List<string>();
        Dictionary<string, string> signatureByTier = new Dictionary<string, string>(StringComparer.Ordinal);

        int width = (int)Math.Ceiling(window.ActualWidth);
        int height = (int)Math.Ceiling(window.ActualHeight);

        foreach ((string name, string path) in Tiers)
        {
            // Swap in exactly one material dictionary, the same way App.ApplyMaterialDictionary does.
            for (int index = _application.Resources.MergedDictionaries.Count - 1; index >= 0; index--)
            {
                Uri? source = _application.Resources.MergedDictionaries[index].Source;
                if (source != null && source.OriginalString.Contains("Material", StringComparison.Ordinal))
                {
                    _application.Resources.MergedDictionaries.RemoveAt(index);
                }
            }

            bool loaded = true;
            string error = string.Empty;
            try
            {
                _application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/HowToFishTrainer;component/" + path, UriKind.Relative)
                });
            }
            catch (Exception ex)
            {
                loaded = false;
                error = ex.GetType().Name + ": " + ex.Message;
            }

            Pump(TimeSpan.FromMilliseconds(700));

            string canvas = Describe(_application.Resources.Contains("GlassCanvasBrush") ? _application.Resources["GlassCanvasBrush"] : null);
            string card = Describe(_application.Resources.Contains("GlassCardBrush") ? _application.Resources["GlassCardBrush"] : null);

            // Render and take a cheap signature: the pixel at a canvas point plus a colour histogram hash.
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            string shot = Path.Combine(outDir, $"material-tier-{name}.png");
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(shot))
            {
                encoder.Save(stream);
            }

            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);

            // Sample a canvas point that is not covered by a card: bottom-right margin.
            int sampleX = Math.Max(0, width - 12);
            int sampleY = Math.Max(0, height - 60);
            int offset = (sampleY * width + sampleX) * 4;
            string canvasPixel = $"{pixels[offset + 2]:X2}{pixels[offset + 1]:X2}{pixels[offset]:X2}";

            long hash = 17;
            for (int index = 0; index < pixels.Length; index += 997)
            {
                hash = hash * 31 + pixels[index];
            }

            string signature = $"{canvasPixel}|{hash}";
            signatureByTier[name] = signature;

            rows.Add(new
            {
                tier = name,
                dictionary = path,
                loaded,
                error,
                canvas_token = canvas,
                card_token = card,
                canvas_pixel = canvasPixel,
                render_signature = hash,
                file = Path.GetFileName(shot),
                bytes = new FileInfo(shot).Length,
            });
            lines.Add($"{name,-7} loaded={loaded} canvas={canvas} card={card}");
            lines.Add($"        canvasPixel=#{canvasPixel} signature={hash} file={Path.GetFileName(shot)} ({new FileInfo(shot).Length} bytes)");
        }

        // All three must be visually distinct, otherwise the menu would be a no-op.
        int distinct = signatureByTier.Values.Distinct().Count();
        bool allLoaded = rows.All(r => (bool)r.GetType().GetProperty("loaded")!.GetValue(r)!);
        bool passed = allLoaded && distinct == Tiers.Length;

        lines.Add($"distinct renders: {distinct}/{Tiers.Length} " +
                  (distinct == Tiers.Length ? "- the three tiers really differ" : "<-- tiers render identically"));

        File.WriteAllText(Path.Combine(outDir, "material-tier-audit.json"),
            JsonSerializer.Serialize(new
            {
                tiers = rows,
                distinct_renders = distinct,
                signatures = signatureByTier,
                passed,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"verdict: {(passed ? "PASS" : "FAIL")} (loaded {rows.Count}/{Tiers.Length}, distinct {distinct}/{Tiers.Length})");

        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        _application.Shutdown();
        return passed ? 0 : 1;
    }

    private static string Describe(object? value)
    {
        return value switch
        {
            null => "<missing>",
            SolidColorBrush solid => solid.Color.ToString(),
            GradientBrush => "<gradient>",
            _ => value.GetType().Name,
        };
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
