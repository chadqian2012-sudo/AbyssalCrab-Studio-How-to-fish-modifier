using System.Windows;
using System.Windows.Media;

namespace HowToFishTrainer.App;

/// <summary>
/// 卡片悬停抬升行为：给每张 FeatureCard 安装一份**独享**的可动画 RenderTransform。
///
/// 为什么需要它：theme.xaml 的 FeatureCardStyle 无法用 Style Setter 提供可动画的
/// RenderTransform —— Style Setter 里的 Freezable 会被 Style.Seal() 冻结，且被所有卡片共享，
/// 对它做 (UIElement.RenderTransform).(TranslateTransform.Y) 动画会抛
/// InvalidOperationException（"该对象已密封或已冻结"）。
/// 本附加属性在元素上装一个 new TranslateTransform(0, 0)，让主题的悬停动画有一个合法、可写的目标。
///
/// 设计约束：首次置 true 时分配一次，之后零分配；无计时器、无事件订阅、无生命周期钩子；
/// 未挂该行为的卡片只是不参与抬升动画，绝不抛异常。
/// </summary>
public static class CardLiftTransform
{
	public static readonly DependencyProperty EnableProperty =
		DependencyProperty.RegisterAttached(
			"Enable",
			typeof(bool),
			typeof(CardLiftTransform),
			new PropertyMetadata(false, OnEnableChanged));

	public static void SetEnable(DependencyObject element, bool value)
	{
		element.SetValue(EnableProperty, value);
	}

	public static bool GetEnable(DependencyObject element)
	{
		return (bool)element.GetValue(EnableProperty);
	}

	private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		// 只在"开启"时动手；关闭时不回滚，避免与元素自身的本地变换打架。
		if (d is not UIElement element || !(bool)e.NewValue)
		{
			return;
		}

		// 元素已经自带 TranslateTransform（例如卡片侧自己挂了本地变换）就尊重它，不覆盖。
		if (element.RenderTransform is TranslateTransform)
		{
			return;
		}

		// 每张卡一份新实例；分配只发生在这条属性首次置 true 时。
		element.RenderTransform = new TranslateTransform(0, 0);
	}
}
