using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HowToFishTrainer.App;

/// <summary>
/// 本地测试版界面的文案清理层：在窗口加载后沿视觉树查找并隐藏“仅房主/需赞助”一类
/// 误导性提示所在的整块容器，避免只改一行文案却把限制说明留在界面上。
/// 逐行还原自已验证程序集 HowToFishTrainer.dll 中的同名类型（IL 语义一致）。
/// </summary>
internal static class LocalTestNoticeUi
{
	private static readonly string[] RetiredNotices =
	{
		"运行时确认模式",
		"联机安全",
		"请不要在未经房主及其他玩家同意的房间中随意使用修改器。高级功能可能影响其他玩家体验及房主世界进度。",
		"连接建议：请尽量不要使用管理员方式启动游戏；游戏与修改器权限级别不一致可能导致连接失败。"
	};

	internal static void Install(Window window)
	{
		window.Loaded += OnLoaded;
	}

	private static void OnLoaded(object sender, RoutedEventArgs e)
	{
		Hide((DependencyObject)sender);
	}

	private static void Hide(DependencyObject root)
	{
		if (root is TextBlock text && RetiredNotices.Contains(text.Text, System.StringComparer.Ordinal))
		{
			DependencyObject ancestor = VisualTreeHelper.GetParent(root);
			while (ancestor != null && ancestor is not Border)
			{
				ancestor = VisualTreeHelper.GetParent(ancestor);
			}
			if (ancestor is UIElement container)
			{
				container.Visibility = Visibility.Collapsed;
			}
		}

		int childCount = VisualTreeHelper.GetChildrenCount(root);
		for (int index = 0; index < childCount; index++)
		{
			Hide(VisualTreeHelper.GetChild(root, index));
		}
	}
}
