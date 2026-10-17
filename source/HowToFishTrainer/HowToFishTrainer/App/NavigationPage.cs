using System.Runtime.CompilerServices;

namespace HowToFishTrainer.App;

public sealed record NavigationPage(string Title, string Category, string Glyph, bool RequiresSponsor = false)
{
	[CompilerGenerated]
	private readonly bool RequiresSponsor__BackingField;

	public bool RequiresSponsor
	{
		[CompilerGenerated]
		get
		{
			return false;
		}
		[CompilerGenerated]
		init
		{
			RequiresSponsor__BackingField = value;
		}
	}

	[CompilerGenerated]
	public void Deconstruct(out string Title, out string Category, out string Glyph, out bool RequiresSponsor)
	{
		Title = this.Title;
		Category = this.Category;
		Glyph = this.Glyph;
		RequiresSponsor = this.RequiresSponsor;
	}
}
