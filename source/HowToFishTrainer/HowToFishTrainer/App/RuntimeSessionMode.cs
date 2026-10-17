using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public static class RuntimeSessionMode
{
	public static SessionMode From(bool server, bool client, bool lobby)
	{
		if (!server)
		{
			if (!client)
			{
				return SessionMode.MainMenu;
			}
			return SessionMode.JoinedFriend;
		}
		if (!lobby)
		{
			return SessionMode.Solo;
		}
		return SessionMode.Host;
	}
}
