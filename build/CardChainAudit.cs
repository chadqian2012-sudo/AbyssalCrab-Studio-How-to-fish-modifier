using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>
/// Reports the container chain above one feature card (type, name, alignment, actual size) so a
/// card height that ignores MinHeight can be traced to the element that stretches it.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class CardChainAudit
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
        nav!.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(600));

        // Pick the Border that carries the shipping card style by checking the resolved style key.
        Border? card = null;
        foreach (Border border in EnumerateVisual<Border>(window))
        {
            if (!border.IsVisible || border.ActualWidth < 200 || border.ActualHeight < 80)
            {
                continue;
            }

            // Identify the shipping card by the style value it resolves, not by tree order: the
            // window's own rounded surface is also a styled Border.
            if (Math.Abs(border.MinHeight - 132) > 0.01)
            {
                continue;
            }

            card = border;
            break;
        }

        if (card == null)
        {
            Console.WriteLine("no styled Border found");
            return 3;
        }

        Console.WriteLine($"card: {card.ActualWidth:0.#} x {card.ActualHeight:0.#}  MinHeight={card.MinHeight}  Padding={card.Padding}");

        // The UniformGrid gives every row one fixed height, so the tallest cell decides the grid.
        // Sweep MinHeight to find where it stops being the binding constraint.
        System.Windows.Controls.Primitives.UniformGrid? grid = EnumerateVisual<System.Windows.Controls.Primitives.UniformGrid>(window).FirstOrDefault(g => g.ActualHeight > 200);
        Console.WriteLine();
        Console.WriteLine("MinHeight sweep (card height / panel extent):");
        foreach (double probe in new[] { 0.0, 1.0, 100.0, 132.0, 150.0, 170.0 })
        {
            card.MinHeight = probe;
            window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(260));
            Console.WriteLine($"   MinHeight={probe,6:0.#}  card={card.ActualHeight:0.#}  panel={grid?.ActualHeight:0.#}");
        }

        card.MinHeight = 132;
        window.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(260));
        Console.WriteLine();
        Console.WriteLine("card content tree (depth 4):");
        Dump(card, 0, 4);
        Console.WriteLine();
        Console.WriteLine("container chain (child -> root):");
        DependencyObject? current = card;
        int depth = 0;
        while (current != null && depth < 14)
        {
            StringBuilder line = new StringBuilder();
            line.Append(new string(' ', depth * 2));
            if (current is FrameworkElement element)
            {
                line.Append($"{element.GetType().Name}{(string.IsNullOrEmpty(element.Name) ? "" : "#" + element.Name)}");
                line.Append($"  {element.ActualWidth:0.#}x{element.ActualHeight:0.#}");
                line.Append($"  HAlign={element.HorizontalAlignment} VAlign={element.VerticalAlignment}");
                if (element is Border border && border.Padding != default)
                {
                    line.Append($"  Padding={border.Padding}");
                }

                if (element is TextBlock)
                {
                    line.Append($"  text={((TextBlock)element).Text}");
                }

                if (element is ScrollViewer viewer)
                {
                    line.Append($"  viewport={viewer.ViewportHeight:0.#} extent={viewer.ExtentHeight:0.#}");
                }
            }
            else
            {
                line.Append(current.GetType().Name);
            }

            Console.WriteLine(line.ToString());

            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else
            {
                break;
            }

            depth++;
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private static void Dump(DependencyObject node, int depth, int maxDepth)
    {
        if (depth > maxDepth)
        {
            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, index);
            StringBuilder line = new StringBuilder();
            line.Append(new string(' ', depth * 2 + 2));
            if (child is FrameworkElement element)
            {
                line.Append($"{element.GetType().Name}{(string.IsNullOrEmpty(element.Name) ? "" : "#" + element.Name)}");
                line.Append($"  {element.ActualWidth:0.#}x{element.ActualHeight:0.#}");
                line.Append($"  min={element.MinHeight:0.#}/max={element.MaxHeight:0.#}");
                if (element is Border border)
                {
                    line.Append($"  pad={border.Padding} border={border.BorderThickness}");
                }

                if (element is TextBlock text)
                {
                    line.Append($"  font={text.FontSize}  \"{(text.Text ?? string.Empty).Substring(0, Math.Min(18, (text.Text ?? string.Empty).Length))}\"");
                }

                if (element is System.Windows.Controls.Primitives.ToggleButton toggle)
                {
                    line.Append($"  toggleH={toggle.Height:0.#}");
                }
            }
            else
            {
                line.Append(child.GetType().Name);
            }

            Console.WriteLine(line.ToString());
            Dump(child, depth + 1, maxDepth);
        }
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
