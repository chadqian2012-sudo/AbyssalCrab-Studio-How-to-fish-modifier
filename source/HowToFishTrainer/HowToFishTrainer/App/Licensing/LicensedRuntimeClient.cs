using System.Threading;
using System.Threading.Tasks;
using HowToFishTrainer.Core;

namespace HowToFishTrainer.App.Licensing;

public sealed class LicensedRuntimeClient(IGameRuntimeClient inner, ISponsorAccessGate accessGate) : IGameRuntimeClient
{
	public ValueTask<RuntimeCommandResult> ExecuteAsync(FeatureCommand command, CancellationToken cancellationToken)
	{
		if (!accessGate.TryAuthorize(command.FeatureId))
		{
			return ValueTask.FromResult(new RuntimeCommandResult(Succeeded: false, "此操作需要激活赞助版；未向游戏发送命令。", null, null, RolledBack: false));
		}
		return inner.ExecuteAsync(command, cancellationToken);
	}
}
