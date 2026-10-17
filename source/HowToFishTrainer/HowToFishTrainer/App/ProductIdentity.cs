using System;
using System.IO;

namespace HowToFishTrainer.App;

public static class ProductIdentity
{
	public const string ProductName = "AbyssalCrab Studio";

	public const string GameName = "How to Fish";

	public const string AppVersion = "1.1.37";

	public static string AppDataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbyssalCrab Studio", "HowToFishTrainer");
}
