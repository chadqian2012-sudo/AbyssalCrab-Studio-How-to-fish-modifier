namespace HowToFishTrainer.App;

public sealed record SkinOption(string Id, string Name, string Rarity)
{
	public string DisplayName
	{
		get
		{
			string text = SkinNameLocalizer.LocalizeName(Name);
			string text2 = SkinNameLocalizer.LocalizeRarity(Rarity);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text + "  ·  " + text2;
			}
			return text;
		}
	}
}
