using System;
using System.IO;
using System.Linq;
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

		// 胶囊的 RenderTransform 是 TransformGroup：[ScaleTransform, TranslateTransform]。
		// TranslateTransform 负责 X/Y 位移（基值写法与"追随最新项"策略不变），
		// ScaleTransform 负责到位时"轻微放大到 1"的收束感。
		if (NavCapsule.RenderTransform is not TransformGroup navTransform)
		{
			return;
		}
		TranslateTransform? transform = navTransform.Children.OfType<TranslateTransform>().FirstOrDefault();
		if (transform == null)
		{
			return;
		}
		// 缩放中心取胶囊自身中心：宽高每帧都在变，所以每次都要跟着更新。
		ScaleTransform? settle = navTransform.Children.OfType<ScaleTransform>().FirstOrDefault();
		if (settle != null)
		{
			settle.CenterX = width / 2;
			settle.CenterY = height / 2;
		}
        double currentX = transform.X;
        double currentY = transform.Y;
        int animationVersion = ++_navAnimationVersion;

		if (!animate)
		{
			// 首帧直接就位，避免窗口刚显示时胶囊从左上角滑过来。
			transform.BeginAnimation(TranslateTransform.XProperty, null);
			transform.BeginAnimation(TranslateTransform.YProperty, null);
			transform.X = origin.X;
			transform.Y = origin.Y;
			if (settle != null)
			{
				settle.BeginAnimation(ScaleTransform.ScaleXProperty, null);
				settle.BeginAnimation(ScaleTransform.ScaleYProperty, null);
				settle.ScaleX = 1;
				settle.ScaleY = 1;
			}
			return;
		}

		// 先把目标写进基值，再交给动画做过渡。
		// 这一步是必要的：动画只存在于时钟上，基值不变。一旦时钟被释放（窗口关闭的清理路径、
		// 或任何 BeginAnimation(prop, null)），属性会回落到基值——不写基值的话胶囊会弹回初始位置。
		// Capture the current animated position before updating the base target; an
        // explicit From also makes the first selection animate instead of jumping.
		transform.X = origin.X;
		transform.Y = origin.Y;

		Duration duration = TryFindResource("MotionSelect") as Duration? ?? new Duration(TimeSpan.FromMilliseconds(250));
		IEasingFunction easing = TryFindResource("EaseSelect") as IEasingFunction
			?? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };

        var xAnimation = new DoubleAnimation(currentX, origin.X, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var yAnimation = new DoubleAnimation(currentY, origin.Y, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        xAnimation.Completed += (_, _) =>
        {
            if (!_appearanceClosing && animationVersion == _navAnimationVersion)
                transform.BeginAnimation(TranslateTransform.XProperty, null);
        };
        yAnimation.Completed += (_, _) =>
        {
            if (!_appearanceClosing && animationVersion == _navAnimationVersion)
                transform.BeginAnimation(TranslateTransform.YProperty, null);
        };
        transform.BeginAnimation(TranslateTransform.XProperty, xAnimation);
        transform.BeginAnimation(TranslateTransform.YProperty, yAnimation);

		// 收束：从 0.97 轻微放大到 1，复用同一个 MotionSelect / EaseSelect（BackEase 带一点点过冲）。
		// 与位移用同一套保护：新动画直接替换旧时钟（不排队），结束后按版本号释放时钟，且只在最新一次里释放。
		if (settle != null)
		{
			settle.ScaleX = 1;
			settle.ScaleY = 1;
			var scaleXAnimation = new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
			var scaleYAnimation = new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
			scaleXAnimation.Completed += (_, _) =>
			{
				if (!_appearanceClosing && animationVersion == _navAnimationVersion)
					settle.BeginAnimation(ScaleTransform.ScaleXProperty, null);
			};
			scaleYAnimation.Completed += (_, _) =>
			{
				if (!_appearanceClosing && animationVersion == _navAnimationVersion)
					settle.BeginAnimation(ScaleTransform.ScaleYProperty, null);
			};
			settle.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
			settle.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
		}
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
