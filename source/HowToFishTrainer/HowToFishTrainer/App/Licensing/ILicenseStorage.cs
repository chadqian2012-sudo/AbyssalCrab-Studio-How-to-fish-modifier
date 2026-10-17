using System.Threading;
using System.Threading.Tasks;

namespace HowToFishTrainer.App.Licensing;

public interface ILicenseStorage
{
	Task<LicenseStoredData?> LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SaveAsync(LicenseStoredData data, CancellationToken cancellationToken = default(CancellationToken));

	Task ClearAsync(CancellationToken cancellationToken = default(CancellationToken));
}
