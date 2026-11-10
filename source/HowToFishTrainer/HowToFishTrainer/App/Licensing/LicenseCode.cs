using System.Text.RegularExpressions;

namespace HowToFishTrainer.App.Licensing;

public static partial class LicenseCode
{
	public static string Normalize(string? value)
	{
		return WhitespacePattern().Replace(value?.Trim().ToUpperInvariant() ?? string.Empty, string.Empty);
	}

	public static bool IsValid(string? value)
	{
		return ActivationCodePattern().IsMatch(Normalize(value));
	}

	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhitespacePattern();

	[GeneratedRegex(@"^ACST-HTF-PRO-[A-Z2-9]{5}(?:-[A-Z2-9]{5}){3}$", RegexOptions.CultureInvariant)]
	private static partial Regex ActivationCodePattern();
}
