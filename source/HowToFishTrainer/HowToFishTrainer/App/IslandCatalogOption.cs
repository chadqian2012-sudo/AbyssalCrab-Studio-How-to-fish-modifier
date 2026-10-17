namespace HowToFishTrainer.App;

public sealed record IslandCatalogOption(string Key, int Index, int? RuntimeIndex, int SceneBuildIndex, string Name, string RawName, bool Available, string Reason, string LoadMode, bool Experimental)
{
	public string DisplayName
	{
		get
		{
			if (!Experimental)
			{
				return $"{Name} · 索引 {Index}";
			}
			return Name + " · 实验场景";
		}
	}
}
