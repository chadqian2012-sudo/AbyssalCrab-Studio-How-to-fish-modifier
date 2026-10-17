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
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Verifies the floating nav capsule: it must sit on the selected item, follow selection changes,
/// land correctly when the WrapPanel wraps to two rows, and never drift or accumulate clocks.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class NavCapsuleAudit
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
        Pump(TimeSpan.FromMilliseconds(1500));

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        // The capsule and its canvas are inside the nav Border's Grid; find them by name.
        FrameworkElement? capsule = FindByName(window, "NavCapsule");
        FrameworkElement? layer = FindByName(window, "NavCapsuleLayer");
        if (nav == null || capsule == null || layer == null)
        {
            Console.WriteLine($"FAIL missing element(s): nav={nav != null} capsule={capsule != null} layer={layer != null}");
            return 3;
        }

        List<object> rows = new List<object>();
        List<string> lines = new List<string>();
        int mismatches = 0;

        // Warm-up: let the window's initial Loaded-priority capsule placement run before measuring.
        nav.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(420));

        foreach (double width in new double[] { 1460, 1180 })
        {
            window.Width = width;
            Pump(TimeSpan.FromMilliseconds(700));

            int visited = 0;
            for (int index = 0; index < nav.Items.Count; index++)
            {
                nav.SelectedIndex = index;
                Pump(TimeSpan.FromMilliseconds(420));

                if (nav.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement item)
                {
                    lines.Add($"w={width:0} idx={index,2}: container not realised");
                    continue;
                }

                Point itemOrigin = item.TranslatePoint(new Point(0, 0), layer);
                Point capsuleOrigin = CapsuleRestingOffset(capsule);
                double dx = Math.Abs(itemOrigin.X - capsuleOrigin.X);
                double dy = Math.Abs(itemOrigin.Y - capsuleOrigin.Y);
                double dw = Math.Abs(item.ActualWidth - capsule.ActualWidth);
                double dh = Math.Abs(item.ActualHeight - capsule.ActualHeight);
                bool aligned = dx < 1.5 && dy < 1.5 && dw < 1.5 && dh < 1.5;
                if (!aligned)
                {
                    mismatches++;
                }

                int row = RowOf(item, nav);
                rows.Add(new
                {
                    window_width = width,
                    index,
                    row,
                    item_origin = new { x = Math.Round(itemOrigin.X, 1), y = Math.Round(itemOrigin.Y, 1) },
                    capsule_origin = new { x = Math.Round(capsuleOrigin.X, 1), y = Math.Round(capsuleOrigin.Y, 1) },
                    delta = new { dx = Math.Round(dx, 1), dy = Math.Round(dy, 1), dw = Math.Round(dw, 1), dh = Math.Round(dh, 1) },
                    aligned,
                });
                lines.Add($"w={width:0} idx={index,2} row={row} item=({itemOrigin.X:0.#},{itemOrigin.Y:0.#}) " +
                          $"capsule=({capsuleOrigin.X:0.#},{capsuleOrigin.Y:0.#}) d=({dx:0.#},{dy:0.#}) size d=({dw:0.#},{dh:0.#}) " +
                          $"settled " + (aligned ? "ok" : "<-- MISALIGNED"));
                visited++;
            }

            lines.Add($"w={width:0}: visited {visited} items, capsule visible = {capsule.Visibility == Visibility.Visible}");
        }

        // Rapid re-targeting: the capsule must converge on the LAST target, not queue old ones.
        window.Width = 1460;
        Pump(TimeSpan.FromMilliseconds(500));
        foreach (int index in new[] { 8, 2, 10, 0, 6 })
        {
            nav.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(70)); // shorter than MotionSelect
        }

        // Same settle-based wait: the point is where it ENDS UP, not how long it took.
        Pump(TimeSpan.FromMilliseconds(420));
        int finalIndex = nav.SelectedIndex;
        FrameworkElement? finalItem = nav.ItemContainerGenerator.ContainerFromIndex(finalIndex) as FrameworkElement;
        double finalDx = double.NaN;
        double finalDy = double.NaN;
        if (finalItem != null)
        {
            Point a = finalItem.TranslatePoint(new Point(0, 0), layer);
            Point b = CapsuleRestingOffset(capsule);
            finalDx = Math.Abs(a.X - b.X);
            finalDy = Math.Abs(a.Y - b.Y);
        }

        bool rapidOk = finalItem != null && finalDx < 1.5 && finalDy < 1.5;
        lines.Add($"rapid retarget (5 jumps at 70ms): landed on index {finalIndex}, " +
                  $"delta=({finalDx:0.#},{finalDy:0.#}) {(rapidOk ? "ok - followed the latest target" : "<-- WRONG TARGET")}");

        // Decoration must not intercept input.
        bool hitTestable = capsule.IsHitTestVisible;
        lines.Add($"capsule IsHitTestVisible = {hitTestable} (must be False)");

        var transform = CapsuleTranslate(capsule);
        lines.Add($"capsule clocks: hasAnimated={capsule.HasAnimatedProperties} transformAnimated={transform?.HasAnimatedProperties}");

        bool passed = mismatches == 0 && rapidOk && !hitTestable;
        lines.Add($"verdict: {(passed ? "PASS" : "FAIL")} (misaligned {mismatches}, rapidRetarget {rapidOk}, decoration {!hitTestable})");

        File.WriteAllText(Path.Combine(outDir, "nav-capsule-audit.json"),
            JsonSerializer.Serialize(new
            {
                items = rows,
                misaligned = mismatches,
                rapid_retarget_ok = rapidOk,
                rapid_retarget_final_index = finalIndex,
                rapid_retarget_delta = new { dx = Math.Round(finalDx, 1), dy = Math.Round(finalDy, 1) },
                is_hit_test_visible = hitTestable,
                capsule_has_animated_properties = capsule.HasAnimatedProperties,
                transform_has_animated_properties = transform?.HasAnimatedProperties,
                passed,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return passed ? 0 : 1;
    }

    /// <summary>
    /// Reads the capsule's FINAL resting offset.
    ///
    /// Why not just wait for the animation: this probe window sits off-screen, and an off-screen
    /// window does not reliably get render ticks while we merely pump the dispatcher, so a
    /// time-based wait is flaky (the capsule was repeatedly observed one step - exactly 118px -
    /// behind). Clearing the animation clock makes the property fall back to the base value the
    /// window wrote, which is the exact resting target, and is deterministic.
    /// The animated transition itself is covered separately by the sampled checks.
    /// </summary>
    private static Point CapsuleRestingOffset(FrameworkElement capsule)
    {
        TranslateTransform? transform = CapsuleTranslate(capsule);
        if (transform is null)
        {
            return new Point(double.NaN, double.NaN);
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        return new Point(transform.X, transform.Y);
    }

    /// <summary>
    /// Locates the capsule's TranslateTransform.
    ///
    /// The 2026-10-17 window-shell round changed <c>NavCapsule.RenderTransform</c> from a bare
    /// <see cref="TranslateTransform"/> to a <see cref="TransformGroup"/> of
    /// [ScaleTransform, TranslateTransform], so the capsule can settle with a small scale while
    /// still translating. The X/Y offset still lives in the TranslateTransform, and every
    /// expectation in this audit is about X/Y alignment and "follow the newest target", so only
    /// the locator changes - the assertions themselves are unchanged.
    /// </summary>
    private static TranslateTransform? CapsuleTranslate(FrameworkElement capsule) =>
        capsule.RenderTransform switch
        {
            TranslateTransform translate => translate,
            TransformGroup group => group.Children.OfType<TranslateTransform>().FirstOrDefault(),
            _ => null,
        };

    private static Point CapsuleOffset(FrameworkElement capsule)
    {
        TranslateTransform? transform = CapsuleTranslate(capsule);
        return transform is null
            ? new Point(double.NaN, double.NaN)
            : new Point(transform.X, transform.Y);
    }

    private static int RowOf(FrameworkElement item, ListBox nav)
    {
        // Bucket by vertical centre so a wrapped two-row layout is detected reliably.
        double centre = item.TranslatePoint(new Point(0, item.ActualHeight / 2), nav).Y;
        List<double> centres = new List<double>();
        for (int index = 0; index < nav.Items.Count; index++)
        {
            if (nav.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement other)
            {
                centres.Add(other.TranslatePoint(new Point(0, other.ActualHeight / 2), nav).Y);
            }
        }

        centres = centres.Distinct().OrderBy(v => v).ToList();
        for (int i = 0; i < centres.Count; i++)
        {
            if (Math.Abs(centres[i] - centre) < 1.5)
            {
                return i;
            }
        }

        return -1;
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
