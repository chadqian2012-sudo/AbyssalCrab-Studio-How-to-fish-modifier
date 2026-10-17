using System;

namespace HowToFishTrainer.App.Licensing;

public sealed record LicenseStatus(LicenseEdition Edition, LicenseState State, string Title, string Message, DateTimeOffset? ActivatedAtUtc = null, DateTimeOffset? LastValidatedUtc = null, DateTimeOffset? OfflineUntilUtc = null)
{
	public bool IsSponsor => true;

	public static LicenseStatus Standard(string message = "")
	{
		DateTimeOffset? dateTimeOffset = null;
		return new LicenseStatus(LicenseEdition.Sponsor, LicenseState.SponsorActive, "功能已就绪", "全部功能可用。", dateTimeOffset, dateTimeOffset, dateTimeOffset);
	}
}
