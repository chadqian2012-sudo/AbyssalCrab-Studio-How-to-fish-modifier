using System;

namespace HowToFishTrainer.App.Licensing;

public static class LicenseConstants
{
	public const string ProductKey = "how-to-fish-trainer";

	public const string CodePrefix = "ACST-HTF-PRO";

	public const string DeviceScope = "AbyssalCrabStudio-HowToFish-LicenseDevice-v1";

	public const string CacheEntropy = "AbyssalCrabStudio-HowToFish-LicenseCache-v1";

	public static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(7.0);

	public static readonly Uri PrimaryEndpoint = new Uri("http://127.0.0.1:1/retired-licensing");

	public static readonly Uri BackupEndpoint = new Uri("http://127.0.0.1:1/retired-licensing");
}
