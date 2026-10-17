using System;
using System.IO;
using System.Text.Json;

namespace HowToFishTrainer.App;

/// <summary>
/// 材质强度档（纯外观设置，不影响任何功能或协议）。
///
/// 三档语义：
///   Solid  实色     —— 完全不透明，可读性最高；也用于低渲染能力自动降级
///   Tinted 着色玻璃 —— 默认档。交接文档 §7.3 要求"默认偏可读的着色玻璃"
///   Clear  通透     —— 透明度最高，玻璃感最强，文字背后最活跃
///
/// 持久化刻意**不复用**原有 settings.json：交接文档 §12 要求"独立视觉设置若需要持久化，
/// 放新的 UI 专用文件，保留旧设置"。所以这里写 ui-appearance.json，只含本文件的键。
/// 读取失败、文件损坏、键缺失都退回默认档，绝不影响启动。
/// </summary>
public enum MaterialStrength
{
	Solid = 0,
	Tinted = 1,
	Clear = 2,
}

public static class UiAppearance
{
	private const string FileName = "ui-appearance.json";

	private const MaterialStrength DefaultStrength = MaterialStrength.Tinted;

	public static MaterialStrength Strength { get; private set; } = DefaultStrength;

	/// <summary>当前档对应的资源字典逻辑名；Tinted 与 Clear 走同一套机制。</summary>
	public static string ResourcePath => Strength switch
	{
		MaterialStrength.Solid => "themes/MaterialSolid.xaml",
		MaterialStrength.Clear => "themes/MaterialClear.xaml",
		_ => "themes/MaterialTinted.xaml",
	};

	public static string DisplayName => Strength switch
	{
		MaterialStrength.Solid => "实色",
		MaterialStrength.Clear => "通透",
		_ => "着色玻璃",
	};

	private static string SettingsPath => Path.Combine(ProductIdentity.AppDataDirectory, FileName);

	/// <summary>启动时调用一次：读取上次选择，损坏或缺失则用默认档。</summary>
	public static void Load()
	{
		try
		{
			if (!File.Exists(SettingsPath))
			{
				return;
			}

			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
			if (document.RootElement.TryGetProperty("materialStrength", out JsonElement element)
				&& element.ValueKind == JsonValueKind.String
				&& Enum.TryParse(element.GetString(), ignoreCase: true, out MaterialStrength parsed)
				&& Enum.IsDefined(parsed))
			{
				Strength = parsed;
			}
		}
		catch (Exception)
		{
			// 设置文件不可读时保持默认档。外观设置绝不能影响启动。
			Strength = DefaultStrength;
		}
	}

	/// <summary>菜单里选档时调用：先改内存值，再尽力持久化。</summary>
	public static void Set(MaterialStrength strength)
	{
		if (!Enum.IsDefined(strength))
		{
			return;
		}

		Strength = strength;
		try
		{
			Directory.CreateDirectory(ProductIdentity.AppDataDirectory);
			File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
				new { materialStrength = strength.ToString() },
				new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception)
		{
			// 写不进去只影响下次启动的默认值，不影响本次运行。
		}
	}
}
