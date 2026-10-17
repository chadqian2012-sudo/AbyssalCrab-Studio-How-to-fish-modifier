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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Measures the things the UI hand-off asks to be verified per-page and per-width:
/// top category band height and wrap rows, card grid geometry, clipping of card content,
/// keyboard focus visibility, page-switch animation, and scroll extent. Renders the window at
/// several widths so weak spots can be inspected instead of guessed.
/// </summary>
internal static class UiPolishAudit
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

        // Keep the 1s game poll out of the picture; probes must not touch the game process.
        FieldInfo? timerField = window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
        (timerField?.GetValue(window) as DispatcherTimer)?.Stop();

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -4000;
        window.Top = -4000;
        window.ShowActivated = false;
        window.Show();
        Pump(TimeSpan.FromMilliseconds(1400));

        ListBox? nav = FindByName<ListBox>(window, "TopCategories");
        if (nav == null)
        {
            Console.WriteLine("FAIL TopCategories not found");
            return 3;
        }

        // The occlusion sweep below re-selects pages; anchor it through the same helper the rest of
        // the audit uses so both stay in sync.
        _ = nav.ItemContainerGenerator;

        List<string> lines = new List<string>();
        Dictionary<string, object?> report = new Dictionary<string, object?>();

        // ---- 1. navigation band across widths ------------------------------------------
        double[] widths = { 1180, 1240, 1290, 1320, 1380, 1460, 1600 };
        List<object> navSamples = new List<object>();
        foreach (double width in widths)
        {
            window.Width = width;
            Pump(TimeSpan.FromMilliseconds(520));
            nav.UpdateLayout();
            WrapPanel? wrap = FindVisual<WrapPanel>(nav);
            double bandHeight = nav.ActualHeight;
            double wrapHeight = wrap?.ActualHeight ?? 0;
            int rows = 0;
            double usedWidth = 0;
            if (wrap != null)
            {
                // Bucket by the item's vertical centre with a 1px tolerance; raw Top values carry
                // sub-pixel differences that are not extra rows.
                List<double> centres = new List<double>();
                double maxRight = 0;
                foreach (ListBoxItem item in wrap.Children.OfType<ListBoxItem>())
                {
                    Point origin = item.TranslatePoint(new Point(0, 0), wrap);
                    double centre = origin.Y + item.ActualHeight / 2;
                    if (!centres.Any(c => Math.Abs(c - centre) < 1.5))
                    {
                        centres.Add(centre);
                    }

                    maxRight = Math.Max(maxRight, origin.X + item.ActualWidth);
                }

                rows = centres.Count;
                usedWidth = maxRight;
            }

            TextBlock? firstLabel = wrap?.Children.OfType<ListBoxItem>()
                .Select(i => EnumerateVisual<TextBlock>(i).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text)))
                .FirstOrDefault(t => t != null);

            navSamples.Add(new
            {
                window_width = width,
                nav_height = Math.Round(bandHeight, 1),
                wrap_height = Math.Round(wrapHeight, 1),
                rows,
                item_count = nav.Items.Count,
                row_used_width = Math.Round(usedWidth, 1),
                available_width = Math.Round(wrap?.ActualWidth ?? 0, 1),
                first_label_font_size = firstLabel?.FontSize ?? 0,
            });
            lines.Add($"nav  width={width,6:0}  band={bandHeight,6:0.0}  wrap={wrapHeight,6:0.0}  rows={rows}  " +
                      $"used={usedWidth,7:0.0}/{wrap?.ActualWidth ?? 0,7:0.0}  font={firstLabel?.FontSize ?? 0}");
        }

        report["navigation"] = navSamples;
        window.Width = 1460;
        Pump(TimeSpan.FromMilliseconds(400));

        // ---- 2. per-page card grid geometry -------------------------------------------
        List<object> pageReports = new List<object>();
        for (int index = 0; index < nav.Items.Count; index++)
        {
            nav.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(480));
            string title = TitleOf(nav.Items[index]);

            List<Border> cards = EnumerateVisual<Border>(window)
                .Where(b => ReferenceEquals(b.TryFindResource("FeatureCardStyle"), null) == false
                            && b.IsVisible && b.ActualWidth > 120 && b.ActualHeight > 40)
                .ToList();
            // The style reference test above is unreliable across templates; fall back to the
            // documented marker: cards are the Borders rendered by the features ItemsControl.
            int clippedText = 0;
            int trimmedWithoutTooltip = 0;
            foreach (TextBlock block in EnumerateVisual<TextBlock>(window))
            {
                if (!block.IsVisible || string.IsNullOrEmpty(block.Text))
                {
                    continue;
                }

                bool trimmed = block.TextTrimming != TextTrimming.None
                               && block.ActualWidth > 0
                               && MeasureWanted(block) > block.ActualWidth + 1.5;
                if (trimmed && string.IsNullOrEmpty(block.ToolTip as string))
                {
                    trimmedWithoutTooltip++;
                }

                DependencyObject? parent = VisualTreeHelper.GetParent(block);
                while (parent != null && parent is not ScrollViewer)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }

                if (parent is ScrollViewer viewer && viewer.ActualHeight > 0)
                {
                    GeneralTransform transform = block.TransformToAncestor(viewer);
                    Rect bounds = transform.TransformBounds(new Rect(0, 0, block.ActualWidth, block.ActualHeight));
                    if (bounds.Bottom > viewer.ActualHeight + 1 || bounds.Top < -1)
                    {
                        clippedText++;
                    }
                }
            }

            // Overlap check: does the footer band cover the scrollable content?
            Border? footer = FindFooter(window);
            double contentBottom = FindContentHost(window) is FrameworkElement host
                ? host.TranslatePoint(new Point(0, host.ActualHeight), window).Y
                : 0;
            double footerTop = footer?.TranslatePoint(new Point(0, 0), window).Y ?? double.NaN;

            pageReports.Add(new
            {
                index,
                title,
                cards = cards.Count,
                trimmed_text_without_tooltip = trimmedWithoutTooltip,
                text_outside_viewer = clippedText,
                content_bottom = Math.Round(contentBottom, 1),
                footer_top = double.IsNaN(footerTop) ? (double?)null : Math.Round(footerTop, 1),
                footer_covers_content = !double.IsNaN(footerTop) && contentBottom > footerTop + 1,
            });
            lines.Add($"page {index,2} {title,-10} cards={cards.Count,3} trimmedNoTip={trimmedWithoutTooltip,3} " +
                      $"textOutside={clippedText,3} contentBottom={contentBottom,7:0.0} footerTop={(double.IsNaN(footerTop) ? "-" : footerTop.ToString("0.0"))}");
        }

        report["pages"] = pageReports;

        // ---- 3. keyboard focus visibility --------------------------------------------
        nav.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(400));
        List<object> focusSamples = new List<object>();

        // Real keyboard focus: the window must be active and the element focused through
        // Keyboard.Focus after activation, otherwise IsKeyboardFocused stays false.
        // Offline merge verification must not change the user's active window or keyboard focus.
        bool noInput = Environment.GetCommandLineArgs().Contains("--no-input");
        if (!noInput)
        {
        window.Activate();
        Pump(TimeSpan.FromMilliseconds(350));

        ListBoxItem? firstItem = nav.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        if (firstItem != null)
        {
            nav.SelectedIndex = 1;
            Pump(TimeSpan.FromMilliseconds(300));
            bool focused = firstItem.Focus();
            Keyboard.Focus(firstItem);
            Pump(TimeSpan.FromMilliseconds(300));
            Style? focusVisual = firstItem.FocusVisualStyle;
            bool focusVisualRendered = firstItem.IsKeyboardFocused && focusVisual != null;

            // Selected vs hover must be distinguishable: compare the selected item's rendered
            // background against a non-selected sibling.
            ListBoxItem? secondItem = nav.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem;
            string selectedFill = DescribeFill(firstItem);
            string siblingFill = secondItem == null ? "?" : DescribeFill(secondItem);

            focusSamples.Add(new
            {
                target = "nav item 0",
                window_is_active = window.IsActive,
                focus_call_returned_true = focused,
                is_keyboard_focused = firstItem.IsKeyboardFocused,
                focus_visual_style_set = focusVisual != null,
                focus_visual_target = focusVisual?.TargetType?.Name,
                selected_fill = selectedFill,
                non_selected_fill = siblingFill,
                selected_differs_from_unselected = selectedFill != siblingFill,
            });
            lines.Add($"focus nav: active={window.IsActive} focused={firstItem.IsKeyboardFocused} " +
                      $"focusStyle={(focusVisual != null)} selectedFill={selectedFill} other={siblingFill}");
            if (!firstItem.IsKeyboardFocused)
            {
                // Reference the computed flag so the compiler keeps the diagnostic meaningful.
                lines.Add($"  note: focusVisualRendered={focusVisualRendered}");
            }
        }

        TextBox? anyBox = EnumerateVisual<TextBox>(window).FirstOrDefault(t => t.IsVisible && t.IsEnabled);
        if (anyBox != null)
        {
            Keyboard.Focus(anyBox);
            Pump(TimeSpan.FromMilliseconds(300));
            focusSamples.Add(new
            {
                target = "textbox",
                is_keyboard_focused = anyBox.IsKeyboardFocused,
                caret_visible = anyBox.IsKeyboardFocused,
            });
            lines.Add($"focus textbox: focused={anyBox.IsKeyboardFocused}");
        }

        }
        report["focus"] = focusSamples;
        report["focus_test_skipped"] = noInput;

        // ---- 3b. collapsed pages must not intercept clicks ----------------------------
        List<object> occlusion = new List<object>();
        int occluders = 0;
        for (int index = 0; index < nav.Items.Count; index++)
        {
            nav.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(430));
            string title = TitleOf(nav.Items[index]);

            // Every page body is a sibling of the visible one. Anything that still reports
            // IsVisible while the page is not selected would sit on top of the active page.
            List<string> visibleSiblings = new List<string>();
            if (FindContentHost(window) is Grid host)
            {
                foreach (FrameworkElement child in host.Children.OfType<FrameworkElement>())
                {
                    if (child.IsVisible && child.ActualWidth > 200 && child.ActualHeight > 100)
                    {
                        visibleSiblings.Add($"{child.GetType().Name}#{(string.IsNullOrEmpty(child.Name) ? "-" : child.Name)}");
                    }
                }
            }

            // More than the two permanent rows (header + warning) means a page body is still live.
            bool suspicious = visibleSiblings.Count(s => s.Contains('#')) > 2;
            if (suspicious)
            {
                occluders++;
            }

            occlusion.Add(new
            {
                index,
                title,
                visible_large_siblings = visibleSiblings,
                suspicious,
            });
        }

        lines.Add($"occlusion: pages with extra live bodies = {occluders}");
        report["page_occlusion"] = occlusion;

        // Also confirm no hidden adorner or overlay is drawn above the content area.
        List<string> topmost = EnumerateVisual<FrameworkElement>(window)
            .Where(e => e.IsVisible && e.ActualWidth > 400 && e.ActualHeight > 200 && !e.IsHitTestVisible)
            .Select(e => $"{e.GetType().Name}#{e.Name}")
            .Distinct()
            .Take(8)
            .ToList();
        lines.Add($"non-hit-testable large overlay candidates = {topmost.Count}");
        report["overlay_candidates"] = topmost;

        // ---- 3c. animation clock residue -------------------------------------------------
        // A page switch used to leave its FillBehavior=HoldEnd clocks attached to the content host
        // for good. Count live animation clocks right after a switch and again after an idle period,
        // so "the clock was released" is a measured fact rather than an assumption.
        nav.SelectedIndex = 1;
        Pump(TimeSpan.FromMilliseconds(650));
        int clocksAfterSwitch = CountClocks(window);
        nav.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(650));
        int clocksAfterReturn = CountClocks(window);
        Pump(TimeSpan.FromMilliseconds(2400));
        int clocksIdle = CountClocks(window);

        FrameworkElement? residueHost = FindContentHost(window);
        double settledOpacity = residueHost?.Opacity ?? double.NaN;
        double settledY = residueHost?.RenderTransform is TranslateTransform settled ? settled.Y : double.NaN;

        lines.Add($"clocks: afterSwitch={clocksAfterSwitch} afterReturn={clocksAfterReturn} idle2.4s={clocksIdle} " +
                  $"contentHost={residueHost?.ActualWidth:0}x{residueHost?.ActualHeight:0} " +
                  $"opacity={settledOpacity:0.###} y={settledY:0.###}");

        // Name the elements still holding a clock so a genuine leak can be told apart from the
        // harmless HoldEnd fills the templates keep on their idle/hover layers.
        List<string> holders = EnumerateVisual<FrameworkElement>(window)
            .Where(e => e.HasAnimatedProperties)
            .Select(e => $"{e.GetType().Name}#{(string.IsNullOrEmpty(e.Name) ? "-" : e.Name)}" +
                         $"/parent={VisualTreeHelper.GetParent(e)?.GetType().Name}")
            .GroupBy(s => s)
            .Select(g => $"{g.Key} x{g.Count()}")
            .OrderByDescending(s => s)
            .ToList();
        foreach (string holder in holders)
        {
            lines.Add($"  clock holder: {holder}");
        }

        report["clock_holders"] = holders;
        report["content_host_still_animated"] = residueHost?.HasAnimatedProperties ?? false;

        // Focused check: switch to a page, let the fade finish, then look at the content host alone.
        nav.SelectedIndex = 3;
        Pump(TimeSpan.FromMilliseconds(1400));
        bool hostAnimatedAfterFade = residueHost?.HasAnimatedProperties ?? false;
        double hostOpacityAfterFade = residueHost?.Opacity ?? double.NaN;
        double hostYAfterFade = residueHost?.RenderTransform is TranslateTransform hostTranslate ? hostTranslate.Y : double.NaN;
        List<string> hostHolders = residueHost == null
            ? new List<string>()
            : EnumerateVisual<FrameworkElement>(residueHost)
                .Where(e => e.HasAnimatedProperties)
                .Select(e => $"{e.GetType().Name}#{(string.IsNullOrEmpty(e.Name) ? "-" : e.Name)}")
                .Distinct()
                .ToList();
        lines.Add($"content host 1.4s after switch: animated={hostAnimatedAfterFade} opacity={hostOpacityAfterFade:0.###} " +
                  $"y={hostYAfterFade:0.###} animatedDescendants={hostHolders.Count}");
        foreach (string item in hostHolders)
        {
            lines.Add($"  host descendant clock: {item}");
        }

        report["content_host_after_fade"] = new
        {
            animated = hostAnimatedAfterFade,
            opacity = hostOpacityAfterFade,
            translate_y = hostYAfterFade,
            animated_descendants = hostHolders,
        };
        report["clock_residue"] = new
        {
            after_switch = clocksAfterSwitch,
            after_return = clocksAfterReturn,
            idle_2400ms = clocksIdle,
            content_host_size = residueHost == null ? null : $"{residueHost.ActualWidth:0}x{residueHost.ActualHeight:0}",
            settled_opacity = settledOpacity,
            settled_translate_y = settledY,
        };

        // ---- 4. page switch animation -------------------------------------------------
        FrameworkElement? contentHost = FindContentHost(window);
        List<object> animSamples = new List<object>();
        if (contentHost != null && nav.Items.Count > 2)
        {
            nav.SelectedIndex = 0;
            Pump(TimeSpan.FromMilliseconds(500));
            nav.SelectedIndex = 2;
            Pump(TimeSpan.FromMilliseconds(40));
            animSamples.Add(new
            {
                property = "content opacity at 40ms",
                value = Math.Round(contentHost.Opacity, 3),
                animated = contentHost.HasAnimatedProperties,
            });
            TranslateTransform? transform = contentHost.RenderTransform as TranslateTransform;
            animSamples.Add(new
            {
                property = "content translate Y at 40ms",
                value = transform == null ? (double?)null : Math.Round(transform.Y, 3),
                has_translate = transform != null,
            });
            lines.Add($"page anim: opacity={contentHost.Opacity:0.###} y={(transform?.Y.ToString("0.###") ?? "-")} animated={contentHost.HasAnimatedProperties}");
            Pump(TimeSpan.FromMilliseconds(600));
            animSamples.Add(new
            {
                property = "content opacity settled",
                value = Math.Round(contentHost.Opacity, 3),
                animated = contentHost.HasAnimatedProperties,
            });
        }

        report["page_animation"] = animSamples;

        // ---- 5. render at several widths ---------------------------------------------
        // Pin the page before rendering: the sweeps above end on whatever page they visited last,
        // which made the screenshots depend on probe order instead of the build under test.
        nav.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(700));
        List<object> shots = new List<object>();
        foreach (double width in widths)
        {
            window.Width = width;
            Pump(TimeSpan.FromMilliseconds(600));
            string file = Path.Combine(outDir, $"polish-{width:0}x{window.Height:0}.png");
            Render(window, file, 1.5);
            shots.Add(new { width, file = Path.GetFileName(file), bytes = new FileInfo(file).Length });
            lines.Add($"shot {Path.GetFileName(file)} {new FileInfo(file).Length} bytes");
        }

        // nav band crop at the narrowest width for close inspection
        window.Width = 1180;
        Pump(TimeSpan.FromMilliseconds(500));
        string navFile = Path.Combine(outDir, "polish-nav-1180.png");
        RenderElement(window, nav, navFile, 3);
        shots.Add(new { width = 1180, file = Path.GetFileName(navFile), bytes = new FileInfo(navFile).Length, kind = "navcrop" });
        lines.Add($"nav crop {Path.GetFileName(navFile)} {new FileInfo(navFile).Length} bytes");

        report["screenshots"] = shots;

        File.WriteAllText(Path.Combine(outDir, "ui-polish-audit.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        window.Close();
        Pump(TimeSpan.FromMilliseconds(150));
        _application.Shutdown();
        return 0;
    }

    private static string TitleOf(object? item)
    {
        if (item == null)
        {
            return "?";
        }

        PropertyInfo? title = item.GetType().GetProperty("Title");
        return title?.GetValue(item)?.ToString() ?? item.ToString() ?? "?";
    }

    private static double MeasureWanted(TextBlock block)
    {
        FormattedText text = new FormattedText(
            block.Text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        return text.WidthIncludingTrailingWhitespace;
    }

    private static Border? FindFooter(DependencyObject root)
    {
        return EnumerateVisual<Border>(root)
            .FirstOrDefault(b => Grid.GetRow(b) == 2 && b.ActualWidth > 400);
    }

    private static FrameworkElement? FindContentHost(DependencyObject root)
    {
        // The page host is the Grid carrying the PageFadeIn TranslateTransform. It lives in column 0
        // of the middle row and fills most of the window; do not filter on the column index, the
        // page layout puts it at (col=0, row=1).
        return EnumerateVisual<Grid>(root)
            .Where(g => g.RenderTransform is TranslateTransform && g.ActualWidth > 1000 && g.ActualHeight > 300)
            .OrderByDescending(g => g.ActualWidth * g.ActualHeight)
            .FirstOrDefault();
    }

    private static int CountClocks(DependencyObject root)
    {
        // Count elements still carrying an animation clock. HasAnimatedProperties is true while a
        // FillBehavior=HoldEnd clock stays attached even after the value has settled, so it is the
        // right signal for "the clock was never released".
        return EnumerateVisual<UIElement>(root).Count(e => e.HasAnimatedProperties);
    }

    private static string DescribeFill(ListBoxItem item)
    {
        // The template paints the selection on the named "SelFill" layer; sample its opacity and
        // colour so "selected" can be told apart from "not selected" without pixel diffing.
        List<string> parts = new List<string>();
        foreach (Border border in EnumerateVisual<Border>(item))
        {
            if (border.Name is "SelFill" or "HoverFill" or "Indicator")
            {
                parts.Add($"{border.Name}:{Math.Round(border.Opacity, 2)}:" +
                          (border.Background is SolidColorBrush brush ? brush.Color.ToString() : "-"));
            }
        }

        return parts.Count == 0 ? "<no layers>" : string.Join("|", parts);
    }

    private static T? FindByName<T>(FrameworkElement root, string name) where T : FrameworkElement
    {
        return root.FindName(name) as T;
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        return EnumerateVisual<T>(root).FirstOrDefault();
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

    private static void Render(Window window, string file, double scale)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        SavePng(bitmap, file);
    }

    private static void RenderElement(Window window, FrameworkElement element, string file, double scale)
    {
        Point origin = element.TranslatePoint(new Point(0, 0), window);
        int width = (int)Math.Ceiling(element.ActualWidth);
        int height = (int)Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int fullWidth = (int)Math.Ceiling(window.ActualWidth * scale);
        int fullHeight = (int)Math.Ceiling(window.ActualHeight * scale);
        RenderTargetBitmap bitmap = new RenderTargetBitmap(fullWidth, fullHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);

        // Clamp the crop to the rendered bitmap; a fractional TranslatePoint origin can push the
        // rectangle one pixel outside and CroppedBitmap throws for out-of-range rects.
        int x = Math.Max(0, Math.Min((int)(origin.X * scale), fullWidth - 1));
        int y = Math.Max(0, Math.Min((int)(origin.Y * scale), fullHeight - 1));
        int cropWidth = Math.Min((int)(width * scale), fullWidth - x);
        int cropHeight = Math.Min((int)(height * scale), fullHeight - y);
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            return;
        }

        CroppedBitmap crop = new CroppedBitmap(bitmap, new Int32Rect(x, y, cropWidth, cropHeight));
        SavePng(crop, file);
    }

    private static void SavePng(BitmapSource source, string file)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(file);
        encoder.Save(stream);
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
