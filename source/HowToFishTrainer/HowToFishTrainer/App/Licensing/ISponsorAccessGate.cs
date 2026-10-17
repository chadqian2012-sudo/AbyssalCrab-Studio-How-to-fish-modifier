using System;

namespace HowToFishTrainer.App.Licensing;

public interface ISponsorAccessGate
{
	bool IsSponsor { get; }

	event EventHandler<SponsorAccessRequestedEventArgs>? ActivationRequested;

	bool TryAuthorize(string featureId);
}
