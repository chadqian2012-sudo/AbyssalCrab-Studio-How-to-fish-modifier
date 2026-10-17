using HowToFishTrainer.Memory;

namespace HowToFishTrainer.App;

public static class CompatibilityPresentation
{
	public static string Status(CompatibilityReport report)
	{
		return report.Status switch
		{
			CompatibilityStatus.Supported => "兼容版本 · 正在连接运行时桥", 
			CompatibilityStatus.CompatibleUpdate => "游戏已更新 · 兼容模式 · 请更新修改器", 
			CompatibilityStatus.WrongArchitecture => "游戏架构不兼容 · 仅只读 · 请更新修改器", 
			_ => "游戏版本不兼容 · 仅只读 · 请更新修改器", 
		};
	}

	public static string UpdateMessage(GameFingerprint observed)
	{
		return "检测到新的游戏版本（Steam Build " + observed.SteamBuildId + "）。\n\n关键运行时契约检查已通过，修改器将以兼容模式继续运行。为获得正式支持，请尽快更新修改器。\n\n如果出现功能异常，请立即按 Ctrl+Shift+F12 停用全部持续功能并停止使用当前版本。";
	}
}
