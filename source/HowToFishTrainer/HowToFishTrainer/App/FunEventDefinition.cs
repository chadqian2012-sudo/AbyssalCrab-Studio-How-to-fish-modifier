namespace HowToFishTrainer.App;

public sealed record FunEventDefinition(string Id, string Title, string Description, FunEventRisk Risk, int RequestedCount)
{
	public string RiskLabel => Risk switch
	{
		FunEventRisk.Light => "轻量", 
		FunEventRisk.Lively => "热闹", 
		_ => "危险", 
	};
}
