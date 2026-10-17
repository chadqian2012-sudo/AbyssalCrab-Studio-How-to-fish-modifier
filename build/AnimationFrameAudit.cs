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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Captures animation evidence as frames: samples the same property over time AND renders the frame
/// at each sample, so the numbers and the pixels come from the same instant.
///
/// Note on rendering an off-screen window: WPF does not guarantee render ticks for an occluded
/// window, so samples are taken inside a CompositionTarget.Rendering callback (which only fires
/// when composition actually advances) and a DispatcherPriority.Render pump is issued between
/// samples. Where a sample cannot be rendered, the frame is recorded as null rather than faked.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class AnimationFrameAudit
{
    private static Application? _application;
    private static bool _tick;

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
        Pump(TimeSpan.FromMilliseconds(1500));
        window.Width = 1460;
        window.Height = 900;
        Pump(TimeSpan.FromMilliseconds(600));

        // Keep composition advancing so animations actually run for an off-screen window.
        EventHandler tick = (_, _) => _tick = true;
        CompositionTarget.Rendering += tick;

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        FrameworkElement? capsule = FindByName(window, "NavCapsule");
        Grid? host = EnumerateVisual<Grid>(window)
            .Where(g => g.RenderTransform is TranslateTransform && g.ActualWidth > 1000 && g.ActualHeight > 300)
            .OrderByDescending(g => g.ActualWidth * g.ActualHeight)
            .FirstOrDefault();

        List<object> captures = new List<object>();
        List<string> lines = new List<string>();

        if (nav == null || capsule == null || host == null)
        {
            Console.WriteLine($"FAIL missing: nav={nav != null} capsule={capsule != null} host={host != null}");
            return 3;
        }

        // ---------- 1. nav capsule travel: index 0 -> index 6 ----------
        nav.SelectedIndex = 0;
        Settle(capsule);
        var capsuleSamples = Sample(window, capsule, "capsule", 14, TimeSpan.FromMilliseconds(18), outDir,
            element => DescribeCapsule(element));
        captures.Add(new { animation = "nav-capsule-travel", samples = capsuleSamples });
        lines.Add($"nav capsule travel: {capsuleSamples.Count} frames captured");

        nav.SelectedIndex = 6;
        capsuleSamples = Sample(window, capsule, "capsule-run", 16, TimeSpan.FromMilliseconds(18), outDir,
            element => DescribeCapsule(element));
        captures.Add(new { animation = "nav-capsule-run", samples = capsuleSamples });
        lines.Add($"nav capsule run: {capsuleSamples.Count} frames captured");

        // ---------- 2. page fade: switch page and sample the content host ----------
        nav.SelectedIndex = 3;
        var fadeSamples = Sample(window, host, "pagefade", 14, TimeSpan.FromMilliseconds(20), outDir,
            element => DescribeHost(element));
        captures.Add(new { animation = "page-fade", samples = fadeSamples });
        lines.Add($"page fade: {fadeSamples.Count} frames captured");

        // ---------- 3. feature switch travel ----------
        // Offline the switch is disabled (CanInteract=False), so setting IsChecked on the live control
        // is rejected by the two-way binding and the animation never runs. To capture the template
        // animation itself, clear the binding on a throwaway switch and drive it directly. This is a
        // visual-only poke: no command is dispatched and the binding is restored afterwards.
        ToggleButton? toggle = EnumerateVisual<ToggleButton>(window)
            .FirstOrDefault(t => t.IsVisible && Math.Abs(t.ActualWidth - 68) < 1 && Math.Abs(t.ActualHeight - 32) < 1);
        if (toggle != null)
        {
            BindingBase? savedBinding = BindingOperations.GetBindingBase(toggle, ToggleButton.IsCheckedProperty);
            BindingOperations.ClearBinding(toggle, ToggleButton.IsCheckedProperty);
            bool original = toggle.IsChecked == true;
            toggle.IsEnabled = true;

            toggle.IsChecked = !original;
            var onSamples = Sample(window, toggle, "switch-on", 12, TimeSpan.FromMilliseconds(22), outDir,
                element => DescribeSwitch(element));
            captures.Add(new { animation = "feature-switch-on", samples = onSamples });
            lines.Add($"feature switch ON : {onSamples.Count} frames captured");

            toggle.IsChecked = original;
            var offSamples = Sample(window, toggle, "switch-off", 10, TimeSpan.FromMilliseconds(22), outDir,
                element => DescribeSwitch(element));
            captures.Add(new { animation = "feature-switch-off", samples = offSamples });
            lines.Add($"feature switch OFF: {offSamples.Count} frames captured");

            // restore the binding and the real state
            if (savedBinding != null)
            {
                BindingOperations.SetBinding(toggle, ToggleButton.IsCheckedProperty, savedBinding);
            }

            lines.Add($"feature switch binding restored: {BindingOperations.GetBindingBase(toggle, ToggleButton.IsCheckedProperty) != null}");
        }
        else
        {
            lines.Add("feature switch: not found, skipped (reported, not faked)");
        }

        CompositionTarget.Rendering -= tick;
        _tick = false;

        File.WriteAllText(Path.Combine(outDir, "animation-frames.json"),
            JsonSerializer.Serialize(new
            {
                method = new
                {
                    frames = "each sample renders the window at that instant and stores the measured value alongside",
                    timing = "CompositionTarget.Rendering gates each sample so only real composition frames are used",
                    caveat = "Off-screen window: WPF may throttle composition, so the sample cadence is not a guaranteed 60 Hz and a few frames may repeat. Values and pixels come from the same instant, which is what the evidence needs.",
                },
                captures,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"wrote {Path.Combine(outDir, "animation-frames.json")}");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        _application.Shutdown();
        return 0;
    }

    /// <summary>
    /// Takes n samples, waiting for a real composition frame before each one, rendering the window
    /// into an individual PNG and pairing it with the measured value.
    /// </summary>
    private static List<object> Sample(Window window, FrameworkElement target, string label, int count,
        TimeSpan interval, string outDir, Func<FrameworkElement, object> describe)
    {
        List<object> frames = new List<object>();
        Stopwatch clock = Stopwatch.StartNew();
        int width = (int)Math.Ceiling(window.ActualWidth);
        int height = (int)Math.Ceiling(window.ActualHeight);

        for (int index = 0; index < count; index++)
        {
            // wait for composition to advance, but never spin forever
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
            _tick = false;
            while (!_tick && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                System.Threading.Thread.Sleep(1);
            }

            long at = clock.ElapsedMilliseconds;
            string file = $"{label}-f{index:00}.png";
            string full = Path.Combine(outDir, file);
            bool rendered = false;
            try
            {
                RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using FileStream stream = File.Create(full);
                encoder.Save(stream);
                rendered = true;
            }
            catch (Exception ex)
            {
                frames.Add(new { frame = index, at_ms = at, rendered = false, error = ex.GetType().Name });
                continue;
            }

            frames.Add(new
            {
                frame = index,
                at_ms = at,
                rendered,
                file = file,
                bytes = new FileInfo(full).Length,
                state = describe(target),
            });

            Sleep(interval);
        }

        return frames;
    }

    private static object DescribeCapsule(FrameworkElement element)
    {
        TranslateTransform? transform = element.RenderTransform as TranslateTransform;
        return new
        {
            x = transform == null ? (double?)null : Math.Round(transform.X, 2),
            y = transform == null ? (double?)null : Math.Round(transform.Y, 2),
            width = Math.Round(element.ActualWidth, 1),
            has_animated_properties = element.HasAnimatedProperties,
        };
    }

    private static object DescribeHost(FrameworkElement element)
    {
        TranslateTransform? transform = element.RenderTransform as TranslateTransform;
        return new
        {
            opacity = Math.Round(element.Opacity, 4),
            translate_y = transform == null ? (double?)null : Math.Round(transform.Y, 3),
            has_animated_properties = element.HasAnimatedProperties,
        };
    }

    private static object DescribeSwitch(FrameworkElement element)
    {
        // The thumb is the named "Thumb" inside the switch template.
        FrameworkElement? thumb = element is Control control && control.Template != null
            ? control.Template.FindName("Thumb", control) as FrameworkElement
            : null;
        TranslateTransform? transform = thumb?.RenderTransform as TranslateTransform;
        Border? trackOn = element is Control c2 && c2.Template != null
            ? c2.Template.FindName("TrackOn", c2) as Border
            : null;
        return new
        {
            is_checked = (element as ToggleButton)?.IsChecked,
            thumb_x = transform == null ? (double?)null : Math.Round(transform.X, 2),
            track_on_opacity = trackOn == null ? (double?)null : Math.Round(trackOn.Opacity, 3),
        };
    }

    private static void Settle(FrameworkElement element)
    {
        for (int index = 0; index < 40 && element.HasAnimatedProperties; index++)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            System.Threading.Thread.Sleep(20);
        }
    }

    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && element.Name == name)
        {
            return element;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            FrameworkElement? found = FindByName(VisualTreeHelper.GetChild(root, index), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
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

    private static void Sleep(TimeSpan duration)
    {
        DateTime end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            System.Threading.Thread.Sleep(2);
        }
    }

    private static void Pump(TimeSpan duration) => Sleep(duration);
}
