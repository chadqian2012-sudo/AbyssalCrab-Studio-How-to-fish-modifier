using System;

namespace HowToFishTrainer.App.Licensing;

public sealed class SponsorAccessRequestedEventArgs(string featureId) : EventArgs
{
	public string FeatureId { get; } = featureId;
}
