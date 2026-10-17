using System.Media;

namespace HowToFishTrainer.App;

public sealed class ToggleSoundPlayer : IToggleSoundPlayer
{
	public void PlayEnabled()
	{
		try
		{
			SystemSounds.Asterisk.Play();
		}
		catch
		{
		}
	}
}
