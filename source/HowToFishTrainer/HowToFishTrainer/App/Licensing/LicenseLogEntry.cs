namespace HowToFishTrainer.App.Licensing;

public sealed record LicenseLogEntry(string Route, string Host, int? HttpStatus, string ServerCode, string NetworkKind, string Message);
