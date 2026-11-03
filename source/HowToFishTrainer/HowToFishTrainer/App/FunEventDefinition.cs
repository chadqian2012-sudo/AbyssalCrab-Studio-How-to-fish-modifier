namespace HowToFishTrainer.App;

public sealed record FunEventDefinition(string Id, string Title, string Description, FunEventRisk Risk, int RequestedCount)
{
	public bool HasAnimalCount => Id is "piranha-rain" or "seagull-migration" or "fish-rain" or "crab-parade" or "whale-fall" or "rare-fish-school" or "seafood-platter" or "seagull-feast" or "ocean-parade";
	public string RiskLabel => Risk switch
	{
		FunEventRisk.Light => "轻量", 
		FunEventRisk.Lively => "热闹", 
		_ => "危险", 
	};
}
