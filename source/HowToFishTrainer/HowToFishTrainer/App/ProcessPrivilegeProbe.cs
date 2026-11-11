namespace HowToFishTrainer.App;

public static class ProcessPrivilegeProbe
{
	public static PrivilegeLevel Current() => PrivilegeLevel.Unknown;

	public static PrivilegeLevel ForProcess(int processId) => PrivilegeLevel.Unknown;
}
