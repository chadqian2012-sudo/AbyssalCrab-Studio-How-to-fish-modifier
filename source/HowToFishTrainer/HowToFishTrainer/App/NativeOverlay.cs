using System.Runtime.InteropServices;

namespace HowToFishTrainer.App;

internal static class NativeOverlay
{
	[DllImport("user32.dll")]
	internal static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool IsIconic(nint hwnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool GetClientRect(nint hwnd, out NativeRect rect);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

	[DllImport("user32.dll")]
	internal static extern uint GetDpiForWindow(nint hwnd);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	internal static extern nint GetWindowLongPtr(nint hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
