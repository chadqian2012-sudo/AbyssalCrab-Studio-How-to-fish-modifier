namespace HowToFishTrainer.App;

public static class ProcessPrivilegeAdvisor
{
	private const string RestartSteps = "1. 关闭修改器和游戏。\n2. 在两者的兼容性属性中取消“以管理员身份运行”。\n3. 先启动游戏并进入房间。\n4. 再启动修改器。";

	public static PrivilegeAdvice? Evaluate(PrivilegeLevel trainer, PrivilegeLevel game, bool accessDenied)
	{
		if (accessDenied)
		{
			return new PrivilegeAdvice("access-denied", "访问游戏进程被拒绝。请按顺序操作：\n1. 关闭修改器和游戏。\n2. 在两者的兼容性属性中取消“以管理员身份运行”。\n3. 先启动游戏并进入房间。\n4. 再启动修改器。");
		}
		if (trainer == PrivilegeLevel.Elevated && game == PrivilegeLevel.Elevated)
		{
			return new PrivilegeAdvice("both-elevated", "修改器和游戏均以管理员身份运行；允许继续，但不推荐。\n1. 关闭修改器和游戏。\n2. 在两者的兼容性属性中取消“以管理员身份运行”。\n3. 先启动游戏并进入房间。\n4. 再启动修改器。");
		}
		if (trainer != PrivilegeLevel.Unknown && game != PrivilegeLevel.Unknown && trainer != game)
		{
			return new PrivilegeAdvice("mismatch", "修改器与游戏的运行权限不一致。请取消“以管理员身份运行”。\n1. 关闭修改器和游戏。\n2. 在两者的兼容性属性中取消“以管理员身份运行”。\n3. 先启动游戏并进入房间。\n4. 再启动修改器。");
		}
		return null;
	}
}
