using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Measures the UI performance envelope the hand-off asks for, on the real window:
///   · idle repaint: whether the app keeps rendering frames while nothing happens
///   · page-switch cost: UI-thread time for a switch, over several switches
///   · visual-effect budget: how many drop shadows / bitmaps effects / blur effects are live
///   · which side of the degradation thresholds this machine reports
/// Records GPU tier, DPI and OS so the numbers can be compared later.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class PerfAudit
{
    private static Application? _application;
    private static int _frameCount;
    private static readonly EventHandler FrameCounter = (_, _) => _frameCount++;

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
        List<string> lines = new List<string>();

        // ---- environment -------------------------------------------------------------------
        int rawTier = RenderCapability.Tier;
        string os = Environment.OSVersion.VersionString;
        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        lines.Add($"environment: OS={os} tier={rawTier >> 16} (raw {rawTier}) " +
                  $"dpi={dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##} " +
                  $"clientAreaAnimation={SystemParameters.ClientAreaAnimation} " +
                  $"processRenderMode={RenderOptions.ProcessRenderMode} " +
                  $"cores={Environment.ProcessorCount}");

        // ---- idle repaint ------------------------------------------------------------------
        // Count CompositionTarget.Rendering ticks while the window is left completely alone. A UI
        // that keeps animating or invalidating forever will keep ticking; an idle one should not.
        // Measure twice: a shorter early window and a later one, because one-shot animations
        // (focus rings, page fade, caret) legitimately still run during the first second.
        List<object> idlePasses = new List<object>();
        foreach ((string label, int milliseconds) in new (string, int)[] { ("early", 2000), ("late", 3000) })
        {
            Pump(TimeSpan.FromMilliseconds(1200));
            _frameCount = 0;
            CompositionTarget.Rendering += FrameCounter;
            Stopwatch idleWatch = Stopwatch.StartNew();
            Pump(TimeSpan.FromMilliseconds(milliseconds));
            idleWatch.Stop();
            CompositionTarget.Rendering -= FrameCounter;
            double fps = _frameCount / idleWatch.Elapsed.TotalSeconds;
            idlePasses.Add(new
            {
                pass = label,
                seconds = milliseconds / 1000.0,
                render_callbacks = _frameCount,
                callbacks_per_second = Math.Round(fps, 1),
                idle_repaint = _frameCount > 0,
            });
            lines.Add($"idle[{label}] {milliseconds / 1000.0:0.#}s: {_frameCount} render callbacks ({fps:0.#}/s) " +
                      (_frameCount == 0 ? "- no idle repaint" : "- repainting"));
        }

        // Who is still holding an animation clock while idle?
        Pump(TimeSpan.FromMilliseconds(500));
        List<string> idleClockHolders = EnumerateVisual<FrameworkElement>(window)
            .Where(e => e.HasAnimatedProperties)
            .Select(e => $"{e.GetType().Name}#{(string.IsNullOrEmpty(e.Name) ? "-" : e.Name)}")
            .GroupBy(s => s)
            .Select(g => $"{g.Key} x{g.Count()}")
            .OrderByDescending(s => s)
            .ToList();
        lines.Add($"idle clock holders: {(idleClockHolders.Count == 0 ? "none" : string.Join(", ", idleClockHolders))}");

        // ---- page switch cost --------------------------------------------------------------
        List<double> switchMs = new List<double>();
        List<int> switchFrames = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            int target = (i % 10) + 1;
            _frameCount = 0;
            CompositionTarget.Rendering += FrameCounter;
            Stopwatch watch = Stopwatch.StartNew();
            nav!.SelectedIndex = target;
            // let the page fade (MotionPage 210ms) complete
            Pump(TimeSpan.FromMilliseconds(320));
            watch.Stop();
            CompositionTarget.Rendering -= FrameCounter;
            switchMs.Add(watch.Elapsed.TotalMilliseconds);
            switchFrames.Add(_frameCount);
        }

        double meanMs = switchMs.Average();
        double maxMs = switchMs.Max();
        double meanFrames = switchFrames.Average();
        double impliedFrameMs = meanFrames > 0 ? meanMs / meanFrames : double.NaN;
        lines.Add($"page switch x8: wall mean={meanMs:0.#}ms max={maxMs:0.#}ms; " +
                  $"render callbacks mean={meanFrames:0.#} -> {impliedFrameMs:0.#}ms per rendered frame " +
                  (impliedFrameMs < 16.7 ? "(within a 60 FPS budget)" : "(above a 60 FPS budget)"));

        // ---- page scroll cost --------------------------------------------------------------
        ScrollViewer? scroller = FindScrollViewer(window);
        double scrollMs = double.NaN;
        if (scroller != null)
        {
            Stopwatch watch = Stopwatch.StartNew();
            for (int step = 0; step < 20; step++)
            {
                scroller.ScrollToVerticalOffset(scroller.ScrollableHeight * step / 20.0);
                Pump(TimeSpan.FromMilliseconds(16));
            }

            watch.Stop();
            scrollMs = watch.Elapsed.TotalMilliseconds / 20;
            lines.Add($"scroll: 20 steps, {scrollMs:0.#}ms per step (includes a 16ms pump, so the " +
                      $"UI work per step is about {Math.Max(0, scrollMs - 16):0.#}ms)");
        }

        // ---- visual effect budget ----------------------------------------------------------
        List<FrameworkElement> elements = EnumerateVisual<FrameworkElement>(window).ToList();
        int shadows = 0;
        int blurs = 0;
        int otherEffects = 0;
        foreach (FrameworkElement element in elements)
        {
            switch (element.Effect)
            {
                case DropShadowEffect:
                    shadows++;
                    break;
                case BlurEffect:
                    blurs++;
                    break;
                case null:
                    break;
                default:
                    otherEffects++;
                    break;
            }
        }

        lines.Add($"visual tree: {elements.Count} elements; DropShadowEffect={shadows} BlurEffect={blurs} other={otherEffects}");
        lines.Add($"observations: VisualBrush={CountResources(window, typeof(VisualBrush))} " +
                  $"SolidColorBrush={CountResources(window, typeof(SolidColorBrush))} " +
                  $"GradientBrush={CountResources(window, typeof(GradientBrush))}");

        var report = new
        {
            environment = new
            {
                os,
                tier = rawTier >> 16,
                tier_raw = rawTier,
                dpi_scale = dpi.DpiScaleX,
                client_area_animation = SystemParameters.ClientAreaAnimation,
                process_render_mode = RenderOptions.ProcessRenderMode.ToString(),
                logical_cores = Environment.ProcessorCount,
            },
            idle = idlePasses,
            idle_clock_holders = idleClockHolders,
            page_switch = new
            {
                samples = switchMs.Count,
                wall_ms_mean = Math.Round(meanMs, 1),
                wall_ms_max = Math.Round(maxMs, 1),
                render_callbacks_mean = Math.Round(meanFrames, 1),
                implied_ms_per_rendered_frame = double.IsNaN(impliedFrameMs) ? 0 : Math.Round(impliedFrameMs, 2),
                within_60fps_budget = impliedFrameMs < 16.7,
            },
            scroll = new { steps = 20, ms_per_step_including_16ms_pump = double.IsNaN(scrollMs) ? 0 : Math.Round(scrollMs, 1) },
            effects = new
            {
                elements = elements.Count,
                drop_shadow = shadows,
                blur = blurs,
                other = otherEffects,
                visual_brush_resources = CountResources(window, typeof(VisualBrush)),
                gradient_brush_resources = CountResources(window, typeof(GradientBrush)),
            },
            method = new
            {
                timing = "Stopwatch around dispatcher pumping; frame count via CompositionTarget.Rendering",
                caveat = "Off-screen window: render callbacks may be throttled, so treat wall-clock figures as an upper bound and frame counts as indicative.",
            },
        };

        File.WriteAllText(Path.Combine(outDir, "perf-audit.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        _application.Shutdown();
        return 0;
    }

    private static int CountResources(DependencyObject root, Type brushType)
    {
        int count = 0;
        foreach (FrameworkElement element in EnumerateVisual<FrameworkElement>(root))
        {
            if (element is Control control && control.Background != null && brushType.IsInstanceOfType(control.Background))
            {
                count++;
            }

            if (element is Border border && border.Background != null && brushType.IsInstanceOfType(border.Background))
            {
                count++;
            }

            if (element is Panel panel && panel.Background != null && brushType.IsInstanceOfType(panel.Background))
            {
                count++;
            }
        }

        return count;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        return EnumerateVisual<ScrollViewer>(root)
            .Where(v => v.IsVisible && v.ScrollableHeight > 1)
            .OrderByDescending(v => v.ActualHeight)
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

    private static void Pump(TimeSpan duration)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            System.Threading.Thread.Sleep(4);
        }
    }
}
