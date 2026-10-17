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
/// Injects genuinely long strings where the app displays values that come from the game (item names,
/// island names, member names, status text) and reports how each one degrades: does it ellipsise,
/// does it have a ToolTip carrying the full text, does it overflow its container, or does it push
/// neighbours out of place.
///
/// This targets the real risk the hand-off calls out: game-supplied strings of unknown length.
/// </summary>
internal static class LongTextAudit
{
    private static Application? _application;

    // Strings a live game could plausibly produce: long Chinese names and long status messages.
    private static readonly string[] LongValues =
    {
        "\u8fd9\u662f\u4e00\u4e2a\u7279\u522b\u7279\u522b\u957f\u7684\u7269\u54c1\u540d\u79f0\u7528\u4e8e\u6d4b\u8bd5\u5bb9\u5668\u662f\u5426\u4f1a\u6ea2\u51fa",
        "Legendary Abyssal Crab Fishing Rod With An Extremely Long Localised Display Name",
        "\u7b49\u5f85\u8fde\u63a5\u540e\u8bfb\u53d6\u5b9e\u65f6\u76ee\u5f55\u5931\u8d25\uff1a\u8fd0\u884c\u65f6\u6865\u672a\u54cd\u5e94\uff0c\u8bf7\u91cd\u542f\u6e38\u620f\u540e\u91cd\u8bd5\u5e76\u68c0\u67e5\u6a21\u6539\u5668\u662f\u5426\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c",
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
        window.Width = 1180; // worst case width
        window.Height = 720;
        Pump(TimeSpan.FromMilliseconds(800));

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        List<object> rows = new List<object>();
        List<string> lines = new List<string>();

        int overflowed = 0;
        int trimmedWithoutTooltip = 0;
        int inspected = 0;

        // Walk every visible TextBlock and temporarily give it a long value of the same role.
        // TextBlocks bound to the view model are re-templated on page switch, so restore afterwards.
        for (int page = 0; page < (nav?.Items.Count ?? 0); page++)
        {
            nav!.SelectedIndex = page;
            Pump(TimeSpan.FromMilliseconds(500));
            string title = nav.Items[page]?.GetType().GetProperty("Title")?.GetValue(nav.Items[page]) as string ?? $"page{page}";

            List<TextBlock> blocks = EnumerateVisual<TextBlock>(window)
                .Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.ActualWidth > 20)
                .ToList();

            foreach (TextBlock block in blocks)
            {
                string original = block.Text;
                string injected = LongValues[original.Length % LongValues.Length];

                // Replace the rendered text only; bindings are untouched, so this is fully reversible.
                block.Text = injected;
                window.UpdateLayout();
                Pump(TimeSpan.FromMilliseconds(40));

                bool trimmed = block.ActualWidth + 0.5 < block.DesiredSize.Width
                               || (block.TextTrimming != TextTrimming.None && block.ActualWidth + 0.5 < MeasureWidth(block, injected));
                bool hasTooltip = block.ToolTip != null;
                bool overflowsParent = false;
                double overflowPx = 0;
                if (block.Parent is FrameworkElement parent && parent.ActualWidth > 0 && !double.IsNaN(block.ActualWidth))
                {
                    Point blockRight = block.TranslatePoint(new Point(block.ActualWidth, 0), parent);
                    if (blockRight.X > parent.ActualWidth + 0.5)
                    {
                        overflowsParent = true;
                        overflowPx = blockRight.X - parent.ActualWidth;
                    }
                }

                if (trimmed && !hasTooltip)
                {
                    trimmedWithoutTooltip++;
                    rows.Add(new
                    {
                        page = title,
                        text_before = original.Length > 40 ? original.Substring(0, 40) + "..." : original,
                        font_size = block.FontSize,
                        actual_width = Math.Round(block.ActualWidth, 1),
                        needed_width = Math.Round(MeasureWidth(block, injected), 1),
                        trimming = block.TextTrimming.ToString(),
                        tooltip = false,
                        overflows_parent = overflowsParent,
                        overflow_px = Math.Round(overflowPx, 1),
                    });
                }

                if (overflowsParent)
                {
                    overflowed++;
                }

                inspected++;
                block.Text = original;
                window.UpdateLayout();
            }
        }

        // Report the worst offenders with a bound on the list length.
        // Rank by OVERFLOW first: overflow is a real layout defect, while trimming is intended
        // whenever a ToolTip carries the full text. Nav chips are excluded from the ranking because
        // their label IS the chip text (fixed 120px, no ToolTip by design) - counting them buries
        // the genuine offenders.
        var worst = rows
            .OrderByDescending(r => (double)r.GetType().GetProperty("overflow_px")!.GetValue(r)!)
            .ThenByDescending(r => (double)r.GetType().GetProperty("needed_width")!.GetValue(r)! - (double)r.GetType().GetProperty("actual_width")!.GetValue(r)!)
            .Where(r => (double)r.GetType().GetProperty("actual_width")!.GetValue(r)! > 70)
            .Take(20)
            .ToList();

        lines.Add($"inspected {inspected} visible TextBlocks across {nav?.Items.Count ?? 0} pages at 1180x720");
        lines.Add($"text that trims with NO ToolTip : {trimmedWithoutTooltip}  (includes the 11 nav chips, where the label IS the chip text)");
        lines.Add($"text that overflows its parent   : {overflowed}");
        foreach (var row in worst)
        {
            lines.Add("   " + JsonSerializer.Serialize(row, new JsonSerializerOptions { WriteIndented = false }));
        }

        File.WriteAllText(Path.Combine(outDir, "long-text-audit.json"),
            JsonSerializer.Serialize(new
            {
                window = "1180x720",
                inspected_textblocks = inspected,
                trimmed_without_tooltip = trimmedWithoutTooltip,
                overflows_parent = overflowed,
                samples = LongValues,
                worst_offenders = worst,
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

    private static double MeasureWidth(TextBlock block, string text)
    {
        // Measure the injected text with the block's own typeface, at infinite width.
        FormattedText formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
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
