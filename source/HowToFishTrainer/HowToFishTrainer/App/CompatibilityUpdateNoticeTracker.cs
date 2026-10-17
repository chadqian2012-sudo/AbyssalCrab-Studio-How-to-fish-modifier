using System;
using System.Collections.Generic;
using HowToFishTrainer.Memory;

namespace HowToFishTrainer.App;

public sealed class CompatibilityUpdateNoticeTracker
{
	private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);

	public bool ShouldShow(GameFingerprint observed, CompatibilityReport report)
	{
		if (report.Status != CompatibilityStatus.CompatibleUpdate || !report.RequiresTrainerUpdate)
		{
			return false;
		}
		return _shown.Add($"{observed.SteamBuildId}|{observed.AssemblySha256}|{observed.AssemblyMvid:D}");
	}
}
