using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace HowToFishTrainer.App.Licensing;

public sealed class FileLicenseLog : ILicenseLog
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

	// 反编译还原：正则实现体在 System/Text/RegularExpressions/Generated/*.cs 中已展开，
	// 这里只保留取值方法，去掉 [GeneratedRegex] 标注，避免生成器因原始分部方法丢失而报 SYSLIB1043。
	// 行为不变：返回的仍是同一个编译期生成的 Regex 实例（LineBreaks / ActivationCodes / DeviceIds）。
	private static Regex LineBreaks()
	{
		return _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__LineBreaks_2.Instance;
	}

	private static Regex ActivationCodes()
	{
		return _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__ActivationCodes_3.Instance;
	}

	private static Regex DeviceIds()
	{
		return _003CRegexGenerator_g_003EFE84FE87364597231FD098E693BAC4062D96F3CA79D61134866D40051DE476093__DeviceIds_4.Instance;
	}
}
