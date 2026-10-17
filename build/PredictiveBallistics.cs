using System;
using System.Numerics;

namespace AbyssalCrab.Runtime
{
    // Native local projectiles advance position first, then velocity, with no inherited shooter
    // velocity or air drag. On each fixed-time interval the gravity displacement is linear in t,
    // so the intercept is a quadratic rather than an approximation of a continuous parabola.
    public static class PredictiveBallistics
    {
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        private static bool FiniteVector(Vector3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);

        private static bool Root(double a, double b, double c, double low, double high, out double t)
        {
            t = double.PositiveInfinity;
            if (Math.Abs(a) <= 1e-12 * Math.Max(1, Math.Abs(b) + Math.Abs(c)))
            {
                if (Math.Abs(b) < 1e-12) return false;
                double linear = -c / b;
                if (linear > 1e-5 && linear >= low - 1e-8 && linear <= high + 1e-8) t = linear;
            }
            else
            {
                double d = b * b - 4 * a * c;
                if (d < 0 && d > -1e-10 * Math.Max(1, b*b + Math.Abs(4*a*c))) d = 0;
                if (d < 0 || !Finite(d)) return false;
                double q = -.5 * (b + (b >= 0 ? 1 : -1) * Math.Sqrt(d));
                double first = q / a;
                double second = Math.Abs(q) > 1e-20 ? c / q : -b / (2*a);
                if (first > 1e-5 && first >= low-1e-8 && first <= high+1e-8) t = first;
                if (second > 1e-5 && second >= low-1e-8 && second <= high+1e-8 && second < t) t = second;
            }
            return Finite(t);
        }

        public static double Drop(double gravity, double t, double step)
        {
            if (!Finite(gravity) || !Finite(t) || !Finite(step) || t < 0 || step <= 0) return double.NaN;
            double n = Math.Floor(t / step);
            return gravity * (n * step * t - .5 * step * step * n * (n + 1));
        }

        public static bool TryIntercept(Vector3 relativePosition, Vector3 targetVelocity, double speed,
            double gravity, double fixedStep, double maxTime, out Vector3 launchDisplacement, out double flightTime)
        {
            launchDisplacement = relativePosition; flightTime = 0;
            if (!FiniteVector(relativePosition) || !FiniteVector(targetVelocity) || !Finite(speed) || speed <= .01 ||
                !Finite(gravity) || !Finite(fixedStep) || fixedStep < .001 || fixedStep > .2 ||
                !Finite(maxTime) || maxTime <= 0 || maxTime > 8 || relativePosition.LengthSquared() < 1e-8f) return false;
            int intervals = Math.Abs(gravity) < 1e-8 ? 1 : (int)Math.Ceiling(maxTime / fixedStep);
            for (int n = 0; n < intervals; n++)
            {
                double constantDrop = -.5 * gravity * fixedStep * fixedStep * n * (n + 1d);
                double rx = relativePosition.X, ry = relativePosition.Y + constantDrop, rz = relativePosition.Z;
                double vx = targetVelocity.X, vy = targetVelocity.Y + gravity * n * fixedStep, vz = targetVelocity.Z;
                double a = vx*vx + vy*vy + vz*vz - speed*speed;
                double b = 2*(rx*vx + ry*vy + rz*vz);
                double c = rx*rx + ry*ry + rz*rz;
                double low = intervals == 1 ? 0 : n * fixedStep;
                double high = intervals == 1 ? maxTime : Math.Min(maxTime, (n+1)*fixedStep);
                if (!Root(a,b,c,low,high,out double t)) continue;
                double x = relativePosition.X + targetVelocity.X*t;
                double y = relativePosition.Y + targetVelocity.Y*t + Drop(gravity,t,fixedStep);
                double z = relativePosition.Z + targetVelocity.Z*t;
                if (!Finite(x) || !Finite(y) || !Finite(z)) return false;
                launchDisplacement = new Vector3((float)x,(float)y,(float)z);
                flightTime = t;
                return FiniteVector(launchDisplacement);
            }
            return false;
        }
    }
}
