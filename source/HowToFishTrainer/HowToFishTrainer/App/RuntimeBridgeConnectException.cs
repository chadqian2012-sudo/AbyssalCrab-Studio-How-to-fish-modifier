using System;

namespace HowToFishTrainer.App;

public sealed class RuntimeBridgeConnectException(RuntimeBridgeConnectStage stage, Exception innerException) : Exception($"Runtime bridge failed during {stage}.", innerException)
{
	public RuntimeBridgeConnectStage Stage { get; } = stage;
}
