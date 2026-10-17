namespace HowToFishTrainer.App.Licensing;

public sealed record LicenseActivationResult(bool Success, LicenseStatus Status, string ErrorCode = "");
