namespace HowToFishTrainer.App;

public sealed record RoomMemberSnapshot(string Key, string DisplayName, string OwnerTail, bool IsLocal, bool IsHost, bool Alive, double Health, double Fullness, double Poison, double Fire, double X, double Y, double Z)
{
	public string RoleLabel
	{
		get
		{
			if (!IsHost)
			{
				if (!IsLocal)
				{
					return "好友";
				}
				return "本机";
			}
			return "房主";
		}
	}

	public string LifeLabel
	{
		get
		{
			if (!Alive)
			{
				return "已倒地";
			}
			return "存活";
		}
	}

	public string PositionLabel => $"{X:0.0}, {Y:0.0}, {Z:0.0}";
}
