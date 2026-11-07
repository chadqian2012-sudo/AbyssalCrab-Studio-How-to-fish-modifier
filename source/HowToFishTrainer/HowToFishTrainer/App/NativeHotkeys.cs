using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace HowToFishTrainer.App;

internal static class NativeHotkeys
{
	internal static bool TryParse(string text, out uint modifiers, out uint virtualKey)
	{
		modifiers = virtualKey = 0;
		string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
		if (parts.Length == 0 || Array.Exists(parts, string.IsNullOrWhiteSpace) || char.IsDigit(parts[^1][0]) || parts[^1].Contains(',')) return false;
		for (int i = 0; i < parts.Length - 1; i++)
		{
			uint value = parts[i].ToUpperInvariant() switch { "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => 0u };
			if (value == 0 || (modifiers & value) != 0) return false;
			modifiers |= value;
		}
		if (!Enum.TryParse<Key>(parts[^1], true, out var key) || !Enum.IsDefined(key) || key is Key.None or Key.Escape or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return false;
		virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
		return virtualKey > 0 && !(virtualKey == 123 && modifiers == 6) && !(key == Key.F && modifiers == 2);
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool UnregisterHotKey(nint window, int id);
}
