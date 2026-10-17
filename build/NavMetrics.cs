using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// Measures the rendered width of each top-category label at several font sizes so the navigation
/// layout can be sized from data instead of guesswork.
/// </summary>
[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

internal static class NavMetrics
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string[] titles = { "玩家状态", "武器战斗", "钓鱼辅助", "金钱背包", "物品生成", "鱼类生成",
                            "世界传送", "房间成员", "解锁存档", "娱乐控制", "趣味事件" };
        string[] glyphs = { "🎮", "⚔", "🎣", "◇", "◈", "🐟", "⌖", "♙", "✦", "◎", "✧" };

        Typeface face = new Typeface(new FontFamily("Microsoft YaHei UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double dpi = 1.5;

        foreach (double size in new[] { 13.0, 12.5, 12.0 })
        {
            List<double> widths = new List<double>();
            foreach (string title in titles)
            {
                FormattedText text = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    face, size, Brushes.Black, dpi);
                widths.Add(text.WidthIncludingTrailingWhitespace);
            }

            double max = widths.Max();
            // Item = padding(2*13) + glyph column(30) + gap(8) + label + margin
            double itemWidth = 13 * 2 + 30 + 8 + max + 2;
            Console.WriteLine($"font {size,5:0.0}: maxLabel={max,6:0.0}  minItem={itemWidth,6:0.0}  " +
                              $"11 items={itemWidth * 11,7:0.0}  10 items={itemWidth * 10,7:0.0}");
        }

        Console.WriteLine();
        foreach (double size in new[] { 13.0, 12.0 })
        {
            Console.WriteLine($"-- per label at {size} --");
            for (int index = 0; index < titles.Length; index++)
            {
                FormattedText text = new FormattedText(titles[index], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    face, size, Brushes.Black, dpi);
                FormattedText glyph = new FormattedText(glyphs[index], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Emoji"), 14, Brushes.Black, dpi);
                Console.WriteLine($"   {titles[index],-6} label={text.WidthIncludingTrailingWhitespace,6:0.0}  glyph={glyph.WidthIncludingTrailingWhitespace,6:0.0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("available widths inside TopCategories (ListBox width minus 2*8 margin):");
        foreach (double window in new[] { 1180.0, 1240.0, 1290.0, 1320.0, 1380.0, 1460.0 })
        {
            Console.WriteLine($"   window {window,6:0} -> {window - 16,7:0.0}");
        }

        return 0;
    }
}
