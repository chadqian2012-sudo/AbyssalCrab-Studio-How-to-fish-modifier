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
using System.Windows.Threading;

/// <summary>
/// Measures the natural (unconstrained) height of one feature card, and the row height the
/// UniformGrid actually hands out, so a dead zone can be attributed to MinHeight versus to the
/// tallest card in the grid.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class CardHeightAudit
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
        window.Width = 1460;
        window.Height = 900;
        Pump(TimeSpan.FromMilliseconds(600));

        ListBox? nav = window.FindName("TopCategories") as ListBox;
        List<object> rows = new List<object>();
        List<string> lines = new List<string>();

        for (int page = 0; page < (nav?.Items.Count ?? 0); page++)
        {
            nav!.SelectedIndex = page;
            Pump(TimeSpan.FromMilliseconds(520));
            string title = (nav.Items[page] as IHaveTitle)?.Title ?? $"page{page}";

            // Cards are the Borders that resolve FeatureCardStyle; identify them by the style's
            // MinHeight value rather than by tree position.
            List<Border> cards = EnumerateVisual<Border>(window)
                .Where(b => b.IsVisible && Math.Abs(b.MinHeight - 132) < 0.01)
                .ToList();
            if (cards.Count == 0)
            {
                continue;
            }

            // Natural height: clear MinHeight on the live cards, force a layout pass, and read back
            // the height the content actually asks for. This separates "the content needs this much"
            // from "MinHeight padded it", and also reveals whether the UniformGrid row is driven by
            // MinHeight at all.
            List<double> saved = cards.Select(c => c.MinHeight).ToList();
            foreach (Border card in cards)
            {
                card.MinHeight = 0;
            }

            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(250));
            List<double> natural = cards.Select(c => c.ActualHeight).ToList();

            // Push MinHeight far below any plausible content and re-measure the panel, so the row
            // height can be attributed to content versus to the declared minimum.
            foreach (Border card in cards)
            {
                card.MinHeight = 1;
            }

            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(250));
            UniformGrid? shrunkPanel = cards[0].Parent is ContentPresenter shrunkPresenter
                ? VisualTreeHelper.GetParent(shrunkPresenter) as UniformGrid
                : null;
            double shrunkPanelHeight = shrunkPanel?.ActualHeight ?? double.NaN;
            int shrunkRows = shrunkPanel?.Rows ?? 0;

            for (int index = 0; index < cards.Count; index++)
            {
                cards[index].MinHeight = saved[index];
            }

            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(250));

            double actualMin = cards.Min(c => c.ActualHeight);
            double actualMax = cards.Max(c => c.ActualHeight);
            double naturalMin = natural.Min();
            double naturalMax = natural.Max();
            double padding = cards[0].Padding.Top + cards[0].Padding.Bottom;
            double minHeight = saved.Min();

            // Row height the panel hands out, and how many rows fit the viewport.
            UniformGrid? panel = cards[0].Parent is ContentPresenter presenter
                ? VisualTreeHelper.GetParent(presenter) as UniformGrid
                : null;
            double panelHeight = panel?.ActualHeight ?? double.NaN;
            int panelRows = panel?.Rows ?? 0;
            double rowHeight = panelRows > 0 && !double.IsNaN(panelHeight) ? panelHeight / panelRows : double.NaN;

            ScrollViewer? viewer = EnumerateVisual<ScrollViewer>(window).FirstOrDefault(v => v.IsVisible && v.ScrollableHeight > 1);
            double viewport = viewer?.ViewportHeight ?? double.NaN;

            string summary = $"{title,-8} actual={actualMin:0.#}..{actualMax:0.#} " +
                             $"minH={minHeight:0.#} content+pad={naturalMin:0.#}..{naturalMax:0.#} " +
                             $"deadZone={actualMin - naturalMin:0.#} rowH={rowHeight:0.#} rows={panelRows} " +
                             $"viewport={viewport:0.#} visibleRows={(rowHeight > 0 && !double.IsNaN(viewport) ? viewport / rowHeight : 0):0.00} " +
                             $"| minH=1 -> panel={shrunkPanelHeight:0.#} rows={shrunkRows} " +
                             $"visibleRows={(shrunkRows > 0 && !double.IsNaN(viewport) ? viewport / (shrunkPanelHeight / shrunkRows) : 0):0.00}";
            lines.Add(summary);
            Console.WriteLine(summary);

            rows.Add(new
            {
                page,
                title,
                cards = cards.Count,
                actual_height_min = Math.Round(actualMin, 1),
                actual_height_max = Math.Round(actualMax, 1),
                content_height_min = Math.Round(naturalMin - padding, 1),
                content_height_max = Math.Round(naturalMax - padding, 1),
                height_with_min_height_zero_min = Math.Round(naturalMin, 1),
                height_with_min_height_zero_max = Math.Round(naturalMax, 1),
                padding,
                min_height = minHeight,
                dead_zone = Math.Round(actualMin - naturalMin, 1),
                row_height = double.IsNaN(rowHeight) ? 0 : Math.Round(rowHeight, 1),
                panel_height = double.IsNaN(panelHeight) ? 0 : Math.Round(panelHeight, 1),
                panel_rows = panelRows,
                panel_height_with_min_1 = double.IsNaN(shrunkPanelHeight) ? 0 : Math.Round(shrunkPanelHeight, 1),
                panel_rows_with_min_1 = shrunkRows,
                viewport = double.IsNaN(viewport) ? 0 : Math.Round(viewport, 1),
                rows_visible_if_min_1 = shrunkRows > 0 && !double.IsNaN(viewport)
                    ? Math.Round(viewport / (shrunkPanelHeight / shrunkRows), 2)
                    : 0,
            });
            lines.Add($"{title,-8} actual={actualMin:0.#}..{actualMax:0.#} content+pad={naturalMin:0.#}..{naturalMax:0.#} " +
                      $"deadZone={actualMin - naturalMin:0.#} rowH={rowHeight:0.#} rows={panelRows} viewport={viewport:0.#} " +
                      $"visibleRows={(rowHeight > 0 ? viewport / rowHeight : 0):0.00}");
        }

        File.WriteAllText(Path.Combine(outDir, "card-height-audit.json"),
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"pages measured: {rows.Count}");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private interface IHaveTitle
    {
        string Title { get; }
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
