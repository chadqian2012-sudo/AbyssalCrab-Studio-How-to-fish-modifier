using System;
using System.Collections.Generic;
using System.Linq;

namespace HowToFishTrainer.App;

public static class IslandNameLocalizer
{
	private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["Coral Island"] = "珊瑚岛",
		["DevIsland"] = "开发者岛"
	};

	public static string Localize(int index, string? localizedName, string? rawName)
	{
		if (!string.IsNullOrWhiteSpace(localizedName) && localizedName.Any((char ch) => ch >= '一' && ch <= '鿿'))
		{
			return localizedName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(rawName) && Names.TryGetValue(rawName.Trim(), out string value))
		{
			return value;
		}
		if (!string.IsNullOrWhiteSpace(rawName) && rawName.Trim().StartsWith("Island", StringComparison.OrdinalIgnoreCase))
		{
			string text = rawName.Trim();
			int length = "Island".Length;
			if (int.TryParse(text.Substring(length, text.Length - length), out var result))
			{
				return $"小岛 {result}";
			}
		}
		if (!string.IsNullOrWhiteSpace(rawName) && rawName.Trim().StartsWith("Mini", StringComparison.OrdinalIgnoreCase))
		{
			string text = rawName.Trim();
			int length = "Mini".Length;
			if (int.TryParse(text.Substring(length, text.Length - length), out var result2))
			{
				return $"海上小岛 {result2}";
			}
		}
		if (string.IsNullOrWhiteSpace(rawName))
		{
			return $"岛屿 {index}";
		}
		return rawName.Trim();
	}
}
