using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HowToFishTrainer.App.Licensing;

public sealed class DpapiLicenseStorage : ILicenseStorage
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AbyssalCrabStudio-HowToFish-LicenseCache-v1");

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly string _path;

	public DpapiLicenseStorage(string appDataDirectory)
	{
		_path = Path.Combine(appDataDirectory, "license.dat");
	}

	public async Task<LicenseStoredData?> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!File.Exists(_path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<LicenseStoredData>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, cancellationToken), Entropy, DataProtectionScope.CurrentUser), JsonOptions);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}
	}

	public async Task SaveAsync(LicenseStoredData data, CancellationToken cancellationToken = default(CancellationToken))
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_path));
		byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions), Entropy, DataProtectionScope.CurrentUser);
		string temporaryPath = _path + ".tmp";
		try
		{
			await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
			File.Move(temporaryPath, _path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	public Task ClearAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (File.Exists(_path))
		{
			File.Delete(_path);
		}
		string path = _path + ".tmp";
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		return Task.CompletedTask;
	}
}
