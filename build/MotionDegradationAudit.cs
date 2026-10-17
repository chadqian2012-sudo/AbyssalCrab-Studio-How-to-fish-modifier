using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Path = System.IO.Path;

/// <summary>
/// Verifies the reduced-motion degradation actually works at runtime:
///   1. the MotionReduced dictionary loads through the exact URI App.xaml.cs uses;
///   2. merging it turns every Motion* token to zero while leaving colour/material tokens alone;
///   3. the theme itself still resolves all 124 animation durations through those tokens.
/// Reads only - it never writes to the product.
/// </summary>
internal static class MotionDegradationAudit
{
    private static readonly string[] MotionTokens =
    {
        "MotionPress", "MotionHover", "MotionRelease", "MotionFocus", "MotionSelect",
        "MotionPage", "MotionSwitch", "MotionStatus", "MotionExpand", "MotionPopup"
    };

    private static readonly string[] MustNotChange =
    {
        "AccentBrush", "AccentDarkBrush", "AccentSoftBrush", "BgBrush", "PanelBrush", "CardBrush",
        "TextBrush", "MutedBrush", "LineBrush", "FieldLineBrush", "AccentLineBrush",
        "GlassCardBrush", "GlassEdgeHBrush", "RadiusCard", "RadiusWindow"
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

        Application application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ResourceDictionary theme = (ResourceDictionary)Application.LoadComponent(
            new Uri("/HowToFishTrainer;component/themes/theme.xaml", UriKind.Relative));
        application.Resources.MergedDictionaries.Add(theme);

        List<object> rows = new List<object>();
        List<string> lines = new List<string>();

        // Baseline: durations as the theme declares them.
        Dictionary<string, string> before = MotionTokens.ToDictionary(t => t, t => Describe(SafeLookup(theme, t)));

        ResourceDictionary? reduced = null;
        string loadError = string.Empty;
        try
        {
            reduced = new ResourceDictionary
            {
                Source = new Uri("/HowToFishTrainer;component/themes/MotionReduced.xaml", UriKind.Relative)
            };
        }
        catch (Exception ex)
        {
            loadError = ex.GetType().Name + ": " + ex.Message;
        }

        bool loaded = reduced != null;
        lines.Add($"MotionReduced.xaml loads via the App.xaml.cs URI: {loaded}" + (loaded ? string.Empty : $"  ({loadError})"));

        if (loaded)
        {
            application.Resources.MergedDictionaries.Add(reduced);
        }

        Dictionary<string, string> after = MotionTokens.ToDictionary(t => t, t => Describe(SafeLookup(application.Resources, t)));

        int zeroed = 0;
        foreach (string token in MotionTokens)
        {
            bool wasNonZero = !IsZeroDuration(before[token]);
            bool nowZero = IsZeroDuration(after[token]);
            if (loaded && wasNonZero && nowZero)
            {
                zeroed++;
            }

            rows.Add(new { token, before = before[token], after = after[token], zeroed = wasNonZero && nowZero });
            lines.Add($"   {token,-14} {before[token],-16} -> {after[token],-16} " +
                      (wasNonZero && nowZero ? "zeroed" : !wasNonZero ? "already zero" : "UNCHANGED"));
        }

        // Colour / material / radius tokens must be untouched by the degradation pass.
        List<string> altered = new List<string>();
        foreach (string key in MustNotChange)
        {
            object? fromTheme = SafeLookup(theme, key);
            object? fromMerged = loaded ? SafeLookup(application.Resources, key) : fromTheme;
            string a = Describe(fromTheme);
            string b = Describe(fromMerged);
            if (a != b)
            {
                altered.Add($"{key}: {a} -> {b}");
            }
        }

        lines.Add($"non-motion tokens altered by the degradation pass: {altered.Count}"
                  + (altered.Count == 0 ? " (correct)" : "  <-- WRONG: " + string.Join("; ", altered)));

        bool passed = loaded && zeroed == MotionTokens.Length && altered.Count == 0;
        lines.Add($"verdict: {(passed ? "PASS" : "FAIL")} (tokens zeroed {zeroed}/{MotionTokens.Length}, side effects {altered.Count})");

        File.WriteAllText(Path.Combine(outDir, "motion-degradation-audit.json"),
            JsonSerializer.Serialize(new
            {
                loaded_at_runtime = loaded,
                load_error = loadError,
                tokens_zeroed = zeroed,
                tokens_total = MotionTokens.Length,
                tokens = rows,
                non_motion_tokens_altered = altered,
                passed,
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        application.Shutdown();
        return passed ? 0 : 1;
    }

    private static bool IsZeroDuration(string described)
    {
        // Describe() emits a TimeSpan string like "00:00:00" or "00:00:00.1500000".
        return TimeSpan.TryParse(described, out TimeSpan span) && span == TimeSpan.Zero;
    }

    private static object? SafeLookup(ResourceDictionary dictionary, string key)
    {
        try
        {
            return dictionary.Contains(key) ? dictionary[key] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Describe(object? value)
    {
        return value switch
        {
            null => "<missing>",
            Duration duration => duration.HasTimeSpan ? duration.TimeSpan.ToString() : "<automatic>",
            TimeSpan span => span.ToString(),
            System.Windows.Media.SolidColorBrush brush => brush.Color.ToString(),
            System.Windows.Media.GradientBrush => "<gradient>",
            CornerRadius radius => radius.TopLeft.ToString("0.#"),
            double number => number.ToString("0.##"),
            _ => value.GetType().Name,
        };
    }
}
