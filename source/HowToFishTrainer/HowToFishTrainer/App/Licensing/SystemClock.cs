using System;

namespace HowToFishTrainer.App.Licensing;

public sealed class SystemClock : ISystemClock
{
	public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
