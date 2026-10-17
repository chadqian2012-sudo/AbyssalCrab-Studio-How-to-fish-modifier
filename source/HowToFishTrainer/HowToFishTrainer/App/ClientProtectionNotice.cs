using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

internal static class ClientProtectionNotice
{
	public const string Message = "当前处于好友房间（客机）。此功能由房主端结算，仅开启本机修改器无法保证生效。请由房主在“房间成员”中为你配置相应保护。本次未开启此功能。";

	public static FeatureToggleState DisabledState => new FeatureToggleState(Enabled: false, "当前处于好友房间（客机）。此功能由房主端结算，仅开启本机修改器无法保证生效。请由房主在“房间成员”中为你配置相应保护。本次未开启此功能。", "需房主支持", IsSuccessful: false);

	public static bool AppliesTo(string id)
	{
		switch (id)
		{
		case "player.health.lock":
		case "player.fullness.lock":
		case "player.poison.immunity":
		case "player.fire.immunity":
			return true;
		default:
			return false;
		}
	}

	public static bool IsRestricted(string id, CapabilitySet? capabilities)
	{
		return false;
	}
}
