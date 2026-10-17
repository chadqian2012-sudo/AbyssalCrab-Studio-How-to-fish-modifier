using System;

namespace HowToFishTrainer.App.Licensing;

public sealed class SponsorAccessGate(Func<LicenseStatus> statusProvider) : ISponsorAccessGate
{
	public bool IsSponsor => true;

	public event EventHandler<SponsorAccessRequestedEventArgs>? ActivationRequested;

	public bool TryAuthorize(string featureId)
	{
		return true;
	}
}
