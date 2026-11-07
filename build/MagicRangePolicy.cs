using System;

namespace AbyssalCrab.Runtime
{
    public static class MagicRangePolicy
    {
        // angle is the half-angle used by GameplayTargeting.Select, not a diameter in degrees.
        public static bool Project(double angle, double verticalFov, double width, double height, out double radius, out bool abbreviated)
        {
            radius = 0; abbreviated = false;
            if (double.IsNaN(angle) || double.IsInfinity(angle) || angle < .5 || angle > 180 ||
                double.IsNaN(verticalFov) || double.IsInfinity(verticalFov) || verticalFov <= 0 || verticalFov >= 180 ||
                double.IsNaN(width) || double.IsInfinity(width) || width <= 0 ||
                double.IsNaN(height) || double.IsInfinity(height) || height <= 0) return false;
            double limit = Math.Min(width, height) * .46;
            double projected = angle >= 90 ? double.PositiveInfinity : height * .5 * Math.Tan(angle * Math.PI / 180) / Math.Tan(verticalFov * Math.PI / 360);
            abbreviated = projected > limit;
            radius = abbreviated ? limit : projected;
            return radius > 0;
        }
    }
}
