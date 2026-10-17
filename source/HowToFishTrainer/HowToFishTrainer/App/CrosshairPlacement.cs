using System;

namespace HowToFishTrainer.App;

public static class CrosshairPlacement
{
	public static bool IsValid(double x, double y)
	{
		if (double.IsFinite(x) && double.IsFinite(y) && x >= 0.0 && x <= 1.0 && y >= 0.0)
		{
			return y <= 1.0;
		}
		return false;
	}

	public static (int X, int Y) Position(int left, int top, int width, int height, double x, double y)
	{
		if (width <= 0 || height <= 0 || !IsValid(x, y))
		{
			throw new ArgumentOutOfRangeException("width");
		}
		return (X: (int)Math.Round((double)left + (double)width * x), Y: (int)Math.Round((double)top + (double)height * y));
	}
}
