using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace HowToFishTrainer.App;

public static class ProcessPrivilegeProbe
{
	private struct TokenElevation
	{
		public int TokenIsElevated;
	}

	private const uint ProcessQueryLimitedInformation = 4096u;

	private const uint TokenQuery = 8u;

	public static PrivilegeLevel Current()
	{
		return PrivilegeLevel.Unknown;
	}

	public static PrivilegeLevel ForProcess(int processId)
	{
		return PrivilegeLevel.Unknown;
	}

	private static PrivilegeLevel ReadElevation(SafeAccessTokenHandle token)
	{
		if (!GetTokenInformation(token, 20, out var information, Marshal.SizeOf<TokenElevation>(), out var _))
		{
			return PrivilegeLevel.Unknown;
		}
		if (information.TokenIsElevated == 0)
		{
			return PrivilegeLevel.Standard;
		}
		return PrivilegeLevel.Elevated;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out TokenElevation information, int informationLength, out int returnLength);
}
