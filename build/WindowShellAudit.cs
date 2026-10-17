using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Independently verifies the "window shell &amp; motion depth" round by reading the live visual tree:
/// shell elevation + the re-scoped text-crispness invariant (this round's shell / chrome / nav decoration
/// layers - ShellSurface and the two decoration Borders - must each be childless, so their DropShadowEffect
/// never becomes an ancestor of text),
/// the window radius hierarchy (22 / 20 / RectangleGeometry 22), the top specular line, the childless
/// floating tool layers, the inactive-window scrims and their IsActive trigger, the nav capsule rim,
/// and 6x zoomed corner renders. Nothing is trusted from any document.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class WindowShellAudit
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

        // Never let the 1s game poll touch a game process; a probe must stay off the game.
        (window.GetType().GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) as DispatcherTimer)?.Stop();

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -4000;
        window.Top = -4000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump(TimeSpan.FromMilliseconds(1500));
        window.Width = 1460;
        window.Height = 900;
        Pump(TimeSpan.FromMilliseconds(600));

        List<Dictionary<string, object?>> checks = new List<Dictionary<string, object?>>();
        List<string> lines = new List<string>();
        List<string> limitations = new List<string>();

        void Check(string name, bool ok, string detail)
        {
            checks.Add(new Dictionary<string, object?> { ["name"] = name, ["passed"] = ok, ["detail"] = detail });
            lines.Add($"[{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        }

        // ---- stale / missing assembly guard -----------------------------------------
        // The Python driver compiles against verification/assemblies; if that extraction has not been
        // refreshed after the round's build, the new elements simply are not there. Report it instead
        // of guessing - the caller is told to refresh the extraction rather than have us patch it.
        FrameworkElement? shell = FindByName(window, "ShellSurface") as FrameworkElement;
        FrameworkElement? capsule = FindByName(window, "NavCapsule") as FrameworkElement;
        FrameworkElement? capsuleLayer = FindByName(window, "NavCapsuleLayer") as FrameworkElement;
        ListBox? nav = FindByName(window, "TopCategories") as ListBox;
        List<string> missing = new List<string>();
        if (shell == null) missing.Add("ShellSurface");
        if (capsule == null) missing.Add("NavCapsule");
        if (capsuleLayer == null) missing.Add("NavCapsuleLayer");
        if (nav == null) missing.Add("TopCategories");
        if (window.TryFindResource("GlassSpecularBrush") == null) missing.Add("GlassSpecularBrush(resource)");
        if (window.TryFindResource("InactiveScrim") == null) missing.Add("InactiveScrim(style)");

        if (missing.Count > 0)
        {
            limitations.Add("The HowToFishTrainer assembly under verification/assemblies does not contain the "
                            + "window-shell round's elements (" + string.Join(", ", missing) + "). "
                            + "Refresh verification/assemblies from the finished release build and re-run; "
                            + "the audit does not repair the extraction itself.");
            File.WriteAllText(Path.Combine(outDir, "window-shell-audit.json"),
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["verdict"] = "STALE_ASSEMBLY",
                    ["passed"] = false,
                    ["stale_assembly"] = true,
                    ["missing_elements"] = missing,
                    ["checks"] = checks,
                    ["limitations"] = limitations,
                }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            Console.WriteLine($"STALE ASSEMBLY: missing {string.Join(", ", missing)}");
            foreach (string line in limitations) Console.WriteLine("  " + line);
            window.Close();
            Pump(TimeSpan.FromMilliseconds(150));
            _application.Shutdown();
            return 3;
        }

        Border? shellBorder = shell as Border;

        // Locate the content layer (the rounded Border that hosts the 3-row shell grid) and the two
        // decoration layers up front: the re-scoped check A (shell / chrome / nav decoration layers) and
        // checks B and D all need them. The content layer is identified by its style key
        // (RoundedWindowContent); if that is unavailable we fall back to the structural marker - the
        // Border whose Child is the 3-row Grid.
        Style? contentStyle = window.TryFindResource("RoundedWindowContent") as Style;
        Border? contentLayer = EnumerateVisual<Border>(window).FirstOrDefault(b => ReferenceEquals(b.Style, contentStyle));
        string contentLayerMethod = "style key RoundedWindowContent";
        if (contentLayer == null)
        {
            contentLayer = EnumerateVisual<Border>(window).FirstOrDefault(b => b.Child is Grid g && g.RowDefinitions.Count == 3);
            contentLayerMethod = "structural: Border whose Child is the 3-row Grid";
        }

        Grid? shellGrid = contentLayer?.Child as Grid;
        Border? topChromeDecor = shellGrid?.Children.OfType<Border>()
            .FirstOrDefault(b => b.Child == null && b.Effect != null && Grid.GetRow(b) == 0);
        Grid? navRow = shellGrid?.Children.OfType<Grid>().FirstOrDefault(g => Grid.GetRow(g) == 1);
        Border? navDecor = navRow?.Children.OfType<Border>()
            .FirstOrDefault(b => b.Child == null && b.Effect != null);

        // ============================================================================
        // A. Shell elevation + the re-scoped text-crispness invariant (the headline check)
        // ============================================================================
        Thickness shellMargin = shell!.Margin;
        bool marginOk = Math.Abs(shellMargin.Left - 12) < 0.01 && Math.Abs(shellMargin.Top - 12) < 0.01
                        && Math.Abs(shellMargin.Right - 12) < 0.01 && Math.Abs(shellMargin.Bottom - 12) < 0.01;
        Check("A.shell_margin", marginOk, $"ShellSurface.Margin={Fmt(shellMargin)} (expected 12 on all sides)");

        DropShadowEffect? shellEffect = shell.Effect as DropShadowEffect;
        Check("A.shell_effect", shellEffect != null,
            shellEffect != null
                ? $"ShellSurface.Effect=DropShadowEffect BlurRadius={shellEffect.BlurRadius} ShadowDepth={shellEffect.ShadowDepth} Opacity={shellEffect.Opacity} Direction={shellEffect.Direction}"
                : $"ShellSurface.Effect={(shell.Effect?.GetType().Name ?? "null")} (expected DropShadowEffect)");

        double shellRadius = shellBorder?.CornerRadius.TopLeft ?? -1;
        Check("A.shell_radius", Math.Abs(shellRadius - 20) < 0.01,
            $"ShellSurface.CornerRadius={shellRadius} (expected 20 = RadiusContainer token)");

        Check("A.shell_hit_test", !shell.IsHitTestVisible,
            $"ShellSurface.IsHitTestVisible={shell.IsHitTestVisible} (expected False)");

        // Re-scoped invariant (this round): every Effect-carrying element that THIS round introduced on the
        // shell / chrome / nav decoration layers must be childless. A DropShadowEffect on an ancestor forces
        // the subtree to render into an intermediate surface, which downgrades text from ClearType sub-pixel
        // antialiasing to grayscale antialiasing - which is exactly why this round split decoration (the
        // shadow-carrying Border) from content (the text). The three decoration layers are ShellSurface, the
        // top-chrome decoration Border and the nav-band decoration Border; each must have zero visual
        // children and therefore zero TextBlock descendants.
        List<object> decorationLayers = new List<object>();
        List<string> decorationOffenders = new List<string>();
        void AuditDecorationLayer(string label, UIElement? element)
        {
            if (element == null)
            {
                decorationOffenders.Add($"{label}(NOT FOUND)");
                decorationLayers.Add(new Dictionary<string, object?>
                {
                    ["layer"] = label,
                    ["found"] = false,
                    ["visual_children"] = (int?)null,
                    ["textblock_descendants"] = (int?)null,
                    ["effect"] = null,
                });
                return;
            }

            int visualChildren = VisualTreeHelper.GetChildrenCount(element);
            int textDescendants = CountDescendants<TextBlock>(element);
            if (visualChildren != 0 || textDescendants != 0)
            {
                decorationOffenders.Add($"{label}(visualChildren={visualChildren}, TextBlock={textDescendants})");
            }

            decorationLayers.Add(new Dictionary<string, object?>
            {
                ["layer"] = label,
                ["found"] = true,
                ["element"] = $"{element.GetType().Name}#{((element as FrameworkElement)?.Name ?? string.Empty)}",
                ["visual_children"] = visualChildren,
                ["textblock_descendants"] = textDescendants,
                ["effect"] = element.Effect?.GetType().Name,
            });
        }

        AuditDecorationLayer("ShellSurface", shell);
        AuditDecorationLayer("top-chrome decoration Border", topChromeDecor);
        AuditDecorationLayer("nav-band decoration Border", navDecor);

        Check("A.decoration_layers_childless", decorationOffenders.Count == 0,
            decorationOffenders.Count == 0
                ? "ShellSurface, the top-chrome decoration Border and the nav-band decoration Border each carry their "
                  + "elevation DropShadowEffect with zero visual children and therefore zero TextBlock descendants. "
                  + "A DropShadowEffect on an ancestor forces the subtree into an intermediate render surface, which "
                  + "downgrades text from ClearType sub-pixel antialiasing to grayscale antialiasing - exactly why this "
                  + "round split the decoration (the shadow-carrying Border) from the content (the text)."
                : "Effect-carrying decoration layer(s) that are NOT childless: " + string.Join("; ", decorationOffenders)
                  + ". A DropShadowEffect on an ancestor forces the subtree into an intermediate render surface and "
                  + "downgrades text from ClearType sub-pixel antialiasing to grayscale antialiasing.");

        // Informational only (NEVER affects `passed`): walk the ENTIRE visual tree and record EVERY element
        // with a non-null Effect plus its TextBlock descendant count. This is the honest picture - the
        // function cards carry GlassShadowSoft on their own Border (FeatureCardStyle, a pre-existing style
        // from the previous round) and do contain text, so card text is grayscale-antialiased. That is a
        // real, pre-existing condition from the round before this one, out of scope here, and it is
        // recorded rather than hidden or "fixed".
        List<DependencyObject> allVisuals = new List<DependencyObject>();
        Collect(window, allVisuals);
        List<object> effectInventory = new List<object>();
        int effectElementCount = 0;
        int effectWithTextCount = 0;
        foreach (DependencyObject visual in allVisuals)
        {
            if (visual is not UIElement element || element.Effect == null)
            {
                continue;
            }

            effectElementCount++;
            int textDescendants = CountDescendants<TextBlock>(element);
            if (textDescendants > 0)
            {
                effectWithTextCount++;
            }

            DropShadowEffect? dse = element.Effect as DropShadowEffect;
            effectInventory.Add(new Dictionary<string, object?>
            {
                ["element"] = $"{element.GetType().Name}#{((element as FrameworkElement)?.Name ?? string.Empty)}",
                ["effect"] = element.Effect.GetType().Name,
                ["blur_radius"] = dse?.BlurRadius,
                ["shadow_depth"] = dse?.ShadowDepth,
                ["opacity"] = dse?.Opacity,
                ["direction"] = dse?.Direction,
                ["textblock_descendants"] = textDescendants,
            });
        }

        lines.Add($"[INFO] effect_inventory (informational, not a verdict): {effectElementCount} effect-carrying "
                  + $"element(s); {effectWithTextCount} have TextBlock descendants (the pre-existing function-card shadows)");
        limitations.Add($"Informational effect_inventory (never affects `passed`): {effectElementCount} element(s) in "
                        + $"the window's visual tree carry a non-null Effect and {effectWithTextCount} of them have "
                        + "TextBlock descendants. The ones with TextBlock descendants are the function cards, whose "
                        + "Border carries GlassShadowSoft from the pre-existing FeatureCardStyle (previous round) and "
                        + "which do contain text - so card text is grayscale-antialiased. That is a real, pre-existing "
                        + "condition from the round before this one, outside the shell / chrome / nav decoration layers "
                        + "this round re-scoped, and is recorded here rather than hidden or \"fixed\".");

        // ============================================================================
        // B. Window radius hierarchy
        // ============================================================================
        WindowChrome chrome = WindowChrome.GetWindowChrome(window);
        double chromeRadius = chrome?.CornerRadius.TopLeft ?? -1;
        Check("B.windowchrome_radius", Math.Abs(chromeRadius - 22) < 0.01,
            $"WindowChrome.GetWindowChrome(window).CornerRadius={chromeRadius} (expected 22 = RadiusWindow token)");

        // contentLayer and contentLayerMethod were resolved above, before check A, and are reused here.
        double contentRadius = contentLayer?.CornerRadius.TopLeft ?? -1;
        Check("B.content_radius", Math.Abs(contentRadius - 20) < 0.01,
            $"content layer CornerRadius={contentRadius} (expected 20; located via {contentLayerMethod})");

        RectangleGeometry? contentClip = contentLayer?.Clip as RectangleGeometry;
        bool clipOk = contentClip != null
                      && Math.Abs(contentClip.RadiusX - 22) < 0.01
                      && Math.Abs(contentClip.RadiusY - 22) < 0.01;
        Check("B.content_clip_radius", clipOk,
            contentClip != null
                ? $"content layer Clip=RectangleGeometry RadiusX={contentClip.RadiusX} RadiusY={contentClip.RadiusY} (expected both 22)"
                : $"content layer Clip={(contentLayer?.Clip?.GetType().Name ?? "null")} (expected RectangleGeometry RadiusX=RadiusY=22)");
        Check("B.content_clip_bounds", contentClip != null && contentLayer != null
            && Math.Abs(contentClip.Rect.Width-contentLayer.ActualWidth)<.01
            && Math.Abs(contentClip.Rect.Height-contentLayer.ActualHeight)<.01,
            $"clip={contentClip?.Rect}; actual content={contentLayer?.ActualWidth}x{contentLayer?.ActualHeight}");
        Check("B.content_clip_four_corners", contentClip != null && contentLayer != null
            && !contentClip.FillContains(new Point(1,1))
            && !contentClip.FillContains(new Point(contentLayer.ActualWidth-1,1))
            && !contentClip.FillContains(new Point(1,contentLayer.ActualHeight-1))
            && !contentClip.FillContains(new Point(contentLayer.ActualWidth-1,contentLayer.ActualHeight-1)),
            "all four content corners lie outside the rounded clip");

        // ============================================================================
        // C. Top specular line
        // ============================================================================
        Brush? specularBrush = window.TryFindResource("GlassSpecularBrush") as Brush;
        List<Border> specularCandidates = EnumerateVisual<Border>(window)
            .Where(b => SameBrush(b.Background, specularBrush))
            .ToList();
        Border? specLine = specularCandidates.FirstOrDefault(
            b => Math.Abs(b.Height - 1) < 0.01 && !b.IsHitTestVisible && b.VerticalAlignment == VerticalAlignment.Top);

        Check("C.specular_line", specularBrush != null && specLine != null,
            specLine != null
                ? $"found Border with Background=GlassSpecularBrush: Height={specLine.Height} VerticalAlignment={specLine.VerticalAlignment} IsHitTestVisible={specLine.IsHitTestVisible}"
                : $"no Border with Background==GlassSpecularBrush (resource {(specularBrush != null ? "present" : "MISSING")}); candidates={specularCandidates.Count}");

        bool inTopRow = false;
        double specularY = double.NaN;
        if (specLine != null)
        {
            if (contentLayer != null)
            {
                specularY = specLine.TranslatePoint(new Point(0, 0), contentLayer).Y;
            }

            if (VisualTreeHelper.GetParent(specLine) is Grid parentGrid && parentGrid.RowDefinitions.Count == 3)
            {
                inTopRow = Grid.GetRow(specLine) == 0;
            }
        }

        Check("C.specular_in_top_row", inTopRow && specularY >= 0 && specularY < 64,
            $"specular line Grid.Row={(specLine != null ? Grid.GetRow(specLine) : -1)} y-in-content={specularY:0.#} "
            + "(top chrome row is row 0 of the 3-row grid, 64 DIP tall)");

        // ============================================================================
        // D. Floating tool layers (childless decoration Borders)
        // ============================================================================
        // shellGrid / topChromeDecor / navDecor were resolved above, before check A, and are reused here.
        Check("D.top_chrome_decor_effect",
            topChromeDecor != null && topChromeDecor.Effect != null
            && topChromeDecor.Child == null && VisualTreeHelper.GetChildrenCount(topChromeDecor) == 0,
            DescribeLayer("top chrome decoration Border", topChromeDecor));

        Check("D.nav_band_decor_effect",
            navDecor != null && navDecor.Effect != null
            && navDecor.Child == null && VisualTreeHelper.GetChildrenCount(navDecor) == 0,
            DescribeLayer("nav band decoration Border", navDecor));

        // ============================================================================
        // E. Inactive-window scrims
        // ============================================================================
        // Located by style identity (StaticResource -> same instance) with a structural fallback:
        // any Border whose Style carries a DataTrigger bound to Window.IsActive.
        Style? inactiveStyle = window.TryFindResource("InactiveScrim") as Style;
        Style? edgeStyle = window.TryFindResource("ShellEdgeScrim") as Style;
        List<Border> scrims = EnumerateVisual<Border>(window)
            .Where(b => ReferenceEquals(b.Style, inactiveStyle)
                        || ReferenceEquals(b.Style, edgeStyle)
                        || HasIsActiveTrigger(b.Style))
            .ToList();

        Check("E.scrims_found", scrims.Count >= 2,
            $"found {scrims.Count} scrim Border(s) using the InactiveScrim/ShellEdgeScrim styles (or an IsActive DataTrigger)");

        // The harness convention is to avoid stealing focus, so the probe is never activated explicitly
        // (it is shown with ShowActivated=false). If the environment nevertheless reports it active, the
        // active-state opacity is measurable; otherwise the inactive state is the natural state and the
        // active-state opacity is recorded as unobservable - a limitation, never a failure.
        bool windowActive = window.IsActive;
        double maxActiveOpacity = scrims.Count == 0 ? -1 : scrims.Max(s => s.Opacity);
        bool activeOpacityOk = windowActive && maxActiveOpacity < 0.02;
        Check("E.scrim_opacity_when_active", activeOpacityOk || !windowActive,
            windowActive
                ? $"window IsActive=true; max scrim Opacity={maxActiveOpacity:0.###} (expected 0 while active)"
                : "window IsActive=false (probe shown with ShowActivated=false and never activated, to avoid stealing "
                  + "focus); active-state opacity is not observable without an explicit Activate()");
        if (!windowActive)
        {
            limitations.Add("The off-screen probe window is not active (ShowActivated=false; the harness avoids "
                            + "stealing focus by never calling Activate()), so the scrim opacity in the active state "
                            + "was not observed live; the IsActive trigger was verified structurally only.");
        }

        List<object> scrimReports = new List<object>();
        bool triggerStructureOk = scrims.Count > 0;
        foreach (Border scrim in scrims)
        {
            (List<double> enter, List<double> exit, string binding) = ActiveTrigger(scrim.Style);
            bool ok = binding.Contains("IsActive") && enter.Count > 0 && exit.Count > 0;
            triggerStructureOk &= ok;
            scrimReports.Add(new Dictionary<string, object?>
            {
                ["style"] = ReferenceEquals(scrim.Style, inactiveStyle) ? "InactiveScrim"
                    : ReferenceEquals(scrim.Style, edgeStyle) ? "ShellEdgeScrim" : "unknown",
                ["binding"] = binding,
                ["enter_targets"] = enter,
                ["exit_targets"] = exit,
                ["opacity_while_active"] = Math.Round(scrim.Opacity, 4),
            });
        }

        Check("E.scrim_trigger_structure", triggerStructureOk,
            $"{scrims.Count} scrim(s); each Style has a DataTrigger on Window.IsActive with EnterActions/ExitActions animating Opacity "
            + $"({string.Join(", ", scrimReports.Select(r => ((Dictionary<string, object?>)r)["style"] + " enter=" + string.Join("/", (List<double>)((Dictionary<string, object?>)r)["enter_targets"]!)))}); "
            + "enter target 0.5 = InactiveScrim, 0.55 = ShellEdgeScrim, exit target 0");

        // Observe the inactive state without taking focus from the user's current application.
        // ShowActivated=false applies to both windows; the active-state transition is not forced.
        // The structural trigger check above covers the unobserved active-state branch.
        bool inactiveObserved = false;
        bool windowActiveDuringInactiveTest = true;
        double maxInactiveOpacity = -1;
        Window? activator = null;
        try
        {
            activator = new Window
            {
                WindowStyle = WindowStyle.None,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = -4000,
                Width = 200,
                Height = 200,
                ShowActivated = false,
                ShowInTaskbar = false,
                Background = Brushes.Transparent,
                Title = "WindowShellAudit-activator",
            };
            activator.Show();
            Pump(TimeSpan.FromMilliseconds(900));
            windowActiveDuringInactiveTest = window.IsActive;
            maxInactiveOpacity = scrims.Count == 0 ? -1 : scrims.Max(s => s.Opacity);
            inactiveObserved = !window.IsActive && maxInactiveOpacity > 0.05;
        }
        catch (Exception ex)
        {
            limitations.Add($"Observing the inactive state threw {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { activator?.Close(); } catch { /* the activator is disposable; closing is best effort */ }
            Pump(TimeSpan.FromMilliseconds(200));
        }

        if (inactiveObserved)
        {
            lines.Add($"[INFO] E.inactive_observed: true - window.IsActive={window.IsActive}, max scrim Opacity={maxInactiveOpacity:0.###}; activation was not forced");
        }
        else
        {
            limitations.Add("The inactive opacity was not observed live (window.IsActive="
                            + $"{windowActiveDuringInactiveTest}; both off-screen windows use ShowActivated=false); "
                            + "the scrim IsActive trigger is verified structurally; activation was not forced.");
            lines.Add($"[INFO] E.inactive_observed: false - window.IsActive={windowActiveDuringInactiveTest}, max scrim Opacity={maxInactiveOpacity:0.###}");
        }

        // ============================================================================
        // F. Navigation capsule rim + alignment
        // ============================================================================
        Border? capsuleBorder = capsule as Border;
        Check("F.capsule_hit_test", !capsule!.IsHitTestVisible,
            $"NavCapsule.IsHitTestVisible={capsule.IsHitTestVisible} (expected False)");

        TransformGroup? capsuleTransform = capsule.RenderTransform as TransformGroup;
        bool hasScale = capsuleTransform != null && capsuleTransform.Children.OfType<ScaleTransform>().Any();
        bool hasTranslate = capsuleTransform != null && capsuleTransform.Children.OfType<TranslateTransform>().Any();
        Check("F.capsule_transform_group", capsuleTransform != null && hasScale && hasTranslate,
            $"NavCapsule.RenderTransform={capsule.RenderTransform?.GetType().Name ?? "null"} children=["
            + $"{string.Join(", ", (capsuleTransform?.Children ?? new TransformCollection()).Select(c => c.GetType().Name))}] "
            + "(need a TransformGroup containing both ScaleTransform and TranslateTransform)");

        Border? rimBorder = capsuleBorder?.Child as Border;
        Check("F.capsule_rim_brush", rimBorder?.BorderBrush != null,
            $"NavCapsule child Border={(rimBorder == null ? "none" : "present")} BorderBrush="
            + $"{(rimBorder?.BorderBrush?.GetType().Name ?? "null")} (expected non-null vertical rim brush)");

        Check("F.capsule_measured_size", capsule.ActualWidth > 0 && capsule.ActualHeight > 0,
            $"NavCapsule ActualSize={capsule.ActualWidth:0.#}x{capsule.ActualHeight:0.#} (expected non-zero)");

        // Warm up the Loaded-priority capsule placement, then select a middle item and read the resting offset.
        nav!.SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(450));
        nav.SelectedIndex = 3;
        Pump(TimeSpan.FromMilliseconds(520));

        int navIndex = nav.SelectedIndex;
        FrameworkElement? navItem = nav.ItemContainerGenerator.ContainerFromIndex(navIndex) as FrameworkElement;
        double adx = double.NaN, ady = double.NaN, adw = double.NaN, adh = double.NaN;
        bool aligned = false;
        if (navItem != null && capsuleLayer != null)
        {
            Point itemOrigin = navItem.TranslatePoint(new Point(0, 0), capsuleLayer);
            Point capsuleOrigin = CapsuleRestingOffset(capsule);
            adx = Math.Abs(itemOrigin.X - capsuleOrigin.X);
            ady = Math.Abs(itemOrigin.Y - capsuleOrigin.Y);
            adw = Math.Abs(navItem.ActualWidth - capsule.ActualWidth);
            adh = Math.Abs(navItem.ActualHeight - capsule.ActualHeight);
            aligned = adx < 1.5 && ady < 1.5 && adw < 1.5 && adh < 1.5;
        }

        Check("F.capsule_aligned_to_selection", aligned,
            $"selected nav index {navIndex}: item=({navItem?.ActualWidth:0.#}x{navItem?.ActualHeight:0.#}) "
            + $"capsule=({capsule.ActualWidth:0.#}x{capsule.ActualHeight:0.#}) delta=({adx:0.#},{ady:0.#},{adw:0.#},{adh:0.#}) (each must be <1.5)");

        // ============================================================================
        // G. Render evidence
        // ============================================================================
        double fullScale = 1.0;
        int zoom = 6;
        int cropSize = 60;
        string fullPath = Path.Combine(outDir, "window-shell-full.png");
        RenderWhole(window, fullPath, fullScale);

        RenderTargetBitmap zoomed = RenderWhole(window, null, zoom);
        string topLeftPath = Path.Combine(outDir, "window-shell-corner-topleft.png");
        CropRegion(zoomed, topLeftPath, zoom, 0, 0, cropSize, cropSize);
        string bottomRightPath = Path.Combine(outDir, "window-shell-corner-bottomright.png");
        CropRegion(zoomed, bottomRightPath, zoom,
            window.ActualWidth - cropSize, window.ActualHeight - cropSize, cropSize, cropSize);

        lines.Add($"render full: {Path.GetFileName(fullPath)} {new FileInfo(fullPath).Length} bytes @{fullScale:0.#}x");
        lines.Add($"render topleft: {Path.GetFileName(topLeftPath)} {new FileInfo(topLeftPath).Length} bytes @{zoom}x crop=(0,0,{cropSize},{cropSize})");
        lines.Add($"render bottomright: {Path.GetFileName(bottomRightPath)} {new FileInfo(bottomRightPath).Length} bytes @{zoom}x "
                  + $"crop=({window.ActualWidth - cropSize:0},{window.ActualHeight - cropSize:0},{cropSize},{cropSize})");

        // ============================================================================
        // Verdict + JSON
        // ============================================================================
        bool passed = checks.All(c => (bool)c["passed"]!);
        lines.Add($"verdict: {(passed ? "PASS" : "FAIL")} ({checks.Count(c => (bool)c["passed"]!)}/{checks.Count} checks passed)");

        Dictionary<string, object?> report = new Dictionary<string, object?>
        {
            ["verdict"] = passed ? "PASS" : "FAIL",
            ["passed"] = passed,
            ["stale_assembly"] = false,
            ["checks"] = checks,
            ["decoration_layers"] = decorationLayers,
            ["effect_inventory"] = new Dictionary<string, object?>
            {
                ["note"] = "Informational only; never affects `passed`. Every element in the window's visual tree "
                         + "with a non-null Effect, with its TextBlock descendant count. A DropShadowEffect on an "
                         + "ancestor forces the subtree into an intermediate render surface, downgrading text from "
                         + "ClearType sub-pixel antialiasing to grayscale antialiasing. The function cards carry "
                         + "GlassShadowSoft on their own Border (pre-existing FeatureCardStyle from the previous "
                         + "round) and contain text, so card text is grayscale-antialiased - a real, pre-existing, "
                         + "out-of-scope condition recorded here, not hidden and not fixed.",
                ["count"] = effectElementCount,
                ["with_textblock_descendants"] = effectWithTextCount,
                ["items"] = effectInventory,
            },
            ["shell"] = new Dictionary<string, object?>
            {
                ["margin"] = Fmt(shellMargin),
                ["corner_radius"] = shellRadius,
                ["is_hit_test_visible"] = shell.IsHitTestVisible,
                ["effect"] = shellEffect == null ? null : new Dictionary<string, object?>
                {
                    ["type"] = shellEffect.GetType().Name,
                    ["blur_radius"] = shellEffect.BlurRadius,
                    ["shadow_depth"] = shellEffect.ShadowDepth,
                    ["opacity"] = shellEffect.Opacity,
                    ["direction"] = shellEffect.Direction,
                },
            },
            ["window_radius"] = new Dictionary<string, object?>
            {
                ["windowchrome_corner_radius"] = chromeRadius,
                ["content_layer_corner_radius"] = contentRadius,
                ["content_layer_located_via"] = contentLayerMethod,
                ["content_clip_type"] = contentLayer?.Clip?.GetType().Name,
                ["content_clip_radius_x"] = contentClip?.RadiusX,
                ["content_clip_radius_y"] = contentClip?.RadiusY,
            },
            ["specular"] = new Dictionary<string, object?>
            {
                ["resource_present"] = specularBrush != null,
                ["found"] = specLine != null,
                ["height"] = specLine?.Height,
                ["vertical_alignment"] = specLine?.VerticalAlignment.ToString(),
                ["is_hit_test_visible"] = specLine?.IsHitTestVisible,
                ["grid_row"] = specLine != null ? Grid.GetRow(specLine) : (int?)null,
                ["y_in_content"] = double.IsNaN(specularY) ? (double?)null : Math.Round(specularY, 1),
                ["in_top_row"] = inTopRow,
            },
            ["tool_layers"] = new Dictionary<string, object?>
            {
                ["top_chrome"] = DescribeLayerObject(topChromeDecor),
                ["nav_band"] = DescribeLayerObject(navDecor),
            },
            ["scrims"] = new Dictionary<string, object?>
            {
                ["found"] = scrims.Count,
                ["items"] = scrimReports,
                ["window_is_active"] = windowActive,
                ["max_opacity_while_active"] = scrims.Count == 0 ? (double?)null : Math.Round(maxActiveOpacity, 4),
                ["inactive_observed"] = inactiveObserved,
                ["activation_forced"] = false,
                ["window_is_active_during_inactive_test"] = windowActiveDuringInactiveTest,
                ["max_opacity_observed_inactive"] = maxInactiveOpacity < 0 ? (double?)null : Math.Round(maxInactiveOpacity, 4),
            },
            ["nav_capsule"] = new Dictionary<string, object?>
            {
                ["is_hit_test_visible"] = capsule.IsHitTestVisible,
                ["transform_type"] = capsule.RenderTransform?.GetType().Name,
                ["has_scale_transform"] = hasScale,
                ["has_translate_transform"] = hasTranslate,
                ["child_rim_border_brush"] = rimBorder?.BorderBrush?.GetType().Name,
                ["actual_width"] = Math.Round(capsule.ActualWidth, 1),
                ["actual_height"] = Math.Round(capsule.ActualHeight, 1),
                ["selected_nav_index"] = navIndex,
                ["alignment_delta"] = new Dictionary<string, object?>
                {
                    ["dx"] = double.IsNaN(adx) ? (double?)null : Math.Round(adx, 2),
                    ["dy"] = double.IsNaN(ady) ? (double?)null : Math.Round(ady, 2),
                    ["dw"] = double.IsNaN(adw) ? (double?)null : Math.Round(adw, 2),
                    ["dh"] = double.IsNaN(adh) ? (double?)null : Math.Round(adh, 2),
                },
                ["aligned"] = aligned,
            },
            ["renders"] = new Dictionary<string, object?>
            {
                ["full_scale"] = fullScale,
                ["zoom"] = zoom,
                ["crop_size"] = cropSize,
                ["files"] = new object[]
                {
                    new Dictionary<string, object?> { ["name"] = "window-shell-full.png", ["bytes"] = new FileInfo(fullPath).Length },
                    new Dictionary<string, object?> { ["name"] = "window-shell-corner-topleft.png", ["bytes"] = new FileInfo(topLeftPath).Length, ["crop"] = new[] { 0, 0, cropSize, cropSize } },
                    new Dictionary<string, object?> { ["name"] = "window-shell-corner-bottomright.png", ["bytes"] = new FileInfo(bottomRightPath).Length, ["crop"] = new[] { (int)(window.ActualWidth - cropSize), (int)(window.ActualHeight - cropSize), cropSize, cropSize } },
                },
            },
            ["limitations"] = limitations,
        };

        File.WriteAllText(Path.Combine(outDir, "window-shell-audit.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

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
    /// Reads the capsule's FINAL resting offset. The probe window sits off-screen and does not reliably
    /// get render ticks, so clearing the animation clocks falls the property back to the base value the
    /// window wrote - the deterministic resting target. (Same technique as NavCapsuleAudit.cs.)
    /// </summary>
    private static Point CapsuleRestingOffset(FrameworkElement capsule)
    {
        if (capsule.RenderTransform is not TransformGroup group)
        {
            return new Point(double.NaN, double.NaN);
        }

        TranslateTransform? transform = group.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (transform == null)
        {
            return new Point(double.NaN, double.NaN);
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        return new Point(transform.X, transform.Y);
    }

    private static bool HasIsActiveTrigger(Style? style)
    {
        if (style == null)
        {
            return false;
        }

        foreach (TriggerBase trigger in style.Triggers)
        {
            if (trigger is DataTrigger dataTrigger
                && dataTrigger.Binding is Binding binding
                && binding.Path?.Path != null
                && binding.Path.Path.Contains("IsActive"))
            {
                return true;
            }
        }

        return false;
    }

    private static (List<double> Enter, List<double> Exit, string Binding) ActiveTrigger(Style? style)
    {
        List<double> enter = new List<double>();
        List<double> exit = new List<double>();
        string binding = string.Empty;
        if (style == null)
        {
            return (enter, exit, binding);
        }

        foreach (TriggerBase trigger in style.Triggers)
        {
            if (trigger is not DataTrigger dataTrigger)
            {
                continue;
            }

            if (dataTrigger.Binding is Binding b && b.Path?.Path != null && b.Path.Path.Contains("IsActive"))
            {
                binding = b.Path.Path;
                CollectOpacity(dataTrigger.EnterActions, enter);
                CollectOpacity(dataTrigger.ExitActions, exit);
            }
        }

        return (enter, exit, binding);
    }

    private static void CollectOpacity(TriggerActionCollection actions, List<double> into)
    {
        foreach (TriggerAction action in actions)
        {
            if (action is BeginStoryboard begin && begin.Storyboard != null)
            {
                foreach (Timeline timeline in begin.Storyboard.Children)
                {
                    if (timeline is DoubleAnimation animation && animation.To.HasValue)
                    {
                        into.Add(animation.To.Value);
                    }
                }
            }
        }
    }

    private static string DescribeLayer(string what, Border? border)
    {
        if (border == null)
        {
            return $"{what}: NOT FOUND";
        }

        DropShadowEffect? dse = border.Effect as DropShadowEffect;
        return $"{what}: Child={(border.Child == null ? "null" : border.Child.GetType().Name)} "
               + $"visualChildren={VisualTreeHelper.GetChildrenCount(border)} Effect={(border.Effect?.GetType().Name ?? "null")}"
               + (dse != null ? $" BlurRadius={dse.BlurRadius} ShadowDepth={dse.ShadowDepth} Opacity={dse.Opacity} Direction={dse.Direction}" : string.Empty);
    }

    private static Dictionary<string, object?>? DescribeLayerObject(Border? border)
    {
        if (border == null)
        {
            return null;
        }

        DropShadowEffect? dse = border.Effect as DropShadowEffect;
        return new Dictionary<string, object?>
        {
            ["child"] = border.Child?.GetType().Name,
            ["visual_children"] = VisualTreeHelper.GetChildrenCount(border),
            ["effect_type"] = border.Effect?.GetType().Name,
            ["blur_radius"] = dse?.BlurRadius,
            ["shadow_depth"] = dse?.ShadowDepth,
            ["opacity"] = dse?.Opacity,
            ["direction"] = dse?.Direction,
        };
    }

    private static bool SameBrush(Brush? a, Brush? b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is LinearGradientBrush la && b is LinearGradientBrush lb)
        {
            if (la.GradientStops.Count != lb.GradientStops.Count
                || la.StartPoint != lb.StartPoint || la.EndPoint != lb.EndPoint)
            {
                return false;
            }

            for (int i = 0; i < la.GradientStops.Count; i++)
            {
                if (la.GradientStops[i].Color != lb.GradientStops[i].Color
                    || Math.Abs(la.GradientStops[i].Offset - lb.GradientStops[i].Offset) > 1e-6)
                {
                    return false;
                }
            }

            return true;
        }

        return a is SolidColorBrush sa && b is SolidColorBrush sb && sa.Color == sb.Color;
    }

    private static string Fmt(Thickness t)
    {
        return $"{t.Left:0.#},{t.Top:0.#},{t.Right:0.#},{t.Bottom:0.#}";
    }

    private static void Collect(DependencyObject root, List<DependencyObject> into)
    {
        into.Add(root);
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            Collect(VisualTreeHelper.GetChild(root, index), into);
        }
    }

    private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int total = 0;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T)
            {
                total++;
            }

            total += CountDescendants<T>(child);
        }

        return total;
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

    // Renders the whole window and crops - never render a child element on its own, that yields a black
    // bitmap in this codebase (see CornerZoomAudit.cs).
    private static RenderTargetBitmap RenderWhole(Window window, string? file, double scale)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        if (file != null)
        {
            SavePng(bitmap, file);
        }

        return bitmap;
    }

    private static void CropRegion(BitmapSource full, string file, double scale, double x, double y, double w, double h)
    {
        int cx = Math.Max(0, (int)Math.Round(x * scale));
        int cy = Math.Max(0, (int)Math.Round(y * scale));
        int cw = Math.Min((int)Math.Round(w * scale), full.PixelWidth - cx);
        int ch = Math.Min((int)Math.Round(h * scale), full.PixelHeight - cy);
        if (cw <= 1 || ch <= 1)
        {
            Console.WriteLine($"  crop {Path.GetFileName(file)} outside bitmap (x={cx} y={cy} w={cw} h={ch})");
            return;
        }

        CroppedBitmap crop = new CroppedBitmap(full, new Int32Rect(cx, cy, cw, ch));
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
            System.Threading.Thread.Sleep(8);
        }
    }
}
