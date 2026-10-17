using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using HowToFishTrainer.App.Licensing;

namespace HowToFishTrainer.App;

public partial class App : Application
{
	private LicenseService? _license;

	private Mutex? _singleInstanceMutex;

	private bool _ownsSingleInstanceMutex;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		// 在创建任何窗口之前决定外观档：窗口一建就会读到 Motion*/Glass* token，之后再换就来不及了。
		ApplyMotionPreference();
		ApplyMaterialTier();
		if (!e.Args.Contains("--ui-preview", StringComparer.OrdinalIgnoreCase) && !TryEnterSingleInstance())
		{
			MessageBox.Show("检测到另一份AbyssalCrab Studio正在运行。请先退出所有旧版和新版修改器，并重启游戏后只运行当前版本。\n\n同时运行多个版本会导致运行时桥互相接管，表现为一直等待运行时管道。", "AbyssalCrab Studio", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			Shutdown(2);
			return;
		}
		_license = new LicenseService(ProductIdentity.AppDataDirectory, "1.1.37");
		MainWindow mainWindow = (MainWindow)(MainWindow = new MainWindow(_license, HowToFishTrainer.App.MainWindow.CreatePreferencesStore()));
		mainWindow.Show();
		InitializeLicenseAsync(mainWindow, _license);
	}

	// 减少动画降级（交接文档 §10.8）。
	//
	// 依据：SystemParameters.ClientAreaAnimation 反映 Windows「在 Windows 中显示动画」设置，
	// 这是 WPF 实际暴露出来的、与动效相关的系统信号；SystemParameters 没有提供与之等价的
	// "透明效果"开关，因此"减少透明度"只能作为应用自己的 UI 设置另行提供，不在此处伪造。
	//
	// 做法：把 MotionReduced.xaml 追加到合并资源字典末尾，用 0 时长覆盖 Motion* 时长 token。
	// 主题里 123 条动画的 Duration 全部引用这些 token，所以一次覆盖即可让整站动效变为
	// "立即到终值"，界面状态仍然正确，只是没有过渡过程。
	//
	// 注意：只覆盖时长，不覆盖颜色、材质、尺寸，也不触碰任何业务资源；紧急停用、关闭、
	// 失败提示本来就不受动画门控，时长归零只会让它们更快可见。
	private void ApplyMotionPreference()
	{
        ApplyMotionPreference(UiMotion.ReduceMotion);
    }

    private void ApplyMotionPreference(bool reducedMotion)
	{
		try
		{
			if (!reducedMotion)
			{
				return;
			}
			ResourceDictionary reduced = new ResourceDictionary
			{
				Source = new Uri("/HowToFishTrainer;component/themes/MotionReduced.xaml", UriKind.Relative)
			};
            // StaticResource durations resolve within the theme dictionary. Update that
            // dictionary before any control/template is constructed, as well as the app lookup.
            ResourceDictionary theme = Resources.MergedDictionaries.FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.EndsWith("themes/theme.xaml", StringComparison.OrdinalIgnoreCase) == true)
                ?? Resources;
            foreach (object key in reduced.Keys) theme[key] = reduced[key];
			Resources.MergedDictionaries.Add(reduced);
		}
		catch (Exception)
		{
			// 降级资源加载失败时保持默认动效，绝不能让启动失败。
		}
	}

	// 材质降级（交接文档 §8「可选增强/回退」、§7.3「浅色模式必须完整可用」）。
	//
	// 材质强度（交接文档 §7.3「材质强度可提供纯 UI 设置，默认偏可读的着色玻璃」
	// 与 §8「低渲染能力…必须可降级」）。
	//
	// 三档：Solid 实色 / Tinted 着色玻璃（默认）/ Clear 通透。
	//   · 默认档由 UiAppearance 决定，来源是独立的 ui-appearance.json（不复用旧 settings.json）。
	//   · 用户可在顶栏「材质」菜单里切换，切换后立即整站生效并持久化。
	//   · 自动降级叠在用户选择之上：RenderCapability.Tier 高 16 位 < 2（软件渲染/远程桌面）
	//     时强制实色，因为此时渐变着色与光晕只是纯开销。
	//
	// 实现：把对应档位字典追加到合并资源字典末尾，用同名键覆盖玻璃 token。
	// 窗口与卡片对这些 token 用的是 DynamicResource，所以替换会立刻反映到已建好的视觉树上；
	// 颜色/尺寸/业务 token 一律不动。
	private void ApplyMaterialTier()
	{
		try
		{
			UiAppearance.Load();

			// 低渲染能力直接压到实色档，忽略用户选择。
			string path = MaterialResourcePath(EffectiveMaterialStrength);
			ApplyMaterialDictionary(path);
		}
		catch (Exception)
		{
			// 保持主题自带的默认材质（着色玻璃），绝不能让启动失败。
		}
	}

	/// <summary>切换材质档：替换材质字典并持久化用户选择。由 MainWindow 的菜单调用。</summary>
	internal static void SwitchMaterialTier(MaterialStrength strength)
	{
		if (Current is not App app)
		{
			return;
		}

        if (!Enum.IsDefined(strength)) return;
        MaterialStrength effective = ResolveMaterialStrength(strength,
            System.Windows.Media.RenderCapability.Tier >> 16, SystemParameters.HighContrast);
        app.ApplyMaterialDictionary(MaterialResourcePath(effective));
		UiAppearance.Set(strength);
	}

    internal static MaterialStrength EffectiveMaterialStrength => ResolveMaterialStrength(UiAppearance.Strength,
        System.Windows.Media.RenderCapability.Tier >> 16, SystemParameters.HighContrast);

    internal static MaterialStrength ResolveMaterialStrength(MaterialStrength preferred, int tier, bool highContrast) =>
        tier < 2 || highContrast ? MaterialStrength.Solid : preferred;

    private static string MaterialResourcePath(MaterialStrength strength) => strength switch
    {
        MaterialStrength.Solid => "themes/MaterialSolid.xaml",
        MaterialStrength.Clear => "themes/MaterialClear.xaml",
        _ => "themes/MaterialTinted.xaml"
    };

	private void ApplyMaterialDictionary(string relativePath)
	{
        // Load first: a missing/broken appearance resource must not remove the working tier.
        ResourceDictionary replacement = new ResourceDictionary
        {
            Source = new Uri("/HowToFishTrainer;component/" + relativePath, UriKind.Relative)
        };
		// 移除上一次合并进去的材质字典，保证同一时刻只有一档生效。
		for (int index = Resources.MergedDictionaries.Count - 1; index >= 0; index--)
		{
			Uri? source = Resources.MergedDictionaries[index].Source;
			if (source == null)
			{
				continue;
			}
			string text = source.OriginalString;
			if (text.Contains("MaterialSolid", StringComparison.Ordinal)
				|| text.Contains("MaterialTinted", StringComparison.Ordinal)
				|| text.Contains("MaterialClear", StringComparison.Ordinal))
			{
				Resources.MergedDictionaries.RemoveAt(index);
			}
		}

		Resources.MergedDictionaries.Add(replacement);
	}

	private bool TryEnterSingleInstance()
	{
		_singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\AbyssalCrabStudio.HowToFishTrainer.SingleInstance", out var createdNew);
		_ownsSingleInstanceMutex = createdNew;
		int? num = TrainerInstancePolicy.FindCompetingProcessId(Environment.ProcessId, TrainerInstancePolicy.SnapshotProcesses());
		if (createdNew)
		{
			return !num.HasValue;
		}
		return false;
	}

	private static async Task InitializeLicenseAsync(MainWindow window, ILicenseClient license)
	{
		try
		{
			await license.InitializeAsync();
		}
		catch (Exception ex)
		{
			window.ApplyLicenseStatus(new LicenseStatus(LicenseEdition.Standard, LicenseState.ServiceUnavailable, "授权服务暂不可用", "启动校验失败：" + ex.Message + " 标准版功能仍可继续使用。"));
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_license?.Dispose();
		if (_ownsSingleInstanceMutex)
		{
			try
			{
				_singleInstanceMutex?.ReleaseMutex();
			}
			catch (ApplicationException)
			{
			}
		}
		_singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}
}
