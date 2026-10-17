using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace HowToFishTrainer.App;

public static class SkinNameLocalizer
{
	private static readonly ImmutableDictionary<string, string> NameTokens = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, string>
	{
		["Default"] = "默认",
		["Gold"] = "黄金",
		["Silver"] = "白银",
		["Obsidian"] = "黑曜",
		["Ivory"] = "象牙",
		["Rainbow"] = "彩虹",
		["Camo"] = "迷彩",
		["Wood"] = "木纹",
		["Rust"] = "锈蚀"
	});

	private static readonly ImmutableDictionary<string, string> RarityLabels = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, string>
	{
		["Common"] = "普通",
		["Uncommon"] = "优良",
		["Rare"] = "稀有",
		["Epic"] = "史诗",
		["Legendary"] = "传说"
	});

	private static readonly Regex Separators = new Regex("[\\s_-]+", RegexOptions.CultureInvariant);

	private static readonly Regex CamelCaseBoundaries = new Regex("(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant);

	public static string LocalizeName(string source)
	{
		string text = source?.Trim() ?? string.Empty;
		if (text.Length == 0)
		{
			return text;
		}
		string[] array = (from token in Separators.Split(CamelCaseBoundaries.Replace(text, " "))
			where token.Length > 0
			select token).ToArray();
		if (array.Length == 0)
		{
			return text;
		}
		string[] array2 = new string[array.Length];
		for (int num = 0; num < array.Length; num++)
		{
			if (!NameTokens.TryGetValue(array[num], out string value))
			{
				return text;
			}
			array2[num] = value;
		}
		return string.Concat(array2);
	}

	public static string LocalizeRarity(string source)
	{
		string text = source?.Trim() ?? string.Empty;
		if (!RarityLabels.TryGetValue(text, out string value))
		{
			return text;
		}
		return value;
	}
}
