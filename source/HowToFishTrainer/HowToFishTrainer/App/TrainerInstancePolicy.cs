using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace HowToFishTrainer.App;

public static class TrainerInstancePolicy
{
	private const string TrainerProcessPrefix = "AbyssalCrab Studio-HowToFish-V";

	public static int? FindCompetingProcessId(int currentProcessId, IEnumerable<(int ProcessId, string ProcessName, string? ProductName)> processes)
	{
		foreach (var (num, text, a) in processes)
		{
			if (num != currentProcessId && (string.Equals(a, "AbyssalCrab Studio", StringComparison.Ordinal) || text.StartsWith("AbyssalCrab Studio-HowToFish-V", StringComparison.OrdinalIgnoreCase)))
			{
				return num;
			}
		}
		return null;
	}

	public static IReadOnlyList<(int ProcessId, string ProcessName, string? ProductName)> SnapshotProcesses()
	{
		List<(int, string, string)> list = new List<(int, string, string)>();
		Process[] processes = Process.GetProcesses();
		foreach (Process process in processes)
		{
			using (process)
			{
				string item = null;
				try
				{
					string text = process.MainModule?.FileName;
					if (!string.IsNullOrWhiteSpace(text))
					{
						item = FileVersionInfo.GetVersionInfo(text).ProductName;
					}
				}
				catch
				{
				}
				try { list.Add((process.Id, process.ProcessName, item)); }
				catch (InvalidOperationException) { }
				catch (System.ComponentModel.Win32Exception) { }
			}
		}
		return list;
	}
}
