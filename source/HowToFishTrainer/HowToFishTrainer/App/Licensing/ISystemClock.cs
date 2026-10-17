using System;

namespace HowToFishTrainer.App.Licensing;

public interface ISystemClock
{
	DateTimeOffset UtcNow { get; }
}
