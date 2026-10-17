using System.CodeDom.Compiler;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace HowToFishTrainer.App.Licensing;

public static class LicenseCode
{
	public static string Normalize(string? value)
	{
		return WhitespacePattern().Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, string.Empty);
	}

	public static bool IsValid(string? value)
	{
		return ActivationCodePattern().IsMatch(Normalize(value));
	}

	// 反编译还原：正则实现体在 System/Text/RegularExpressions/Generated/*.cs 中已展开，
	// 去掉 [GeneratedRegex] 标注以避免 SYSLIB1043；返回的实例与原来完全一致，校验语义不变。
	private static Regex WhitespacePattern()
	{
		return _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__WhitespacePattern_0.Instance;
	}

	private static Regex ActivationCodePattern()
	{
		return _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodePattern_1.Instance;
	}
}
