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
/// Verifies the solid-material degradation tier end to end:
///   1. MaterialSolid.xaml loads through the URI App.xaml.cs uses;
///   2. merging it actually overrides the Glass* tokens (gradients -> opaque solids);
///   3. a real MainWindow still constructs with the degraded materials and the glass layers
///      resolve to the solid brushes (this is what DynamicResource buys us);
///   4. colour/radius/business tokens are untouched.
/// </summary>
internal static class MaterialFallbackAudit
{
    private static readonly string[] GlassTokens =
    {
        "GlassCanvasBrush", "GlassChromeBrush", "GlassNavBrush", "GlassCardBrush", "GlassNavCapsuleBrush",
        "GlassEdgeHBrush", "GlassEdgeVBrush", "GlassEdgeChromeBrush", "GlassHaloAccentBrush", "GlassHaloWarmBrush",
    };

    private static readonly string[] MustNotChange =
    {
        "AccentBrush", "AccentDarkBrush", "AccentSoftBrush", "AccentWashBrush", "AccentLineBrush",
        "BgBrush", "PanelBrush", "CardBrush", "TextBrush", "MutedBrush", "LineBrush", "FieldLineBrush",
        "RadiusCard", "RadiusWindow", "MotionPage", "MotionSelect",
    };

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

        ResourceDictionary theme = (ResourceDictionary)Application.LoadComponent(
            new Uri("/HowToFishTrainer;component/themes/theme.xaml", UriKind.Relative));
        _application.Resources.MergedDictionaries.Add(theme);

        List<string> lines = new List<string>();

        // 1) does the solid dictionary load?
        ResourceDictionary? solid = null;
        string loadError = string.Empty;
        try
        {
            solid = new ResourceDictionary
            {
                Source = new Uri("/HowToFishTrainer;component/themes/MaterialSolid.xaml", UriKind.Relative)
            };
        }
        catch (Exception ex)
        {
            loadError = ex.GetType().Name + ": " + ex.Message;
        }

        bool loaded = solid != null;
        lines.Add($"MaterialSolid.xaml loads via the App.xaml.cs URI: {loaded}" + (loaded ? string.Empty : $"  ({loadError})"));
        if (!loaded)
        {
            return 1;
        }

        Dictionary<string, string> before = GlassTokens.ToDictionary(t => t, t => Describe(_application.Resources.Contains(t) ? _application.Resources[t] : null));
        _application.Resources.MergedDictionaries.Add(solid);
        Dictionary<string, string> after = GlassTokens.ToDictionary(t => t, t => Describe(_application.Resources[t]));

        int overridden = 0;
        foreach (string token in GlassTokens)
        {
            bool changed = before[token] != after[token];
            bool nowSolid = after[token].StartsWith("#", StringComparison.Ordinal);
            if (changed || nowSolid)
            {
                overridden++;
            }

            lines.Add($"   {token,-22} {before[token],-12} -> {after[token],-12} " +
                      (nowSolid && (changed || before[token] == "<gradient>") ? "solid" : changed ? "changed" : "unchanged"));
        }

        // 2) colour / radius / motion tokens must be untouched
        List<string> altered = new List<string>();
        foreach (string key in MustNotChange)
        {
            object? value = theme.Contains(key) ? theme[key] : null;
            object? merged = _application.Resources.Contains(key) ? _application.Resources[key] : null;
            if (Describe(value) != Describe(merged))
            {
                altered.Add($"{key}: {Describe(value)} -> {Describe(merged)}");
            }
        }

        lines.Add($"non-material tokens altered: {altered.Count}" + (altered.Count == 0 ? " (correct)" : "  <-- WRONG: " + string.Join("; ", altered)));

        // 3) a real window must still build with degraded materials, and its glass layers must
        //    resolve through DynamicResource to the solid brushes.
        string windowBrush = "not built";
        bool windowBuilt = false;
        int solidCanvasPixels = 0;
        try
        {
            Type licenseInterface = trainer.GetType("HowToFishTrainer.App.Licensing.ILicenseClient", true)!;
            Type licenseStatus = trainer.GetType("HowToFishTrainer.App.Licensing.LicenseStatus", true)!;
            object fake = FakeLicenseClient.Create(licenseInterface, licenseStatus, sponsor: true);
            Window window = (Window)Activator.CreateInstance(trainer.GetType("HowToFishTrainer.App.MainWindow", true)!, new[] { fake })!;
            (window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as DispatcherTimer)?.Stop();
            window.Left = -4000;
            window.Top = -4000;
            window.Show();
            Pump(TimeSpan.FromMilliseconds(1400));
            windowBuilt = true;

            // The window root Border carries GlassCanvasBrush via DynamicResource.
            Border? surface = FindWindowSurface(window);
            windowBrush = surface == null ? "surface not found" : Describe(surface.Background);

            // Render it so the degraded material path is exercised for real.
            int width = (int)Math.Ceiling(window.ActualWidth);
            int height = (int)Math.Ceiling(window.ActualHeight);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            string shot = Path.Combine(outDir, "material-solid-window.png");
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(shot))
            {
                encoder.Save(stream);
            }

            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            // Count how much of the render is the solid canvas colour. This is the real proof that
            // the DynamicResource swap reached the visual tree: on the glass path the canvas is a
            // gradient and this exact colour would appear on 0 pixels.
            string canvasHex = "EEF3F6";
            int canvasPixels = 0;
            for (int index = 0; index + 3 < pixels.Length; index += 4)
            {
                if (pixels[index + 2] == 0xEE && pixels[index + 1] == 0xF3 && pixels[index] == 0xF6)
                {
                    canvasPixels++;
                }
            }

            solidCanvasPixels = canvasPixels;
            lines.Add($"window built with degraded materials: {windowBuilt}; window surface border brush = {windowBrush}");
            lines.Add($"rendered {width}x{height}; solid canvas #{canvasHex} covers {canvasPixels:N0} px " +
                      $"({100.0 * canvasPixels / (pixels.Length / 4):0.#}% of the window)");
            window.Close();
            Pump(TimeSpan.FromMilliseconds(200));
        }
        catch (Exception ex)
        {
            lines.Add($"WINDOW BUILD FAILED with degraded materials: {ex.GetType().Name}: {ex.Message}");
        }

        bool canvasIsSolid = windowBrush.StartsWith("#", StringComparison.Ordinal);
        // The canvas must genuinely be the solid colour over a large area, not just the token table.
        bool passed = loaded && overridden == GlassTokens.Length && altered.Count == 0 && windowBuilt
                      && canvasIsSolid && solidCanvasPixels > 50000;

        File.WriteAllText(Path.Combine(outDir, "material-fallback-audit.json"),
            JsonSerializer.Serialize(new
            {
                loaded_at_runtime = loaded,
                load_error = loadError,
                tokens_overridden = overridden,
                tokens_total = GlassTokens.Length,
                tokens = GlassTokens.Select(t => new { token = t, before = before[t], after = after[t] }).ToList(),
                non_material_tokens_altered = altered,
                window_built_with_degraded_materials = windowBuilt,
                window_surface_brush = windowBrush,
                solid_canvas_pixels = solidCanvasPixels,
                passed,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        lines.Add($"verdict: {(passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"verdict: {(passed ? "PASS" : "FAIL")} (overridden {overridden}/{GlassTokens.Length}, side effects {altered.Count}, window {windowBuilt})");

        _application.Shutdown();
        return passed ? 0 : 1;
    }

    private static Border? FindWindowSurface(DependencyObject root)
    {
        // The root content Border is the one carrying the window surface style (RoundedWindowSurface):
        // it spans the whole window and has a non-zero BorderThickness. Do NOT match on size alone -
        // the page host Grid and other panels also fill the window and would match first.
        return EnumerateVisual<Border>(root)
            .Where(b => b.ActualWidth > 1000 && b.ActualHeight > 700 && b.BorderThickness.Left > 0)
            .OrderByDescending(b => b.ActualWidth * b.ActualHeight)
            .FirstOrDefault();
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

    private static string Describe(object? value)
    {
        return value switch
        {
            null => "<missing>",
            SolidColorBrush solid => solid.Color.ToString(),
            GradientBrush => "<gradient>",
            CornerRadius radius => radius.TopLeft.ToString("0.#"),
            Duration duration => duration.HasTimeSpan ? duration.TimeSpan.ToString() : "<auto>",
            _ => value.GetType().Name,
        };
    }

    private static void Pump(TimeSpan duration)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            System.Threading.Thread.Sleep(8);
        }
    }
}
