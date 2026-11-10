using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HowToFishTrainer.App.Licensing;

public sealed partial class FileLicenseLog : ILicenseLog
{
	private readonly string _path;

	public FileLicenseLog(string appDataDirectory)
	{
		_path = Path.Combine(appDataDirectory, "license.log");
	}

	public void Write(LicenseLogEntry entry)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			string text = $"{DateTimeOffset.Now:O} route={Clean(entry.Route)} host={Clean(entry.Host)} status={entry.HttpStatus?.ToString() ?? "none"} code={Clean(entry.ServerCode)} network={Clean(entry.NetworkKind)} message={Clean(entry.Message)}";
			File.AppendAllText(_path, text + Environment.NewLine);
		}
		catch
		{
		}
	}

	private static string Clean(string? value)
	{
		string input = LineBreaks().Replace(value ?? string.Empty, " ").Trim();
		input = ActivationCodes().Replace(input, "[activation-code]");
		return DeviceIds().Replace(input, "[device-id]");
	}

	[GeneratedRegex(@"[\r\n\t]+")]
	private static partial Regex LineBreaks();

	[GeneratedRegex("ACST-[A-Z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ActivationCodes();

	[GeneratedRegex("win-[0-9a-f]{16,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex DeviceIds();
}
