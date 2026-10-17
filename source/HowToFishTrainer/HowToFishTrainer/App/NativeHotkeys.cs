using System.Runtime.InteropServices;

namespace HowToFishTrainer.App;

internal static class NativeHotkeys
{
	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool UnregisterHotKey(nint window, int id);
}
