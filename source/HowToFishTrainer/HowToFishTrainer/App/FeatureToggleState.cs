namespace HowToFishTrainer.App;

public sealed record FeatureToggleState(bool Enabled, string Status, string StateLabel, bool IsSuccessful)
{
    public string SpinSpeedText { get; init; } = "4";
    public double AppliedSpinSpeed { get; init; } = 4d;
}
