using System;
using System.Threading;
using System.Threading.Tasks;

namespace HowToFishTrainer.App.Licensing;

public interface ILicenseClient : IDisposable
{
	LicenseStatus Current { get; }

	string DeviceId { get; }

	event EventHandler<LicenseStatus>? StatusChanged;

	Task<LicenseStatus> InitializeAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<LicenseActivationResult> ActivateAsync(string activationCode, CancellationToken cancellationToken = default(CancellationToken));

	Task<LicenseStatus> RefreshAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<LicenseStatus> ClearLocalAsync(CancellationToken cancellationToken = default(CancellationToken));
}
