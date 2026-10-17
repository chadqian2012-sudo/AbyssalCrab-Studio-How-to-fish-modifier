using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace HowToFishTrainer.App.Licensing;

public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
	private readonly string _fallbackPath;

	private readonly Func<string?> _machineGuidReader;

	private string? _cached;

	public WindowsDeviceIdentityProvider(string appDataDirectory, Func<string?>? machineGuidReader = null)
	{
		_fallbackPath = Path.Combine(appDataDirectory, "device.id");
		_machineGuidReader = machineGuidReader ?? new Func<string>(ReadMachineGuid);
	}

	public string GetDeviceId()
	{
		if (_cached != null)
		{
			return _cached;
		}
		string text = _machineGuidReader();
		if (string.IsNullOrWhiteSpace(text))
		{
			text = ReadOrCreateFallbackId();
		}
		_cached = ComputeDeviceId("AbyssalCrabStudio-HowToFish-LicenseDevice-v1", text);
		return _cached;
	}

	public static string ComputeDeviceId(string scope, string source)
	{
		byte[] inArray = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "|" + source.Trim()));
		return "win-" + Convert.ToHexString(inArray).ToLowerInvariant();
	}

	private static string? ReadMachineGuid()
	{
		try
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography", writable: false);
			return registryKey?.GetValue("MachineGuid")?.ToString();
		}
		catch
		{
			return null;
		}
	}

	private string ReadOrCreateFallbackId()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_fallbackPath));
		if (File.Exists(_fallbackPath))
		{
			string text = File.ReadAllText(_fallbackPath).Trim();
			if (Guid.TryParse(text, out var _))
			{
				return text;
			}
		}
		string text2 = Guid.NewGuid().ToString("D");
		File.WriteAllText(_fallbackPath, text2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		return text2;
	}
}
