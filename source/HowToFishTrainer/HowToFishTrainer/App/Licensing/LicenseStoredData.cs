using System;

namespace HowToFishTrainer.App.Licensing;

public sealed record LicenseStoredData(string ActivationCode, LicenseEdition Edition, string ProductKey, DateTimeOffset ActivatedAtUtc, DateTimeOffset LastValidatedUtc, DateTimeOffset LastObservedUtc);
