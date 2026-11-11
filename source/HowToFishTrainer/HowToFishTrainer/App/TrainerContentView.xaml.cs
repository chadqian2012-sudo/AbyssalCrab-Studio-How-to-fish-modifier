using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace HowToFishTrainer.App;

public partial class TrainerContentView : UserControl
{
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompact), typeof(bool), typeof(TrainerContentView), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;
    public bool IsCompact => (bool)GetValue(IsCompactProperty);
    private bool _navCapsuleMovePending;
    private bool _appearanceClosing;
    private int _navAnimationVersion;
    private MainViewModel Model => (MainViewModel)DataContext;
    internal Action<int>? OpenJournalRequested { get; set; }
    public TrainerContentView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _appearanceClosing = false; MoveNavCapsule(false); };
        Unloaded += (_, _) => { _appearanceClosing = true; _navAnimationVersion++; };
    }
    private void OpenJournal_Click(object sender, RoutedEventArgs e) => OpenJournalRequested?.Invoke(0);
    private void OpenRecap_Click(object sender, RoutedEventArgs e) => OpenJournalRequested?.Invoke(2);
    private void OpenSaveBackups_Click(object sender, RoutedEventArgs e) => OpenJournalRequested?.Invoke(3);
	private void TopCategories_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (IsCompact) TopCategories.ScrollIntoView(TopCategories.SelectedItem);
		ScheduleNavCapsuleMove();
	}

	private void TopCategories_ScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		if (!_appearanceClosing) MoveNavCapsule(false);
	}

	protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
	{
		base.OnRenderSizeChanged(sizeInfo);
		SetValue(IsCompactPropertyKey, sizeInfo.NewSize.Width < 900);
		if (IsCompact) TopCategories.ScrollIntoView(TopCategories.SelectedItem);
		// 换行阈值跨过时（1180–1320 区间）条目会重新排布，胶囊要跟着回到新的真实位置。
		ScheduleNavCapsuleMove();
	}

	private void ScheduleNavCapsuleMove()
	{
		if (_appearanceClosing || _navCapsuleMovePending)
		{
			return;
		}
		_navCapsuleMovePending = true;
		// 等布局完成再量容器位置，否则换行后的坐标还是旧的。
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
		{
			_navCapsuleMovePending = false;
			if (!_appearanceClosing) MoveNavCapsule(animate: true);
		}));
	}

	private void MoveNavCapsule(bool animate)
	{
		if (NavCapsule == null || TopCategories == null || TopCategories.Items.Count == 0)
		{
			return;
		}

		int index = TopCategories.SelectedIndex;
		if (index < 0)
		{
			NavCapsule.Visibility = Visibility.Collapsed;
			return;
		}

		if (TopCategories.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container
			|| container.ActualWidth <= 0)
		{
			// 容器还没生成出来（虚拟化/首次布局），下一次布局事件会再试。
			return;
		}

		// 量到胶囊层坐标系。胶囊层与 ListBox 是同一个 Grid 的两个兄弟，尺寸一致。
		Point origin = container.TranslatePoint(new Point(0, 0), NavCapsuleLayer);
		double width = container.ActualWidth;
		double height = container.ActualHeight;
		if (width <= 0 || height <= 0)
		{
			return;
		}

		NavCapsule.Visibility = Visibility.Visible;
		NavCapsule.Width = width;
		NavCapsule.Height = height;

		NavScale.CenterX = width / 2;
		NavScale.CenterY = height / 2;
		double currentX = NavOffset.X;
		double currentY = NavOffset.Y;
		int animationVersion = ++_navAnimationVersion;
		// The final position is the base value, including when clocks are removed.
		NavOffset.X = origin.X;
		NavOffset.Y = origin.Y;

		Duration duration = animate
			? TryFindResource("MotionSelect") as Duration? ?? new Duration(TimeSpan.FromMilliseconds(250))
			: new Duration(TimeSpan.Zero);
		if (duration.HasTimeSpan && duration.TimeSpan == TimeSpan.Zero)
		{
			ClearNavAnimations();
			return;
		}

		IEasingFunction easing = TryFindResource("EaseSelect") as IEasingFunction
			?? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
		var moveX = new DoubleAnimation(currentX, origin.X, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
		var moveY = new DoubleAnimation(currentY, origin.Y, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
		var scale = new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
		moveX.Completed += (_, _) =>
		{
			if (!_appearanceClosing && animationVersion == _navAnimationVersion) ClearNavAnimations();
		};
		NavOffset.BeginAnimation(TranslateTransform.XProperty, moveX);
		NavOffset.BeginAnimation(TranslateTransform.YProperty, moveY);
		NavScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
		NavScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
	}

	private void ClearNavAnimations()
	{
		NavOffset.BeginAnimation(TranslateTransform.XProperty, null);
		NavOffset.BeginAnimation(TranslateTransform.YProperty, null);
		NavScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		NavScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
	}

    private void ExportTeleport_Click(object sender,RoutedEventArgs args)
    {
        var dialog=new SaveFileDialog{Title="导出传送点",Filter="JSON 文件|*.json",FileName="AbyssalCrab-传送点.json",DefaultExt=".json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{AtomicLocalFile.Write(dialog.FileName,Model.ExportTeleportPoints(),text=>System.Text.Json.JsonDocument.Parse(text).Dispose());Model.PositionStatus="传送点与标记已导出";}
        catch(Exception e){Model.PositionStatus="导出失败 · "+e.Message;}
    }
    private void ImportTeleport_Click(object sender,RoutedEventArgs args)
    {
        var dialog=new OpenFileDialog{Title="导入传送点（同名保留原值）",Filter="JSON 文件|*.json"};
        if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
        try{if(new FileInfo(dialog.FileName).Length>4*1024*1024)throw new InvalidDataException("传送点文件超过 4 MB");Model.ImportTeleportPoints(File.ReadAllText(dialog.FileName));}
        catch(Exception e){Model.PositionStatus="导入失败，现有坐标保持不变 · "+e.Message;}
    }
}
