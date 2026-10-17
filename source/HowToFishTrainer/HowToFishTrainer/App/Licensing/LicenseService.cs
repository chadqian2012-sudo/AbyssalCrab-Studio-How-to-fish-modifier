using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace HowToFishTrainer.App.Licensing;
public sealed class LicenseService : ILicenseClient
{
    private readonly HttpClient? _compatibilityClient;
    public LicenseStatus Current { get; private set; } = LicenseStatus.Standard();
    public string DeviceId => "local-developer-test";
    public event EventHandler<LicenseStatus>? StatusChanged;
    public LicenseService(string appDataDirectory, string appVersion) { }
    public LicenseService(ILicenseStorage storage, IDeviceIdentityProvider device, ISystemClock clock, ILicenseLog log, HttpClient httpClient, IReadOnlyList<Uri> endpoints, string appVersion)
    { _compatibilityClient = httpClient; }
    private LicenseStatus SetCurrent()
    { Current = LicenseStatus.Standard(); StatusChanged?.Invoke(this, Current); return Current; }
    public Task<LicenseStatus> InitializeAsync(CancellationToken cancellationToken = default) => Task.FromResult(SetCurrent());
    public Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken = default) => Task.FromResult(SetCurrent());
    public Task<LicenseStatus> ClearLocalAsync(CancellationToken cancellationToken = default) => Task.FromResult(SetCurrent());
    public Task<LicenseActivationResult> ActivateAsync(string activationCode, CancellationToken cancellationToken = default) => Task.FromResult(new LicenseActivationResult(true, SetCurrent()));
    public void Dispose() => _compatibilityClient?.Dispose();
}
