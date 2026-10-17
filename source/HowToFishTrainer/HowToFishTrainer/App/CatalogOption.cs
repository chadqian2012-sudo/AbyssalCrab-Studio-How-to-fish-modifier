using System;
using System.Collections.Generic;

namespace HowToFishTrainer.App;

public sealed record CatalogOption(int Id, string Name, bool IsFish, string Kind = "其他", int Worth = 0, double Damage = 0.0, IReadOnlyList<PerformanceStat>? Stats = null, IReadOnlyList<AttachmentOption>? SightOptions = null, IReadOnlyList<AttachmentOption>? BarrelOptions = null, IReadOnlyList<AttachmentOption>? AmmoOptions = null, bool SupportsExtendedMagazine = false, bool SupportsLaserSight = false, IReadOnlyList<SkinOption>? SkinOptions = null)
{
	public string DisplayName => $"{Name}  ·  ID {Id}";

	public IReadOnlyList<PerformanceStat> Performance => Stats ?? Array.Empty<PerformanceStat>();

	public IReadOnlyList<AttachmentOption> Sights => SightOptions ?? Array.Empty<AttachmentOption>();

	public IReadOnlyList<AttachmentOption> Barrels => BarrelOptions ?? Array.Empty<AttachmentOption>();

	public IReadOnlyList<AttachmentOption> Ammo => AmmoOptions ?? Array.Empty<AttachmentOption>();

	public IReadOnlyList<SkinOption> Skins => SkinOptions ?? Array.Empty<SkinOption>();

	public bool HasSkins => Skins.Count > 0;

	public bool IsWeapon
	{
		get
		{
			if (!string.Equals(Kind, "枪械", StringComparison.Ordinal) && !(Damage > 0.0))
			{
				return Sights.Count + Barrels.Count + Ammo.Count > 0;
			}
			return true;
		}
	}
}
