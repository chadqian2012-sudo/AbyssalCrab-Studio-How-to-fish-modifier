using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

internal static class SpinSpeedVerification
{
    public static void Main(string[] args)
    {
        var spin = Assembly.LoadFrom(args[0]).GetType("AbyssalCrab.Runtime.SpinRuntime", true);
        var read = spin.GetMethod("ReadSpeed");
        var yaw = spin.GetMethod("CalculateYaw");
        float Speed(JObject config) => (float)read.Invoke(null, new object[] { config });
        float Angle(float start, float elapsed, float speed) =>
            (float)yaw.Invoke(null, new object[] { start, elapsed, speed });
        void Expect(float actual, float expected)
        {
            if (!(Math.Abs(actual - expected) < .001f)) throw new Exception($"Expected {expected}, received {actual}");
        }
        Expect(Speed(new JObject()), 4f);
        foreach (double value in new[] { 2d, 15d, 29.5d, 30d })
            Expect(Speed(new JObject { ["rotationsPerSecond"] = value }), (float)value);
        foreach (double invalid in new[] { 1.99d, 30.1d, double.NaN, double.PositiveInfinity })
        {
            try { Speed(new JObject { ["rotationsPerSecond"] = invalid }); }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException) { continue; }
            throw new Exception($"Unexpectedly accepted {invalid}");
        }
        Expect(Angle(0f, 1f / 120f, 30f), 90f);
        Expect(Angle(345f, 1f / 120f, 30f), 75f);
        Expect(Angle(0f, 1f, 29.5f), 180f);
        Console.WriteLine("Spin speed checks passed: default 4, limits 2 and 30, decimal 29.5, invalid values and yaw wrap.");
    }
}
