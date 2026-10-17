using System;

namespace HowToFishTrainer.App.Licensing;

public sealed class AllowAllSponsorAccessGate : ISponsorAccessGate
{
	public static AllowAllSponsorAccessGate Instance { get; } = new AllowAllSponsorAccessGate();

	public bool IsSponsor => true;

	public event EventHandler<SponsorAccessRequestedEventArgs>? ActivationRequested;

	private AllowAllSponsorAccessGate()
	{
	}

	public bool TryAuthorize(string featureId)
	{
		return true;
	}
}
