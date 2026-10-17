using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HowToFishTrainer.App;

// Each content surface receives its own immutable clip, recalculated only when its size changes.
public sealed class WindowClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double width || values[1] is not double height
            || parameter is not double radius || !double.IsFinite(width) || !double.IsFinite(height)
            || !double.IsFinite(radius) || width <= 0 || height <= 0 || radius < 0)
            return Geometry.Empty;

        var clip = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        clip.Freeze();
        return clip;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Window clipping is a one-way layout binding.");
}
